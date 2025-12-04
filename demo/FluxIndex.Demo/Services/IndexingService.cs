using FileFlux;
using FileFlux.Core;
using FluxIndex.Core.Domain.Entities;
using IEmbeddingService = FluxIndex.Core.Application.Interfaces.IEmbeddingService;
using IVectorStore = FluxIndex.Core.Application.Interfaces.IVectorStore;
using FluxIndexDocumentChunk = FluxIndex.Core.Domain.Entities.DocumentChunk;
using FileFluxChunk = FileFlux.Core.DocumentChunk;

namespace FluxIndex.Demo.Services;

/// <summary>
/// Service for indexing uploaded files using FileFlux
/// </summary>
public class IndexingService
{
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly DemoState _state;
    private readonly ILogger<IndexingService> _logger;

    public IndexingService(
        IDocumentProcessor documentProcessor,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        DemoState state,
        ILogger<IndexingService> logger)
    {
        _documentProcessor = documentProcessor;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _state = state;
        _logger = logger;
    }

    public async Task<IndexingResult> IndexFileAsync(IFormFile file)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Save uploaded file to temp location
            var tempPath = Path.Combine(Path.GetTempPath(), $"fluxindex_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}");
            await using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            _logger.LogInformation("Processing file: {FileName}", file.FileName);

            // Process document with FileFlux
            var chunkingOptions = new ChunkingOptions
            {
                Strategy = ChunkingStrategies.Auto,
                MaxChunkSize = 1024,
                OverlapSize = 128
            };

            var fileFluxChunks = await _documentProcessor.ProcessAsync(tempPath, chunkingOptions);
            var chunkList = fileFluxChunks.ToList();

            _logger.LogInformation("Created {ChunkCount} chunks from {FileName}", chunkList.Count, file.FileName);

            // Create document ID
            var documentId = Guid.NewGuid().ToString();

            // Process each chunk
            var documentChunks = new List<FluxIndexDocumentChunk>();
            var chunkIndex = 0;

            foreach (var fileFluxChunk in chunkList)
            {
                // Generate embedding
                var embedding = await _embeddingService.GenerateEmbeddingAsync(fileFluxChunk.Content);

                // Create FluxIndex chunk
                var docChunk = new FluxIndexDocumentChunk(fileFluxChunk.Content, chunkIndex)
                {
                    DocumentId = documentId,
                    TokenCount = fileFluxChunk.Tokens,
                    Embedding = embedding,
                    Metadata = new Dictionary<string, object>
                    {
                        ["source"] = file.FileName,
                        ["contentType"] = "text",
                        ["quality"] = fileFluxChunk.Quality,
                        ["importance"] = fileFluxChunk.Importance
                    }
                };

                documentChunks.Add(docChunk);
                chunkIndex++;
            }

            // Store in vector store
            await _vectorStore.StoreBatchAsync(documentChunks);

            // Update state
            _state.AddDocument(documentId, file.FileName, documentChunks.Count);

            // Cleanup temp file
            try { File.Delete(tempPath); } catch { }

            stopwatch.Stop();

            return new IndexingResult
            {
                Success = true,
                DocumentId = documentId,
                FileName = file.FileName,
                ChunkCount = documentChunks.Count,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                Message = $"Successfully indexed {file.FileName} with {documentChunks.Count} chunks"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index file: {FileName}", file.FileName);
            stopwatch.Stop();

            return new IndexingResult
            {
                Success = false,
                FileName = file.FileName,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                Message = $"Failed to index: {ex.Message}"
            };
        }
    }
}

public record IndexingResult
{
    public bool Success { get; init; }
    public string? DocumentId { get; init; }
    public string FileName { get; init; } = "";
    public int ChunkCount { get; init; }
    public long ProcessingTimeMs { get; init; }
    public string Message { get; init; } = "";
}
