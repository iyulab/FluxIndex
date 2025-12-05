using FluxIndex.Service.Shared.Common;
using FluxIndex.Service.Shared.DTOs.Auth;

namespace FluxIndex.Service.Application.Interfaces.Services;

/// <summary>
/// Service interface for API key management.
/// </summary>
public interface IApiKeyService
{
    Task<ApiKeyDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<ApiKeyDto>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<CreateApiKeyResponse> CreateAsync(CreateApiKeyRequest request, CancellationToken cancellationToken = default);
    Task<ApiKeyDto> UpdateAsync(Guid id, UpdateApiKeyRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task ActivateAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiKeyDto?> ValidateAsync(string rawKey, CancellationToken cancellationToken = default);
}
