using System.Security.Cryptography;
using FluxIndex.Stack.Vault.Interfaces;
using FluxIndex.Stack.Vault.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Stack.Vault.Services;

/// <summary>
/// Service for computing content hashes using SHA256.
/// </summary>
public class ContentHashService : IContentHashService
{
    private readonly ILogger<ContentHashService> _logger;
    private readonly VaultOptions _options;

    public ContentHashService(
        ILogger<ContentHashService> logger,
        IOptions<VaultOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920, // 80KB buffer for better performance
                useAsync: true);

            return await ComputeHashAsync(stream, cancellationToken);
        }
        catch (IOException ex) when (ex is not FileNotFoundException)
        {
            _logger.LogWarning(ex, "Failed to compute hash for locked file: {FilePath}", filePath);
            throw;
        }
    }

    public async Task<string> ComputeHashAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return $"sha256:{Convert.ToHexString(hashBytes).ToLowerInvariant()}";
    }

    public bool AreEqual(string hash1, string hash2)
    {
        if (string.IsNullOrEmpty(hash1) || string.IsNullOrEmpty(hash2))
            return false;

        return string.Equals(hash1, hash2, StringComparison.OrdinalIgnoreCase);
    }
}
