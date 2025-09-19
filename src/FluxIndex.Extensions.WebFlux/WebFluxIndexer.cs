using FluxIndex.Application.Interfaces;
using FluxIndex.Domain.Entities;
using FluxIndex.Extensions.WebFlux.Models;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Extensions.WebFlux;

/// <summary>
/// WebFlux-powered indexer for FluxIndex
/// Extends the base indexer with web crawling capabilities
/// </summary>
public class WebFluxIndexer
{
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IDocumentRepository _documentRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<WebFluxIndexer> _logger;

    public WebFluxIndexer(
        IDocumentProcessor documentProcessor,
        IDocumentRepository documentRepository,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ILogger<WebFluxIndexer> logger)
    {
        _documentProcessor = documentProcessor ?? throw new ArgumentNullException(nameof(documentProcessor));
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Index a website by URL
    /// </summary>
    public async Task<string> IndexWebsiteAsync(
        string url,
        CrawlOptions? crawlOptions = null,
        ChunkingOptions? chunkingOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        _logger.LogInformation("Starting website indexing for: {Url}", url);

        try
        {
            // Process URL with WebFlux
            var document = await _documentProcessor.ProcessUrlAsync(url, crawlOptions, chunkingOptions, cancellationToken);

            // Store document
            await _documentRepository.AddAsync(document, cancellationToken);

            // Generate embeddings and store in vector database
            await ProcessDocumentChunks(document, cancellationToken);

            _logger.LogInformation("Successfully indexed website: {Url}, DocumentId: {DocumentId}", url, document.Id);

            return document.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index website: {Url}", url);
            throw;
        }
    }

    /// <summary>
    /// Index multiple websites concurrently
    /// </summary>
    public async Task<IEnumerable<string>> IndexWebsitesAsync(
        IEnumerable<string> urls,
        CrawlOptions? crawlOptions = null,
        ChunkingOptions? chunkingOptions = null,
        int maxConcurrency = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(urls);

        _logger.LogInformation("Starting bulk website indexing for {Count} URLs", urls.Count());

        // Process URLs concurrently
        var documents = await _documentProcessor.ProcessUrlsAsync(urls, crawlOptions, chunkingOptions, maxConcurrency, cancellationToken);

        var documentIds = new List<string>();

        foreach (var document in documents)
        {
            try
            {
                // Store document
                await _documentRepository.AddAsync(document, cancellationToken);

                // Generate embeddings and store in vector database
                await ProcessDocumentChunks(document, cancellationToken);

                documentIds.Add(document.Id);

                _logger.LogInformation("Successfully indexed document: {DocumentId}", document.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to index document: {DocumentId}", document.Id);
                // Continue with other documents
            }
        }

        _logger.LogInformation("Completed bulk indexing. Successfully indexed {Count}/{Total} websites",
            documentIds.Count, documents.Count());

        return documentIds;
    }

    /// <summary>
    /// Index website with progress reporting
    /// </summary>
    public async IAsyncEnumerable<WebIndexingProgress> IndexWebsiteWithProgressAsync(
        string url,
        CrawlOptions? crawlOptions = null,
        ChunkingOptions? chunkingOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        _logger.LogInformation("Starting website indexing with progress for: {Url}", url);

        var progress = new WebIndexingProgress
        {
            Url = url,
            Status = IndexingStatus.Starting,
            Message = "Initializing web crawling...",
            StartTime = DateTime.UtcNow
        };

        yield return progress;

        // Crawling phase
        progress.Status = IndexingStatus.Crawling;
        progress.Message = "Crawling and processing web content...";
        yield return progress;

        Document? document = null;
        Exception? crawlError = null;
        try
        {
            document = await _documentProcessor.ProcessUrlAsync(url, crawlOptions, chunkingOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            crawlError = ex;
            _logger.LogError(ex, "Failed to crawl website: {Url}", url);
        }

        if (crawlError != null)
        {
            progress.Status = IndexingStatus.Failed;
            progress.Message = $"Crawling failed: {crawlError.Message}";
            progress.Error = crawlError;
            progress.EndTime = DateTime.UtcNow;
            progress.Duration = progress.EndTime - progress.StartTime;
            yield return progress;
            yield break;
        }

        // Storing phase
        progress.Status = IndexingStatus.Storing;
        progress.Message = "Storing document...";
        progress.ChunksProcessed = document!.Chunks.Count;
        yield return progress;

        Exception? storeError = null;
        try
        {
            await _documentRepository.AddAsync(document, cancellationToken);
        }
        catch (Exception ex)
        {
            storeError = ex;
            _logger.LogError(ex, "Failed to store document: {DocumentId}", document.Id);
        }

        if (storeError != null)
        {
            progress.Status = IndexingStatus.Failed;
            progress.Message = $"Storage failed: {storeError.Message}";
            progress.Error = storeError;
            progress.EndTime = DateTime.UtcNow;
            progress.Duration = progress.EndTime - progress.StartTime;
            yield return progress;
            yield break;
        }

        // Embedding phase
        progress.Status = IndexingStatus.Embedding;
        progress.Message = "Generating embeddings...";
        yield return progress;

        Exception? embeddingError = null;
        try
        {
            await ProcessDocumentChunks(document, cancellationToken);
        }
        catch (Exception ex)
        {
            embeddingError = ex;
            _logger.LogError(ex, "Failed to process embeddings: {DocumentId}", document.Id);
        }

        if (embeddingError != null)
        {
            progress.Status = IndexingStatus.Failed;
            progress.Message = $"Embedding failed: {embeddingError.Message}";
            progress.Error = embeddingError;
            progress.EndTime = DateTime.UtcNow;
            progress.Duration = progress.EndTime - progress.StartTime;
            yield return progress;
            yield break;
        }

        // Completion
        progress.Status = IndexingStatus.Completed;
        progress.Message = "Indexing completed successfully";
        progress.DocumentId = document.Id;
        progress.EndTime = DateTime.UtcNow;
        progress.Duration = progress.EndTime - progress.StartTime;

        yield return progress;

        _logger.LogInformation("Successfully indexed website: {Url}, DocumentId: {DocumentId}", url, document.Id);
    }

    private async Task ProcessDocumentChunks(Document document, CancellationToken cancellationToken)
    {
        foreach (var chunk in document.Chunks)
        {
            try
            {
                // Generate embedding for chunk
                var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk.Content, cancellationToken);

                // Set embedding on chunk and store in vector database
                chunk.SetEmbedding(embedding);
                await _vectorStore.StoreAsync(chunk, cancellationToken);

                _logger.LogDebug("Stored chunk {ChunkId} in vector database", chunk.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process chunk {ChunkId}", chunk.Id);
                // Continue with other chunks
            }
        }
    }
}

/// <summary>
/// Web indexing progress information
/// </summary>
public class WebIndexingProgress
{
    public string Url { get; set; } = string.Empty;
    public IndexingStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? DocumentId { get; set; }
    public int ChunksProcessed { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration { get; set; }
    public Exception? Error { get; set; }
}

/// <summary>
/// Indexing status enumeration
/// </summary>
public enum IndexingStatus
{
    Starting,
    Crawling,
    Storing,
    Embedding,
    Completed,
    Failed
}