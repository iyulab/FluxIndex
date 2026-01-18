namespace FluxIndex.Extensions.FileVault.Domain.Enums;

/// <summary>
/// Status of a tracked file in the vault.
/// </summary>
public enum TrackedFileStatus
{
    /// <summary>
    /// File is discovered but not yet tracked.
    /// </summary>
    Untracked = 0,

    /// <summary>
    /// File is queued for processing.
    /// </summary>
    Queued = 1,

    /// <summary>
    /// File is currently being processed (extracting, chunking, etc.).
    /// </summary>
    Processing = 2,

    /// <summary>
    /// File has been successfully processed and indexed.
    /// </summary>
    Memorized = 3,

    /// <summary>
    /// File content has changed since last memorization.
    /// </summary>
    Stale = 4,

    /// <summary>
    /// Original source file has been deleted.
    /// </summary>
    Orphaned = 5,

    /// <summary>
    /// File has been explicitly removed from vault.
    /// </summary>
    Removed = 6,

    /// <summary>
    /// An error occurred during processing.
    /// </summary>
    Error = 7
}
