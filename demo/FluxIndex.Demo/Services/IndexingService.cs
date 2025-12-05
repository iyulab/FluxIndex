using FileFlux;
using FileFlux.Core;
using FluxIndex.Core.Application.Interfaces;
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
    private readonly IImageExtractionService? _imageExtractionService;
    private readonly IImageStore? _imageStore;
    private readonly IExtractedImageRepository? _imageRepository;

    public IndexingService(
        IDocumentProcessor documentProcessor,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        DemoState state,
        ILogger<IndexingService> logger,
        IImageExtractionService? imageExtractionService = null,
        IImageStore? imageStore = null,
        IExtractedImageRepository? imageRepository = null)
    {
        _documentProcessor = documentProcessor;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _state = state;
        _logger = logger;
        _imageExtractionService = imageExtractionService;
        _imageStore = imageStore;
        _imageRepository = imageRepository;
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
            // Use Token strategy for proper chunking with embedding size limits
            var chunkingOptions = new ChunkingOptions
            {
                Strategy = ChunkingStrategies.Token,
                MaxChunkSize = 500,  // Token limit per chunk (safe for embeddings)
                OverlapSize = 50
            };

            var fileFluxChunks = await _documentProcessor.ProcessAsync(tempPath, chunkingOptions);
            var chunkList = fileFluxChunks.ToList();

            _logger.LogInformation("Created {ChunkCount} chunks from {FileName}", chunkList.Count, file.FileName);

            // Create document ID
            var documentId = Guid.NewGuid().ToString();

            // Process each chunk
            var documentChunks = new List<FluxIndexDocumentChunk>();
            var chunkIndex = 0;
            var totalImagesExtracted = 0;

            foreach (var fileFluxChunk in chunkList)
            {
                var content = fileFluxChunk.Content;
                var hasImages = false;

                // Debug: Log a snippet of the content to see what FileFlux outputs
                _logger.LogDebug(
                    "Processing chunk {Index}, content length: {Length}, first 200 chars: {Content}",
                    chunkIndex,
                    content.Length,
                    content.Length > 200 ? content[..200] + "..." : content);

                // Extract images if services are available
                if (_imageExtractionService != null && _imageStore != null)
                {
                    if (_imageExtractionService.HasEmbeddedImages(content))
                    {
                        var extractionResult = await _imageExtractionService.ExtractAndStoreAsync(
                            documentId,
                            content,
                            _imageStore,
                            placeholderFormat: "[Image: {id}]");

                        if (extractionResult.HasImages)
                        {
                            content = extractionResult.ProcessedContent;
                            hasImages = true;
                            totalImagesExtracted += extractionResult.ImageCount;

                            _logger.LogDebug(
                                "Extracted {Count} images from chunk {Index}",
                                extractionResult.ImageCount, chunkIndex);

                            // Store image metadata if repository available
                            if (_imageRepository != null)
                            {
                                // Set chunk ID for each image
                                foreach (var img in extractionResult.StoredImages)
                                {
                                    img.ChunkId = $"{documentId}_chunk_{chunkIndex}";
                                }
                                await _imageRepository.AddRangeAsync(extractionResult.StoredImages);
                            }
                        }
                    }
                }

                // Generate embedding for cleaned content
                var embedding = await _embeddingService.GenerateEmbeddingAsync(content);

                // Create FluxIndex chunk
                var docChunk = new FluxIndexDocumentChunk(content, chunkIndex)
                {
                    DocumentId = documentId,
                    TokenCount = fileFluxChunk.Tokens,
                    Embedding = embedding,
                    Metadata = new Dictionary<string, object>
                    {
                        ["source"] = file.FileName,
                        ["contentType"] = hasImages ? "text_with_images" : "text",
                        ["quality"] = fileFluxChunk.Quality,
                        ["importance"] = fileFluxChunk.Importance,
                        ["hasExtractedImages"] = hasImages
                    }
                };

                documentChunks.Add(docChunk);
                chunkIndex++;
            }

            if (totalImagesExtracted > 0)
            {
                _logger.LogInformation(
                    "Extracted {Count} total images from {FileName}",
                    totalImagesExtracted, file.FileName);
            }

            // Store in vector store
            await _vectorStore.StoreBatchAsync(documentChunks);

            // Update state
            _state.AddDocument(documentId, file.FileName, documentChunks.Count);

            // Cleanup temp file
            try { File.Delete(tempPath); } catch { }

            stopwatch.Stop();

            var imageMessage = totalImagesExtracted > 0
                ? $" ({totalImagesExtracted} images extracted)"
                : "";

            return new IndexingResult
            {
                Success = true,
                DocumentId = documentId,
                FileName = file.FileName,
                ChunkCount = documentChunks.Count,
                ExtractedImageCount = totalImagesExtracted,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                Message = $"Successfully indexed {file.FileName} with {documentChunks.Count} chunks{imageMessage}"
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
    public int ExtractedImageCount { get; init; }
    public long ProcessingTimeMs { get; init; }
    public string Message { get; init; } = "";
}
