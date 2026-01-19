using FluxIndex.Extensions.FileVault.Domain.Entities;

namespace FluxIndex.Extensions.FileVault.Interfaces;

/// <summary>
/// Pipeline service for processing vault entries.
/// Simplified stages: Source → Extracted → Memorized
/// </summary>
public interface IVaultPipeline
{
    /// <summary>
    /// Full memorize pipeline: extract → chunk → embed → commit.
    /// Used for new files or when source content has changed.
    /// </summary>
    Task<MemorizeResult> MemorizeAsync(VaultEntry entry, MemorizeOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Refresh pipeline: chunk → embed → commit (skips extraction).
    /// Used when only vault/ files have been edited (append-text.md, qa.md).
    /// </summary>
    Task<MemorizeResult> RefreshAsync(VaultEntry entry, MemorizeOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Extract content from source file to vault/refined.md.
    /// </summary>
    Task ExtractAsync(VaultEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Removes chunks from vector store for the given entry.
    /// </summary>
    Task RemoveAsync(VaultEntry entry, CancellationToken ct = default);
}

/// <summary>
/// Options for memorize/refresh operations.
/// </summary>
public sealed class MemorizeOptions
{
    /// <summary>
    /// Maximum chunk size in tokens.
    /// </summary>
    public int MaxChunkSize { get; set; } = 1024;

    /// <summary>
    /// Overlap size between chunks in tokens.
    /// </summary>
    public int OverlapSize { get; set; } = 128;

    /// <summary>
    /// Chunking strategy (e.g., "Auto", "Semantic", "Paragraph").
    /// </summary>
    public string Strategy { get; set; } = "Auto";

    /// <summary>
    /// Language code for language-aware chunking.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Commit message for git.
    /// </summary>
    public string? CommitMessage { get; set; }

    /// <summary>
    /// Skip git commit after operation.
    /// </summary>
    public bool SkipCommit { get; set; }
}

/// <summary>
/// Result of a memorize/refresh operation.
/// </summary>
public sealed class MemorizeResult
{
    /// <summary>
    /// Number of chunks created and indexed.
    /// </summary>
    public int ChunkCount { get; init; }

    /// <summary>
    /// Total content length in characters.
    /// </summary>
    public int ContentLength { get; init; }

    /// <summary>
    /// Processing duration.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Git commit hash (if committed).
    /// </summary>
    public string? CommitHash { get; init; }

    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    public static MemorizeResult Succeeded(int chunkCount, int contentLength, TimeSpan duration, string? commitHash = null) => new()
    {
        Success = true,
        ChunkCount = chunkCount,
        ContentLength = contentLength,
        Duration = duration,
        CommitHash = commitHash
    };

    public static MemorizeResult Failed(string errorMessage, TimeSpan duration) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        Duration = duration
    };
}
