namespace FluxIndex.Stack.Vault.Interfaces;

/// <summary>
/// Service for computing content hashes for change detection.
/// </summary>
public interface IContentHashService
{
    /// <summary>
    /// Computes the hash of a file's content.
    /// </summary>
    Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the hash of a stream's content.
    /// </summary>
    Task<string> ComputeHashAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares two hashes for equality.
    /// </summary>
    bool AreEqual(string hash1, string hash2);
}
