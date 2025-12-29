namespace FluxIndex.Stack.Vault.Enums;

/// <summary>
/// Represents the state of a watched folder.
/// </summary>
public enum WatcherStatus
{
    /// <summary>
    /// Folder is being actively monitored.
    /// </summary>
    Active,

    /// <summary>
    /// Monitoring is paused.
    /// </summary>
    Paused,

    /// <summary>
    /// Error occurred (e.g., folder not accessible).
    /// </summary>
    Error,

    /// <summary>
    /// Folder has been deleted or is no longer valid.
    /// </summary>
    Invalid
}
