using FluxIndex.Service.Domain.Entities;

namespace FluxIndex.Service.Application.Interfaces.Repositories;

/// <summary>
/// Repository interface for ApiKey entity.
/// </summary>
public interface IApiKeyRepository
{
    Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiKey?> GetByKeyHashAsync(string keyHash, CancellationToken cancellationToken = default);
    Task<ApiKey?> GetByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
    Task<List<ApiKey>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<(List<ApiKey> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiKey> AddAsync(ApiKey apiKey, CancellationToken cancellationToken = default);
    Task UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);
}
