using FluxIndex.Stack.Domain.Entities;

namespace FluxIndex.Stack.Application.Interfaces.Repositories;

/// <summary>
/// Repository interface for DocumentChunk entity.
/// </summary>
public interface IDocumentChunkRepository
{
    Task<DocumentChunk?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<DocumentChunk>> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<List<DocumentChunk>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task AddAsync(DocumentChunk chunk, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default);
    Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid chunkId, CancellationToken cancellationToken = default);
    Task UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default);
    Task<int> GetCountAsync(Guid? documentId = null, CancellationToken cancellationToken = default);
    Task<(List<DocumentChunk> Items, int TotalCount)> GetPagedAsync(
        int page = 1,
        int pageSize = 20,
        Guid? documentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs vector similarity search using pgvector cosine distance.
    /// </summary>
    /// <param name="queryEmbedding">The query embedding vector.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="documentIds">Optional filter by document IDs.</param>
    /// <param name="minScore">Minimum similarity score (0-1).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of chunks with similarity scores, ordered by score descending.</returns>
    Task<List<(DocumentChunk Chunk, double Score)>> SearchByVectorAsync(
        float[] queryEmbedding,
        int limit = 10,
        IEnumerable<Guid>? documentIds = null,
        double minScore = 0.0,
        CancellationToken cancellationToken = default);
}
