using FluxIndex.Service.Domain.Entities;

namespace FluxIndex.Service.Application.Interfaces.Repositories;

/// <summary>
/// Repository interface for Document entity.
/// </summary>
public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Document?> GetByIdWithChunksAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Document>> GetByCollectionIdAsync(Guid collectionId, CancellationToken cancellationToken = default);
    Task<(List<Document> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        Guid? collectionId = null,
        DocumentStatus? status = null,
        CancellationToken cancellationToken = default);
    Task<Document> AddAsync(Document document, CancellationToken cancellationToken = default);
    Task UpdateAsync(Document document, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ContentHashExistsAsync(string contentHash, CancellationToken cancellationToken = default);
    Task<int> GetCountAsync(Guid? collectionId = null, DocumentStatus? status = null, CancellationToken cancellationToken = default);
    Task<long> GetTotalFileSizeAsync(Guid? collectionId = null, CancellationToken cancellationToken = default);
}
