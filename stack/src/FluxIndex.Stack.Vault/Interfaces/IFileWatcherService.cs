using FluxIndex.Stack.Vault.Entities;

namespace FluxIndex.Stack.Vault.Interfaces;

/// <summary>
/// Service for monitoring file system changes.
/// </summary>
public interface IFileWatcherService
{
    /// <summary>
    /// Starts watching a folder.
    /// </summary>
    Task StartWatchingAsync(WatchedFolder folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops watching a folder.
    /// </summary>
    Task StopWatchingAsync(Guid folderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops all watchers.
    /// </summary>
    Task StopAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a watcher.
    /// </summary>
    WatcherInfo? GetWatcherInfo(Guid folderId);

    /// <summary>
    /// Gets information about all active watchers.
    /// </summary>
    IReadOnlyList<WatcherInfo> GetAllWatchers();

    /// <summary>
    /// Event raised when a file is created.
    /// </summary>
    event EventHandler<FileChangeEventArgs>? FileCreated;

    /// <summary>
    /// Event raised when a file is modified.
    /// </summary>
    event EventHandler<FileChangeEventArgs>? FileModified;

    /// <summary>
    /// Event raised when a file is deleted.
    /// </summary>
    event EventHandler<FileChangeEventArgs>? FileDeleted;

    /// <summary>
    /// Event raised when a file is renamed.
    /// </summary>
    event EventHandler<FileRenamedEventArgs>? FileRenamed;

    /// <summary>
    /// Event raised when an error occurs.
    /// </summary>
    event EventHandler<WatcherErrorEventArgs>? Error;
}

/// <summary>
/// Information about a file change event.
/// </summary>
public class FileChangeEventArgs : EventArgs
{
    public Guid WatchedFolderId { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Information about a file rename event.
/// </summary>
public class FileRenamedEventArgs : FileChangeEventArgs
{
    public string OldFilePath { get; init; } = string.Empty;
    public string OldFileName { get; init; } = string.Empty;
}

/// <summary>
/// Information about a watcher error.
/// </summary>
public class WatcherErrorEventArgs : EventArgs
{
    public Guid WatchedFolderId { get; init; }
    public Exception Exception { get; init; } = null!;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Information about an active watcher.
/// </summary>
public class WatcherInfo
{
    public Guid FolderId { get; init; }
    public string Path { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime StartedAt { get; init; }
    public int EventsReceived { get; init; }
    public DateTime? LastEventAt { get; init; }
}
