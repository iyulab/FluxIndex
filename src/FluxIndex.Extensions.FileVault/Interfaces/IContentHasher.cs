using FluxIndex.Extensions.FileVault.Domain.ValueObjects;

namespace FluxIndex.Extensions.FileVault.Interfaces;

/// <summary>
/// Computes content hashes for file change detection.
/// </summary>
public interface IContentHasher
{
    /// <summary>
    /// Computes a hash for the given file.
    /// </summary>
    Task<ContentHash> ComputeHashAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Computes a hash for the given stream.
    /// </summary>
    Task<ContentHash> ComputeHashAsync(Stream stream, CancellationToken ct = default);

    /// <summary>
    /// Computes a hash for the given bytes.
    /// </summary>
    ContentHash ComputeHash(byte[] data);

    /// <summary>
    /// Computes a hash for the given string content.
    /// </summary>
    ContentHash ComputeHash(string content);
}
