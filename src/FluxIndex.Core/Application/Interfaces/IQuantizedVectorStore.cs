using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Extended vector store interface supporting quantized vector storage and search.
/// Provides optimized search operations using compressed vector representations.
/// </summary>
public interface IQuantizedVectorStore : IVectorStore
{
    /// <summary>
    /// Store a document chunk with both original and quantized embeddings.
    /// </summary>
    /// <param name="chunk">The document chunk to store</param>
    /// <param name="quantizedEmbedding">Pre-computed quantized embedding</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The ID of the stored chunk</returns>
    Task<string> StoreWithQuantizedAsync(
        DocumentChunk chunk,
        QuantizedVector quantizedEmbedding,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Store multiple document chunks with quantized embeddings in batch.
    /// </summary>
    /// <param name="chunksWithQuantized">Chunks paired with their quantized embeddings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The IDs of the stored chunks</returns>
    Task<IEnumerable<string>> StoreBatchWithQuantizedAsync(
        IEnumerable<(DocumentChunk Chunk, QuantizedVector QuantizedEmbedding)> chunksWithQuantized,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search using quantized vectors for faster approximate search.
    /// Uses quantized distance computation without dequantization.
    /// </summary>
    /// <param name="queryQuantized">Quantized query vector</param>
    /// <param name="topK">Number of results to return</param>
    /// <param name="minScore">Minimum similarity score threshold</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Matching document chunks with similarity scores</returns>
    Task<IEnumerable<(DocumentChunk Chunk, float Score)>> SearchQuantizedAsync(
        QuantizedVector queryQuantized,
        int topK = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search using quantized vectors for initial filtering, then rerank with original vectors.
    /// Provides better accuracy than pure quantized search with improved performance.
    /// </summary>
    /// <param name="queryEmbedding">Original query embedding for reranking</param>
    /// <param name="queryQuantized">Quantized query vector for initial search</param>
    /// <param name="topK">Number of final results to return</param>
    /// <param name="candidateMultiplier">Multiplier for candidate pool size (default 3x)</param>
    /// <param name="minScore">Minimum similarity score threshold</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Matching document chunks with accurate similarity scores</returns>
    Task<IEnumerable<(DocumentChunk Chunk, float Score)>> SearchWithRerankAsync(
        float[] queryEmbedding,
        QuantizedVector queryQuantized,
        int topK = 10,
        int candidateMultiplier = 3,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the quantized embedding for a specific chunk.
    /// </summary>
    /// <param name="chunkId">The chunk ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The quantized embedding or null if not found</returns>
    Task<QuantizedVector?> GetQuantizedEmbeddingAsync(
        string chunkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the store has quantized embeddings for a chunk.
    /// </summary>
    /// <param name="chunkId">The chunk ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if quantized embedding exists</returns>
    Task<bool> HasQuantizedEmbeddingAsync(
        string chunkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the quantized embedding for an existing chunk.
    /// </summary>
    /// <param name="chunkId">The chunk ID</param>
    /// <param name="quantizedEmbedding">New quantized embedding</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if update was successful</returns>
    Task<bool> UpdateQuantizedEmbeddingAsync(
        string chunkId,
        QuantizedVector quantizedEmbedding,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get statistics about quantized storage.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Storage statistics</returns>
    Task<QuantizedStorageStats> GetQuantizedStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The quantizer used by this store.
    /// </summary>
    IVectorQuantizer? Quantizer { get; }

    /// <summary>
    /// Whether this store supports quantized operations.
    /// </summary>
    bool SupportsQuantization { get; }
}

/// <summary>
/// Statistics about quantized vector storage.
/// </summary>
public class QuantizedStorageStats
{
    /// <summary>
    /// Total number of chunks with quantized embeddings.
    /// </summary>
    public int QuantizedChunkCount { get; init; }

    /// <summary>
    /// Total number of chunks without quantized embeddings.
    /// </summary>
    public int UnquantizedChunkCount { get; init; }

    /// <summary>
    /// Total storage size of quantized embeddings in bytes.
    /// </summary>
    public long QuantizedStorageSizeBytes { get; init; }

    /// <summary>
    /// Estimated storage size if all embeddings were stored as float32.
    /// </summary>
    public long EstimatedOriginalSizeBytes { get; init; }

    /// <summary>
    /// Compression ratio achieved (original/compressed).
    /// </summary>
    public float CompressionRatio => EstimatedOriginalSizeBytes > 0
        ? (float)EstimatedOriginalSizeBytes / QuantizedStorageSizeBytes
        : 1.0f;

    /// <summary>
    /// Quantization type used.
    /// </summary>
    public QuantizationType? QuantizationType { get; init; }
}
