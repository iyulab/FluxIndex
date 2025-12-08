using FluxIndex.Stack.Domain.Entities;

namespace FluxIndex.Stack.Application.Interfaces.Repositories;

/// <summary>
/// Repository interface for ChunkEmbedding entity.
/// Provides model-aware embedding storage and retrieval.
/// </summary>
public interface IChunkEmbeddingRepository
{
    Task<ChunkEmbedding?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the embedding for a specific chunk and model.
    /// </summary>
    Task<ChunkEmbedding?> GetByChunkAndModelAsync(
        Guid chunkId,
        Guid embeddingModelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all embeddings for a chunk.
    /// </summary>
    Task<List<ChunkEmbedding>> GetByChunkIdAsync(
        Guid chunkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all embeddings for a specific model.
    /// </summary>
    Task<List<ChunkEmbedding>> GetByModelIdAsync(
        Guid embeddingModelId,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets embeddings for multiple chunks with a specific model.
    /// </summary>
    Task<List<ChunkEmbedding>> GetByChunkIdsAndModelAsync(
        IEnumerable<Guid> chunkIds,
        Guid embeddingModelId,
        CancellationToken cancellationToken = default);

    Task AddAsync(ChunkEmbedding embedding, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<ChunkEmbedding> embeddings, CancellationToken cancellationToken = default);

    Task UpdateAsync(ChunkEmbedding embedding, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all embeddings for a specific chunk.
    /// </summary>
    Task DeleteByChunkIdAsync(Guid chunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all embeddings for a specific model.
    /// </summary>
    Task DeleteByModelIdAsync(Guid embeddingModelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes embeddings for a chunk with a specific model.
    /// </summary>
    Task DeleteByChunkAndModelAsync(
        Guid chunkId,
        Guid embeddingModelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs vector similarity search for a specific embedding model.
    /// </summary>
    Task<List<(ChunkEmbedding Embedding, double Score)>> SearchByVectorAsync(
        float[] queryEmbedding,
        Guid embeddingModelId,
        int limit = 10,
        IEnumerable<Guid>? documentIds = null,
        double minScore = 0.0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets count of embeddings for a specific model.
    /// </summary>
    Task<int> GetCountByModelAsync(Guid embeddingModelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets chunks that don't have embeddings for the specified model.
    /// </summary>
    Task<List<Guid>> GetChunkIdsWithoutEmbeddingAsync(
        Guid embeddingModelId,
        IEnumerable<Guid>? documentIds = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a chunk has an embedding for the specified model.
    /// </summary>
    Task<bool> ExistsAsync(
        Guid chunkId,
        Guid embeddingModelId,
        CancellationToken cancellationToken = default);
}
