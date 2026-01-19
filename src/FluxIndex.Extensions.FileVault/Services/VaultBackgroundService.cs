using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// Background service for processing vault queue jobs.
/// Handles memorize, refresh, and remove operations.
/// </summary>
public sealed class VaultBackgroundService : BackgroundService
{
    private readonly ILogger<VaultBackgroundService> _logger;
    private readonly IVaultQueueService _queueService;
    private readonly IVaultPipeline _pipeline;
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
        IVaultPipeline pipeline,
        IVaultStorageService storage,
        IOptions<FileVaultOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _options = options?.Value ?? new FileVaultOptions();
        _concurrencyLimiter = new SemaphoreSlim(_options.MaxConcurrentProcessing);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Vault background service starting...");

        // Recover any stuck jobs from previous run
        var recovered = await _queueService.RecoverStuckJobsAsync(stoppingToken);
        if (recovered > 0)
        {
            _logger.LogInformation("Recovered {Count} stuck jobs from previous run", recovered);
        }

        // Recover entries in partial removal or deleted states
        await RecoverPartialRemovalsAsync(stoppingToken);

        _logger.LogInformation("Vault background service started");

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
                _logger.LogError(ex, "Error in vault background service loop");
                await Task.Delay(1000, stoppingToken);
            }
        }

        _logger.LogInformation("Vault background service stopped");
    }

    private async Task ProcessJobAsync(VaultJob job, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Processing {JobType} job {JobId}: {FilePath}", job.JobType, job.Id, job.FilePath);

            // Load or create entry
            var entry = VaultEntry.LoadByHash(job.FilepathHash, _storage.BasePath)
                        ?? VaultEntry.Create(job.FilePath, _storage.BasePath);

            switch (job.JobType)
            {
                case VaultJobType.Memorize:
                    await _pipeline.MemorizeAsync(entry, null, ct);
                    break;

                case VaultJobType.Refresh:
                    await _pipeline.RefreshAsync(entry, null, ct);
                    break;

                case VaultJobType.Remove:
                    await ProcessRemoveJobAsync(entry, ct);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown job type: {job.JobType}");
            }

            await _queueService.CompleteAsync(job.Id, ct);
            _logger.LogDebug("Completed {JobType} job {JobId}", job.JobType, job.Id);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning("File not found for job {JobId}: {FilePath}", job.Id, job.FilePath);
            await _queueService.FailAsync(job.Id, $"File not found: {ex.Message}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed {JobType} job {JobId}: {FilePath}", job.JobType, job.Id, job.FilePath);
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
    private async Task ProcessRemoveJobAsync(VaultEntry entry, CancellationToken ct)
    {
        _logger.LogInformation("Processing remove job for {SourcePath}", entry.SourcePath);

        // Check if we're recovering from a partial removal
        if (entry.SyncStatus == SyncStatus.RemovalPartial && entry.RemovalPhase == "Vector")
        {
            // Vector already deleted, skip to storage deletion
            _logger.LogInformation("Recovering partial removal for {SourcePath}, skipping vector deletion", entry.SourcePath);
        }
        else
        {
            // Phase 1: Mark as removal pending and delete from vector store
            entry.MarkRemovalPending();
            entry.SaveMetadata();

            try
            {
                await _pipeline.RemoveAsync(entry, ct);

                // Mark vector phase complete
                entry.MarkRemovalPartial("Vector");
                entry.SaveMetadata();
                _logger.LogDebug("Vector removal completed for {SourcePath}", entry.SourcePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Vector removal failed for {SourcePath}", entry.SourcePath);
                entry.MarkSyncError($"Vector removal failed: {ex.Message}");
                entry.SaveMetadata();
                throw;
            }
        }

        // Phase 2: Delete entry storage
        try
        {
            await _storage.DeleteEntryStorageAsync(entry, ct);
            _logger.LogInformation("Storage removal completed for {SourcePath}", entry.SourcePath);
            // Entry directory is now deleted, no need to save metadata
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage removal failed for {SourcePath}", entry.SourcePath);
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
                _logger.LogInformation("Recovering partial removal for {SourcePath} (status: {Status}, phase: {Phase})",
                    entry.SourcePath, entry.SyncStatus, entry.RemovalPhase ?? "none");

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
                _logger.LogInformation("Re-queueing source-deleted entry for {SourcePath}", entry.SourcePath);

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
            _logger.LogInformation("Recovered {Count} entries in removal/deleted states", recovered);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Vault background service stopping...");

        // Wait for active jobs to complete (with timeout)
        var waitStart = DateTime.UtcNow;
        var maxWait = TimeSpan.FromSeconds(30);

        while (_concurrencyLimiter.CurrentCount < _options.MaxConcurrentProcessing)
        {
            if (DateTime.UtcNow - waitStart > maxWait)
            {
                _logger.LogWarning("Timeout waiting for active jobs to complete");
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
}
