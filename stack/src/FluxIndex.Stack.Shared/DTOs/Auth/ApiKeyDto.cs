namespace FluxIndex.Stack.Shared.DTOs.Auth;

/// <summary>
/// API key information DTO (excludes sensitive data).
/// </summary>
public class ApiKeyDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string KeyPrefix { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime? LastUsedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public int? RateLimitPerMinute { get; init; }
    public int? RateLimitPerDay { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Request to create a new API key.
/// </summary>
public class CreateApiKeyRequest
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = "Reader";
    public DateTime? ExpiresAt { get; set; }
    public int? RateLimitPerMinute { get; set; }
    public int? RateLimitPerDay { get; set; }
}

/// <summary>
/// Response after creating an API key.
/// Contains the raw key which should only be shown once.
/// </summary>
public class CreateApiKeyResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string RawKey { get; init; } = string.Empty;
    public string KeyPrefix { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public DateTime? ExpiresAt { get; init; }
    public string Message { get; init; } = "Store this key securely. It will not be shown again.";
}

/// <summary>
/// Request to update an API key.
/// </summary>
public class UpdateApiKeyRequest
{
    public string? Name { get; init; }
    public int? RateLimitPerMinute { get; init; }
    public int? RateLimitPerDay { get; init; }
}
