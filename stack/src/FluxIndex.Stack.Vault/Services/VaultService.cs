using System.Diagnostics;
using FluxIndex.Stack.Vault.Entities;
using FluxIndex.Stack.Vault.Enums;
using FluxIndex.Stack.Vault.Interfaces;
using FluxIndex.Stack.Vault.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Stack.Vault.Services;

/// <summary>
/// Core vault service for managing file tracking and memorization.
/// </summary>
public class VaultService : IVaultService
{
    private readonly ILogger<VaultService> _logger;
    private readonly VaultOptions _options;
    private readonly ITrackedFileRepository _trackedFileRepository;
    private readonly IWatchedFolderRepository _watchedFolderRepository;
    private readonly ITrackedFileVersionRepository _versionRepository;
    private readonly IContentHashService _hashService;
    private readonly IVaultStorageService _storageService;
    private readonly IFileWatcherService _watcherService;

    public VaultService(
        ILogger<VaultService> logger,
        IOptions<VaultOptions> options,
        ITrackedFileRepository trackedFileRepository,
        IWatchedFolderRepository watchedFolderRepository,
        ITrackedFileVersionRepository versionRepository,
        IContentHashService hashService,
        IVaultStorageService storageService,
        IFileWatcherService watcherService)
    {
        _logger = logger;
        _options = options.Value;
        _trackedFileRepository = trackedFileRepository;
        _watchedFolderRepository = watchedFolderRepository;
        _versionRepository = versionRepository;
        _hashService = hashService;
        _storageService = storageService;
        _watcherService = watcherService;

        // Subscribe to file watcher events
        _watcherService.FileCreated += OnFileCreated;
        _watcherService.FileModified += OnFileModified;
        _watcherService.FileDeleted += OnFileDeleted;
        _watcherService.FileRenamed += OnFileRenamed;
        _watcherService.Error += OnWatcherError;
    }

    #region Watched Folder Operations

    public async Task<WatchedFolder> AddWatchedFolderAsync(
        string path,
        string? name = null,
        bool isRecursive = true,
        bool autoMemorize = true,
        string[]? includePatterns = null,
        string[]? excludePatterns = null,
        Guid? collectionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {fullPath}");
        }

        if (await _watchedFolderRepository.ExistsByPathAsync(fullPath, cancellationToken))
        {
            throw new InvalidOperationException($"Folder is already being watched: {fullPath}");
        }

        var folder = WatchedFolder.Create(
            fullPath,
            name,
            isRecursive,
            autoMemorize,
            collectionId);

        folder.SetPatterns(
            includePatterns ?? _options.DefaultPatterns.Include,
            excludePatterns ?? _options.DefaultPatterns.Exclude);

        await _watchedFolderRepository.AddAsync(folder, cancellationToken);

        if (_options.EnableRealTimeWatch)
        {
            await _watcherService.StartWatchingAsync(folder, cancellationToken);
        }

        _logger.LogInformation("Added watched folder: {Path} (ID: {FolderId})", fullPath, folder.Id);

        return folder;
    }

    public async Task RemoveWatchedFolderAsync(Guid folderId, bool removeTrackedFiles = true, CancellationToken cancellationToken = default)
    {
        await _watcherService.StopWatchingAsync(folderId, cancellationToken);

        if (removeTrackedFiles)
        {
            var files = await _trackedFileRepository.GetByWatchedFolderIdAsync(folderId, cancellationToken);
            foreach (var file in files)
            {
                await _storageService.DeleteArtifactsAsync(file.Id, cancellationToken);
                await _trackedFileRepository.DeleteAsync(file.Id, cancellationToken);
            }
        }

        await _watchedFolderRepository.DeleteAsync(folderId, cancellationToken);
        _logger.LogInformation("Removed watched folder: {FolderId}", folderId);
    }

    public async Task PauseWatchingAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        var folder = await _watchedFolderRepository.GetByIdAsync(folderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Folder not found: {folderId}");

        await _watcherService.StopWatchingAsync(folderId, cancellationToken);
        folder.Pause();
        await _watchedFolderRepository.UpdateAsync(folder, cancellationToken);
        _logger.LogInformation("Paused watching folder: {FolderId}", folderId);
    }

    public async Task ResumeWatchingAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        var folder = await _watchedFolderRepository.GetByIdAsync(folderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Folder not found: {folderId}");

        folder.Resume();
        await _watchedFolderRepository.UpdateAsync(folder, cancellationToken);

        if (_options.EnableRealTimeWatch)
        {
            await _watcherService.StartWatchingAsync(folder, cancellationToken);
        }

        _logger.LogInformation("Resumed watching folder: {FolderId}", folderId);
    }

    public async Task<WatchedFolder> UpdateFolderPathAsync(Guid folderId, string newPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPath);

        var fullPath = Path.GetFullPath(newPath);

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {fullPath}");
        }

        var folder = await _watchedFolderRepository.GetByIdAsync(folderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Folder not found: {folderId}");

        // Check if the new path is already being watched by another folder
        var existingFolder = await _watchedFolderRepository.GetByPathAsync(fullPath, cancellationToken);
        if (existingFolder != null && existingFolder.Id != folderId)
        {
            throw new InvalidOperationException($"Path is already being watched by another folder: {fullPath}");
        }

        var oldPath = folder.Path;

        // Stop watching old path
        await _watcherService.StopWatchingAsync(folderId, cancellationToken);

        // Update the path (this also reactivates if was Invalid)
        folder.UpdatePath(fullPath);
        await _watchedFolderRepository.UpdateAsync(folder, cancellationToken);

        // Start watching new path if enabled
        if (_options.EnableRealTimeWatch && folder.Status == Enums.WatcherStatus.Active)
        {
            await _watcherService.StartWatchingAsync(folder, cancellationToken);
        }

        _logger.LogInformation("Updated folder path: {OldPath} -> {NewPath} (ID: {FolderId})", oldPath, fullPath, folderId);

        return folder;
    }

    public async Task<WatchedFolder?> GetWatchedFolderAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        return await _watchedFolderRepository.GetByIdAsync(folderId, cancellationToken);
    }

    public async Task<List<WatchedFolder>> GetAllWatchedFoldersAsync(CancellationToken cancellationToken = default)
    {
        return await _watchedFolderRepository.GetAllAsync(cancellationToken);
    }

    #endregion

    #region File Operations

    public async Task<TrackedFile> MemorizeFileAsync(string sourcePath, Guid? watchedFolderId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullPath = Path.GetFullPath(sourcePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File not found: {fullPath}");
        }

        var fileInfo = new FileInfo(fullPath);

        // Check file size limit
        if (fileInfo.Length > _options.MaxFileSizeMB * 1024 * 1024)
        {
            throw new InvalidOperationException(
                $"File exceeds maximum size of {_options.MaxFileSizeMB}MB: {fullPath}");
        }

        // Check if already tracked
        var existing = await _trackedFileRepository.GetBySourcePathAsync(fullPath, cancellationToken);
        if (existing != null)
        {
            if (existing.Status == TrackedFileStatus.Memorized)
            {
                _logger.LogDebug("File already memorized: {Path}", fullPath);
                return existing;
            }
            // If not memorized, queue for reprocessing
            existing.MarkAsQueued();
            await _trackedFileRepository.UpdateAsync(existing, cancellationToken);
            return existing;
        }

        // Create new tracked file
        var trackedFile = TrackedFile.Create(fullPath, watchedFolderId);

        // Compute hash
        var hash = await _hashService.ComputeHashAsync(fullPath, cancellationToken);
        trackedFile.UpdateFileInfo(fileInfo.Length, fileInfo.LastWriteTimeUtc, hash);
        trackedFile.MarkAsQueued();

        await _trackedFileRepository.AddAsync(trackedFile, cancellationToken);

        _logger.LogInformation("Queued file for memorization: {Path}", fullPath);

        return trackedFile;
    }

    public async Task UnmemorizeFileAsync(Guid fileId, bool deleteArtifacts = true, CancellationToken cancellationToken = default)
    {
        var file = await _trackedFileRepository.GetByIdAsync(fileId, cancellationToken)
            ?? throw new KeyNotFoundException($"Tracked file not found: {fileId}");

        if (deleteArtifacts)
        {
            await _storageService.DeleteArtifactsAsync(fileId, cancellationToken);
        }

        file.ResetToUntracked();
        await _trackedFileRepository.UpdateAsync(file, cancellationToken);

        _logger.LogInformation("Unmemorized file: {FileId}", fileId);
    }

    public async Task<TrackedFile> ReprocessFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await _trackedFileRepository.GetByIdAsync(fileId, cancellationToken)
            ?? throw new KeyNotFoundException($"Tracked file not found: {fileId}");

        if (!File.Exists(file.SourcePath))
        {
            file.MarkAsOrphaned();
            await _trackedFileRepository.UpdateAsync(file, cancellationToken);
            throw new FileNotFoundException($"Source file no longer exists: {file.SourcePath}");
        }

        var fileInfo = new FileInfo(file.SourcePath);
        var hash = await _hashService.ComputeHashAsync(file.SourcePath, cancellationToken);

        file.UpdateFileInfo(fileInfo.Length, fileInfo.LastWriteTimeUtc, hash);
        file.MarkAsQueued();
        await _trackedFileRepository.UpdateAsync(file, cancellationToken);

        _logger.LogInformation("Queued file for reprocessing: {FileId}", fileId);

        return file;
    }

    public async Task<TrackedFile?> GetTrackedFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        return await _trackedFileRepository.GetByIdAsync(fileId, cancellationToken);
    }

    public async Task<TrackedFile?> GetTrackedFileByPathAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return await _trackedFileRepository.GetBySourcePathAsync(Path.GetFullPath(sourcePath), cancellationToken);
    }

    public async Task<List<TrackedFile>> GetTrackedFilesByFolderAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        return await _trackedFileRepository.GetByWatchedFolderIdAsync(folderId, cancellationToken);
    }

    #endregion

    #region Scan Operations

    public async Task<ScanResult> ScanFolderAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();

        var folder = await _watchedFolderRepository.GetByIdAsync(folderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Folder not found: {folderId}");

        if (!Directory.Exists(folder.Path))
        {
            folder.MarkAsInvalid();
            await _watchedFolderRepository.UpdateAsync(folder, cancellationToken);
            throw new DirectoryNotFoundException($"Folder no longer exists: {folder.Path}");
        }

        var searchOption = folder.IsRecursive
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        var allFiles = Directory.GetFiles(folder.Path, "*.*", searchOption);
        var matchingFiles = allFiles.Where(f => folder.ShouldIncludeFile(Path.GetFileName(f))).ToList();
        var existingPaths = new HashSet<string>(matchingFiles, StringComparer.OrdinalIgnoreCase);

        int newFilesQueued = 0;
        int changedFilesQueued = 0;
        int skippedFiles = 0;

        foreach (var filePath in matchingFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var existing = await _trackedFileRepository.GetBySourcePathAsync(filePath, cancellationToken);

                if (existing == null)
                {
                    // New file
                    if (folder.AutoMemorize)
                    {
                        await MemorizeFileAsync(filePath, folderId, cancellationToken);
                        newFilesQueued++;
                    }
                    else
                    {
                        skippedFiles++;
                    }
                }
                else
                {
                    // Check for changes
                    var fileInfo = new FileInfo(filePath);
                    var currentHash = await _hashService.ComputeHashAsync(filePath, cancellationToken);

                    if (existing.HasChangedSince(currentHash))
                    {
                        existing.UpdateFileInfo(fileInfo.Length, fileInfo.LastWriteTimeUtc, currentHash);
                        existing.MarkAsStale();
                        existing.MarkAsQueued();
                        await _trackedFileRepository.UpdateAsync(existing, cancellationToken);
                        changedFilesQueued++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing file during scan: {Path}", filePath);
                errors.Add($"{filePath}: {ex.Message}");
            }
        }

        // Mark orphaned files
        var orphanedCount = await _trackedFileRepository.MarkOrphanedFilesAsync(
            folderId, existingPaths, cancellationToken);

        folder.UpdateLastScanned();
        await _watchedFolderRepository.UpdateAsync(folder, cancellationToken);

        stopwatch.Stop();

        _logger.LogInformation(
            "Scan completed for {Path}: {Total} files, {New} new, {Changed} changed, {Orphaned} orphaned",
            folder.Path, matchingFiles.Count, newFilesQueued, changedFilesQueued, orphanedCount);

        return new ScanResult
        {
            FolderId = folderId,
            TotalFilesFound = matchingFiles.Count,
            NewFilesQueued = newFilesQueued,
            ChangedFilesQueued = changedFilesQueued,
            OrphanedFilesDetected = orphanedCount,
            SkippedFiles = skippedFiles,
            Errors = errors,
            Duration = stopwatch.Elapsed
        };
    }

    public async Task<SyncResult> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();

        var folders = await _watchedFolderRepository.GetActiveAsync(cancellationToken);
        int totalFilesProcessed = 0;
        int totalFilesQueued = 0;
        int totalOrphansCleaned = 0;

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await ScanFolderAsync(folder.Id, cancellationToken);
                totalFilesProcessed += result.TotalFilesFound;
                totalFilesQueued += result.NewFilesQueued + result.ChangedFilesQueued;
                totalOrphansCleaned += result.OrphanedFilesDetected;
                errors.AddRange(result.Errors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing folder: {FolderId}", folder.Id);
                errors.Add($"Folder {folder.Path}: {ex.Message}");
            }
        }

        // Optionally cleanup orphans
        if (_options.AutoCleanupOrphans)
        {
            totalOrphansCleaned += await CleanupOrphanedFilesAsync(cancellationToken);
        }

        stopwatch.Stop();

        return new SyncResult
        {
            FoldersScanned = folders.Count,
            FilesProcessed = totalFilesProcessed,
            FilesQueued = totalFilesQueued,
            OrphanedFilesCleaned = totalOrphansCleaned,
            Errors = errors,
            Duration = stopwatch.Elapsed
        };
    }

    public async Task<int> CleanupOrphanedFilesAsync(CancellationToken cancellationToken = default)
    {
        var orphanedFiles = await _trackedFileRepository.GetOrphanedFilesAsync(cancellationToken);
        int cleaned = 0;

        foreach (var file in orphanedFiles)
        {
            await _storageService.DeleteArtifactsAsync(file.Id, cancellationToken);
            file.MarkAsRemoved();
            await _trackedFileRepository.UpdateAsync(file, cancellationToken);
            cleaned++;
        }

        if (cleaned > 0)
        {
            _logger.LogInformation("Cleaned up {Count} orphaned files", cleaned);
        }

        return cleaned;
    }

    public async Task<VaultStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var activeWatchers = _watcherService.GetAllWatchers().Count(w => w.IsActive);

        var totalCount = await _trackedFileRepository.GetTotalCountAsync(cancellationToken);
        var memorizedCount = await _trackedFileRepository.GetCountByStatusAsync(TrackedFileStatus.Memorized, cancellationToken);
        var queuedCount = await _trackedFileRepository.GetCountByStatusAsync(TrackedFileStatus.Queued, cancellationToken);
        var processingCount = await _trackedFileRepository.GetCountByStatusAsync(TrackedFileStatus.Processing, cancellationToken);
        var staleCount = await _trackedFileRepository.GetCountByStatusAsync(TrackedFileStatus.Stale, cancellationToken);
        var orphanedCount = await _trackedFileRepository.GetCountByStatusAsync(TrackedFileStatus.Orphaned, cancellationToken);
        var errorCount = await _trackedFileRepository.GetCountByStatusAsync(TrackedFileStatus.Error, cancellationToken);

        return new VaultStatus
        {
            IsEnabled = _options.Enabled,
            ActiveWatchers = activeWatchers,
            TotalTrackedFiles = totalCount,
            MemorizedFiles = memorizedCount,
            QueuedFiles = queuedCount,
            ProcessingFiles = processingCount,
            StaleFiles = staleCount,
            OrphanedFiles = orphanedCount,
            ErrorFiles = errorCount
        };
    }

    #endregion

    #region Event Handlers

    private async void OnFileCreated(object? sender, FileChangeEventArgs e)
    {
        try
        {
            var folder = await _watchedFolderRepository.GetByIdAsync(e.WatchedFolderId);
            if (folder?.AutoMemorize == true)
            {
                await MemorizeFileAsync(e.FilePath, e.WatchedFolderId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file created event: {Path}", e.FilePath);
        }
    }

    private async void OnFileModified(object? sender, FileChangeEventArgs e)
    {
        try
        {
            var trackedFile = await _trackedFileRepository.GetBySourcePathAsync(e.FilePath);
            if (trackedFile != null && trackedFile.Status == TrackedFileStatus.Memorized)
            {
                var currentHash = await _hashService.ComputeHashAsync(e.FilePath);
                if (trackedFile.HasChangedSince(currentHash))
                {
                    trackedFile.MarkAsStale();
                    await _trackedFileRepository.UpdateAsync(trackedFile);
                    _logger.LogDebug("File marked as stale: {Path}", e.FilePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file modified event: {Path}", e.FilePath);
        }
    }

    private async void OnFileDeleted(object? sender, FileChangeEventArgs e)
    {
        try
        {
            var trackedFile = await _trackedFileRepository.GetBySourcePathAsync(e.FilePath);
            if (trackedFile != null)
            {
                trackedFile.MarkAsOrphaned();
                await _trackedFileRepository.UpdateAsync(trackedFile);
                _logger.LogDebug("File marked as orphaned: {Path}", e.FilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file deleted event: {Path}", e.FilePath);
        }
    }

    private async void OnFileRenamed(object? sender, FileRenamedEventArgs e)
    {
        try
        {
            var trackedFile = await _trackedFileRepository.GetBySourcePathAsync(e.OldFilePath);
            if (trackedFile != null)
            {
                // Mark old path as orphaned and queue new path
                trackedFile.MarkAsOrphaned();
                await _trackedFileRepository.UpdateAsync(trackedFile);

                var folder = await _watchedFolderRepository.GetByIdAsync(e.WatchedFolderId);
                if (folder?.AutoMemorize == true && folder.ShouldIncludeFile(e.FileName))
                {
                    await MemorizeFileAsync(e.FilePath, e.WatchedFolderId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file renamed event: {OldPath} -> {NewPath}", e.OldFilePath, e.FilePath);
        }
    }

    private async void OnWatcherError(object? sender, WatcherErrorEventArgs e)
    {
        try
        {
            var folder = await _watchedFolderRepository.GetByIdAsync(e.WatchedFolderId);
            if (folder != null)
            {
                folder.MarkAsError(e.Exception.Message);
                await _watchedFolderRepository.UpdateAsync(folder);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling watcher error event for folder {FolderId}", e.WatchedFolderId);
        }
    }

    #endregion
}
