using System.Security.Cryptography;

namespace FluxIndex.Service.Domain.Entities;

/// <summary>
/// Represents an API key for authentication.
/// </summary>
public class ApiKey
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string KeyHash { get; private set; } = string.Empty;
    public string KeyPrefix { get; private set; } = string.Empty; // First 8 chars for identification
    public ApiKeyRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime? LastUsedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Rate limiting
    public int? RateLimitPerMinute { get; private set; }
    public int? RateLimitPerDay { get; private set; }

    private ApiKey() { } // EF Core

    /// <summary>
    /// Creates a new API key and returns both the entity and the raw key value.
    /// The raw key should be shown to the user only once.
    /// </summary>
    public static (ApiKey apiKey, string rawKey) Create(
        string name,
        ApiKeyRole role = ApiKeyRole.Reader,
        DateTime? expiresAt = null,
        int? rateLimitPerMinute = null,
        int? rateLimitPerDay = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Generate a secure random key
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var rawKey = $"fxi_{Convert.ToBase64String(keyBytes).Replace("+", "-").Replace("/", "_").TrimEnd('=')}";

        // Hash the key for storage
        var keyHash = HashKey(rawKey);

        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = name,
            KeyHash = keyHash,
            KeyPrefix = rawKey[..12], // "fxi_" + first 8 chars
            Role = role,
            IsActive = true,
            ExpiresAt = expiresAt,
            RateLimitPerMinute = rateLimitPerMinute,
            RateLimitPerDay = rateLimitPerDay,
            CreatedAt = DateTime.UtcNow
        };

        return (apiKey, rawKey);
    }

    public static string HashKey(string rawKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(rawKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool ValidateKey(string rawKey)
    {
        if (string.IsNullOrEmpty(rawKey)) return false;
        if (!IsActive) return false;
        if (ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow) return false;

        var hash = HashKey(rawKey);
        return hash == KeyHash;
    }

    public void RecordUsage()
    {
        LastUsedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void UpdateRateLimits(int? perMinute, int? perDay)
    {
        RateLimitPerMinute = perMinute;
        RateLimitPerDay = perDay;
    }
}

public enum ApiKeyRole
{
    /// <summary>
    /// Can search and read documents
    /// </summary>
    Reader,

    /// <summary>
    /// Can upload, modify, and delete documents
    /// </summary>
    Writer,

    /// <summary>
    /// Full access including settings and API key management
    /// </summary>
    Admin
}
