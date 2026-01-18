namespace FluxIndex.Extensions.FileVault.Domain.Enums;

/// <summary>
/// Status of a watched folder.
/// </summary>
public enum WatcherStatus
{
    /// <summary>
    /// Actively watching for file changes.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Watching is temporarily paused.
    /// </summary>
    Paused = 1,

    /// <summary>
    /// An error occurred (e.g., folder access denied).
    /// </summary>
    Error = 2,

    /// <summary>
    /// Folder is invalid (e.g., deleted or moved).
    /// </summary>
    Invalid = 3
}
