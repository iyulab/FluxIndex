using System.Collections.Concurrent;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// File watcher service with debouncing and event-based architecture.
/// </summary>
public sealed class FileWatcherService : IFileWatcherService
{
    private readonly ILogger<FileWatcherService> _logger;
    private readonly FileVaultOptions _options;
    private readonly ConcurrentDictionary<Guid, WatcherContext> _watchers = new();
    private readonly ConcurrentDictionary<string, DebounceContext> _debounceContexts = new();
    private bool _disposed;

    public event EventHandler<FileChangeEventArgs>? FileCreated;
    public event EventHandler<FileChangeEventArgs>? FileModified;
    public event EventHandler<FileChangeEventArgs>? FileDeleted;
    public event EventHandler<FileRenamedEventArgs>? FileRenamed;
    public event EventHandler<WatcherErrorEventArgs>? Error;

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        IOptions<FileVaultOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new FileVaultOptions();
    }

    public Task StartWatchingAsync(WatchedFolder folder, CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FileWatcherService));

        if (!Directory.Exists(folder.Path))
        {
            _logger.LogWarning("Cannot start watching: folder does not exist: {Path}", folder.Path);
            return Task.CompletedTask;
        }

        if (_watchers.ContainsKey(folder.Id))
        {
            _logger.LogDebug("Watcher already exists for folder {FolderId}", folder.Id);
            return Task.CompletedTask;
        }

        try
        {
            var watcher = new FileSystemWatcher(folder.Path)
            {
                IncludeSubdirectories = folder.IsRecursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                InternalBufferSize = _options.WatcherBufferSize
            };

            var context = new WatcherContext
            {
                Watcher = watcher,
                Folder = folder,
                Info = new WatcherInfo
                {
                    FolderId = folder.Id,
                    Path = folder.Path,
                    IsActive = true,
                    StartedAt = DateTimeOffset.UtcNow
                }
            };

            // Subscribe to events
            watcher.Created += (s, e) => OnFileEvent(context, e.FullPath, FileEventType.Created);
            watcher.Changed += (s, e) => OnFileEvent(context, e.FullPath, FileEventType.Modified);
            watcher.Deleted += (s, e) => OnFileEvent(context, e.FullPath, FileEventType.Deleted);
            watcher.Renamed += (s, e) => OnRenamedEvent(context, e.OldFullPath, e.FullPath);
            watcher.Error += (s, e) => OnWatcherError(context, e.GetException());

            if (_watchers.TryAdd(folder.Id, context))
            {
                watcher.EnableRaisingEvents = true;
                _logger.LogInformation(
                    "Started watching folder {FolderId}: {Path} (recursive={IsRecursive})",
                    folder.Id, folder.Path, folder.IsRecursive);
            }
            else
            {
                watcher.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start watching folder {Path}", folder.Path);
            OnError(new WatcherErrorEventArgs
            {
                FolderId = folder.Id,
                FolderPath = folder.Path,
                Exception = ex,
                ErrorMessage = ex.Message
            });
        }

        return Task.CompletedTask;
    }

    public Task StopWatchingAsync(Guid folderId, CancellationToken ct = default)
    {
        if (_watchers.TryRemove(folderId, out var context))
        {
            context.Watcher.EnableRaisingEvents = false;
            context.Watcher.Dispose();
            _logger.LogInformation("Stopped watching folder {FolderId}", folderId);
        }

        return Task.CompletedTask;
    }

    public async Task StopAllAsync(CancellationToken ct = default)
    {
        var folderIds = _watchers.Keys.ToList();
        foreach (var folderId in folderIds)
        {
            await StopWatchingAsync(folderId, ct);
        }
    }

    public WatcherInfo? GetWatcherInfo(Guid folderId)
    {
        return _watchers.TryGetValue(folderId, out var context) ? context.Info : null;
    }

    public IReadOnlyList<WatcherInfo> GetAllWatchers()
    {
        return _watchers.Values.Select(c => c.Info).ToList();
    }

    private void OnFileEvent(WatcherContext context, string filePath, FileEventType eventType)
    {
        // Skip directories
        if (Directory.Exists(filePath))
            return;

        // Check include/exclude patterns
        if (!context.Folder.ShouldIncludeFile(filePath))
            return;

        context.Info.EventsReceived++;
        context.Info.LastEventAt = DateTimeOffset.UtcNow;

        // Delete events are processed immediately (no debouncing)
        if (eventType == FileEventType.Deleted)
        {
            OnFileDeleted(new FileChangeEventArgs
            {
                FolderId = context.Folder.Id,
                FilePath = filePath
            });
            return;
        }

        // Debounce Created and Modified events
        var key = $"{context.Folder.Id}:{filePath}";
        var debounceContext = _debounceContexts.GetOrAdd(key, _ => new DebounceContext());

        lock (debounceContext)
        {
            // Cancel previous timer if exists
            debounceContext.Timer?.Dispose();

            // Record the event type (prefer Created over Modified if both occur)
            if (eventType == FileEventType.Created || debounceContext.EventType == null)
            {
                debounceContext.EventType = eventType;
            }

            // Start new debounce timer
            debounceContext.Timer = new Timer(
                _ => ProcessDebouncedEvent(context, filePath, key),
                null,
                _options.DebounceDelayMs,
                Timeout.Infinite);
        }
    }

    private void ProcessDebouncedEvent(WatcherContext context, string filePath, string key)
    {
        if (!_debounceContexts.TryRemove(key, out var debounceContext))
            return;

        lock (debounceContext)
        {
            debounceContext.Timer?.Dispose();

            var eventArgs = new FileChangeEventArgs
            {
                FolderId = context.Folder.Id,
                FilePath = filePath
            };

            if (debounceContext.EventType == FileEventType.Created)
            {
                OnFileCreated(eventArgs);
            }
            else
            {
                OnFileModified(eventArgs);
            }
        }
    }

    private void OnRenamedEvent(WatcherContext context, string oldPath, string newPath)
    {
        context.Info.EventsReceived++;
        context.Info.LastEventAt = DateTimeOffset.UtcNow;

        FileRenamed?.Invoke(this, new FileRenamedEventArgs
        {
            FolderId = context.Folder.Id,
            OldPath = oldPath,
            NewPath = newPath
        });
    }

    private void OnWatcherError(WatcherContext context, Exception exception)
    {
        _logger.LogError(exception, "FileSystemWatcher error for folder {FolderId}: {Path}",
            context.Folder.Id, context.Folder.Path);

        OnError(new WatcherErrorEventArgs
        {
            FolderId = context.Folder.Id,
            FolderPath = context.Folder.Path,
            Exception = exception,
            ErrorMessage = exception.Message
        });

        // Attempt to restart the watcher
        TryRestartWatcher(context);
    }

    private void TryRestartWatcher(WatcherContext context)
    {
        try
        {
            context.Watcher.EnableRaisingEvents = false;
            Thread.Sleep(1000); // Brief pause before restart
            context.Watcher.EnableRaisingEvents = true;
            _logger.LogInformation("Successfully restarted watcher for folder {FolderId}", context.Folder.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart watcher for folder {FolderId}", context.Folder.Id);
            context.Folder.MarkAsError(ex.Message);
        }
    }

    private void OnFileCreated(FileChangeEventArgs e)
    {
        _logger.LogDebug("File created: {FilePath}", e.FilePath);
        FileCreated?.Invoke(this, e);
    }

    private void OnFileModified(FileChangeEventArgs e)
    {
        _logger.LogDebug("File modified: {FilePath}", e.FilePath);
        FileModified?.Invoke(this, e);
    }

    private void OnFileDeleted(FileChangeEventArgs e)
    {
        _logger.LogDebug("File deleted: {FilePath}", e.FilePath);
        FileDeleted?.Invoke(this, e);
    }

    private void OnError(WatcherErrorEventArgs e)
    {
        Error?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var context in _watchers.Values)
        {
            context.Watcher.EnableRaisingEvents = false;
            context.Watcher.Dispose();
        }
        _watchers.Clear();

        foreach (var debounce in _debounceContexts.Values)
        {
            debounce.Timer?.Dispose();
        }
        _debounceContexts.Clear();
    }

    private enum FileEventType
    {
        Created,
        Modified,
        Deleted
    }

    private sealed class WatcherContext
    {
        public required FileSystemWatcher Watcher { get; init; }
        public required WatchedFolder Folder { get; init; }
        public required WatcherInfo Info { get; init; }
    }

    private sealed class DebounceContext
    {
        public Timer? Timer { get; set; }
        public FileEventType? EventType { get; set; }
    }
}
