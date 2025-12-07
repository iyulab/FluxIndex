using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Shared.DTOs.Auth;

namespace FluxIndex.Stack.Application.Mappings;

/// <summary>
/// Extension methods for mapping ApiKey entities to DTOs.
/// </summary>
public static class ApiKeyMappings
{
    public static ApiKeyDto ToDto(this ApiKey entity)
    {
        return new ApiKeyDto
        {
            Id = entity.Id,
            Name = entity.Name,
            KeyPrefix = entity.KeyPrefix,
            Role = entity.Role.ToString(),
            IsActive = entity.IsActive,
            LastUsedAt = entity.LastUsedAt,
            ExpiresAt = entity.ExpiresAt,
            RateLimitPerMinute = entity.RateLimitPerMinute,
            RateLimitPerDay = entity.RateLimitPerDay,
            CreatedAt = entity.CreatedAt
        };
    }

    public static ApiKeyRole ParseRole(string role)
    {
        return Enum.TryParse<ApiKeyRole>(role, true, out var result)
            ? result
            : ApiKeyRole.Reader;
    }
}
