using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Shared.DTOs.Documents;
using FluxIndex.Stack.Vault.Entities;
using FluxIndex.Stack.Vault.Enums;
using FluxIndex.Stack.Vault.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Api.BackgroundServices;

/// <summary>
/// Background service that manages file system watching and processes queued vault files.
/// </summary>
public class VaultBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IFileWatcherService _fileWatcherService;
    private readonly ILogger<VaultBackgroundService> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _idleInterval = TimeSpan.FromSeconds(10);

    public VaultBackgroundService(
        IServiceProvider serviceProvider,
        IFileWatcherService fileWatcherService,
        ILogger<VaultBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _fileWatcherService = fileWatcherService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Vault background service started");

        // Subscribe to file watcher events
        SubscribeToFileWatcherEvents();

        // Initialize watchers for all active folders
        await InitializeWatchersAsync(stoppingToken);

        // Recover any files stuck in Processing state
        await RecoverStuckFilesAsync(stoppingToken);

        // Main processing loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var vaultService = scope.ServiceProvider.GetService<IVaultService>();

                if (vaultService == null)
                {
                    _logger.LogWarning("IVaultService not registered, waiting...");
                    await Task.Delay(_idleInterval, stoppingToken);
                    continue;
                }

                var status = await vaultService.GetStatusAsync(stoppingToken);

                if (status.QueuedFiles > 0)
                {
                    _logger.LogDebug("Processing queued vault files ({QueuedCount} queued)", status.QueuedFiles);
                    await ProcessNextQueuedFileAsync(stoppingToken);
                    await Task.Delay(_pollingInterval, stoppingToken);
                }
                else
                {
                    await Task.Delay(_idleInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in vault background service");
                await Task.Delay(_idleInterval, stoppingToken);
            }
        }

        // Cleanup on shutdown
        await ShutdownAsync();
        _logger.LogInformation("Vault background service stopped");
    }

    private void SubscribeToFileWatcherEvents()
    {
        _fileWatcherService.FileCreated += OnFileCreated;
        _fileWatcherService.FileModified += OnFileModified;
        _fileWatcherService.FileDeleted += OnFileDeleted;
        _fileWatcherService.FileRenamed += OnFileRenamed;
        _fileWatcherService.Error += OnWatcherError;
    }

    private void UnsubscribeFromFileWatcherEvents()
    {
        _fileWatcherService.FileCreated -= OnFileCreated;
        _fileWatcherService.FileModified -= OnFileModified;
        _fileWatcherService.FileDeleted -= OnFileDeleted;
        _fileWatcherService.FileRenamed -= OnFileRenamed;
        _fileWatcherService.Error -= OnWatcherError;
    }

    private async Task InitializeWatchersAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var watchedFolderRepo = scope.ServiceProvider.GetRequiredService<IWatchedFolderRepository>();

            var activeFolders = await watchedFolderRepo.GetActiveAsync(stoppingToken);

            foreach (var folder in activeFolders)
            {
                try
                {
                    await _fileWatcherService.StartWatchingAsync(folder, stoppingToken);
                    _logger.LogInformation("Started watching folder: {Path}", folder.Path);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start watcher for folder: {Path}", folder.Path);
                }
            }

            _logger.LogInformation("Initialized {Count} file watchers", activeFolders.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing file watchers");
        }
    }

    private async Task RecoverStuckFilesAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var trackedFileRepo = scope.ServiceProvider.GetRequiredService<ITrackedFileRepository>();

            var processingFiles = await trackedFileRepo.GetByStatusAsync(TrackedFileStatus.Processing, stoppingToken);

            if (processingFiles.Count > 0)
            {
                var ids = processingFiles.Select(f => f.Id);
                var recoveredCount = await trackedFileRepo.BulkUpdateStatusAsync(
                    ids, TrackedFileStatus.Queued, stoppingToken);

                _logger.LogWarning("Recovered {Count} stuck files from Processing state", recoveredCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recovering stuck files on startup");
        }
    }

    private async Task ProcessNextQueuedFileAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var trackedFileRepo = scope.ServiceProvider.GetRequiredService<ITrackedFileRepository>();
        var vaultService = scope.ServiceProvider.GetRequiredService<IVaultService>();

        var nextFile = await trackedFileRepo.GetNextQueuedAsync(stoppingToken);
        if (nextFile == null)
            return;

        try
        {
            // Mark as processing
            nextFile.MarkAsProcessing();
            await trackedFileRepo.UpdateAsync(nextFile, stoppingToken);

            // Process the file through VaultService
            await ProcessFileAsync(nextFile, scope.ServiceProvider, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file: {FilePath}", nextFile.SourcePath);
            nextFile.MarkAsError(ex.Message);
            await trackedFileRepo.UpdateAsync(nextFile, stoppingToken);
        }
    }

    private async Task ProcessFileAsync(
        TrackedFile trackedFile,
        IServiceProvider serviceProvider,
        CancellationToken stoppingToken)
    {
        var trackedFileRepo = serviceProvider.GetRequiredService<ITrackedFileRepository>();
        var contentHashService = serviceProvider.GetRequiredService<IContentHashService>();
        var documentService = serviceProvider.GetService<IDocumentService>();

        // Verify file still exists
        if (!File.Exists(trackedFile.SourcePath))
        {
            trackedFile.MarkAsOrphaned();
            await trackedFileRepo.UpdateAsync(trackedFile, stoppingToken);
            _logger.LogWarning("File no longer exists, marked as orphaned: {FilePath}", trackedFile.SourcePath);
            return;
        }

        // Calculate content hash and update file info
        var contentHash = await contentHashService.ComputeHashAsync(trackedFile.SourcePath, stoppingToken);
        var fileInfo = new FileInfo(trackedFile.SourcePath);
        trackedFile.UpdateFileInfo(fileInfo.Length, fileInfo.LastWriteTimeUtc, contentHash);

        // If document service is not available, just update file info without indexing
        if (documentService == null)
        {
            _logger.LogWarning("IDocumentService not available, file tracked but not indexed: {FilePath}", trackedFile.SourcePath);
            trackedFile.MarkAsMemorized(Guid.Empty);
            await trackedFileRepo.UpdateAsync(trackedFile, stoppingToken);
            return;
        }

        // Create document and queue for indexing via DocumentService
        var uploadRequest = new UploadDocumentRequest
        {
            Title = trackedFile.FileName,
            SourceType = "vault",
            Metadata = new Dictionary<string, object>
            {
                ["vault_source_path"] = trackedFile.SourcePath,
                ["vault_tracked_file_id"] = trackedFile.Id,
                ["vault_watched_folder_id"] = trackedFile.WatchedFolderId,
                ["vault_content_hash"] = contentHash
            }
        };

        await using var fileStream = File.OpenRead(trackedFile.SourcePath);
        var response = await documentService.UploadAsync(uploadRequest, fileStream, trackedFile.FileName, stoppingToken);

        // Update TrackedFile with the created Document ID
        trackedFile.MarkAsMemorized(response.DocumentId);
        await trackedFileRepo.UpdateAsync(trackedFile, stoppingToken);

        _logger.LogInformation(
            "Vault file indexed: {FileName} -> Document {DocumentId}, Job {JobId}",
            trackedFile.FileName, response.DocumentId, response.JobId);
    }

    private async Task ShutdownAsync()
    {
        UnsubscribeFromFileWatcherEvents();
        await _fileWatcherService.StopAllAsync();
    }

    #region File Watcher Event Handlers

    private void OnFileCreated(object? sender, FileChangeEventArgs e)
    {
        _logger.LogDebug("File created: {FilePath}", e.FilePath);
        _ = HandleFileCreatedAsync(e);
    }

    private void OnFileModified(object? sender, FileChangeEventArgs e)
    {
        _logger.LogDebug("File modified: {FilePath}", e.FilePath);
        _ = HandleFileModifiedAsync(e);
    }

    private void OnFileDeleted(object? sender, FileChangeEventArgs e)
    {
        _logger.LogDebug("File deleted: {FilePath}", e.FilePath);
        _ = HandleFileDeletedAsync(e);
    }

    private void OnFileRenamed(object? sender, FileRenamedEventArgs e)
    {
        _logger.LogDebug("File renamed: {OldPath} -> {NewPath}", e.OldFilePath, e.FilePath);
        _ = HandleFileRenamedAsync(e);
    }

    private void OnWatcherError(object? sender, WatcherErrorEventArgs e)
    {
        _logger.LogError(e.Exception, "File watcher error for folder {FolderId}", e.WatchedFolderId);
    }

    private async Task HandleFileCreatedAsync(FileChangeEventArgs e)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var watchedFolderRepo = scope.ServiceProvider.GetRequiredService<IWatchedFolderRepository>();
            var trackedFileRepo = scope.ServiceProvider.GetRequiredService<ITrackedFileRepository>();

            var folder = await watchedFolderRepo.GetByIdAsync(e.WatchedFolderId);
            if (folder == null || !folder.AutoMemorize)
                return;

            // Check if file matches patterns
            if (!folder.ShouldIncludeFile(e.FileName))
                return;

            // Check if already tracked
            if (await trackedFileRepo.ExistsBySourcePathAsync(e.FilePath))
                return;

            // Create and queue new tracked file
            var trackedFile = TrackedFile.Create(e.FilePath, e.WatchedFolderId);
            trackedFile.MarkAsQueued();
            await trackedFileRepo.AddAsync(trackedFile);

            _logger.LogInformation("New file queued for processing: {FilePath}", e.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file created event: {FilePath}", e.FilePath);
        }
    }

    private async Task HandleFileModifiedAsync(FileChangeEventArgs e)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var trackedFileRepo = scope.ServiceProvider.GetRequiredService<ITrackedFileRepository>();
            var contentHashService = scope.ServiceProvider.GetRequiredService<IContentHashService>();

            var trackedFile = await trackedFileRepo.GetBySourcePathAsync(e.FilePath);
            if (trackedFile == null)
                return;

            // Check if content actually changed
            var newHash = await contentHashService.ComputeHashAsync(e.FilePath);
            if (!trackedFile.HasChangedSince(newHash))
                return;

            // Mark as stale to trigger reprocessing
            trackedFile.MarkAsStale();
            trackedFile.MarkAsQueued();
            await trackedFileRepo.UpdateAsync(trackedFile);

            _logger.LogInformation("Modified file queued for reprocessing: {FilePath}", e.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file modified event: {FilePath}", e.FilePath);
        }
    }

    private async Task HandleFileDeletedAsync(FileChangeEventArgs e)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var trackedFileRepo = scope.ServiceProvider.GetRequiredService<ITrackedFileRepository>();

            var trackedFile = await trackedFileRepo.GetBySourcePathAsync(e.FilePath);
            if (trackedFile == null)
                return;

            trackedFile.MarkAsOrphaned();
            await trackedFileRepo.UpdateAsync(trackedFile);

            _logger.LogInformation("Deleted file marked as orphaned: {FilePath}", e.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file deleted event: {FilePath}", e.FilePath);
        }
    }

    private async Task HandleFileRenamedAsync(FileRenamedEventArgs e)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var trackedFileRepo = scope.ServiceProvider.GetRequiredService<ITrackedFileRepository>();
            var watchedFolderRepo = scope.ServiceProvider.GetRequiredService<IWatchedFolderRepository>();

            // Handle as delete old + create new
            var oldTrackedFile = await trackedFileRepo.GetBySourcePathAsync(e.OldFilePath);
            if (oldTrackedFile != null)
            {
                oldTrackedFile.MarkAsOrphaned();
                await trackedFileRepo.UpdateAsync(oldTrackedFile);
            }

            // Check if new path should be tracked
            var folder = await watchedFolderRepo.GetByIdAsync(e.WatchedFolderId);
            if (folder?.AutoMemorize == true && folder.ShouldIncludeFile(e.FileName))
            {
                if (!await trackedFileRepo.ExistsBySourcePathAsync(e.FilePath))
                {
                    var newTrackedFile = TrackedFile.Create(e.FilePath, e.WatchedFolderId);
                    newTrackedFile.MarkAsQueued();
                    await trackedFileRepo.AddAsync(newTrackedFile);
                }
            }

            _logger.LogInformation("File renamed: {OldPath} -> {NewPath}", e.OldFilePath, e.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file renamed event: {OldPath} -> {NewPath}", e.OldFilePath, e.FilePath);
        }
    }

    #endregion
}
