using FileFlux;
using FileFlux.Core;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.SDK;
using Microsoft.Extensions.Logging;
using FluxIndexDocumentChunk = FluxIndex.Core.Domain.Entities.DocumentChunk;
using FileFluxChunk = FileFlux.Core.DocumentChunk;

namespace FluxIndex.SDK.Extensions.FileFlux;

/// <summary>
/// FileFlux integration service for FluxIndex - bridges FileFlux document processing with FluxIndex indexing
/// Leverages FileFlux 0.10.x stateful document processor with 5-stage pipeline
/// </summary>
public partial class FileFluxIntegration
{
    private readonly IDocumentProcessorFactory _processorFactory;
    private readonly ILanguageProfileProvider _languageProfileProvider;
    private readonly Indexer _indexer;
    private readonly ILogger<FileFluxIntegration> _logger;
    private readonly FileFluxOptions _options;

    /// <summary>
    /// Current FileFlux version for metadata tracking
    /// </summary>
    private const string FileFluxVersion = "0.10.0";

    public FileFluxIntegration(
        IDocumentProcessorFactory processorFactory,
        ILanguageProfileProvider languageProfileProvider,
        Indexer indexer,
        ILogger<FileFluxIntegration> logger,
        Microsoft.Extensions.Options.IOptions<FileFluxOptions>? options = null)
    {
        _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
        _languageProfileProvider = languageProfileProvider ?? throw new ArgumentNullException(nameof(languageProfileProvider));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new FileFluxOptions();
    }

    /// <summary>
    /// Process a file with FileFlux and index with FluxIndex using FileFlux 0.10.x API
    /// </summary>
    public async Task<string> ProcessAndIndexAsync(
        string filePath,
        ProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Use streaming API if enabled
        if (_options.UseStreamingApi)
        {
            return await ProcessAndIndexStreamingAsync(filePath, options, null, cancellationToken);
        }

        options ??= new ProcessingOptions
        {
            ChunkingStrategy = _options.DefaultChunkingStrategy,
            MaxChunkSize = _options.DefaultMaxChunkSize,
            OverlapSize = _options.DefaultOverlapSize,
            Language = _options.DefaultLanguage,
            EnableMetadataEnrichment = _options.EnableMetadataEnrichment,
            MetadataSchema = _options.DefaultMetadataSchema
        };

        LogProcessingFile(_logger, filePath, options.Language ?? "auto");

        try
        {
            var chunkingOptions = new ChunkingOptions
            {
                Strategy = options.ChunkingStrategy,
                MaxChunkSize = options.MaxChunkSize,
                OverlapSize = options.OverlapSize
            };

            // Add custom properties for language and metadata enrichment
            ApplyCustomProperties(chunkingOptions, options);

            var fluxIndexChunks = new List<FluxIndexDocumentChunk>();
            var chunkIndex = 0;

            // Use FileFlux 0.10.x factory pattern to process document
            await using var processor = _processorFactory.Create(filePath);
            var processingOpts = new global::FileFlux.Core.ProcessingOptions { Chunking = chunkingOptions };
            await processor.ProcessAsync(processingOpts, cancellationToken);
            var fileFluxChunks = processor.Result?.Chunks;

            foreach (var fileFluxChunk in fileFluxChunks ?? [])
            {
                var fluxChunk = ConvertToFluxIndexChunk(fileFluxChunk, chunkIndex++, filePath);
                fluxIndexChunks.Add(fluxChunk);
            }

            if (fluxIndexChunks.Count == 0)
            {
                LogNoChunksGenerated(_logger, filePath);
                throw new InvalidOperationException($"No chunks generated from file: {filePath}");
            }

            // Create FluxIndex Document
            var documentId = Path.GetFileNameWithoutExtension(filePath);
            var document = Document.Create(documentId);
            document.Content = string.Join("\n", fluxIndexChunks.Select(c => c.Content));
            document.Chunks = fluxIndexChunks;

            // Set document metadata
            document.Metadata["source_file"] = filePath;
            document.Metadata["source_type"] = "file";
            document.Metadata["file_extension"] = Path.GetExtension(filePath);
            document.Metadata["processed_at"] = DateTime.UtcNow.ToString("O");
            document.Metadata["processor"] = "FileFlux";
            document.Metadata["fileflux_version"] = FileFluxVersion;
            document.Metadata["strategy"] = options.ChunkingStrategy;

            // Add language profile metadata (FileFlux 0.4.8)
            var effectiveLanguage = DetermineEffectiveLanguage(options.Language, document.Content);
            if (!string.IsNullOrEmpty(effectiveLanguage))
            {
                document.Metadata["language"] = effectiveLanguage;
                AddLanguageProfileMetadata(document.Metadata, effectiveLanguage);
            }

            // Index with FluxIndex
            var indexedDocumentId = await _indexer.IndexDocumentAsync(
                document: document,
                cancellationToken: cancellationToken);

            LogSuccessfullyProcessedAndIndexed(_logger, fluxIndexChunks.Count, indexedDocumentId);

            return indexedDocumentId;
        }
        catch (FileNotFoundException ex)
        {
            LogFileNotFound(_logger, ex, filePath);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            LogAccessDenied(_logger, ex, filePath);
            throw;
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorProcessingFile(_logger, ex, filePath);
            throw new InvalidOperationException($"Failed to process file: {filePath}", ex);
        }
    }

    /// <summary>
    /// Process a file with FileFlux streaming API and index with FluxIndex (memory-efficient for large files)
    /// </summary>
    public async Task<string> ProcessAndIndexStreamingAsync(
        string filePath,
        ProcessingOptions? options = null,
        IProgress<FileProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ProcessingOptions
        {
            ChunkingStrategy = _options.DefaultChunkingStrategy,
            MaxChunkSize = _options.DefaultMaxChunkSize,
            OverlapSize = _options.DefaultOverlapSize,
            Language = _options.DefaultLanguage,
            EnableMetadataEnrichment = _options.EnableMetadataEnrichment,
            MetadataSchema = _options.DefaultMetadataSchema
        };

        LogProcessingFileStreaming(_logger, filePath, options.Language ?? "auto");

        try
        {
            var chunkingOptions = new ChunkingOptions
            {
                Strategy = options.ChunkingStrategy,
                MaxChunkSize = options.MaxChunkSize,
                OverlapSize = options.OverlapSize
            };

            // Add custom properties for language and metadata enrichment
            ApplyCustomProperties(chunkingOptions, options);

            var documentId = Path.GetFileNameWithoutExtension(filePath);
            var fluxIndexChunks = new List<FluxIndexDocumentChunk>();
            var chunkIndex = 0;
            var totalChunks = 0;

            // Use FileFlux 0.10.x factory pattern with streaming API for memory-efficient processing
            await using var processor = _processorFactory.Create(filePath);
            var processingOpts = new global::FileFlux.Core.ProcessingOptions { Chunking = chunkingOptions };
            await foreach (var fileFluxChunk in processor.ProcessStreamAsync(processingOpts, cancellationToken))
            {
                var fluxChunk = ConvertToFluxIndexChunk(fileFluxChunk, chunkIndex++, filePath);
                fluxIndexChunks.Add(fluxChunk);
                totalChunks++;

                // Immediate indexing for ultra-large files (if enabled)
                if (_options.EnableImmediateIndexing && fluxIndexChunks.Count >= _options.ImmediateIndexingBatchSize)
                {
                    LogImmediateBatchIndexing(_logger, fluxIndexChunks.Count);
                    await BatchIndexChunksAsync(documentId, fluxIndexChunks, cancellationToken);
                    fluxIndexChunks.Clear();
                }

                // Report progress (estimate based on chunks processed)
                if (progress != null && totalChunks % 10 == 0)
                {
                    progress.Report(new FileProcessingProgress
                    {
                        FilePath = filePath,
                        PercentComplete = 0, // Cannot estimate for streaming
                        CurrentStep = "Processing",
                        TotalSteps = 1,
                        ChunksProcessed = totalChunks
                    });
                }
            }

            if (totalChunks == 0)
            {
                LogNoChunksGenerated(_logger, filePath);
                throw new InvalidOperationException($"No chunks generated from file: {filePath}");
            }

            // Create FluxIndex Document with remaining chunks
            if (fluxIndexChunks.Count != 0)
            {
                var document = Document.Create(documentId);
                document.Content = string.Join("\n", fluxIndexChunks.Select(c => c.Content));
                document.Chunks = fluxIndexChunks;

                // Set document metadata
                document.Metadata["source_file"] = filePath;
                document.Metadata["source_type"] = "file";
                document.Metadata["file_extension"] = Path.GetExtension(filePath);
                document.Metadata["processed_at"] = DateTime.UtcNow.ToString("O");
                document.Metadata["processor"] = "FileFlux";
                document.Metadata["fileflux_version"] = FileFluxVersion;
                document.Metadata["strategy"] = options.ChunkingStrategy;
                document.Metadata["streaming_mode"] = true;

                // Add language profile metadata (FileFlux 0.4.8)
                var effectiveLanguage = DetermineEffectiveLanguage(options.Language, document.Content);
                if (!string.IsNullOrEmpty(effectiveLanguage))
                {
                    document.Metadata["language"] = effectiveLanguage;
                    AddLanguageProfileMetadata(document.Metadata, effectiveLanguage);
                }

                // Index with FluxIndex
                var indexedDocumentId = await _indexer.IndexDocumentAsync(
                    document: document,
                    cancellationToken: cancellationToken);

                LogSuccessfullyProcessedAndIndexedStreaming(_logger, totalChunks, indexedDocumentId);

                return indexedDocumentId;
            }

            return documentId;
        }
        catch (FileNotFoundException ex)
        {
            LogFileNotFound(_logger, ex, filePath);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            LogAccessDenied(_logger, ex, filePath);
            throw;
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorProcessingFile(_logger, ex, filePath);
            throw new InvalidOperationException($"Failed to process file: {filePath}", ex);
        }
    }

    /// <summary>
    /// Batch index chunks for immediate indexing mode (ultra-large files)
    /// </summary>
    private async Task BatchIndexChunksAsync(
        string documentId,
        List<FluxIndexDocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        if (chunks.Count == 0) return;

        try
        {
            // Create partial document for batch
            var partialDocument = Document.Create($"{documentId}_partial_{Guid.NewGuid():N}");
            partialDocument.Content = string.Join("\n", chunks.Select(c => c.Content));
            partialDocument.Chunks = chunks;
            partialDocument.Metadata["parent_document_id"] = documentId;
            partialDocument.Metadata["is_partial"] = true;

            await _indexer.IndexDocumentAsync(partialDocument, cancellationToken);
            LogBatchIndexedChunks(_logger, chunks.Count, documentId);
        }
        catch (Exception ex)
        {
            LogFailedToBatchIndexChunks(_logger, ex, documentId);
            // Don't throw - continue processing
        }
    }

    /// <summary>
    /// Apply custom properties to FileFlux ChunkingOptions based on ProcessingOptions
    /// Leverages FileFlux 0.4.8 language profiles and metadata enrichment
    /// </summary>
    private static void ApplyCustomProperties(ChunkingOptions chunkingOptions, ProcessingOptions options)
    {
        // Language-aware chunking (FileFlux 0.4.8 - 11 language profiles)
        if (!string.IsNullOrEmpty(options.Language))
        {
            chunkingOptions.CustomProperties["language"] = options.Language;
        }
        else if (options.EnableLanguageAutoDetection)
        {
            // Signal FileFlux to use auto-detection via ILanguageProfileProvider
            chunkingOptions.CustomProperties["enableLanguageAutoDetection"] = true;
        }

        // Metadata enrichment settings
        if (options.EnableMetadataEnrichment)
        {
            chunkingOptions.CustomProperties["enableMetadataEnrichment"] = true;
            chunkingOptions.CustomProperties["metadataSchema"] = options.MetadataSchema;
        }
    }

    /// <summary>
    /// Determine effective language using explicit setting or auto-detection
    /// </summary>
    private string? DetermineEffectiveLanguage(string? explicitLanguage, string? contentSample)
    {
        // Use explicit language if provided
        if (!string.IsNullOrEmpty(explicitLanguage))
        {
            return explicitLanguage;
        }

        // Auto-detect if enabled and content is available
        if (_options.EnableLanguageAutoDetection && !string.IsNullOrEmpty(contentSample))
        {
            try
            {
                var detectedProfile = _languageProfileProvider.DetectAndGetProfile(contentSample);
                LogAutoDetectedLanguage(_logger, detectedProfile.LanguageCode, detectedProfile.ScriptCode);
                return detectedProfile.LanguageCode;
            }
            catch (Exception ex)
            {
                LogLanguageAutoDetectionFailed(_logger, ex);
                return _languageProfileProvider.DefaultProfile.LanguageCode;
            }
        }

        return null;
    }

    /// <summary>
    /// Add extended language profile metadata to document metadata (FileFlux 0.4.8)
    /// </summary>
    private void AddLanguageProfileMetadata(Dictionary<string, object> metadata, string languageCode)
    {
        if (!_options.IncludeLanguageProfileMetadata)
            return;

        try
        {
            var profile = _languageProfileProvider.GetProfile(languageCode);

            metadata["ff_language_name"] = profile.LanguageName;
            metadata["ff_script_code"] = profile.ScriptCode;
            metadata["ff_writing_direction"] = profile.WritingDirection.ToString();

            var writingDirection = profile.WritingDirection.ToString();
            LogAddedLanguageProfileMetadata(_logger, profile.LanguageName, profile.ScriptCode, writingDirection);
        }
        catch (Exception ex)
        {
            LogFailedToAddLanguageProfileMetadata(_logger, ex, languageCode);
        }
    }

    private static FluxIndexDocumentChunk ConvertToFluxIndexChunk(FileFluxChunk fileFluxChunk, int chunkIndex, string filePath)
    {
        var documentId = Path.GetFileNameWithoutExtension(filePath);
        var fluxChunk = new FluxIndexDocumentChunk(fileFluxChunk.Content, chunkIndex)
        {
            DocumentId = documentId,
            TokenCount = fileFluxChunk.Tokens
        };

        // Initialize metadata dictionary
        fluxChunk.Metadata ??= new Dictionary<string, object>();

        // ============================================================
        // Standard RAG metadata (no prefix) - for consumer applications
        // These fields enable source citation display in RAG responses
        // ============================================================
        fluxChunk.Metadata["documentId"] = documentId;
        fluxChunk.Metadata["fileName"] = Path.GetFileName(filePath);
        fluxChunk.Metadata["filePath"] = filePath;
        fluxChunk.Metadata["chunkIndex"] = chunkIndex;

        // Determine title from available sources (priority order)
        var title = fileFluxChunk.Metadata?.Title
                 ?? fileFluxChunk.SourceInfo?.Title
                 ?? Path.GetFileNameWithoutExtension(filePath);
        fluxChunk.Metadata["title"] = title;

        // Page number if available (for PDFs, Word docs)
        if (fileFluxChunk.Location?.StartPage.HasValue == true)
        {
            fluxChunk.Metadata["pageNumber"] = fileFluxChunk.Location.StartPage.Value;
        }

        // ============================================================
        // FileFlux internal metadata (ff_ prefix) - for debugging/analysis
        // ============================================================
        fluxChunk.Metadata["ff_chunk_id"] = fileFluxChunk.Id;
        fluxChunk.Metadata["ff_chunk_index"] = fileFluxChunk.Index;
        fluxChunk.Metadata["ff_quality_score"] = fileFluxChunk.Quality;
        fluxChunk.Metadata["ff_importance_score"] = fileFluxChunk.Importance;
        fluxChunk.Metadata["ff_density_score"] = fileFluxChunk.Density;
        fluxChunk.Metadata["ff_strategy"] = fileFluxChunk.Strategy;

        // Map position information
        if (fileFluxChunk.Location != null)
        {
            fluxChunk.Metadata["ff_start_char"] = fileFluxChunk.Location.StartChar;
            fluxChunk.Metadata["ff_end_char"] = fileFluxChunk.Location.EndChar;
            if (fileFluxChunk.Location.StartPage.HasValue)
                fluxChunk.Metadata["ff_start_page"] = fileFluxChunk.Location.StartPage.Value;
            if (fileFluxChunk.Location.EndPage.HasValue)
                fluxChunk.Metadata["ff_end_page"] = fileFluxChunk.Location.EndPage.Value;
            if (!string.IsNullOrEmpty(fileFluxChunk.Location.Section))
                fluxChunk.Metadata["ff_section"] = fileFluxChunk.Location.Section;
        }

        // Map document metadata from FileFlux
        if (fileFluxChunk.Metadata != null)
        {
            if (!string.IsNullOrEmpty(fileFluxChunk.Metadata.Title))
                fluxChunk.Metadata["ff_title"] = fileFluxChunk.Metadata.Title;
            if (!string.IsNullOrEmpty(fileFluxChunk.Metadata.Author))
                fluxChunk.Metadata["ff_author"] = fileFluxChunk.Metadata.Author;
            if (fileFluxChunk.Metadata.CreatedAt.HasValue)
                fluxChunk.Metadata["ff_created_date"] = fileFluxChunk.Metadata.CreatedAt.Value;
            if (!string.IsNullOrEmpty(fileFluxChunk.Metadata.Language))
                fluxChunk.Metadata["ff_language"] = fileFluxChunk.Metadata.Language;
        }

        // Preserve FileFlux custom properties
        if (fileFluxChunk.Props != null && fileFluxChunk.Props.Count > 0)
        {
            foreach (var prop in fileFluxChunk.Props)
            {
                fluxChunk.Metadata[$"ff_{prop.Key}"] = prop.Value;
            }
        }

        // Map SourceMetadataInfo (FileFlux 0.4.0+)
        if (fileFluxChunk.SourceInfo != null)
        {
            if (!string.IsNullOrEmpty(fileFluxChunk.SourceInfo.SourceId))
                fluxChunk.Metadata["ff_source_id"] = fileFluxChunk.SourceInfo.SourceId;
            if (!string.IsNullOrEmpty(fileFluxChunk.SourceInfo.SourceType))
                fluxChunk.Metadata["ff_source_type"] = fileFluxChunk.SourceInfo.SourceType;
            if (!string.IsNullOrEmpty(fileFluxChunk.SourceInfo.Title))
                fluxChunk.Metadata["ff_source_title"] = fileFluxChunk.SourceInfo.Title;
            if (!string.IsNullOrEmpty(fileFluxChunk.SourceInfo.Language))
                fluxChunk.Metadata["ff_detected_language"] = fileFluxChunk.SourceInfo.Language;
            if (fileFluxChunk.SourceInfo.LanguageConfidence > 0)
                fluxChunk.Metadata["ff_language_confidence"] = fileFluxChunk.SourceInfo.LanguageConfidence;
            if (fileFluxChunk.SourceInfo.WordCount > 0)
                fluxChunk.Metadata["ff_word_count"] = fileFluxChunk.SourceInfo.WordCount;
            if (fileFluxChunk.SourceInfo.ChunkCount > 0)
                fluxChunk.Metadata["ff_total_chunks"] = fileFluxChunk.SourceInfo.ChunkCount;
            if (fileFluxChunk.SourceInfo.PageCount.HasValue)
                fluxChunk.Metadata["ff_page_count"] = fileFluxChunk.SourceInfo.PageCount.Value;
        }

        // Map HeadingPath for hierarchical navigation (FileFlux 0.4.0+)
        if (fileFluxChunk.Location?.HeadingPath?.Count > 0)
        {
            fluxChunk.Metadata["ff_heading_path"] = string.Join(" > ", fileFluxChunk.Location.HeadingPath);
            fluxChunk.Metadata["ff_heading_depth"] = fileFluxChunk.Location.HeadingPath.Count;
        }

        // Map context dependency score
        if (fileFluxChunk.ContextDependency > 0)
        {
            fluxChunk.Metadata["ff_context_dependency"] = fileFluxChunk.ContextDependency;
        }

        return fluxChunk;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing file with FileFlux: {FilePath}, Language: {Language}")]
    private static partial void LogProcessingFile(ILogger logger, string filePath, string language);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No chunks generated from file: {FilePath}")]
    private static partial void LogNoChunksGenerated(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully processed and indexed {ChunkCount} chunks for document {DocumentId}")]
    private static partial void LogSuccessfullyProcessedAndIndexed(ILogger logger, int chunkCount, string documentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "File not found: {FilePath}")]
    private static partial void LogFileNotFound(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Access denied to file: {FilePath}")]
    private static partial void LogAccessDenied(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error processing file: {FilePath}")]
    private static partial void LogUnexpectedErrorProcessingFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing file with FileFlux streaming API: {FilePath}, Language: {Language}")]
    private static partial void LogProcessingFileStreaming(ILogger logger, string filePath, string language);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Immediate batch indexing: {ChunkCount} chunks")]
    private static partial void LogImmediateBatchIndexing(ILogger logger, int chunkCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully processed and indexed {ChunkCount} chunks for document {DocumentId} (streaming mode)")]
    private static partial void LogSuccessfullyProcessedAndIndexedStreaming(ILogger logger, int chunkCount, string documentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Batch indexed {ChunkCount} chunks for document {DocumentId}")]
    private static partial void LogBatchIndexedChunks(ILogger logger, int chunkCount, string documentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to batch index chunks for document {DocumentId}")]
    private static partial void LogFailedToBatchIndexChunks(ILogger logger, Exception exception, string documentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Auto-detected language: {Language} ({Script})")]
    private static partial void LogAutoDetectedLanguage(ILogger logger, string language, string script);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Language auto-detection failed, using default")]
    private static partial void LogLanguageAutoDetectionFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Added language profile metadata: {Language} ({Script}, {Direction})")]
    private static partial void LogAddedLanguageProfileMetadata(ILogger logger, string language, string script, string direction);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to add language profile metadata for {LanguageCode}")]
    private static partial void LogFailedToAddLanguageProfileMetadata(ILogger logger, Exception exception, string languageCode);

    #endregion
}

/// <summary>
/// Processing options for FileFlux integration with FluxIndex
/// </summary>
public class ProcessingOptions
{
    /// <summary>
    /// Chunking strategy to use (Auto, Smart, Intelligent, Semantic, Paragraph, FixedSize, Hierarchical, PageLevel)
    /// </summary>
    public string ChunkingStrategy { get; set; } = ChunkingStrategies.Auto;

    /// <summary>
    /// Maximum chunk size in tokens
    /// </summary>
    public int MaxChunkSize { get; set; } = 1024;

    /// <summary>
    /// Overlap size between chunks in tokens
    /// </summary>
    public int OverlapSize { get; set; } = 128;

    /// <summary>
    /// Language code for language-aware chunking (e.g., "ko", "en", "zh", "ja", "ar")
    /// FileFlux 0.4.8 supports 11 language profiles with comprehensive text segmentation
    /// Set to null or empty to enable automatic language detection
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Enable automatic language detection using Unicode script analysis (FileFlux 0.4.8)
    /// When enabled and Language is not set, FileFlux will auto-detect the document language
    /// </summary>
    public bool EnableLanguageAutoDetection { get; set; } = true;

    /// <summary>
    /// Enable metadata enrichment with AI-powered extraction
    /// </summary>
    public bool EnableMetadataEnrichment { get; set; }

    /// <summary>
    /// Metadata schema for enrichment (General, Academic, Technical, Legal, Medical)
    /// </summary>
    public string MetadataSchema { get; set; } = "General";
}

/// <summary>
/// Progress information for file processing
/// </summary>
public class FileProcessingProgress
{
    /// <summary>
    /// File path being processed
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Percentage complete (0-100)
    /// </summary>
    public double PercentComplete { get; init; }

    /// <summary>
    /// Current processing step
    /// </summary>
    public string? CurrentStep { get; init; }

    /// <summary>
    /// Total number of steps
    /// </summary>
    public int TotalSteps { get; init; }

    /// <summary>
    /// Number of chunks processed so far
    /// </summary>
    public int ChunksProcessed { get; init; }
}
