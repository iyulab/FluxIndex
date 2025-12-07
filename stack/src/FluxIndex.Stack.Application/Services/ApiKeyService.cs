using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Application.Mappings;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Auth;

namespace FluxIndex.Stack.Application.Services;

/// <summary>
/// Service implementation for API key management.
/// </summary>
public class ApiKeyService : IApiKeyService
{
    private readonly IApiKeyRepository _apiKeyRepository;

    public ApiKeyService(IApiKeyRepository apiKeyRepository)
    {
        _apiKeyRepository = apiKeyRepository;
    }

    public async Task<ApiKeyDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var apiKey = await _apiKeyRepository.GetByIdAsync(id, cancellationToken);
        return apiKey?.ToDto();
    }

    public async Task<PagedResult<ApiKeyDto>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _apiKeyRepository.GetPagedAsync(page, pageSize, cancellationToken);
        var dtos = items.Select(k => k.ToDto()).ToList();
        return PagedResult<ApiKeyDto>.Create(dtos, page, pageSize, totalCount);
    }

    public async Task<CreateApiKeyResponse> CreateAsync(CreateApiKeyRequest request, CancellationToken cancellationToken = default)
    {
        if (await _apiKeyRepository.NameExistsAsync(request.Name, cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException($"API key with name '{request.Name}' already exists.");
        }

        var role = ApiKeyMappings.ParseRole(request.Role);
        var (apiKey, rawKey) = ApiKey.Create(
            request.Name,
            role,
            request.ExpiresAt,
            request.RateLimitPerMinute,
            request.RateLimitPerDay);

        await _apiKeyRepository.AddAsync(apiKey, cancellationToken);

        return new CreateApiKeyResponse
        {
            Id = apiKey.Id,
            Name = apiKey.Name,
            RawKey = rawKey,
            KeyPrefix = apiKey.KeyPrefix,
            Role = apiKey.Role.ToString(),
            ExpiresAt = apiKey.ExpiresAt
        };
    }

    public async Task<ApiKeyDto> UpdateAsync(Guid id, UpdateApiKeyRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = await _apiKeyRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"API key with id '{id}' not found.");

        if (request.Name != null)
        {
            if (await _apiKeyRepository.NameExistsAsync(request.Name, id, cancellationToken))
            {
                throw new InvalidOperationException($"API key with name '{request.Name}' already exists.");
            }
        }

        if (request.RateLimitPerMinute.HasValue || request.RateLimitPerDay.HasValue)
        {
            apiKey.UpdateRateLimits(
                request.RateLimitPerMinute ?? apiKey.RateLimitPerMinute,
                request.RateLimitPerDay ?? apiKey.RateLimitPerDay);
        }

        await _apiKeyRepository.UpdateAsync(apiKey, cancellationToken);
        return apiKey.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await _apiKeyRepository.ExistsAsync(id, cancellationToken))
        {
            throw new KeyNotFoundException($"API key with id '{id}' not found.");
        }

        await _apiKeyRepository.DeleteAsync(id, cancellationToken);
    }

    public async Task ActivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var apiKey = await _apiKeyRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"API key with id '{id}' not found.");

        apiKey.Activate();
        await _apiKeyRepository.UpdateAsync(apiKey, cancellationToken);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var apiKey = await _apiKeyRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"API key with id '{id}' not found.");

        apiKey.Deactivate();
        await _apiKeyRepository.UpdateAsync(apiKey, cancellationToken);
    }

    public async Task<ApiKeyDto?> ValidateAsync(string rawKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(rawKey)) return null;

        var keyHash = ApiKey.HashKey(rawKey);
        var apiKey = await _apiKeyRepository.GetByKeyHashAsync(keyHash, cancellationToken);

        if (apiKey == null) return null;
        if (!apiKey.ValidateKey(rawKey)) return null;

        apiKey.RecordUsage();
        await _apiKeyRepository.UpdateAsync(apiKey, cancellationToken);

        return apiKey.ToDto();
    }
}
