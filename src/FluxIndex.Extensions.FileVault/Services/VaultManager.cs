using System.Collections.Concurrent;
using System.Diagnostics;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// Main vault implementation providing file-based tracking with Git integration.
/// </summary>
public sealed class VaultManager : IVault
{
    private readonly IContentHasher _hasher;
    private readonly IGitService _git;
    private readonly IVaultPipeline _pipeline;
    private readonly IFileWatcherService _fileWatcher;
    private readonly IVaultStorageService _storage;
    private readonly PatternMatcher _patternMatcher;
    private readonly ILogger<VaultManager> _logger;
    private readonly FileVaultOptions _options;

    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, WatchOptions> _watchOptions = new();
    private readonly ConcurrentDictionary<Guid, WatchedFolder> _watchedFolders = new();
    private DateTimeOffset? _lastSyncTime;

    public string VaultBasePath { get; }

    public VaultManager(
        IContentHasher hasher,
        IGitService git,
        IVaultPipeline pipeline,
        IFileWatcherService fileWatcher,
        IVaultStorageService storage,
        ILogger<VaultManager> logger,
        IOptions<FileVaultOptions> options)
    {
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _git = git ?? throw new ArgumentNullException(nameof(git));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _fileWatcher = fileWatcher ?? throw new ArgumentNullException(nameof(fileWatcher));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _patternMatcher = new PatternMatcher();

        VaultBasePath = _options.VaultBasePath ?? _options.VaultDirectoryName;
    }

    #region Entry Management

    public async Task<VaultEntry> AddAsync(string filePath, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(filePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Source file not found", fullPath);

        // Compute hash
        var hash = await _hasher.ComputeHashAsync(fullPath, ct);

        // Check if already exists
        var existingEntry = await GetByHashAsync(hash.Value, ct);
        if (existingEntry != null)
        {
            _logger.LogDebug("Entry already exists for hash {Hash}", hash.Value);
            return existingEntry;
        }

        // Determine vault base path (relative to source file's directory)
        var sourceDir = Path.GetDirectoryName(fullPath) ?? ".";
        var vaultBase = Path.Combine(sourceDir, VaultBasePath);

        // Create entry
        var entry = VaultEntry.Create(fullPath, hash, vaultBase);

        // Initialize Git repo
        await _git.InitAsync(entry.VaultPath, ct);

        // Save source info
        entry.SaveSourceInfo();

        // Initial commit
        await _git.CommitAsync(entry.VaultPath, "init: source registered", ct);

        _logger.LogInformation("Added vault entry for {FilePath} -> {VaultPath}", fullPath, entry.VaultPath);
        return entry;
    }

    public async Task<VaultEntry?> GetAsync(string filePath, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(filePath);

        if (!File.Exists(fullPath))
            return null;

        var hash = await _hasher.ComputeHashAsync(fullPath, ct);
        return await GetByHashAsync(hash.Value, ct);
    }

    public Task<VaultEntry?> GetByHashAsync(string hash, CancellationToken ct = default)
    {
        // Search in all potential vault locations
        var searchPaths = new List<string>();

        // Current directory
        searchPaths.Add(Path.Combine(VaultBasePath, hash));

        // Check watched folders
        foreach (var watchPath in _watchers.Keys)
        {
            searchPaths.Add(Path.Combine(watchPath, VaultBasePath, hash));
        }

        foreach (var vaultPath in searchPaths)
        {
            if (Directory.Exists(vaultPath) && File.Exists(Path.Combine(vaultPath, "source.json")))
            {
                try
                {
                    var entry = VaultEntry.Load(vaultPath);
                    return Task.FromResult<VaultEntry?>(entry);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load vault entry from {Path}", vaultPath);
                }
            }
        }

        return Task.FromResult<VaultEntry?>(null);
    }

    public Task<IReadOnlyList<VaultEntry>> ListAsync(ProcessingStage? stageFilter = null, CancellationToken ct = default)
    {
        var entries = new List<VaultEntry>();

        // Search all vault directories
        var searchPaths = new List<string> { VaultBasePath };
        foreach (var watchPath in _watchers.Keys)
        {
            searchPaths.Add(Path.Combine(watchPath, VaultBasePath));
        }

        foreach (var basePath in searchPaths)
        {
            if (!Directory.Exists(basePath))
                continue;

            foreach (var dir in Directory.GetDirectories(basePath))
            {
                var sourceJson = Path.Combine(dir, "source.json");
                if (!File.Exists(sourceJson))
                    continue;

                try
                {
                    var entry = VaultEntry.Load(dir);
                    if (stageFilter == null || entry.Stage == stageFilter)
                    {
                        entries.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load vault entry from {Path}", dir);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<VaultEntry>>(entries);
    }

    public Task RemoveAsync(string filePath, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(filePath);
        var sourceDir = Path.GetDirectoryName(fullPath) ?? ".";

        // Find and remove the vault entry
        var vaultBase = Path.Combine(sourceDir, VaultBasePath);

        if (!Directory.Exists(vaultBase))
            return Task.CompletedTask;

        foreach (var dir in Directory.GetDirectories(vaultBase))
        {
            var sourceJson = Path.Combine(dir, "source.json");
            if (!File.Exists(sourceJson))
                continue;

            try
            {
                var entry = VaultEntry.Load(dir);
                if (entry.SourcePath.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(dir, recursive: true);
                    _logger.LogInformation("Removed vault entry for {FilePath}", fullPath);
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check vault entry at {Path}", dir);
            }
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Pipeline Commands

    public async Task ExtractAsync(string filePath, CancellationToken ct = default)
    {
        var entry = await GetOrAddAsync(filePath, ct);
        await _pipeline.ExtractAsync(entry, ct);
    }

    public async Task RefineAsync(string filePath, CancellationToken ct = default)
    {
        var entry = await GetOrAddAsync(filePath, ct);
        await _pipeline.RefineAsync(entry, ct);
    }

    public async Task ChunkAsync(string filePath, ChunkingOptions? options = null, CancellationToken ct = default)
    {
        var entry = await GetOrAddAsync(filePath, ct);
        await _pipeline.ChunkAsync(entry, options, ct);
    }

    public async Task MemorizeAsync(string filePath, CancellationToken ct = default)
    {
        var entry = await GetOrAddAsync(filePath, ct);
        await _pipeline.MemorizeAsync(entry, ct);
    }

    public async Task<VaultEntry> ProcessAsync(string filePath, ChunkingOptions? options = null, CancellationToken ct = default)
    {
        var entry = await AddAsync(filePath, ct);
        await _pipeline.ProcessToStageAsync(entry, ProcessingStage.Memorized, ct);
        return entry;
    }

    private async Task<VaultEntry> GetOrAddAsync(string filePath, CancellationToken ct)
    {
        var entry = await GetAsync(filePath, ct);
        if (entry != null)
            return entry;

        return await AddAsync(filePath, ct);
    }

    #endregion

    #region Status & Diff

    public async Task<VaultStatus> StatusAsync(CancellationToken ct = default)
    {
        var entries = await ListAsync(ct: ct);
        var changedEntries = new List<VaultEntry>();

        var sourceCount = 0;
        var extractedCount = 0;
        var refinedCount = 0;
        var chunkedCount = 0;
        var memorizedCount = 0;
        var changedSourceCount = 0;
        var changedRefinedCount = 0;
        var errorCount = 0;
        var orphanedCount = 0;
        long totalStorageSize = 0;

        foreach (var entry in entries)
        {
            switch (entry.Stage)
            {
                case ProcessingStage.Source: sourceCount++; break;
                case ProcessingStage.Extracted: extractedCount++; break;
                case ProcessingStage.Refined: refinedCount++; break;
                case ProcessingStage.Chunked: chunkedCount++; break;
                case ProcessingStage.Memorized: memorizedCount++; break;
            }

            // Check if source file exists (orphaned check)
            if (!File.Exists(entry.SourcePath))
            {
                orphanedCount++;
                continue;
            }

            // Check for source changes
            if (await HasSourceChangedAsync(entry.SourcePath, ct))
            {
                changedSourceCount++;
                changedEntries.Add(entry);
            }
            // Check for refined changes (if applicable)
            else if (entry.Stage >= ProcessingStage.Refined && await HasRefinedChangedAsync(entry.SourcePath, ct))
            {
                changedRefinedCount++;
                changedEntries.Add(entry);
            }

            // Calculate storage size
            totalStorageSize += await _storage.GetStorageSizeAsync(entry.Id, ct);
        }

        // Watcher status
        var watcherInfos = _fileWatcher.GetAllWatchers();
        var folders = _watchedFolders.Values.ToList();

        return new VaultStatus
        {
            TotalEntries = entries.Count,
            SourceCount = sourceCount,
            ExtractedCount = extractedCount,
            RefinedCount = refinedCount,
            ChunkedCount = chunkedCount,
            MemorizedCount = memorizedCount,
            ChangedSourceCount = changedSourceCount,
            ChangedRefinedCount = changedRefinedCount,
            ChangedEntries = changedEntries,
            ActiveWatcherCount = folders.Count(f => f.Status == WatcherStatus.Active),
            PausedWatcherCount = folders.Count(f => f.Status == WatcherStatus.Paused),
            ErrorWatcherCount = folders.Count(f => f.Status == WatcherStatus.Error),
            QueuedCount = 0, // Will be updated when queue service is integrated
            ProcessingCount = 0,
            ErrorCount = errorCount,
            OrphanedCount = orphanedCount,
            LastSyncTime = _lastSyncTime,
            TotalStorageSizeBytes = totalStorageSize
        };
    }

    public async Task<string> DiffAsync(string filePath, string? stage = null, CancellationToken ct = default)
    {
        var entry = await GetAsync(filePath, ct);
        if (entry == null)
            return "";

        var targetFile = stage?.ToLowerInvariant() switch
        {
            "extracted" => "extracted.md",
            "refined" => "refined.md",
            _ => null
        };

        return await _git.DiffAsync(entry.VaultPath, targetFile, ct);
    }

    public async Task<IReadOnlyList<GitCommit>> LogAsync(string filePath, int maxCount = 10, CancellationToken ct = default)
    {
        var entry = await GetAsync(filePath, ct);
        if (entry == null)
            return [];

        return await _git.LogAsync(entry.VaultPath, maxCount, ct);
    }

    #endregion

    #region Change Detection

    public async Task<bool> HasSourceChangedAsync(string filePath, CancellationToken ct = default)
    {
        var entry = await GetAsync(filePath, ct);
        if (entry == null)
            return false;

        if (!File.Exists(entry.SourcePath))
            return true; // Source deleted

        var currentHash = await _hasher.ComputeHashAsync(entry.SourcePath, ct);
        return !entry.SourceHash.Equals(currentHash);
    }

    public async Task<bool> HasRefinedChangedAsync(string filePath, CancellationToken ct = default)
    {
        var entry = await GetAsync(filePath, ct);
        if (entry == null || entry.Stage < ProcessingStage.Refined)
            return false;

        var status = await _git.StatusAsync(entry.VaultPath, ct);
        return status.ModifiedFiles.Any(f => f.EndsWith("refined.md"));
    }

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var status = await StatusAsync(ct);
        var processedCount = 0;
        var skippedCount = 0;
        var queuedCount = 0;
        var errorCount = 0;
        var errors = new List<SyncError>();
        var orphansCleaned = 0;

        // Process changed entries
        foreach (var entry in status.ChangedEntries)
        {
            try
            {
                // Determine restart point
                var sourceChanged = await HasSourceChangedAsync(entry.SourcePath, ct);
                var fromStage = sourceChanged ? ProcessingStage.Source : ProcessingStage.Refined;

                // Reprocess
                await _pipeline.ReprocessFromStageAsync(entry, fromStage, ct);
                processedCount++;
            }
            catch (Exception ex)
            {
                errorCount++;
                errors.Add(new SyncError
                {
                    FilePath = entry.SourcePath,
                    ErrorMessage = ex.Message,
                    Exception = ex
                });
                _logger.LogError(ex, "Failed to sync entry {Path}", entry.SourcePath);
            }
        }

        // Cleanup orphans if enabled
        if (_options.AutoCleanupOrphans)
        {
            orphansCleaned = await CleanupOrphanedEntriesAsync(ct);
        }

        skippedCount = status.TotalEntries - status.ChangedEntries.Count - status.OrphanedCount;
        _lastSyncTime = DateTimeOffset.UtcNow;

        return new SyncResult
        {
            ProcessedCount = processedCount,
            SkippedCount = skippedCount,
            QueuedCount = queuedCount,
            ErrorCount = errorCount,
            Errors = errors,
            FoldersScanned = _watchedFolders.Count,
            NewFilesDiscovered = 0,
            ChangedFilesDetected = status.ChangedSourceCount + status.ChangedRefinedCount,
            OrphansDetected = status.OrphanedCount,
            OrphansCleaned = orphansCleaned,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion

    #region Folder Watching

    public Task WatchFolderAsync(string folderPath, WatchOptions? options = null, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(folderPath);

        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Folder not found: {fullPath}");

        if (_watchers.ContainsKey(fullPath))
        {
            _logger.LogWarning("Already watching folder: {Path}", fullPath);
            return Task.CompletedTask;
        }

        options ??= new WatchOptions();
        _watchOptions[fullPath] = options;

        var watcher = new FileSystemWatcher(fullPath)
        {
            IncludeSubdirectories = options.IsRecursive,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        watcher.Created += OnFileCreated;
        watcher.Changed += OnFileChanged;
        watcher.Deleted += OnFileDeleted;

        _watchers[fullPath] = watcher;
        _logger.LogInformation("Started watching folder: {Path}", fullPath);

        return Task.CompletedTask;
    }

    public Task UnwatchFolderAsync(string folderPath, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(folderPath);

        if (_watchers.TryGetValue(fullPath, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _watchers.Remove(fullPath);
            _watchOptions.Remove(fullPath);
            _logger.LogInformation("Stopped watching folder: {Path}", fullPath);
        }

        return Task.CompletedTask;
    }

    public async Task<ScanResult> ScanFolderAsync(string folderPath, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var fullPath = Path.GetFullPath(folderPath);

        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Folder not found: {fullPath}");

        var options = _watchOptions.GetValueOrDefault(fullPath) ?? new WatchOptions();
        var searchOption = options.IsRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var newEntries = new List<VaultEntry>();
        var changedEntries = new List<VaultEntry>();
        var orphanedPaths = new List<string>();
        var errors = new List<ScanError>();
        var existingCount = 0;
        var skippedCount = 0;
        var scannedCount = 0;

        var files = Directory.EnumerateFiles(fullPath, "*", searchOption);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            scannedCount++;

            // Check patterns
            if (!_patternMatcher.ShouldInclude(file, options.IncludePatterns, options.ExcludePatterns))
            {
                skippedCount++;
                continue;
            }

            try
            {
                var existing = await GetAsync(file, ct);
                if (existing != null)
                {
                    existingCount++;

                    // Check if changed
                    if (await HasSourceChangedAsync(file, ct))
                    {
                        changedEntries.Add(existing);
                    }
                }
                else
                {
                    var entry = await AddAsync(file, ct);
                    newEntries.Add(entry);

                    if (options.AutoProcess)
                    {
                        await _pipeline.ProcessToStageAsync(entry, ProcessingStage.Memorized, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(new ScanError { FilePath = file, ErrorMessage = ex.Message });
                _logger.LogWarning(ex, "Failed to process file during scan: {Path}", file);
            }
        }

        sw.Stop();
        _logger.LogInformation("Scanned folder {Path}: {New} new, {Changed} changed, {Existing} existing in {Duration}ms",
            fullPath, newEntries.Count, changedEntries.Count, existingCount, sw.ElapsedMilliseconds);

        return new ScanResult
        {
            ScannedCount = scannedCount,
            NewFilesCount = newEntries.Count,
            ExistingFilesCount = existingCount,
            ChangedFilesCount = changedEntries.Count,
            SkippedFilesCount = skippedCount,
            OrphanedFilesCount = orphanedPaths.Count,
            NewEntries = newEntries,
            ChangedEntries = changedEntries,
            OrphanedPaths = orphanedPaths,
            ErrorCount = errors.Count,
            Errors = errors,
            Duration = sw.Elapsed
        };
    }

    public async Task<ScanResult> ScanFolderAsync(Guid folderId, CancellationToken ct = default)
    {
        if (!_watchedFolders.TryGetValue(folderId, out var folder))
            throw new KeyNotFoundException($"Watched folder not found: {folderId}");

        return await ScanFolderAsync(folder.Path, ct);
    }

    #endregion

    #region Event Handlers

    private async void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (!ShouldProcessFile(e.FullPath))
            return;

        try
        {
            await AddAsync(e.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add created file: {Path}", e.FullPath);
        }
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!ShouldProcessFile(e.FullPath))
            return;

        try
        {
            var entry = await GetAsync(e.FullPath);
            if (entry != null)
            {
                _logger.LogDebug("File changed: {Path}", e.FullPath);
                // Entry will be reprocessed on next sync
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle file change: {Path}", e.FullPath);
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        _logger.LogDebug("File deleted: {Path}", e.FullPath);
        // Deleted files will be detected on next sync
    }

    private bool ShouldProcessFile(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        // Find the watch options for this path
        foreach (var (watchPath, options) in _watchOptions)
        {
            if (filePath.StartsWith(watchPath, StringComparison.OrdinalIgnoreCase))
            {
                return _patternMatcher.ShouldInclude(filePath, options.IncludePatterns, options.ExcludePatterns);
            }
        }

        return true;
    }

    #endregion

    #region Watched Folder Management

    public async Task<WatchedFolder> AddWatchedFolderAsync(
        string folderPath,
        string? name = null,
        bool isRecursive = true,
        bool autoMemorize = false,
        string[]? includePatterns = null,
        string[]? excludePatterns = null,
        CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(folderPath);

        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Folder not found: {fullPath}");

        // Check if already watching
        var existing = _watchedFolders.Values.FirstOrDefault(f =>
            f.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            _logger.LogWarning("Folder already being watched: {Path}", fullPath);
            return existing;
        }

        var folder = WatchedFolder.Create(
            fullPath,
            name ?? Path.GetFileName(fullPath),
            isRecursive,
            autoMemorize);

        folder.SetPatterns(
            includePatterns ?? _options.DefaultIncludePatterns.ToArray(),
            excludePatterns ?? _options.DefaultExcludePatterns.ToArray());

        _watchedFolders[folder.Id] = folder;

        // Start watching
        await _fileWatcher.StartWatchingAsync(folder, ct);

        _logger.LogInformation("Added watched folder: {Name} ({Path})", folder.Name, folder.Path);

        return folder;
    }

    public Task<WatchedFolder?> GetWatchedFolderAsync(Guid folderId, CancellationToken ct = default)
    {
        _watchedFolders.TryGetValue(folderId, out var folder);
        return Task.FromResult(folder);
    }

    public Task<IReadOnlyList<WatchedFolder>> GetAllWatchedFoldersAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<WatchedFolder>>(_watchedFolders.Values.ToList());
    }

    public async Task RemoveWatchedFolderAsync(Guid folderId, bool removeTrackedFiles = false, CancellationToken ct = default)
    {
        if (!_watchedFolders.TryRemove(folderId, out var folder))
            return;

        await _fileWatcher.StopWatchingAsync(folderId, ct);

        if (removeTrackedFiles)
        {
            // Remove all tracked files from this folder
            var entries = await ListAsync(ct: ct);
            foreach (var entry in entries.Where(e => e.SourcePath.StartsWith(folder.Path, StringComparison.OrdinalIgnoreCase)))
            {
                await RemoveAsync(entry.SourcePath, ct);
            }
        }

        _logger.LogInformation("Removed watched folder: {Name} ({Path})", folder.Name, folder.Path);
    }

    public async Task PauseWatchingAsync(Guid folderId, CancellationToken ct = default)
    {
        if (!_watchedFolders.TryGetValue(folderId, out var folder))
            throw new KeyNotFoundException($"Watched folder not found: {folderId}");

        folder.Pause();
        await _fileWatcher.StopWatchingAsync(folderId, ct);

        _logger.LogInformation("Paused watching folder: {Name}", folder.Name);
    }

    public async Task ResumeWatchingAsync(Guid folderId, CancellationToken ct = default)
    {
        if (!_watchedFolders.TryGetValue(folderId, out var folder))
            throw new KeyNotFoundException($"Watched folder not found: {folderId}");

        folder.Resume();
        await _fileWatcher.StartWatchingAsync(folder, ct);

        _logger.LogInformation("Resumed watching folder: {Name}", folder.Name);
    }

    #endregion

    #region Orphan Management

    public async Task<int> CleanupOrphanedEntriesAsync(CancellationToken ct = default)
    {
        var orphans = await GetOrphanedEntriesAsync(ct);
        var cleanedCount = 0;

        foreach (var entry in orphans)
        {
            try
            {
                // Delete vault directory
                if (Directory.Exists(entry.VaultPath))
                {
                    Directory.Delete(entry.VaultPath, recursive: true);
                }

                // Delete storage artifacts
                await _storage.DeleteArtifactsAsync(entry.Id, ct);

                cleanedCount++;
                _logger.LogDebug("Cleaned up orphaned entry: {Path}", entry.SourcePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup orphaned entry: {Path}", entry.SourcePath);
            }
        }

        if (cleanedCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} orphaned entries", cleanedCount);
        }

        return cleanedCount;
    }

    public async Task<IReadOnlyList<VaultEntry>> GetOrphanedEntriesAsync(CancellationToken ct = default)
    {
        var entries = await ListAsync(ct: ct);
        return entries.Where(e => !File.Exists(e.SourcePath)).ToList();
    }

    #endregion
}
