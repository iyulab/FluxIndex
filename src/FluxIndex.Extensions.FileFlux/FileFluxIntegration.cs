using FileFlux;
using FileFlux.Domain;
using FluxIndex.Domain.Entities;
using FluxIndex.SDK;
using Microsoft.Extensions.Logging;
using FluxIndexDocumentChunk = FluxIndex.Domain.Entities.DocumentChunk;
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
    /// Process a file with FileFlux and index with FluxIndex using FileFlux 0.2.12 API
    /// </summary>
    public async Task<string> ProcessAndIndexAsync(
        string filePath,
        ProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ProcessingOptions
        {
            ChunkingStrategy = _options.DefaultChunkingStrategy,
            MaxChunkSize = _options.DefaultMaxChunkSize,
            OverlapSize = _options.DefaultOverlapSize
        };

        _logger.LogInformation("Processing file with FileFlux: {FilePath}", filePath);

        try
        {
            var chunkingOptions = new ChunkingOptions
            {
                Strategy = options.ChunkingStrategy,
                MaxChunkSize = options.MaxChunkSize,
                OverlapSize = options.OverlapSize
            };

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
            document.Metadata["fileflux_version"] = "0.2.12";
            document.Metadata["strategy"] = options.ChunkingStrategy;

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

        return fluxChunk;
    }
}

/// <summary>
/// Processing options for FileFlux integration with FluxIndex
/// </summary>
public class ProcessingOptions
{
    /// <summary>
    /// Chunking strategy to use (Auto, Smart, Intelligent, Semantic, Paragraph, FixedSize)
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
}
