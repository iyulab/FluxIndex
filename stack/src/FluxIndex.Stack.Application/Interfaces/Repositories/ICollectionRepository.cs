using FluxIndex.Stack.Domain.Entities;

namespace FluxIndex.Stack.Application.Interfaces.Repositories;

/// <summary>
/// Repository interface for Collection entity.
/// </summary>
public interface ICollectionRepository
{
    Task<Collection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Collection?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<List<Collection>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<(List<Collection> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Collection> AddAsync(Collection collection, CancellationToken cancellationToken = default);
    Task UpdateAsync(Collection collection, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> GetDocumentCountAsync(Guid collectionId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);
}
