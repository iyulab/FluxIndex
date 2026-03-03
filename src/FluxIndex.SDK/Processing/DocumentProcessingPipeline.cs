using System.Text.Json;
using FileFlux;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure.Conversion;
using Microsoft.Extensions.Logging;
using IFluxIndexEmbeddingService = FluxIndex.Core.Application.Interfaces.IEmbeddingService;
using IFluxIndexTextCompletionService = FluxIndex.Core.Application.Interfaces.ITextCompletionService;
using IFluxIndexContextualEnrichmentService = FluxIndex.Core.Application.Interfaces.IContextualEnrichmentService;
using IFluxIndexQAGenerationService = FluxIndex.Core.Application.Interfaces.IQAGenerationService;
using System.Globalization;

namespace FluxIndex.SDK.Processing;

/// <summary>
/// Document processing pipeline for extracting, chunking, enriching, and embedding documents.
///
/// Pipeline order (optimized for RAG quality):
/// 1. ExtractImages - Extract images from document (optional, parallel-ready)
/// 2. FileFlux Pipeline - Single call handles: Extract → Parse → Refine → Chunk → Enhance
///    - Refine stage (v0.8.7+): Noise removal, OCR fixes, Markdown conversion via RefiningOptions
/// 3. ContextualEnrich - Add document context to each chunk (Anthropic Contextual Retrieval)
/// 4. Embed - Generate embeddings (uses contextualized text if enrichment was performed)
/// 5. Metadata - Enrich document metadata (optional LLM-based)
/// 6. QAGenerate - Generate QA pairs for evaluation (optional)
/// 7. Save - Output files
/// </summary>
public partial class DocumentProcessingPipeline
{
    private static readonly char[] WordSplitSeparators = [' ', '\t', '\n', '\r'];
    private static readonly JsonSerializerOptions s_camelCaseJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDocumentProcessorFactory _processorFactory;
    private readonly IFluxIndexEmbeddingService? _embeddingService;
    private readonly IFluxIndexTextCompletionService? _textCompletionService;
    private readonly IFluxIndexContextualEnrichmentService? _contextualEnrichmentService;
    private readonly IFluxIndexQAGenerationService? _qaGenerationService;
    private readonly IMarkdownConverter? _markdownConverter;
    private readonly ILogger<DocumentProcessingPipeline> _logger;

    public DocumentProcessingPipeline(
        IDocumentProcessorFactory processorFactory,
        IFluxIndexEmbeddingService? embeddingService,
        IFluxIndexTextCompletionService? textCompletionService,
        IFluxIndexContextualEnrichmentService? contextualEnrichmentService,
        IFluxIndexQAGenerationService? qaGenerationService,
        ILogger<DocumentProcessingPipeline> logger,
        IMarkdownConverter? markdownConverter = null)
    {
        _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
        _embeddingService = embeddingService;
        _textCompletionService = textCompletionService;
        _contextualEnrichmentService = contextualEnrichmentService;
        _qaGenerationService = qaGenerationService;
        _markdownConverter = markdownConverter;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Process a document file and return structured results.
    /// </summary>
    public async Task<DocumentProcessingResult> ProcessAsync(
        string filePath,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DocumentProcessingOptions();
        var result = new DocumentProcessingResult
        {
            SourcePath = filePath,
            DocumentId = Path.GetFileNameWithoutExtension(filePath),
            Stats = { StartTime = DateTime.UtcNow }
        };

        try
        {
            ReportProgress(options, ProcessingStage.Initializing, 0, "Initializing pipeline...");

            // Validate input
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}", filePath);
            }

            var fileInfo = new FileInfo(filePath);
            result.Metadata.FileSize = fileInfo.Length;
            result.Metadata.FileExtension = fileInfo.Extension;
            result.Metadata.CreatedDate = fileInfo.CreationTimeUtc;
            result.Metadata.ModifiedDate = fileInfo.LastWriteTimeUtc;

            // Build ChunkingOptions with RefiningOptions (FileFlux v0.8.7+ 5-stage pipeline)
            var chunkingOptions = new ChunkingOptions
            {
                Strategy = options.ChunkingStrategy,
                MaxChunkSize = options.MaxChunkSize,
                OverlapSize = options.OverlapSize,
                // Use FileFlux's RefiningOptions for text cleaning (replaces EnableTextCleaning)
                // Note: RefiningOptions.ForRAG was removed in FileFlux 0.9.7
                RefiningOptions = RefiningOptions.Default  // Default refining with Markdown conversion
            };

            if (!string.IsNullOrEmpty(options.Language))
            {
                chunkingOptions.CustomProperties["language"] = options.Language;
            }

            // Stage 1: Extract raw content (for images and metadata)
            ReportProgress(options, ProcessingStage.Extracting, 5, "Extracting content from document...");
            var extractStart = DateTime.UtcNow;

            RawContent? rawContent = null;
            if (options.ExtractImages)
            {
                try
                {
                    await using var extractProcessor = _processorFactory.Create(filePath);
                    await extractProcessor.ExtractAsync(cancellationToken);
                    rawContent = extractProcessor.Result.Raw;
                    if (rawContent?.Images?.Count > 0)
                    {
                        foreach (var image in rawContent.Images.Where(i => i.Data != null && i.Data.Length > 0))
                        {
                            var imageId = image.Id ?? $"img_{result.Images.Count:D3}";
                            var imageExtension = GetImageExtension(image.MimeType);
                            var fileName = $"{imageId}{imageExtension}";
                            if (!result.Images.ContainsKey(fileName))
                            {
                                result.Images[fileName] = image.Data!;
                            }
                        }
                        result.Metadata.ImageCount = result.Images.Count;
                        result.Stats.TotalImages = result.Images.Count;
                    }
                }
                catch (Exception ex)
                {
                    LogImageExtractionFailed(_logger, ex);
                }
            }

            // Stage 2-4: Parse → Refine → Chunk (single FileFlux pipeline call)
            // FileFlux v0.9.x handles: Extract → Refine → Chunk → Enrich (stateful processor)
            ReportProgress(options, ProcessingStage.Chunking, 20, "Processing and chunking document...");
            var chunkStart = DateTime.UtcNow;

            await using var chunkProcessor = _processorFactory.Create(filePath);
            var processingOptions = new FileFlux.Core.ProcessingOptions { Chunking = chunkingOptions };
            await chunkProcessor.ProcessAsync(processingOptions, cancellationToken);
            var chunkList = (chunkProcessor.Result.Chunks ?? []).ToList();

            // Reconstruct full text from chunks for statistics and enrichment
            var fullText = string.Join("\n\n", chunkList.Select(c => c.Content));
            result.ExtractedText = rawContent?.Text ?? fullText;
            result.Metadata.CharacterCount = result.ExtractedText.Length;
            result.Metadata.WordCount = CountWords(result.ExtractedText);
            result.Stats.ExtractionTime = DateTime.UtcNow - extractStart;

            LogExtractedContent(_logger, result.Metadata.CharacterCount, result.Metadata.WordCount, filePath);

            // Extract language from FileFlux metadata (v0.8.4+)
            var firstChunk = chunkList.FirstOrDefault();
            if (firstChunk?.Metadata != null)
            {
                if (!string.IsNullOrEmpty(firstChunk.Metadata.Language))
                    result.Metadata.DetectedLanguage = firstChunk.Metadata.Language;
            }

            var chunkIndex = 0;
            foreach (var fileFluxChunk in chunkList)
            {
                var chunkResult = new ChunkResult
                {
                    Id = $"{result.DocumentId}_chunk_{chunkIndex:D3}",
                    Index = chunkIndex,
                    Content = fileFluxChunk.Content ?? string.Empty,
                    CharacterCount = fileFluxChunk.Content?.Length ?? 0,
                    TokenCount = EstimateTokenCount(fileFluxChunk.Content ?? string.Empty)
                };

                if (fileFluxChunk.Location != null)
                {
                    chunkResult.StartPosition = fileFluxChunk.Location.StartChar;
                    chunkResult.EndPosition = fileFluxChunk.Location.EndChar;

                    if (fileFluxChunk.Location.HeadingPath?.Count > 0)
                    {
                        chunkResult.Metadata["heading_path"] = string.Join(" > ", fileFluxChunk.Location.HeadingPath);
                    }

                    if (!string.IsNullOrEmpty(fileFluxChunk.Location.Section))
                    {
                        chunkResult.Metadata["section"] = fileFluxChunk.Location.Section;
                    }
                }

                chunkResult.Metadata["quality"] = fileFluxChunk.Quality;
                chunkResult.Metadata["strategy"] = fileFluxChunk.Strategy;

                result.Chunks.Add(chunkResult);
                chunkIndex++;
            }

            result.Stats.ChunkingTime = DateTime.UtcNow - chunkStart;
            result.Stats.TotalChunks = result.Chunks.Count;

            LogCreatedChunks(_logger, result.Chunks.Count);

            // Stage 5: Contextual Enrichment BEFORE embedding (if enabled)
            if (options.EnableContextualEnrichment && _contextualEnrichmentService != null)
            {
                ReportProgress(options, ProcessingStage.ContextualEnrichment, 40, "Adding contextual enrichment...");
                var enrichStart = DateTime.UtcNow;

                // Use extracted text (already refined by FileFlux v0.8.7+)
                var chunkContents = result.Chunks.Select(c => c.Content).ToList();

                var contextSummaries = await _contextualEnrichmentService.GenerateContextBatchAsync(
                    chunkContents, fullText, cancellationToken);

                for (int i = 0; i < result.Chunks.Count && i < contextSummaries.Count; i++)
                {
                    result.Chunks[i].ContextSummary = contextSummaries[i];
                }

                result.Stats.ContextualEnrichmentTime = DateTime.UtcNow - enrichStart;
                result.Stats.EnrichedChunks = contextSummaries.Count(s => !string.IsNullOrEmpty(s));

                LogEnrichedChunks(_logger, result.Stats.EnrichedChunks, result.Chunks.Count);
            }

            // Stage 6: Generate embeddings (uses contextualized text if available)
            if (options.GenerateEmbeddings && _embeddingService != null)
            {
                ReportProgress(options, ProcessingStage.GeneratingEmbeddings, 55, "Generating embeddings...");
                var embedStart = DateTime.UtcNow;

                // Use contextualized text for embedding if context was generated
                var textsForEmbedding = result.Chunks
                    .Select(c => c.GetContextualizedText())
                    .ToList();

                var embeddings = (await _embeddingService.GenerateEmbeddingsBatchAsync(
                    textsForEmbedding, cancellationToken)).ToList();

                for (int i = 0; i < result.Chunks.Count && i < embeddings.Count; i++)
                {
                    result.Chunks[i].Embedding = embeddings[i];
                }

                result.Stats.EmbeddingTime = DateTime.UtcNow - embedStart;
                LogGeneratedEmbeddings(_logger, result.Chunks.Count);
            }

            // Stage 7: Enrich metadata (if enabled)
            if (options.EnableMetadataEnrichment && _textCompletionService != null)
            {
                ReportProgress(options, ProcessingStage.EnrichingMetadata, 75, "Enriching metadata...");
                await EnrichMetadataAsync(result, cancellationToken);
            }

            // Stage 8: Generate QA pairs (if enabled) - AFTER embedding as final optional stage
            if (options.EnableQAGeneration && _qaGenerationService != null)
            {
                ReportProgress(options, ProcessingStage.GeneratingQA, 85, "Generating QA pairs...");
                var qaStart = DateTime.UtcNow;

                var chunkInputs = result.Chunks.Select(c => new Core.Application.Interfaces.ChunkInput
                {
                    ChunkId = c.Id,
                    Content = c.Content
                }).ToList();

                var qaResults = await _qaGenerationService.GenerateFromChunksBatchAsync(
                    chunkInputs, options.MaxQAPairsPerChunk, cancellationToken);

                foreach (var chunkQA in qaResults)
                {
                    foreach (var qa in chunkQA.QAPairs)
                    {
                        result.QAPairs.Add(new QAPairResult
                        {
                            ChunkId = chunkQA.ChunkId,
                            Question = qa.Question,
                            Answer = qa.Answer,
                            Context = qa.Context,
                            QualityScore = qa.QualityScore
                        });
                    }
                }

                result.Stats.QAGenerationTime = DateTime.UtcNow - qaStart;
                result.Stats.TotalQAPairs = result.QAPairs.Count;

                LogGeneratedQAPairs(_logger, result.QAPairs.Count, result.Chunks.Count);
            }

            // Stage 9: Save output files
            ReportProgress(options, ProcessingStage.SavingOutput, 92, "Saving output files...");
            await SaveOutputFilesAsync(result, options, cancellationToken);

            result.Stats.EndTime = DateTime.UtcNow;
            result.Success = true;

            ReportProgress(options, ProcessingStage.Complete, 100, "Processing complete!");
            LogDocumentProcessingCompleted(_logger, result.Stats.Duration.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            LogDocumentProcessingFailed(_logger, ex, filePath);

            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Stats.EndTime = DateTime.UtcNow;

            ReportProgress(options, ProcessingStage.Failed, 0, $"Processing failed: {ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// Extract content from document without chunking or embedding.
    /// Use this to get intermediate results that can be persisted and resumed later.
    /// </summary>
    /// <param name="filePath">Path to document file</param>
    /// <param name="extractImages">Whether to extract images</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extraction result with raw text, images, and source hash for change detection</returns>
    public Task<ExtractionResult> ExtractOnlyAsync(
        string filePath,
        bool extractImages = true,
        CancellationToken cancellationToken = default)
    {
        return ExtractOnlyAsync(filePath, new ExtractionOptions { ExtractImages = extractImages }, cancellationToken);
    }

    /// <summary>
    /// Extract content from document with configurable options.
    /// Use this to get intermediate results that can be persisted and resumed later.
    /// </summary>
    /// <param name="filePath">Path to document file</param>
    /// <param name="options">Extraction options including markdown conversion</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extraction result with raw text, optional markdown, images, and source hash</returns>
    public async Task<ExtractionResult> ExtractOnlyAsync(
        string filePath,
        ExtractionOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = new ExtractionResult
        {
            SourcePath = filePath,
            DocumentId = Path.GetFileNameWithoutExtension(filePath)
        };

        try
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}", filePath);
            }

            // Compute source hash for change detection
            result.SourceHash = ExtractionResult.ComputeFileHash(filePath);

            // Extract file metadata
            var fileInfo = new FileInfo(filePath);
            result.Metadata.FileSize = fileInfo.Length;
            result.Metadata.FileExtension = fileInfo.Extension;
            result.Metadata.CreatedDate = fileInfo.CreationTimeUtc;
            result.Metadata.ModifiedDate = fileInfo.LastWriteTimeUtc;

            // Extract raw content using FileFlux 0.9.x factory pattern
            RawContent? rawContent = null;
            try
            {
                await using var extractProcessor = _processorFactory.Create(filePath);
                await extractProcessor.ExtractAsync(cancellationToken);
                rawContent = extractProcessor.Result.Raw;
                result.ExtractedText = rawContent?.Text ?? string.Empty;
            }
            catch
            {
                // Fallback to ProcessAsync for text extraction
                var rawText = await ExtractRawTextAsync(filePath, cancellationToken);
                result.ExtractedText = rawText;
            }

            result.Metadata.CharacterCount = result.ExtractedText.Length;
            result.Metadata.WordCount = CountWords(result.ExtractedText);

            LogExtractedCharacters(_logger, result.Metadata.CharacterCount, filePath);

            // Extract images if enabled
            if (options.ExtractImages)
            {
                if (rawContent?.Images != null && rawContent.Images.Count > 0)
                {
                    // Use images from rawContent if available
                    foreach (var image in rawContent.Images.Where(i => i.Data != null && i.Data.Length > 0))
                    {
                        var imageId = image.Id ?? $"img_{result.Images.Count:D3}";
                        var imageExtension = GetImageExtension(image.MimeType);
                        var fileName = $"{imageId}{imageExtension}";
                        if (!result.Images.ContainsKey(fileName))
                        {
                            result.Images[fileName] = image.Data!;
                        }
                    }
                }
                else
                {
                    result.Images = await ExtractImagesAsync(filePath, cancellationToken);
                }
                result.Metadata.ImageCount = result.Images.Count;
            }

            // Convert to Markdown if enabled (FileFlux v0.8.6+)
            if (options.ConvertToMarkdown)
            {
                var markdownResult = await ConvertToMarkdownInternalAsync(
                    rawContent ?? new RawContent { Text = result.ExtractedText },
                    options.MarkdownOptions,
                    cancellationToken);

                result.MarkdownText = markdownResult.markdown;
                result.MarkdownStatistics = markdownResult.statistics;

                LogConvertedToMarkdown(_logger, markdownResult.statistics?.HeadingCount ?? 0, markdownResult.statistics?.TableCount ?? 0, markdownResult.statistics?.ListCount ?? 0);
            }

            result.ExtractedAt = DateTime.UtcNow;
            result.Success = true;

            return result;
        }
        catch (Exception ex)
        {
            LogExtractionFailed(_logger, ex, filePath);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Convert text content to structured Markdown using FileFlux MarkdownConverter.
    /// </summary>
    /// <param name="text">Text to convert</param>
    /// <param name="options">Markdown conversion options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Converted markdown and statistics</returns>
    public async Task<(string markdown, MarkdownConversionStatistics? statistics)> ConvertToMarkdownAsync(
        string text,
        MarkdownOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var rawContent = new RawContent { Text = text };
        return await ConvertToMarkdownInternalAsync(rawContent, options, cancellationToken);
    }

    private async Task<(string markdown, MarkdownConversionStatistics? statistics)> ConvertToMarkdownInternalAsync(
        RawContent rawContent,
        MarkdownOptions? options,
        CancellationToken cancellationToken)
    {
        // If no converter available, create one without LLM support
        var converter = _markdownConverter ?? new MarkdownConverter();

        var conversionOptions = new MarkdownConversionOptions();
        if (options != null)
        {
            conversionOptions.PreserveHeadings = options.PreserveHeadings;
            conversionOptions.ConvertTables = options.ConvertTables;
            conversionOptions.PreserveLists = options.PreserveLists;
            conversionOptions.IncludeImagePlaceholders = options.IncludeImagePlaceholders;
            conversionOptions.UseLLMInference = options.UseLLMInference;
            conversionOptions.DetectCodeBlocks = options.DetectCodeBlocks;
            conversionOptions.NormalizeWhitespace = options.NormalizeWhitespace;
        }

        var result = await converter.ConvertAsync(rawContent, conversionOptions, cancellationToken);

        var statistics = new MarkdownConversionStatistics
        {
            HeadingCount = result.Statistics.HeadingCount,
            TableCount = result.Statistics.TableCount,
            ListCount = result.Statistics.ListCount,
            CodeBlockCount = result.Statistics.CodeBlockCount,
            ImagePlaceholderCount = result.Statistics.ImagePlaceholderCount,
            Method = result.Method.ToString(),
            OriginalLength = result.OriginalLength,
            MarkdownLength = result.MarkdownLength,
            Warnings = result.Warnings
        };

        return (result.Markdown, statistics);
    }

    /// <summary>
    /// Process from already-extracted content, skipping the extraction stage.
    /// Use this to resume processing from persisted extraction results or user-edited content.
    /// </summary>
    /// <param name="content">Text content to process (can be extracted text or user-edited)</param>
    /// <param name="options">Processing options for chunking, embedding, etc.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processing result with chunks and embeddings</returns>
    public async Task<DocumentProcessingResult> ProcessFromContentAsync(
        string content,
        ContentProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ContentProcessingOptions();
        var result = new DocumentProcessingResult
        {
            DocumentId = options.DocumentId ?? $"content_{DateTime.UtcNow:yyyyMMddHHmmss}",
            ExtractedText = content,
            Stats = { StartTime = DateTime.UtcNow }
        };

        try
        {
            ReportContentProgress(options, ProcessingStage.Initializing, 0, "Initializing from content...");

            result.Metadata.CharacterCount = content.Length;
            result.Metadata.WordCount = CountWords(content);

            // Stage: Chunk content
            ReportContentProgress(options, ProcessingStage.Chunking, 20, "Creating chunks...");
            var chunkStart = DateTime.UtcNow;

            var chunkingOptions = new ChunkingOptions
            {
                Strategy = options.ChunkingStrategy,
                MaxChunkSize = options.MaxChunkSize,
                OverlapSize = options.OverlapSize
            };

            if (!string.IsNullOrEmpty(options.Language))
            {
                chunkingOptions.CustomProperties["language"] = options.Language;
            }

            // FileFlux IDocumentProcessorFactory works with files, so create a temporary file
            var tempFilePath = Path.Combine(Path.GetTempPath(), $"fluxindex_content_{Guid.NewGuid()}.txt");
            List<DocumentChunk> chunkList;
            try
            {
                await File.WriteAllTextAsync(tempFilePath, content, cancellationToken);
                await using var tempProcessor = _processorFactory.Create(tempFilePath);
                var tempProcessingOptions = new FileFlux.Core.ProcessingOptions { Chunking = chunkingOptions };
                await tempProcessor.ProcessAsync(tempProcessingOptions, cancellationToken);
                chunkList = (tempProcessor.Result.Chunks ?? []).ToList();
            }
            finally
            {
                // Clean up temporary file
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { /* Ignore cleanup errors */ }
                }
            }

            // Extract language from FileFlux metadata
            var firstChunk = chunkList.FirstOrDefault();
            if (firstChunk?.Metadata != null && !string.IsNullOrEmpty(firstChunk.Metadata.Language))
            {
                result.Metadata.DetectedLanguage = firstChunk.Metadata.Language;
            }

            var chunkIndex = 0;
            foreach (var fileFluxChunk in chunkList)
            {
                var chunkResult = new ChunkResult
                {
                    Id = $"{result.DocumentId}_chunk_{chunkIndex:D3}",
                    Index = chunkIndex,
                    Content = fileFluxChunk.Content ?? string.Empty,
                    CharacterCount = fileFluxChunk.Content?.Length ?? 0,
                    TokenCount = EstimateTokenCount(fileFluxChunk.Content ?? string.Empty)
                };

                if (fileFluxChunk.Location != null)
                {
                    chunkResult.StartPosition = fileFluxChunk.Location.StartChar;
                    chunkResult.EndPosition = fileFluxChunk.Location.EndChar;

                    if (fileFluxChunk.Location.HeadingPath?.Count > 0)
                    {
                        chunkResult.Metadata["heading_path"] = string.Join(" > ", fileFluxChunk.Location.HeadingPath);
                    }
                }

                chunkResult.Metadata["quality"] = fileFluxChunk.Quality;
                chunkResult.Metadata["strategy"] = fileFluxChunk.Strategy;

                result.Chunks.Add(chunkResult);
                chunkIndex++;
            }

            result.Stats.ChunkingTime = DateTime.UtcNow - chunkStart;
            result.Stats.TotalChunks = result.Chunks.Count;

            LogCreatedChunksFromContent(_logger, result.Chunks.Count);

            // Stage: Contextual Enrichment (if enabled)
            if (options.EnableContextualEnrichment && _contextualEnrichmentService != null)
            {
                ReportContentProgress(options, ProcessingStage.ContextualEnrichment, 40, "Adding contextual enrichment...");
                var enrichStart = DateTime.UtcNow;

                var chunkContents = result.Chunks.Select(c => c.Content).ToList();
                var contextSummaries = await _contextualEnrichmentService.GenerateContextBatchAsync(
                    chunkContents, content, cancellationToken);

                for (int i = 0; i < result.Chunks.Count && i < contextSummaries.Count; i++)
                {
                    result.Chunks[i].ContextSummary = contextSummaries[i];
                }

                result.Stats.ContextualEnrichmentTime = DateTime.UtcNow - enrichStart;
                result.Stats.EnrichedChunks = contextSummaries.Count(s => !string.IsNullOrEmpty(s));
            }

            // Stage: Generate embeddings
            if (options.GenerateEmbeddings && _embeddingService != null)
            {
                ReportContentProgress(options, ProcessingStage.GeneratingEmbeddings, 60, "Generating embeddings...");
                var embedStart = DateTime.UtcNow;

                var textsForEmbedding = result.Chunks
                    .Select(c => c.GetContextualizedText())
                    .ToList();

                var embeddings = (await _embeddingService.GenerateEmbeddingsBatchAsync(
                    textsForEmbedding, cancellationToken)).ToList();

                for (int i = 0; i < result.Chunks.Count && i < embeddings.Count; i++)
                {
                    result.Chunks[i].Embedding = embeddings[i];
                }

                result.Stats.EmbeddingTime = DateTime.UtcNow - embedStart;
                LogGeneratedEmbeddings(_logger, result.Chunks.Count);
            }

            // Stage: Generate QA pairs (if enabled)
            if (options.EnableQAGeneration && _qaGenerationService != null)
            {
                ReportContentProgress(options, ProcessingStage.GeneratingQA, 85, "Generating QA pairs...");
                var qaStart = DateTime.UtcNow;

                var chunkInputs = result.Chunks.Select(c => new Core.Application.Interfaces.ChunkInput
                {
                    ChunkId = c.Id,
                    Content = c.Content
                }).ToList();

                var qaResults = await _qaGenerationService.GenerateFromChunksBatchAsync(
                    chunkInputs, options.MaxQAPairsPerChunk, cancellationToken);

                foreach (var chunkQA in qaResults)
                {
                    foreach (var qa in chunkQA.QAPairs)
                    {
                        result.QAPairs.Add(new QAPairResult
                        {
                            ChunkId = chunkQA.ChunkId,
                            Question = qa.Question,
                            Answer = qa.Answer,
                            Context = qa.Context,
                            QualityScore = qa.QualityScore
                        });
                    }
                }

                result.Stats.QAGenerationTime = DateTime.UtcNow - qaStart;
                result.Stats.TotalQAPairs = result.QAPairs.Count;
            }

            result.Stats.EndTime = DateTime.UtcNow;
            result.Success = true;

            ReportContentProgress(options, ProcessingStage.Complete, 100, "Processing complete!");
            LogContentProcessingCompleted(_logger, result.Stats.Duration.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            LogContentProcessingFailed(_logger, ex);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Stats.EndTime = DateTime.UtcNow;

            ReportContentProgress(options, ProcessingStage.Failed, 0, $"Processing failed: {ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// Resume processing from an extraction result, optionally with modified content.
    /// </summary>
    /// <param name="extractionResult">Previous extraction result</param>
    /// <param name="modifiedContent">Optional modified content (if null, uses original extracted text)</param>
    /// <param name="options">Processing options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processing result with chunks and embeddings</returns>
    public async Task<DocumentProcessingResult> ProcessFromExtractionAsync(
        ExtractionResult extractionResult,
        string? modifiedContent = null,
        ContentProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Validate extraction result
        if (!extractionResult.Success)
        {
            LogCannotProcessFromFailedExtraction(_logger, extractionResult.ErrorMessage);
            return new DocumentProcessingResult
            {
                DocumentId = extractionResult.DocumentId,
                SourcePath = extractionResult.SourcePath,
                Success = false,
                ErrorMessage = $"Cannot process from failed extraction: {extractionResult.ErrorMessage}",
                Stats = { StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow }
            };
        }

        options ??= new ContentProcessingOptions();
        options.DocumentId ??= extractionResult.DocumentId;

        var content = modifiedContent ?? extractionResult.ExtractedText;
        var result = await ProcessFromContentAsync(content, options, cancellationToken);

        // Copy metadata from extraction
        result.SourcePath = extractionResult.SourcePath;
        result.Images = new Dictionary<string, byte[]>(extractionResult.Images);
        result.Metadata.FileSize = extractionResult.Metadata.FileSize;
        result.Metadata.FileExtension = extractionResult.Metadata.FileExtension;
        result.Metadata.CreatedDate = extractionResult.Metadata.CreatedDate;
        result.Metadata.ModifiedDate = extractionResult.Metadata.ModifiedDate;
        result.Metadata.ImageCount = extractionResult.Images.Count;

        return result;
    }

    /// <summary>
    /// Check if source file has changed since extraction.
    /// </summary>
    public static bool HasSourceChanged(string filePath, string previousHash)
    {
        if (!File.Exists(filePath)) return true;
        var currentHash = ExtractionResult.ComputeFileHash(filePath);
        return !string.Equals(previousHash, currentHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Process and save output to specified directory.
    /// </summary>
    public async Task<DocumentProcessingResult> ProcessAndSaveAsync(
        string filePath,
        string? outputDirectory = null,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DocumentProcessingOptions();

        if (string.IsNullOrEmpty(outputDirectory))
        {
            outputDirectory = Path.Combine(
                Path.GetDirectoryName(filePath) ?? ".",
                Path.GetFileName(filePath) + "_output");
        }

        options.OutputDirectory = outputDirectory;

        return await ProcessAsync(filePath, options, cancellationToken);
    }

    private async Task<string> ExtractRawTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // For PDF files, use PdfDocumentReader directly to access StructuralHints for table quality
        if (extension == ".pdf")
        {
            return await ExtractPdfWithQualityCheckAsync(filePath, cancellationToken);
        }

        // Use FileFlux for extraction - it handles various document types
        await using var fullDocProcessor = _processorFactory.Create(filePath);
        var fullDocOptions = new FileFlux.Core.ProcessingOptions
        {
            Chunking = new ChunkingOptions
            {
                Strategy = "FullDocument", // Get full text first
                MaxChunkSize = int.MaxValue
            }
        };
        await fullDocProcessor.ProcessAsync(fullDocOptions, cancellationToken);

        return string.Join("\n\n", (fullDocProcessor.Result.Chunks ?? []).Select(c => c.Content));
    }

    private async Task<string> ExtractPdfWithQualityCheckAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            var reader = new PdfDocumentReader();
            var rawContent = await reader.ExtractAsync(stream, Path.GetFileName(filePath), null, cancellationToken);

            // Check table quality using StructuralHints (FileFlux v0.7.2+)
            if (rawContent.Hints != null)
            {
                var tablesDetected = rawContent.Hints.TryGetValue("TablesDetected", out var tablesObj)
                    ? Convert.ToInt32(tablesObj, CultureInfo.InvariantCulture) : 0;
                var lowConfidenceTables = rawContent.Hints.TryGetValue("LowConfidenceTables", out var lowConfObj)
                    ? Convert.ToInt32(lowConfObj, CultureInfo.InvariantCulture) : 0;
                var minConfidence = rawContent.Hints.TryGetValue("MinTableConfidence", out var confObj)
                    ? Convert.ToDouble(confObj, CultureInfo.InvariantCulture) : 1.0;

                if (tablesDetected > 0)
                {
                    LogPdfTableQuality(_logger, tablesDetected, lowConfidenceTables, minConfidence);

                    if (lowConfidenceTables > 0)
                    {
                        LogPdfLowConfidenceTables(_logger, lowConfidenceTables);
                    }
                }
            }

            return rawContent.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            LogPdfQualityCheckFailed(_logger, ex, filePath);

            // Fallback to standard FileFlux extraction
            await using var fallbackProcessor = _processorFactory.Create(filePath);
            var fallbackOptions = new FileFlux.Core.ProcessingOptions
            {
                Chunking = new ChunkingOptions
                {
                    Strategy = "FullDocument",
                    MaxChunkSize = int.MaxValue
                }
            };
            await fallbackProcessor.ProcessAsync(fallbackOptions, cancellationToken);

            return string.Join("\n\n", (fallbackProcessor.Result.Chunks ?? []).Select(c => c.Content));
        }
    }


    /// <summary>
    /// Extract images from document using FileFlux's IDocumentProcessor.
    /// Supported formats (FileFlux v0.8.5+): HTML, DOCX, PDF, PPTX, XLSX
    /// </summary>
    private async Task<Dictionary<string, byte[]>> ExtractImagesAsync(string filePath, CancellationToken cancellationToken)
    {
        var images = new Dictionary<string, byte[]>();
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // Formats with image extraction support in FileFlux v0.8.5+
        var supportedFormats = new[] { ".html", ".htm", ".docx", ".pdf", ".pptx", ".xlsx" };

        if (!supportedFormats.Contains(extension))
        {
            LogImageExtractionNotSupported(_logger, extension);
            return images;
        }

        try
        {
            // Use IDocumentProcessorFactory.ExtractAsync for unified image extraction
            await using var imageProcessor = _processorFactory.Create(filePath);
            await imageProcessor.ExtractAsync(cancellationToken);
            var rawContent = imageProcessor.Result.Raw;

            if (rawContent?.Images != null && rawContent.Images.Count > 0)
            {
                foreach (var image in rawContent.Images.Where(i => i.Data != null && i.Data.Length > 0))
                {
                    var imageId = image.Id ?? $"img_{images.Count:D3}";
                    var imageExtension = GetImageExtension(image.MimeType);
                    var fileName = $"{imageId}{imageExtension}";

                    // Avoid duplicate keys
                    if (!images.ContainsKey(fileName))
                    {
                        images[fileName] = image.Data!;
                    }
                }

                LogExtractedImages(_logger, images.Count, filePath);
            }
        }
        catch (Exception ex)
        {
            LogFailedToExtractImages(_logger, ex, filePath);
        }

        return images;
    }

    private async Task<string> CleanTextAsync(string text, CancellationToken cancellationToken)
    {
        if (_textCompletionService == null || string.IsNullOrWhiteSpace(text))
            return text;

        try
        {
            var prompt = $@"Clean and preprocess the following extracted text:
1. Remove unnecessary whitespace, line breaks, and formatting artifacts
2. Fix obvious OCR errors if present
3. Normalize punctuation and spacing
4. Remove header/footer noise if detected
5. Keep all original information intact - do not summarize or modify meaning

Text to clean:
{text}

Cleaned text:";

            var cleaned = await _textCompletionService.CompleteAsync(prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = 4000, Temperature = 0.1f }, cancellationToken);
            return string.IsNullOrWhiteSpace(cleaned) ? text : cleaned;
        }
        catch (Exception ex)
        {
            LogTextCleaningFailed(_logger, ex);
            return text;
        }
    }

    private async Task EnrichMetadataAsync(DocumentProcessingResult result, CancellationToken cancellationToken)
    {
        if (_textCompletionService == null)
            return;

        try
        {
            var sampleText = result.ExtractedText.Length > 2000
                ? result.ExtractedText[..2000]
                : result.ExtractedText;

            var prompt = $@"Analyze this document excerpt and extract metadata in JSON format:
{{
  ""title"": ""inferred document title"",
  ""language"": ""detected language code (en, ko, ja, etc.)"",
  ""contentType"": ""type of content (article, report, manual, etc.)"",
  ""topics"": [""main topics""]
}}

Document excerpt:
{sampleText}

JSON response:";

            var response = await _textCompletionService.CompleteAsync(prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = 500, Temperature = 0.3f }, cancellationToken);

            if (!string.IsNullOrWhiteSpace(response))
            {
                var startIdx = response.IndexOf('{');
                var endIdx = response.LastIndexOf('}');
                if (startIdx >= 0 && endIdx > startIdx)
                {
                    var json = response[startIdx..(endIdx + 1)];
                    var metadata = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                    if (metadata != null)
                    {
                        if (metadata.TryGetValue("title", out var title))
                            result.Metadata.Title = title.GetString();
                        // Only override language if not already set by FileFlux
                        if (string.IsNullOrEmpty(result.Metadata.DetectedLanguage) &&
                            metadata.TryGetValue("language", out var lang))
                            result.Metadata.DetectedLanguage = lang.GetString();
                        if (metadata.TryGetValue("contentType", out var contentType))
                            result.Metadata.ContentType = contentType.GetString();
                        if (metadata.TryGetValue("topics", out var topics))
                            result.Metadata.CustomMetadata["topics"] = topics.ToString();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogMetadataEnrichmentFailed(_logger, ex);
        }
    }

    private async Task SaveOutputFilesAsync(
        DocumentProcessingResult result,
        DocumentProcessingOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.OutputDirectory))
            return;

        Directory.CreateDirectory(options.OutputDirectory);

        // Save extracted text
        if (options.SaveExtractedText && !string.IsNullOrEmpty(result.ExtractedText))
        {
            var extractPath = Path.Combine(options.OutputDirectory, "extract.md");
            await File.WriteAllTextAsync(extractPath, result.ExtractedText, cancellationToken);
            LogSavedExtractedText(_logger, extractPath);
        }

        // Save cleaned text
        if (options.SaveCleanedText && !string.IsNullOrEmpty(result.CleanedText))
        {
            var cleanedPath = Path.Combine(options.OutputDirectory, "cleaned.md");
            await File.WriteAllTextAsync(cleanedPath, result.CleanedText, cancellationToken);
            LogSavedCleanedText(_logger, cleanedPath);
        }

        // Save images
        if (result.Images.Count != 0)
        {
            var imagesDir = Path.Combine(options.OutputDirectory, "images");
            Directory.CreateDirectory(imagesDir);

            foreach (var (name, data) in result.Images)
            {
                var imagePath = Path.Combine(imagesDir, name);
                await File.WriteAllBytesAsync(imagePath, data, cancellationToken);
            }
            LogSavedImages(_logger, result.Images.Count, imagesDir);
        }

        // Save metadata
        if (options.SaveMetadata)
        {
            var metadataPath = Path.Combine(options.OutputDirectory, "metadata.json");
            var metadataJson = JsonSerializer.Serialize(result.Metadata, s_camelCaseJsonOptions);
            await File.WriteAllTextAsync(metadataPath, metadataJson, cancellationToken);
            LogSavedMetadata(_logger, metadataPath);
        }

        // Save chunks
        if (options.SaveChunks && result.Chunks.Count != 0)
        {
            var chunksDir = Path.Combine(options.OutputDirectory, "chunks");
            Directory.CreateDirectory(chunksDir);

            foreach (var chunk in result.Chunks)
            {
                var chunkMdPath = Path.Combine(chunksDir, $"{chunk.Index:D3}.md");
                await File.WriteAllTextAsync(chunkMdPath, chunk.Content, cancellationToken);

                var chunkMetadata = new
                {
                    chunk.Id,
                    chunk.Index,
                    chunk.TokenCount,
                    chunk.CharacterCount,
                    chunk.StartPosition,
                    chunk.EndPosition,
                    chunk.ContextSummary,
                    HasEmbedding = chunk.Embedding != null,
                    EmbeddingDimension = chunk.Embedding?.Length ?? 0,
                    chunk.Metadata
                };

                var chunkJsonPath = Path.Combine(chunksDir, $"{chunk.Index:D3}.json");
                var chunkJson = JsonSerializer.Serialize(chunkMetadata, s_camelCaseJsonOptions);
                await File.WriteAllTextAsync(chunkJsonPath, chunkJson, cancellationToken);
            }
            LogSavedChunks(_logger, result.Chunks.Count, chunksDir);
        }

        // Save QA pairs
        if (options.SaveQAPairs && result.QAPairs.Count != 0)
        {
            var qaPath = Path.Combine(options.OutputDirectory, "qa_pairs.json");
            var qaJson = JsonSerializer.Serialize(result.QAPairs, s_camelCaseJsonOptions);
            await File.WriteAllTextAsync(qaPath, qaJson, cancellationToken);
            LogSavedQAPairs(_logger, result.QAPairs.Count, qaPath);
        }
    }

    private static void ReportProgress(DocumentProcessingOptions options, ProcessingStage stage, int percentage, string message)
    {
        options.OnProgress?.Invoke(new ProcessingProgress
        {
            Stage = stage,
            Percentage = percentage,
            Message = message
        });
    }

    private static void ReportContentProgress(ContentProcessingOptions options, ProcessingStage stage, int percentage, string message)
    {
        options.OnProgress?.Invoke(new ProcessingProgress
        {
            Stage = stage,
            Percentage = percentage,
            Message = message
        });
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split(WordSplitSeparators, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var cjkCount = text.Count(c => c >= 0x4E00 && c <= 0x9FFF);
        var otherCount = text.Length - cjkCount;

        return (cjkCount / 2) + (otherCount / 4);
    }

    private static string GetImageExtension(string? mimeType)
    {
        return mimeType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            _ => ".jpg"
        };
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "Image extraction failed, continuing without images")]
    private static partial void LogImageExtractionFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted {CharCount} characters, {WordCount} words from {FilePath}")]
    private static partial void LogExtractedContent(ILogger logger, int charCount, int wordCount, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created {ChunkCount} chunks")]
    private static partial void LogCreatedChunks(ILogger logger, int chunkCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Enriched {EnrichedCount}/{TotalCount} chunks with context")]
    private static partial void LogEnrichedChunks(ILogger logger, int enrichedCount, int totalCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated embeddings for {ChunkCount} chunks")]
    private static partial void LogGeneratedEmbeddings(ILogger logger, int chunkCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated {QACount} QA pairs from {ChunkCount} chunks")]
    private static partial void LogGeneratedQAPairs(ILogger logger, int qaCount, int chunkCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Document processing completed in {Duration}ms")]
    private static partial void LogDocumentProcessingCompleted(ILogger logger, double duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "Document processing failed for {FilePath}")]
    private static partial void LogDocumentProcessingFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted {CharCount} characters from {FilePath}")]
    private static partial void LogExtractedCharacters(ILogger logger, int charCount, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Converted to Markdown: {Headings} headings, {Tables} tables, {Lists} lists")]
    private static partial void LogConvertedToMarkdown(ILogger logger, int headings, int tables, int lists);

    [LoggerMessage(Level = LogLevel.Error, Message = "Extraction failed for {FilePath}")]
    private static partial void LogExtractionFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created {ChunkCount} chunks from content")]
    private static partial void LogCreatedChunksFromContent(ILogger logger, int chunkCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Content processing completed in {Duration}ms")]
    private static partial void LogContentProcessingCompleted(ILogger logger, double duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "Content processing failed")]
    private static partial void LogContentProcessingFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot process from failed extraction: {ErrorMessage}")]
    private static partial void LogCannotProcessFromFailedExtraction(ILogger logger, string? errorMessage);

    [LoggerMessage(Level = LogLevel.Information, Message = "PDF table quality: {Tables} tables detected, {LowConf} low-confidence, min score: {MinConf:F2}")]
    private static partial void LogPdfTableQuality(ILogger logger, int tables, int lowConf, double minConf);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PDF contains {Count} low-confidence tables (score < 0.5). Table content may be formatted as plain text for better readability.")]
    private static partial void LogPdfLowConfidenceTables(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PDF quality check failed for {FilePath}, falling back to standard extraction")]
    private static partial void LogPdfQualityCheckFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Image extraction not supported for format: {Extension}")]
    private static partial void LogImageExtractionNotSupported(ILogger logger, string extension);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Extracted {Count} images from {FilePath}")]
    private static partial void LogExtractedImages(ILogger logger, int count, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to extract images from {FilePath}")]
    private static partial void LogFailedToExtractImages(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Text cleaning failed, using original text")]
    private static partial void LogTextCleaningFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Metadata enrichment failed")]
    private static partial void LogMetadataEnrichmentFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Saved extracted text to {Path}")]
    private static partial void LogSavedExtractedText(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Saved cleaned text to {Path}")]
    private static partial void LogSavedCleanedText(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Saved {Count} images to {Path}")]
    private static partial void LogSavedImages(ILogger logger, int count, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Saved metadata to {Path}")]
    private static partial void LogSavedMetadata(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Saved {Count} chunks to {Path}")]
    private static partial void LogSavedChunks(ILogger logger, int count, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Saved {Count} QA pairs to {Path}")]
    private static partial void LogSavedQAPairs(ILogger logger, int count, string path);

    #endregion
}
