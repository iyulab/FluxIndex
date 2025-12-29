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
public class FileWatcherService : IFileWatcherService, IDisposable
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
            _logger.LogWarning("Watcher already exists for folder {FolderId}", folder.Id);
            return Task.CompletedTask;
        }

        if (!Directory.Exists(folder.Path))
        {
            _logger.LogError("Cannot watch folder that doesn't exist: {Path}", folder.Path);
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
                _logger.LogInformation("Started watching folder: {Path} (ID: {FolderId})", folder.Path, folder.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start watching folder: {Path}", folder.Path);
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
            _logger.LogInformation("Stopped watching folder: {Path} (ID: {FolderId})", context.Path, folderId);
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
        _logger.LogInformation("Stopped all watchers");
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
        _logger.LogError(e.GetException(), "Watcher error for folder {FolderId}", folderId);

        Error?.Invoke(this, new WatcherErrorEventArgs
        {
            WatchedFolderId = folderId,
            Exception = e.GetException()
        });

        // Attempt to restart the watcher
        if (_watchers.TryGetValue(folderId, out var context))
        {
            try
            {
                context.Watcher.EnableRaisingEvents = false;
                context.Watcher.EnableRaisingEvents = true;
                _logger.LogInformation("Restarted watcher for folder {FolderId}", folderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restart watcher for folder {FolderId}", folderId);
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
                _logger.LogTrace("File excluded by pattern: {FileName} matches {Pattern}", fileName, pattern);
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
    }

    private class WatcherContext
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
}
