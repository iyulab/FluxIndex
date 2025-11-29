using FileFlux;
using FileFlux.Domain;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.SDK;
using Microsoft.Extensions.Logging;
using FluxIndexDocumentChunk = FluxIndex.Core.Domain.Entities.DocumentChunk;
using FileFluxChunk = FileFlux.Domain.DocumentChunk;

namespace FluxIndex.Extensions.FileFlux;

/// <summary>
/// FileFlux integration service for FluxIndex - bridges FileFlux document processing with FluxIndex indexing
/// </summary>
public class FileFluxIntegration
{
    private readonly IDocumentProcessor _fileFluxProcessor;
    private readonly Indexer _indexer;
    private readonly ILogger<FileFluxIntegration> _logger;
    private readonly FileFluxOptions _options;

    public FileFluxIntegration(
        IDocumentProcessor fileFluxProcessor,
        Indexer indexer,
        ILogger<FileFluxIntegration> logger,
        Microsoft.Extensions.Options.IOptions<FileFluxOptions>? options = null)
    {
        _fileFluxProcessor = fileFluxProcessor ?? throw new ArgumentNullException(nameof(fileFluxProcessor));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new FileFluxOptions();
    }

    /// <summary>
    /// Process a file with FileFlux and index with FluxIndex using FileFlux 0.4.x API
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

        _logger.LogInformation("Processing file with FileFlux: {FilePath}, Language: {Language}",
            filePath, options.Language ?? "auto");

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

            // Use FileFlux API to process document (returns DocumentChunk[])
            var fileFluxChunks = await _fileFluxProcessor.ProcessAsync(filePath, chunkingOptions, cancellationToken);

            foreach (var fileFluxChunk in fileFluxChunks)
            {
                var fluxChunk = ConvertToFluxIndexChunk(fileFluxChunk, chunkIndex++, filePath);
                fluxIndexChunks.Add(fluxChunk);
            }

            if (!fluxIndexChunks.Any())
            {
                _logger.LogWarning("No chunks generated from file: {FilePath}", filePath);
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
            document.Metadata["fileflux_version"] = "0.4.7";
            document.Metadata["strategy"] = options.ChunkingStrategy;
            if (!string.IsNullOrEmpty(options.Language))
                document.Metadata["language"] = options.Language;

            // Index with FluxIndex
            var indexedDocumentId = await _indexer.IndexDocumentAsync(
                document: document,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Successfully processed and indexed {ChunkCount} chunks for document {DocumentId}",
                fluxIndexChunks.Count, indexedDocumentId);

            return indexedDocumentId;
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "File not found: {FilePath}", filePath);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied to file: {FilePath}", filePath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing file: {FilePath}", filePath);
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

        _logger.LogInformation("Processing file with FileFlux streaming API: {FilePath}, Language: {Language}",
            filePath, options.Language ?? "auto");

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

            // Use FileFlux streaming API for memory-efficient processing (returns IAsyncEnumerable<DocumentChunk>)
            await foreach (var fileFluxChunk in _fileFluxProcessor.ProcessStreamAsync(filePath, chunkingOptions, cancellationToken))
            {
                var fluxChunk = ConvertToFluxIndexChunk(fileFluxChunk, chunkIndex++, filePath);
                fluxIndexChunks.Add(fluxChunk);
                totalChunks++;

                // Immediate indexing for ultra-large files (if enabled)
                if (_options.EnableImmediateIndexing && fluxIndexChunks.Count >= _options.ImmediateIndexingBatchSize)
                {
                    _logger.LogDebug("Immediate batch indexing: {ChunkCount} chunks", fluxIndexChunks.Count);
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
                _logger.LogWarning("No chunks generated from file: {FilePath}", filePath);
                throw new InvalidOperationException($"No chunks generated from file: {filePath}");
            }

            // Create FluxIndex Document with remaining chunks
            if (fluxIndexChunks.Any())
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
                document.Metadata["fileflux_version"] = "0.4.7";
                document.Metadata["strategy"] = options.ChunkingStrategy;
                document.Metadata["streaming_mode"] = true;
                if (!string.IsNullOrEmpty(options.Language))
                    document.Metadata["language"] = options.Language;

                // Index with FluxIndex
                var indexedDocumentId = await _indexer.IndexDocumentAsync(
                    document: document,
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Successfully processed and indexed {ChunkCount} chunks for document {DocumentId} (streaming mode)",
                    totalChunks, indexedDocumentId);

                return indexedDocumentId;
            }

            return documentId;
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "File not found: {FilePath}", filePath);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied to file: {FilePath}", filePath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing file: {FilePath}", filePath);
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
        if (!chunks.Any()) return;

        try
        {
            // Create partial document for batch
            var partialDocument = Document.Create($"{documentId}_partial_{Guid.NewGuid():N}");
            partialDocument.Content = string.Join("\n", chunks.Select(c => c.Content));
            partialDocument.Chunks = chunks;
            partialDocument.Metadata["parent_document_id"] = documentId;
            partialDocument.Metadata["is_partial"] = true;

            await _indexer.IndexDocumentAsync(partialDocument, cancellationToken);
            _logger.LogDebug("Batch indexed {ChunkCount} chunks for document {DocumentId}", chunks.Count, documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch index chunks for document {DocumentId}", documentId);
            // Don't throw - continue processing
        }
    }

    /// <summary>
    /// Apply custom properties to FileFlux ChunkingOptions based on ProcessingOptions
    /// Leverages FileFlux 0.4.x language profiles and metadata enrichment
    /// </summary>
    private void ApplyCustomProperties(ChunkingOptions chunkingOptions, ProcessingOptions options)
    {
        // Language-aware chunking (FileFlux 0.4.x - 11 language profiles)
        if (!string.IsNullOrEmpty(options.Language))
        {
            chunkingOptions.CustomProperties["language"] = options.Language;
        }

        // Metadata enrichment settings
        if (options.EnableMetadataEnrichment)
        {
            chunkingOptions.CustomProperties["enableMetadataEnrichment"] = true;
            chunkingOptions.CustomProperties["metadataSchema"] = options.MetadataSchema;
        }
    }

    private FluxIndexDocumentChunk ConvertToFluxIndexChunk(FileFluxChunk fileFluxChunk, int chunkIndex, string filePath)
    {
        var fluxChunk = new FluxIndexDocumentChunk(fileFluxChunk.Content, chunkIndex)
        {
            DocumentId = Path.GetFileNameWithoutExtension(filePath),
            TokenCount = fileFluxChunk.Tokens
        };

        // Map FileFlux chunk metadata to FluxIndex
        fluxChunk.Metadata ??= new Dictionary<string, object>();
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
    /// FileFlux 0.4.x supports language-aware processing with sentence boundary detection
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Enable metadata enrichment with AI-powered extraction
    /// </summary>
    public bool EnableMetadataEnrichment { get; set; } = false;

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
