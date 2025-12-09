namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Interface for Qdrant vector search integration.
/// Provides high-performance vector similarity search using Qdrant backend.
/// </summary>
public interface IQdrantSearchService
{
    /// <summary>
    /// Check if Qdrant service is available.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Search for similar vectors in Qdrant.
    /// </summary>
    /// <param name="queryEmbedding">The query embedding vector</param>
    /// <param name="topK">Maximum number of results to return</param>
    /// <param name="collectionId">Optional collection ID to filter by</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of chunk IDs with their similarity scores</returns>
    Task<List<(Guid ChunkId, float Score)>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        Guid? collectionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search using multiple vectors (for ColBERT-style late interaction).
    /// </summary>
    /// <param name="queryEmbeddings">Multiple query token embeddings</param>
    /// <param name="topK">Maximum number of results</param>
    /// <param name="collectionId">Optional collection filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of chunk IDs with aggregated scores</returns>
    Task<List<(Guid ChunkId, float Score)>> SearchMultiVectorAsync(
        IReadOnlyList<float[]> queryEmbeddings,
        int topK,
        Guid? collectionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Index a chunk's embedding in Qdrant.
    /// </summary>
    /// <param name="chunkId">The chunk ID</param>
    /// <param name="documentId">The parent document ID</param>
    /// <param name="collectionId">The collection ID</param>
    /// <param name="embedding">The embedding vector</param>
    /// <param name="metadata">Optional metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task IndexAsync(
        Guid chunkId,
        Guid documentId,
        Guid collectionId,
        float[] embedding,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch index multiple chunks.
    /// </summary>
    /// <param name="chunks">Collection of chunks to index</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BatchIndexAsync(
        IReadOnlyList<QdrantIndexRequest> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a chunk from Qdrant index.
    /// </summary>
    /// <param name="chunkId">The chunk ID to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteAsync(Guid chunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get Qdrant health and statistics.
    /// </summary>
    Task<QdrantHealthInfo> GetHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Request model for batch indexing in Qdrant.
/// </summary>
public record QdrantIndexRequest(
    Guid ChunkId,
    Guid DocumentId,
    Guid CollectionId,
    float[] Embedding,
    Dictionary<string, object>? Metadata = null);

/// <summary>
/// Qdrant health and statistics information.
/// </summary>
public record QdrantHealthInfo(
    bool IsHealthy,
    long TotalVectors,
    long IndexedVectors,
    double AverageSearchTimeMs,
    string Version);
