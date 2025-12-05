using FluxIndex.Service.Shared.Common;
using FluxIndex.Service.Shared.DTOs.Collections;

namespace FluxIndex.Service.Application.Interfaces.Services;

/// <summary>
/// Service interface for collection operations.
/// </summary>
public interface ICollectionService
{
    Task<CollectionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CollectionDto?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<List<CollectionDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<CollectionDto>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<CollectionDto> CreateAsync(CreateCollectionRequest request, CancellationToken cancellationToken = default);
    Task<CollectionDto> UpdateAsync(Guid id, UpdateCollectionRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
