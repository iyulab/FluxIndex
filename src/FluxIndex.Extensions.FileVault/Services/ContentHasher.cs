using System.Security.Cryptography;
using System.Text;
using FluxIndex.Extensions.FileVault.Domain.ValueObjects;
using FluxIndex.Extensions.FileVault.Interfaces;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// SHA256-based content hasher for file change detection.
/// </summary>
public sealed class ContentHasher : IContentHasher
{
    /// <inheritdoc />
    public async Task<ContentHash> ComputeHashAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        return await ComputeHashAsync(stream, ct);
    }

    /// <inheritdoc />
    public async Task<ContentHash> ComputeHashAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var hashBytes = await SHA256.HashDataAsync(stream, ct);
        return ContentHash.FromBytes(hashBytes);
    }

    /// <inheritdoc />
    public ContentHash ComputeHash(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var hashBytes = SHA256.HashData(data);
        return ContentHash.FromBytes(hashBytes);
    }

    /// <inheritdoc />
    public ContentHash ComputeHash(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var contentBytes = Encoding.UTF8.GetBytes(content);
        return ComputeHash(contentBytes);
    }
}
