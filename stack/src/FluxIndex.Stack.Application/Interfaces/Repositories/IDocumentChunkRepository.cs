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
}
