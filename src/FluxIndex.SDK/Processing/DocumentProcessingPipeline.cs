using System.Text.Json;
using FileFlux;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Microsoft.Extensions.Logging;
using IFluxIndexEmbeddingService = FluxIndex.Core.Application.Interfaces.IEmbeddingService;
using IFluxIndexTextCompletionService = FluxIndex.Core.Application.Interfaces.ITextCompletionService;
using IFluxIndexContextualEnrichmentService = FluxIndex.Core.Application.Interfaces.IContextualEnrichmentService;
using IFluxIndexQAGenerationService = FluxIndex.Core.Application.Interfaces.IQAGenerationService;

namespace FluxIndex.SDK.Processing;

/// <summary>
/// Document processing pipeline for extracting, chunking, enriching, and embedding documents.
///
/// Pipeline order (optimized for RAG quality):
/// 1. Extract - Text extraction from document
/// 2. Images - Extract images (parallel with cleaning)
/// 3. Clean - Preprocess text (noise removal, OCR fixes) BEFORE chunking
/// 4. Chunk - Split into semantic chunks
/// 5. ContextualEnrich - Add document context to each chunk (Anthropic Contextual Retrieval)
/// 6. Embed - Generate embeddings (uses contextualized text if enrichment was performed)
/// 7. Metadata - Enrich document metadata
/// 8. QAGenerate - Generate QA pairs for evaluation (optional)
/// 9. Save - Output files
/// </summary>
public class DocumentProcessingPipeline
{
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IFluxIndexEmbeddingService? _embeddingService;
    private readonly IFluxIndexTextCompletionService? _textCompletionService;
    private readonly IFluxIndexContextualEnrichmentService? _contextualEnrichmentService;
    private readonly IFluxIndexQAGenerationService? _qaGenerationService;
    private readonly ILogger<DocumentProcessingPipeline> _logger;

    public DocumentProcessingPipeline(
        IDocumentProcessor documentProcessor,
        IFluxIndexEmbeddingService? embeddingService,
        IFluxIndexTextCompletionService? textCompletionService,
        IFluxIndexContextualEnrichmentService? contextualEnrichmentService,
        IFluxIndexQAGenerationService? qaGenerationService,
        ILogger<DocumentProcessingPipeline> logger)
    {
        _documentProcessor = documentProcessor ?? throw new ArgumentNullException(nameof(documentProcessor));
        _embeddingService = embeddingService;
        _textCompletionService = textCompletionService;
        _contextualEnrichmentService = contextualEnrichmentService;
        _qaGenerationService = qaGenerationService;
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

            // Stage 1: Extract text
            ReportProgress(options, ProcessingStage.Extracting, 5, "Extracting text from document...");
            var extractStart = DateTime.UtcNow;

            var rawText = await ExtractRawTextAsync(filePath, cancellationToken);
            result.ExtractedText = rawText;
            result.Metadata.CharacterCount = rawText.Length;
            result.Metadata.WordCount = CountWords(rawText);
            result.Stats.ExtractionTime = DateTime.UtcNow - extractStart;

            _logger.LogInformation("Extracted {CharCount} characters, {WordCount} words from {FilePath}",
                result.Metadata.CharacterCount, result.Metadata.WordCount, filePath);

            // Stage 2: Extract images (if enabled)
            if (options.ExtractImages)
            {
                ReportProgress(options, ProcessingStage.ExtractingImages, 10, "Extracting images...");
                result.Images = await ExtractImagesAsync(filePath, cancellationToken);
                result.Metadata.ImageCount = result.Images.Count;
                result.Stats.TotalImages = result.Images.Count;
            }

            // Stage 3: Clean/preprocess text BEFORE chunking (if enabled)
            var textForChunking = rawText;
            if (options.EnableTextCleaning && _textCompletionService != null)
            {
                ReportProgress(options, ProcessingStage.Cleaning, 15, "Cleaning text...");
                var cleanStart = DateTime.UtcNow;
                result.CleanedText = await CleanTextAsync(rawText, cancellationToken);
                textForChunking = result.CleanedText;
                result.Stats.CleaningTime = DateTime.UtcNow - cleanStart;
            }

            // Stage 4: Chunk document
            ReportProgress(options, ProcessingStage.Chunking, 25, "Creating chunks...");
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

            // Use cleaned text for chunking if available
            var fileFluxChunks = await ChunkTextAsync(textForChunking, filePath, chunkingOptions, cancellationToken);
            var chunkList = fileFluxChunks.ToList();

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

            _logger.LogInformation("Created {ChunkCount} chunks", result.Chunks.Count);

            // Stage 5: Contextual Enrichment BEFORE embedding (if enabled)
            if (options.EnableContextualEnrichment && _contextualEnrichmentService != null)
            {
                ReportProgress(options, ProcessingStage.ContextualEnrichment, 40, "Adding contextual enrichment...");
                var enrichStart = DateTime.UtcNow;

                var fullText = result.CleanedText ?? result.ExtractedText;
                var chunkContents = result.Chunks.Select(c => c.Content).ToList();

                var contextSummaries = await _contextualEnrichmentService.GenerateContextBatchAsync(
                    chunkContents, fullText, cancellationToken);

                for (int i = 0; i < result.Chunks.Count && i < contextSummaries.Count; i++)
                {
                    result.Chunks[i].ContextSummary = contextSummaries[i];
                }

                result.Stats.ContextualEnrichmentTime = DateTime.UtcNow - enrichStart;
                result.Stats.EnrichedChunks = contextSummaries.Count(s => !string.IsNullOrEmpty(s));

                _logger.LogInformation("Enriched {EnrichedCount}/{TotalCount} chunks with context",
                    result.Stats.EnrichedChunks, result.Chunks.Count);
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
                _logger.LogInformation("Generated embeddings for {ChunkCount} chunks", result.Chunks.Count);
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

                _logger.LogInformation("Generated {QACount} QA pairs from {ChunkCount} chunks",
                    result.QAPairs.Count, result.Chunks.Count);
            }

            // Stage 9: Save output files
            ReportProgress(options, ProcessingStage.SavingOutput, 92, "Saving output files...");
            await SaveOutputFilesAsync(result, options, cancellationToken);

            result.Stats.EndTime = DateTime.UtcNow;
            result.Success = true;

            ReportProgress(options, ProcessingStage.Complete, 100, "Processing complete!");
            _logger.LogInformation("Document processing completed in {Duration}ms", result.Stats.Duration.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document processing failed for {FilePath}", filePath);

            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Stats.EndTime = DateTime.UtcNow;

            ReportProgress(options, ProcessingStage.Failed, 0, $"Processing failed: {ex.Message}");
            return result;
        }
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
        // Use FileFlux for extraction - it handles various document types
        var chunks = await _documentProcessor.ProcessAsync(filePath, new ChunkingOptions
        {
            Strategy = "FullDocument", // Get full text first
            MaxChunkSize = int.MaxValue
        }, cancellationToken);

        return string.Join("\n\n", chunks.Select(c => c.Content));
    }

    private async Task<IEnumerable<FileFlux.Core.DocumentChunk>> ChunkTextAsync(
        string text,
        string filePath,
        ChunkingOptions options,
        CancellationToken cancellationToken)
    {
        // For cleaned text, we need to re-chunk
        // If text is same as original, use FileFlux directly
        return await _documentProcessor.ProcessAsync(filePath, options, cancellationToken);
    }

    private async Task<Dictionary<string, byte[]>> ExtractImagesAsync(string filePath, CancellationToken cancellationToken)
    {
        var images = new Dictionary<string, byte[]>();

        try
        {
            await using var stream = File.OpenRead(filePath);
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            if (extension is ".html" or ".htm")
            {
                var reader = new HtmlDocumentReader();
                var rawContent = await reader.ExtractAsync(stream, Path.GetFileName(filePath), cancellationToken);

                if (rawContent.Images != null)
                {
                    int imageIndex = 0;
                    foreach (var image in rawContent.Images.Where(i => i.Data != null))
                    {
                        var imageName = $"img_{imageIndex:D3}";
                        var imageExtension = GetImageExtension(image.MimeType);
                        images[$"{imageName}{imageExtension}"] = image.Data!;
                        imageIndex++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract images from {FilePath}", filePath);
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

            var cleaned = await _textCompletionService.GenerateCompletionAsync(prompt, 4000, 0.1f, cancellationToken);
            return string.IsNullOrWhiteSpace(cleaned) ? text : cleaned;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Text cleaning failed, using original text");
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

            var response = await _textCompletionService.GenerateCompletionAsync(prompt, 500, 0.3f, cancellationToken);

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
                        if (metadata.TryGetValue("language", out var lang))
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
            _logger.LogWarning(ex, "Metadata enrichment failed");
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
            _logger.LogDebug("Saved extracted text to {Path}", extractPath);
        }

        // Save cleaned text
        if (options.SaveCleanedText && !string.IsNullOrEmpty(result.CleanedText))
        {
            var cleanedPath = Path.Combine(options.OutputDirectory, "cleaned.md");
            await File.WriteAllTextAsync(cleanedPath, result.CleanedText, cancellationToken);
            _logger.LogDebug("Saved cleaned text to {Path}", cleanedPath);
        }

        // Save images
        if (result.Images.Any())
        {
            var imagesDir = Path.Combine(options.OutputDirectory, "images");
            Directory.CreateDirectory(imagesDir);

            foreach (var (name, data) in result.Images)
            {
                var imagePath = Path.Combine(imagesDir, name);
                await File.WriteAllBytesAsync(imagePath, data, cancellationToken);
            }
            _logger.LogDebug("Saved {Count} images to {Path}", result.Images.Count, imagesDir);
        }

        // Save metadata
        if (options.SaveMetadata)
        {
            var metadataPath = Path.Combine(options.OutputDirectory, "metadata.json");
            var metadataJson = JsonSerializer.Serialize(result.Metadata, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await File.WriteAllTextAsync(metadataPath, metadataJson, cancellationToken);
            _logger.LogDebug("Saved metadata to {Path}", metadataPath);
        }

        // Save chunks
        if (options.SaveChunks && result.Chunks.Any())
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
                var chunkJson = JsonSerializer.Serialize(chunkMetadata, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                await File.WriteAllTextAsync(chunkJsonPath, chunkJson, cancellationToken);
            }
            _logger.LogDebug("Saved {Count} chunks to {Path}", result.Chunks.Count, chunksDir);
        }

        // Save QA pairs
        if (options.SaveQAPairs && result.QAPairs.Any())
        {
            var qaPath = Path.Combine(options.OutputDirectory, "qa_pairs.json");
            var qaJson = JsonSerializer.Serialize(result.QAPairs, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await File.WriteAllTextAsync(qaPath, qaJson, cancellationToken);
            _logger.LogDebug("Saved {Count} QA pairs to {Path}", result.QAPairs.Count, qaPath);
        }
    }

    private void ReportProgress(DocumentProcessingOptions options, ProcessingStage stage, int percentage, string message)
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
        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
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
}
