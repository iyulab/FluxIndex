namespace FluxIndex.Stack.Vault.Enums;

/// <summary>
/// Represents the state of a file being tracked by the vault.
/// </summary>
public enum TrackedFileStatus
{
    /// <summary>
    /// File discovered but not yet tracked.
    /// </summary>
    Untracked,

    /// <summary>
    /// File is queued for memorization.
    /// </summary>
    Queued,

    /// <summary>
    /// File is being processed (extracting, chunking, embedding).
    /// </summary>
    Processing,

    /// <summary>
    /// File has been successfully memorized and indexed.
    /// </summary>
    Memorized,

    /// <summary>
    /// File has changed since last memorization.
    /// </summary>
    Stale,

    /// <summary>
    /// Source file has been deleted.
    /// </summary>
    Orphaned,

    /// <summary>
    /// File has been removed from the vault.
    /// </summary>
    Removed,

    /// <summary>
    /// Error occurred during processing.
    /// </summary>
    Error
}
