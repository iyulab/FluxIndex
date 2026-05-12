using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// Background service for processing vault queue jobs.
/// Handles memorize, refresh, and remove operations.
/// Uses IServiceScopeFactory to access scoped services like IVaultPipeline.
/// </summary>
public sealed partial class VaultBackgroundService : BackgroundService
{
    private readonly ILogger<VaultBackgroundService> _logger;
    private readonly IVaultQueueService _queueService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVaultStorageService _storage;
    private readonly FileVaultOptions _options;
    private readonly SemaphoreSlim _concurrencyLimiter;

    /// <summary>
    /// Active polling interval (when queue has items).
    /// </summary>
    private const int ActivePollingMs = 2000;

    /// <summary>
    /// Idle polling interval (when queue is empty).
    /// </summary>
    private const int IdlePollingMs = 10000;

    public VaultBackgroundService(
        ILogger<VaultBackgroundService> logger,
        IVaultQueueService queueService,
        IServiceScopeFactory scopeFactory,
        IVaultStorageService storage,
        IOptions<FileVaultOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _options = options?.Value ?? new FileVaultOptions();
        _concurrencyLimiter = new SemaphoreSlim(_options.MaxConcurrentProcessing);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogServiceStarting(_logger);

        // Recover any stuck jobs from previous run
        var recovered = await _queueService.RecoverStuckJobsAsync(stoppingToken);
        if (recovered > 0)
        {
            LogRecoveredStuckJobs(_logger, recovered);
        }

        // Recover entries in partial removal or deleted states
        await RecoverPartialRemovalsAsync(stoppingToken);

        LogServiceStarted(_logger);

        var consecutiveEmptyCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_options.EnableBackgroundProcessing || _queueService.IsPaused)
                {
                    await Task.Delay(IdlePollingMs, stoppingToken);
                    continue;
                }

                var job = await _queueService.DequeueAsync(stoppingToken);

                if (job == null)
                {
                    consecutiveEmptyCount++;
                    // Use longer interval when queue has been empty for a while
                    var delay = consecutiveEmptyCount > 5 ? IdlePollingMs : ActivePollingMs;
                    await Task.Delay(delay, stoppingToken);
                    continue;
                }

                consecutiveEmptyCount = 0;

                // Process job with concurrency limit
                await _concurrencyLimiter.WaitAsync(stoppingToken);

                _ = ProcessJobAsync(job, stoppingToken)
                    .ContinueWith(_ => _concurrencyLimiter.Release(), TaskScheduler.Default);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogServiceLoopError(_logger, ex);
                await Task.Delay(1000, stoppingToken);
            }
        }

        LogServiceStopped(_logger);
    }

    private async Task ProcessJobAsync(VaultJob job, CancellationToken ct)
    {
        try
        {
            LogProcessingJob(_logger, job.JobType, job.Id, job.FilePath);

            // Load or create entry
            var entry = VaultEntry.LoadByHash(job.FilepathHash, _storage.BasePath)
                        ?? VaultEntry.Create(job.FilePath, _storage.BasePath);

            // Use scope to access scoped services like IVaultPipeline
            using var scope = _scopeFactory.CreateScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<IVaultPipeline>();

            var memorizeOptions = new MemorizeOptions
            {
                MaxChunkSize = _options.Chunking.MaxChunkSize,
                OverlapSize = _options.Chunking.OverlapSize,
                Strategy = _options.Chunking.Strategy,
                Language = _options.Chunking.Language,
                // Wire checkpoint hooks so the pipeline uses per-chunk processing for crash-resilient
                // resume. After each chunk is fully embedded+stored, persist progress so a host
                // restart can resume from chunk N+1 rather than restarting from 0.
                StartFromChunkIndex = job.LastCompletedChunkIndex,
                CheckpointCallback = async (chunkIndex, callbackCt) =>
                    await _queueService.UpdateCheckpointAsync(job.Id, chunkIndex, callbackCt),
            };

            switch (job.JobType)
            {
                case VaultJobType.Memorize:
                    await pipeline.MemorizeAsync(entry, memorizeOptions, ct);
                    break;

                case VaultJobType.Refresh:
                    await pipeline.RefreshAsync(entry, memorizeOptions, ct);
                    break;

                case VaultJobType.Remove:
                    await ProcessRemoveJobAsync(entry, pipeline, ct);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown job type: {job.JobType}");
            }

            await _queueService.CompleteAsync(job.Id, ct);
            LogCompletedJob(_logger, job.JobType, job.Id);
        }
        catch (FileNotFoundException ex)
        {
            LogFileNotFoundForJob(_logger, job.Id, job.FilePath);
            await _queueService.FailAsync(job.Id, $"File not found: {ex.Message}", ct);
        }
        catch (Exception ex)
        {
            LogFailedJob(_logger, ex, job.JobType, job.Id, job.FilePath);
            await _queueService.FailAsync(job.Id, ex.Message, ct);

            // Auto-retry if enabled
            if (_options.EnableAutoRetry && job.CanRetry)
            {
                await Task.Delay(_options.RetryDelayMs, ct);
                await _queueService.RetryAsync(job.Id, ct);
            }
        }
    }

    /// <summary>
    /// Processes a remove job with phased execution for atomicity.
    /// Phase 1: Delete vectors from vector store
    /// Phase 2: Delete storage (entry directory)
    /// </summary>
    private async Task ProcessRemoveJobAsync(VaultEntry entry, IVaultPipeline pipeline, CancellationToken ct)
    {
        LogProcessingRemove(_logger, entry.SourcePath);

        // Check if we're recovering from a partial removal
        if (entry.SyncStatus == SyncStatus.RemovalPartial && entry.RemovalPhase == "Vector")
        {
            // Vector already deleted, skip to storage deletion
            LogRecoveringPartialRemoval(_logger, entry.SourcePath);
        }
        else
        {
            // Phase 1: Mark as removal pending and delete from vector store
            entry.MarkRemovalPending();
            entry.SaveMetadata();

            try
            {
                await pipeline.RemoveAsync(entry, ct);

                // Mark vector phase complete
                entry.MarkRemovalPartial("Vector");
                entry.SaveMetadata();
                LogVectorRemovalCompleted(_logger, entry.SourcePath);
            }
            catch (Exception ex)
            {
                LogVectorRemovalFailed(_logger, ex, entry.SourcePath);
                entry.MarkSyncError($"Vector removal failed: {ex.Message}");
                entry.SaveMetadata();
                throw;
            }
        }

        // Phase 2: Delete entry storage
        try
        {
            await _storage.DeleteEntryStorageAsync(entry, ct);
            LogStorageRemovalCompleted(_logger, entry.SourcePath);
            // Entry directory is now deleted, no need to save metadata
        }
        catch (Exception ex)
        {
            LogStorageRemovalFailed(_logger, ex, entry.SourcePath);
            // Entry is in RemovalPartial state with Vector phase complete
            // Next retry will skip vector deletion
            throw;
        }
    }

    /// <summary>
    /// Recovers entries that are in partial removal state from previous runs.
    /// Should be called during startup after RecoverStuckJobsAsync.
    /// </summary>
    public async Task RecoverPartialRemovalsAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_storage.BasePath))
            return;

        var recovered = 0;
        foreach (var dir in Directory.GetDirectories(_storage.BasePath))
        {
            ct.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(dir);
            var entry = VaultEntry.LoadByHash(dirName, _storage.BasePath);

            if (entry == null)
                continue;

            // Check for entries stuck in removal states
            if (entry.SyncStatus == SyncStatus.RemovalPending ||
                entry.SyncStatus == SyncStatus.RemovalPartial)
            {
                LogRecoveringPartialRemovalStartup(_logger, entry.SourcePath, entry.SyncStatus, entry.RemovalPhase ?? "none");

                await _queueService.EnqueueRemoveAsync(
                    entry.FilepathHash,
                    entry.SourcePath,
                    VaultJobPriority.High,
                    ct);

                recovered++;
            }
            // Also recover entries marked as SourceDeleted that weren't queued
            else if (entry.SyncStatus == SyncStatus.SourceDeleted)
            {
                LogRequeueingSourceDeleted(_logger, entry.SourcePath);

                await _queueService.EnqueueRemoveAsync(
                    entry.FilepathHash,
                    entry.SourcePath,
                    VaultJobPriority.Normal,
                    ct);

                recovered++;
            }
        }

        if (recovered > 0)
        {
            LogRecoveredRemovalEntries(_logger, recovered);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        LogServiceStopping(_logger);

        // Wait for active jobs to complete (with timeout)
        var waitStart = DateTime.UtcNow;
        var maxWait = TimeSpan.FromSeconds(30);

        while (_concurrencyLimiter.CurrentCount < _options.MaxConcurrentProcessing)
        {
            if (DateTime.UtcNow - waitStart > maxWait)
            {
                LogTimeoutWaiting(_logger);
                break;
            }

            await Task.Delay(500, cancellationToken);
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _concurrencyLimiter.Dispose();
        base.Dispose();
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Vault background service starting...")]
    private static partial void LogServiceStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovered {Count} stuck jobs from previous run")]
    private static partial void LogRecoveredStuckJobs(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Vault background service started")]
    private static partial void LogServiceStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error in vault background service loop")]
    private static partial void LogServiceLoopError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Vault background service stopped")]
    private static partial void LogServiceStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing {JobType} job {JobId}: {FilePath}")]
    private static partial void LogProcessingJob(ILogger logger, VaultJobType jobType, Guid jobId, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Completed {JobType} job {JobId}")]
    private static partial void LogCompletedJob(ILogger logger, VaultJobType jobType, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "File not found for job {JobId}: {FilePath}")]
    private static partial void LogFileNotFoundForJob(ILogger logger, Guid jobId, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed {JobType} job {JobId}: {FilePath}")]
    private static partial void LogFailedJob(ILogger logger, Exception exception, VaultJobType jobType, Guid jobId, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing remove job for {SourcePath}")]
    private static partial void LogProcessingRemove(ILogger logger, string sourcePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovering partial removal for {SourcePath}, skipping vector deletion")]
    private static partial void LogRecoveringPartialRemoval(ILogger logger, string sourcePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Vector removal completed for {SourcePath}")]
    private static partial void LogVectorRemovalCompleted(ILogger logger, string sourcePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vector removal failed for {SourcePath}")]
    private static partial void LogVectorRemovalFailed(ILogger logger, Exception exception, string sourcePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Storage removal completed for {SourcePath}")]
    private static partial void LogStorageRemovalCompleted(ILogger logger, string sourcePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Storage removal failed for {SourcePath}")]
    private static partial void LogStorageRemovalFailed(ILogger logger, Exception exception, string sourcePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovering partial removal for {SourcePath} (status: {Status}, phase: {Phase})")]
    private static partial void LogRecoveringPartialRemovalStartup(ILogger logger, string sourcePath, SyncStatus status, string phase);

    [LoggerMessage(Level = LogLevel.Information, Message = "Re-queueing source-deleted entry for {SourcePath}")]
    private static partial void LogRequeueingSourceDeleted(ILogger logger, string sourcePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovered {Count} entries in removal/deleted states")]
    private static partial void LogRecoveredRemovalEntries(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Vault background service stopping...")]
    private static partial void LogServiceStopping(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Timeout waiting for active jobs to complete")]
    private static partial void LogTimeoutWaiting(ILogger logger);

    #endregion
}
