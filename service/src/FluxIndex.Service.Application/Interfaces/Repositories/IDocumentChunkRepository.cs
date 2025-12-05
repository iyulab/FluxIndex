using FluxIndex.Service.Domain.Entities;

namespace FluxIndex.Service.Application.Interfaces.Repositories;

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
    Task<int> GetCountAsync(Guid? documentId = null, CancellationToken cancellationToken = default);
}
