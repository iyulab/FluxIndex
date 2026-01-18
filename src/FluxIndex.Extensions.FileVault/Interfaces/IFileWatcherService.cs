using FluxIndex.Extensions.FileVault.Domain.Entities;

namespace FluxIndex.Extensions.FileVault.Interfaces;

/// <summary>
/// Service for watching folders and detecting file changes.
/// Provides event-based file change detection with debouncing.
/// </summary>
public interface IFileWatcherService : IDisposable
{
    /// <summary>
    /// Raised when a new file is created.
    /// </summary>
    event EventHandler<FileChangeEventArgs>? FileCreated;

    /// <summary>
    /// Raised when a file is modified.
    /// </summary>
    event EventHandler<FileChangeEventArgs>? FileModified;

    /// <summary>
    /// Raised when a file is deleted.
    /// </summary>
    event EventHandler<FileChangeEventArgs>? FileDeleted;

    /// <summary>
    /// Raised when a file is renamed.
    /// </summary>
    event EventHandler<FileRenamedEventArgs>? FileRenamed;

    /// <summary>
    /// Raised when an error occurs in the watcher.
    /// </summary>
    event EventHandler<WatcherErrorEventArgs>? Error;

    /// <summary>
    /// Starts watching the specified folder.
    /// </summary>
    Task StartWatchingAsync(WatchedFolder folder, CancellationToken ct = default);

    /// <summary>
    /// Stops watching the specified folder.
    /// </summary>
    Task StopWatchingAsync(Guid folderId, CancellationToken ct = default);

    /// <summary>
    /// Stops all active watchers.
    /// </summary>
    Task StopAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets information about a specific watcher.
    /// </summary>
    WatcherInfo? GetWatcherInfo(Guid folderId);

    /// <summary>
    /// Gets information about all active watchers.
    /// </summary>
    IReadOnlyList<WatcherInfo> GetAllWatchers();
}

/// <summary>
/// Event args for file change events.
/// </summary>
public sealed class FileChangeEventArgs : EventArgs
{
    public Guid FolderId { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event args for file rename events.
/// </summary>
public sealed class FileRenamedEventArgs : EventArgs
{
    public Guid FolderId { get; init; }
    public string OldPath { get; init; } = string.Empty;
    public string NewPath { get; init; } = string.Empty;
    public string OldFileName => Path.GetFileName(OldPath);
    public string NewFileName => Path.GetFileName(NewPath);
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event args for watcher errors.
/// </summary>
public sealed class WatcherErrorEventArgs : EventArgs
{
    public Guid FolderId { get; init; }
    public string FolderPath { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Information about an active watcher.
/// </summary>
public sealed class WatcherInfo
{
    public Guid FolderId { get; init; }
    public string Path { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public int EventsReceived { get; set; }
    public DateTimeOffset? LastEventAt { get; set; }
}
