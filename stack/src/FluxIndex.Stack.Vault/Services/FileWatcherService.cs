using System.Collections.Concurrent;
using FluxIndex.Stack.Vault.Entities;
using FluxIndex.Stack.Vault.Interfaces;
using FluxIndex.Stack.Vault.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Stack.Vault.Services;

/// <summary>
/// Service for monitoring file system changes using FileSystemWatcher.
/// Implements debouncing and error recovery strategies.
/// </summary>
public partial class FileWatcherService : IFileWatcherService, IDisposable
{
    private readonly ILogger<FileWatcherService> _logger;
    private readonly VaultOptions _options;
    private readonly DebounceService _debounceService;
    private readonly ConcurrentDictionary<Guid, WatcherContext> _watchers = new();
    private bool _disposed;

    public event EventHandler<FileChangeEventArgs>? FileCreated;
    public event EventHandler<FileChangeEventArgs>? FileModified;
    public event EventHandler<FileChangeEventArgs>? FileDeleted;
    public event EventHandler<FileRenamedEventArgs>? FileRenamed;
    public event EventHandler<WatcherErrorEventArgs>? Error;

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        ILoggerFactory loggerFactory,
        IOptions<VaultOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _debounceService = new DebounceService(
            loggerFactory.CreateLogger<DebounceService>(),
            TimeSpan.FromMilliseconds(_options.DebounceDelayMs));
    }

    public Task StartWatchingAsync(WatchedFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (_watchers.ContainsKey(folder.Id))
        {
            LogWatcherAlreadyExists(_logger, folder.Id);
            return Task.CompletedTask;
        }

        if (!Directory.Exists(folder.Path))
        {
            LogCannotWatchMissingFolder(_logger, folder.Path);
            throw new DirectoryNotFoundException($"Directory not found: {folder.Path}");
        }

        try
        {
            var watcher = CreateWatcher(folder);
            var context = new WatcherContext
            {
                FolderId = folder.Id,
                Path = folder.Path,
                Watcher = watcher,
                StartedAt = DateTime.UtcNow,
                IncludePatterns = folder.IncludePatterns,
                ExcludePatterns = folder.ExcludePatterns
            };

            if (_watchers.TryAdd(folder.Id, context))
            {
                watcher.EnableRaisingEvents = true;
                LogStartedWatching(_logger, folder.Path, folder.Id);
            }
        }
        catch (Exception ex)
        {
            LogStartWatchingFailed(_logger, folder.Path, ex);
            throw;
        }

        return Task.CompletedTask;
    }

    public Task StopWatchingAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        if (_watchers.TryRemove(folderId, out var context))
        {
            context.Watcher.EnableRaisingEvents = false;
            context.Watcher.Dispose();
            LogStoppedWatching(_logger, context.Path, folderId);
        }

        return Task.CompletedTask;
    }

    public Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var kvp in _watchers)
        {
            kvp.Value.Watcher.EnableRaisingEvents = false;
            kvp.Value.Watcher.Dispose();
        }
        _watchers.Clear();
        LogStoppedAllWatchers(_logger);
        return Task.CompletedTask;
    }

    public WatcherInfo? GetWatcherInfo(Guid folderId)
    {
        if (_watchers.TryGetValue(folderId, out var context))
        {
            return CreateWatcherInfo(context);
        }
        return null;
    }

    public IReadOnlyList<WatcherInfo> GetAllWatchers()
    {
        return _watchers.Values.Select(CreateWatcherInfo).ToList();
    }

    private FileSystemWatcher CreateWatcher(WatchedFolder folder)
    {
        var watcher = new FileSystemWatcher(folder.Path)
        {
            IncludeSubdirectories = folder.IsRecursive,
            InternalBufferSize = _options.WatcherBufferSize,
            NotifyFilter = NotifyFilters.FileName
                         | NotifyFilters.LastWrite
                         | NotifyFilters.Size
                         | NotifyFilters.CreationTime
        };

        // Set up filters if specific patterns are specified
        if (folder.IncludePatterns.Length == 1 && !folder.IncludePatterns[0].Contains(','))
        {
            watcher.Filter = folder.IncludePatterns[0];
        }
        else
        {
            watcher.Filter = "*.*"; // Watch all, filter in handlers
        }

        var folderId = folder.Id;

        watcher.Created += (s, e) => HandleCreated(folderId, e);
        watcher.Changed += (s, e) => HandleChanged(folderId, e);
        watcher.Deleted += (s, e) => HandleDeleted(folderId, e);
        watcher.Renamed += (s, e) => HandleRenamed(folderId, e);
        watcher.Error += (s, e) => HandleError(folderId, e);

        return watcher;
    }

    private void HandleCreated(Guid folderId, FileSystemEventArgs e)
    {
        if (!ShouldProcessFile(folderId, e.FullPath)) return;

        var key = $"created:{e.FullPath}";
        _ = _debounceService.DebounceAsync(key, () =>
        {
            IncrementEventCount(folderId);
            FileCreated?.Invoke(this, new FileChangeEventArgs
            {
                WatchedFolderId = folderId,
                FilePath = e.FullPath,
                FileName = e.Name ?? Path.GetFileName(e.FullPath)
            });
            return Task.CompletedTask;
        });
    }

    private void HandleChanged(Guid folderId, FileSystemEventArgs e)
    {
        if (!ShouldProcessFile(folderId, e.FullPath)) return;

        var key = $"changed:{e.FullPath}";
        _ = _debounceService.DebounceAsync(key, () =>
        {
            IncrementEventCount(folderId);
            FileModified?.Invoke(this, new FileChangeEventArgs
            {
                WatchedFolderId = folderId,
                FilePath = e.FullPath,
                FileName = e.Name ?? Path.GetFileName(e.FullPath)
            });
            return Task.CompletedTask;
        });
    }

    private void HandleDeleted(Guid folderId, FileSystemEventArgs e)
    {
        if (!ShouldProcessFile(folderId, e.FullPath)) return;

        // No debounce for delete events - they should be processed immediately
        IncrementEventCount(folderId);
        FileDeleted?.Invoke(this, new FileChangeEventArgs
        {
            WatchedFolderId = folderId,
            FilePath = e.FullPath,
            FileName = e.Name ?? Path.GetFileName(e.FullPath)
        });
    }

    private void HandleRenamed(Guid folderId, RenamedEventArgs e)
    {
        if (!ShouldProcessFile(folderId, e.FullPath)) return;

        IncrementEventCount(folderId);
        FileRenamed?.Invoke(this, new FileRenamedEventArgs
        {
            WatchedFolderId = folderId,
            FilePath = e.FullPath,
            FileName = e.Name ?? Path.GetFileName(e.FullPath),
            OldFilePath = e.OldFullPath,
            OldFileName = e.OldName ?? Path.GetFileName(e.OldFullPath)
        });
    }

    private void HandleError(Guid folderId, ErrorEventArgs e)
    {
        var exception = e.GetException();
        LogWatcherError(_logger, folderId, exception);

        Error?.Invoke(this, new WatcherErrorEventArgs
        {
            WatchedFolderId = folderId,
            Exception = exception
        });

        // Attempt to restart the watcher
        if (_watchers.TryGetValue(folderId, out var context))
        {
            try
            {
                context.Watcher.EnableRaisingEvents = false;
                context.Watcher.EnableRaisingEvents = true;
                LogRestartedWatcher(_logger, folderId);
            }
            catch (Exception ex)
            {
                LogRestartWatcherFailed(_logger, folderId, ex);
            }
        }
    }

    private bool ShouldProcessFile(Guid folderId, string filePath)
    {
        if (!_watchers.TryGetValue(folderId, out var context))
            return false;

        var fileName = Path.GetFileName(filePath);

        // Skip directories
        if (Directory.Exists(filePath))
            return false;

        // Check exclude patterns first
        foreach (var pattern in context.ExcludePatterns)
        {
            if (MatchesPattern(fileName, pattern))
            {
                LogFileExcludedByPattern(_logger, fileName, pattern);
                return false;
            }
        }

        // If no include patterns, include all non-excluded files
        if (context.IncludePatterns.Length == 0)
            return true;

        // Check include patterns
        foreach (var pattern in context.IncludePatterns)
        {
            if (MatchesPattern(fileName, pattern))
                return true;
        }

        return false;
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        var regexPattern = "^" +
            System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") +
            "$";

        return System.Text.RegularExpressions.Regex.IsMatch(
            fileName,
            regexPattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private void IncrementEventCount(Guid folderId)
    {
        if (_watchers.TryGetValue(folderId, out var context))
        {
            Interlocked.Increment(ref context.EventsReceived);
            context.LastEventAt = DateTime.UtcNow;
        }
    }

    private static WatcherInfo CreateWatcherInfo(WatcherContext context)
    {
        return new WatcherInfo
        {
            FolderId = context.FolderId,
            Path = context.Path,
            IsActive = context.Watcher.EnableRaisingEvents,
            StartedAt = context.StartedAt,
            EventsReceived = context.EventsReceived,
            LastEventAt = context.LastEventAt
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kvp in _watchers)
        {
            kvp.Value.Watcher.Dispose();
        }
        _watchers.Clear();
        _debounceService.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class WatcherContext
    {
        public Guid FolderId { get; init; }
        public string Path { get; init; } = string.Empty;
        public FileSystemWatcher Watcher { get; init; } = null!;
        public DateTime StartedAt { get; init; }
        public int EventsReceived;
        public DateTime? LastEventAt;
        public string[] IncludePatterns { get; init; } = Array.Empty<string>();
        public string[] ExcludePatterns { get; init; } = Array.Empty<string>();
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "Watcher already exists for folder {FolderId}")]
    private static partial void LogWatcherAlreadyExists(ILogger logger, Guid folderId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Cannot watch folder that doesn't exist: {Path}")]
    private static partial void LogCannotWatchMissingFolder(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started watching folder: {Path} (ID: {FolderId})")]
    private static partial void LogStartedWatching(ILogger logger, string path, Guid folderId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to start watching folder: {Path}")]
    private static partial void LogStartWatchingFailed(ILogger logger, string path, Exception? exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped watching folder: {Path} (ID: {FolderId})")]
    private static partial void LogStoppedWatching(ILogger logger, string path, Guid folderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped all watchers")]
    private static partial void LogStoppedAllWatchers(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Watcher error for folder {FolderId}")]
    private static partial void LogWatcherError(ILogger logger, Guid folderId, Exception? exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restarted watcher for folder {FolderId}")]
    private static partial void LogRestartedWatcher(ILogger logger, Guid folderId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to restart watcher for folder {FolderId}")]
    private static partial void LogRestartWatcherFailed(ILogger logger, Guid folderId, Exception? exception);

    [LoggerMessage(Level = LogLevel.Trace, Message = "File excluded by pattern: {FileName} matches {Pattern}")]
    private static partial void LogFileExcludedByPattern(ILogger logger, string fileName, string pattern);

    #endregion
}
