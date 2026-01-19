using System.Security.Cryptography;
using System.Text;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// Generates a consistent hash from a file path for use as a vault entry identifier.
/// Uses path normalization + SHA256 to produce a stable, unique directory name.
/// </summary>
public static class FilepathHasher
{
    /// <summary>
    /// Hash length for directory names (16 chars = 64 bits = collision-safe for typical file counts).
    /// </summary>
    private const int HashLength = 16;

    /// <summary>
    /// Computes a stable hash from a file path.
    /// The path is normalized before hashing to ensure consistency across platforms.
    /// </summary>
    /// <param name="filePath">The file path to hash.</param>
    /// <returns>A 16-character lowercase hex string.</returns>
    public static string ComputeHash(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var normalizedPath = NormalizePath(filePath);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));

        // Take first 8 bytes (16 hex chars) for reasonable collision resistance
        return Convert.ToHexString(hashBytes, 0, HashLength / 2).ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes a file path for consistent hashing.
    /// </summary>
    /// <param name="filePath">The file path to normalize.</param>
    /// <returns>Normalized path string.</returns>
    public static string NormalizePath(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        // Get full path to resolve relative paths
        var fullPath = Path.GetFullPath(filePath);

        // Normalize directory separators to forward slashes
        var normalized = fullPath.Replace('\\', '/');

        // Convert to lowercase for case-insensitive comparison (Windows/macOS compatibility)
        normalized = normalized.ToLowerInvariant();

        // Remove trailing slashes
        normalized = normalized.TrimEnd('/');

        return normalized;
    }

    /// <summary>
    /// Validates that a hash string is in the expected format.
    /// </summary>
    /// <param name="hash">The hash to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    public static bool IsValidHash(string? hash)
    {
        if (string.IsNullOrEmpty(hash) || hash.Length != HashLength)
            return false;

        return hash.All(c => char.IsAsciiHexDigitLower(c));
    }
}
