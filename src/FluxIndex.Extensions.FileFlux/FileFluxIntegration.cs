using FileFlux;
using FluxIndex.Domain.Entities;
using FluxIndex.SDK;
using Microsoft.Extensions.Logging;
using FluxIndexDocumentChunk = FluxIndex.Domain.Entities.DocumentChunk;
using FileFluxDocumentChunk = FileFlux.Domain.DocumentChunk;

namespace FluxIndex.Extensions.FileFlux;

/// <summary>
/// FileFlux integration service for FluxIndex - bridges FileFlux document processing with FluxIndex indexing
/// </summary>
public class FileFluxIntegration
{
    private readonly IDocumentProcessor _fileFlux;
    private readonly Indexer _indexer;
    private readonly ILogger<FileFluxIntegration> _logger;

    public FileFluxIntegration(
        IDocumentProcessor fileFlux,
        Indexer indexer,
        ILogger<FileFluxIntegration> logger)
    {
        _fileFlux = fileFlux ?? throw new ArgumentNullException(nameof(fileFlux));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Process a file with FileFlux and index with FluxIndex using the latest FileFlux API
    /// </summary>
    public async Task<string> ProcessAndIndexAsync(
        string filePath,
        ProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ProcessingOptions();

        _logger.LogInformation("Processing file with FileFlux: {FilePath}", filePath);

        try
        {
            var chunkingOptions = new ChunkingOptions
            {
                Strategy = options.ChunkingStrategy,
                MaxChunkSize = options.MaxChunkSize,
                OverlapSize = options.OverlapSize
            };

            // FileFlux document processing using our implementation
            var fluxIndexChunks = new List<FluxIndexDocumentChunk>();
            var chunkIndex = 0;

            var fileFluxChunks = _fileFlux.ProcessAsync(filePath, chunkingOptions);
            await foreach (var fileFluxChunk in fileFluxChunks)
            {
                // Convert to FluxIndex DocumentChunk
                var fluxChunk = new FluxIndexDocumentChunk(fileFluxChunk.Content, chunkIndex++)
                {
                    DocumentId = Path.GetFileNameWithoutExtension(filePath)
                };

                // Metadata mapping
                fluxChunk.Metadata ??= new Dictionary<string, object>();
                fluxChunk.Metadata["StartPosition"] = fileFluxChunk.StartPosition;
                fluxChunk.Metadata["EndPosition"] = fileFluxChunk.EndPosition;
                fluxChunk.Metadata["CharacterCount"] = fileFluxChunk.Content.Length;
                fluxChunk.Metadata["OriginalChunkIndex"] = fileFluxChunk.ChunkIndex;

                // Preserve FileFlux metadata
                if (fileFluxChunk.Metadata != null)
                {
                    foreach (var prop in fileFluxChunk.Metadata)
                    {
                        fluxChunk.Metadata[$"fileflux_{prop.Key}"] = prop.Value;
                    }
                }

                fluxIndexChunks.Add(fluxChunk);
            }

            if (!fluxIndexChunks.Any())
            {
                _logger.LogWarning("No chunks generated from file: {FilePath}", filePath);
                throw new InvalidOperationException($"No chunks generated from file: {filePath}");
            }

            // FluxIndex Document 생성
            var documentId = Path.GetFileNameWithoutExtension(filePath);
            var document = Document.Create(documentId);
            document.Content = string.Join("\n", fluxIndexChunks.Select(c => c.Content));
            document.Chunks = fluxIndexChunks;

            // Document 메타데이터 설정
            document.Metadata["source_file"] = filePath;
            document.Metadata["source_type"] = "file";
            document.Metadata["file_extension"] = Path.GetExtension(filePath);
            document.Metadata["processed_at"] = DateTime.UtcNow.ToString("O");
            document.Metadata["processor"] = "FileFlux";
            document.Metadata["strategy"] = options.ChunkingStrategy;

            // FluxIndex에 인덱싱
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
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "File format not supported: {FilePath}", filePath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing file: {FilePath}", filePath);
            throw new InvalidOperationException($"Failed to process file: {filePath}", ex);
        }
    }
}

/// <summary>
/// Processing options for FileFlux integration with FluxIndex
/// </summary>
public class ProcessingOptions
{
    /// <summary>
    /// Chunking strategy to use (Auto, Smart, MemoryOptimizedIntelligent, Intelligent, Semantic, Paragraph, FixedSize)
    /// </summary>
    public string ChunkingStrategy { get; set; } = "Auto";

    /// <summary>
    /// Maximum chunk size in characters
    /// </summary>
    public int MaxChunkSize { get; set; } = 512;

    /// <summary>
    /// Overlap size between chunks in characters
    /// </summary>
    public int OverlapSize { get; set; } = 64;

    /// <summary>
    /// Whether to preserve document structure during chunking
    /// </summary>
    public bool PreserveStructure { get; set; } = true;
}