using FluxIndex.Extensions.FileVault.Domain.Entities;

namespace FluxIndex.Extensions.FileVault.Interfaces;

/// <summary>
/// Service for managing the vault processing queue.
/// Jobs are persisted to SQLite for crash recovery.
/// </summary>
public interface IVaultQueueService
{
    /// <summary>
    /// Enqueues a memorize job.
    /// </summary>
    Task<VaultJob> EnqueueMemorizeAsync(
        string filepathHash,
        string filePath,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues a memorize job with priority.
    /// </summary>
    Task<VaultJob> EnqueueMemorizeAsync(
        string filepathHash,
        string filePath,
        VaultJobPriority priority,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues a refresh job.
    /// </summary>
    Task<VaultJob> EnqueueRefreshAsync(
        string filepathHash,
        string filePath,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues a refresh job with priority.
    /// </summary>
    Task<VaultJob> EnqueueRefreshAsync(
        string filepathHash,
        string filePath,
        VaultJobPriority priority,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues a remove job.
    /// </summary>
    Task<VaultJob> EnqueueRemoveAsync(
        string filepathHash,
        string filePath,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues a remove job with priority.
    /// </summary>
    Task<VaultJob> EnqueueRemoveAsync(
        string filepathHash,
        string filePath,
        VaultJobPriority priority,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues multiple jobs.
    /// </summary>
    Task<IReadOnlyList<VaultJob>> EnqueueBatchAsync(
        IEnumerable<(string FilepathHash, string FilePath)> files,
        VaultJobType jobType = VaultJobType.Memorize,
        VaultJobPriority priority = VaultJobPriority.Normal,
        CancellationToken ct = default);

    /// <summary>
    /// Dequeues the next job for processing.
    /// Returns null if queue is empty or paused.
    /// </summary>
    Task<VaultJob?> DequeueAsync(CancellationToken ct = default);

    /// <summary>
    /// Marks a job as completed.
    /// </summary>
    Task CompleteAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Marks a job as failed.
    /// </summary>
    Task FailAsync(Guid jobId, string errorMessage, CancellationToken ct = default);

    /// <summary>
    /// Retries a failed job.
    /// </summary>
    Task<bool> RetryAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Cancels a queued or processing job.
    /// </summary>
    Task<bool> CancelAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Gets a job by ID.
    /// </summary>
    Task<VaultJob?> GetJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Gets jobs with optional filters.
    /// </summary>
    Task<IReadOnlyList<VaultJob>> GetJobsAsync(
        VaultJobStatus? statusFilter = null,
        VaultJobType? typeFilter = null,
        int? limit = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the current queue statistics.
    /// </summary>
    Task<QueueStatistics> GetStatisticsAsync(CancellationToken ct = default);

    /// <summary>
    /// Recovers stuck jobs (Processing → Queued) after crash.
    /// Should be called on startup.
    /// </summary>
    Task<int> RecoverStuckJobsAsync(CancellationToken ct = default);

    /// <summary>
    /// Clears completed and cancelled jobs.
    /// </summary>
    Task<int> ClearCompletedAsync(CancellationToken ct = default);

    /// <summary>
    /// Clears failed jobs.
    /// </summary>
    Task<int> ClearFailedAsync(CancellationToken ct = default);

    /// <summary>
    /// Clears all jobs (use with caution).
    /// </summary>
    Task ClearAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Pauses queue processing.
    /// </summary>
    void Pause();

    /// <summary>
    /// Resumes queue processing.
    /// </summary>
    void Resume();

    /// <summary>
    /// Whether the queue is paused.
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Event raised when a job is enqueued.
    /// </summary>
    event EventHandler<VaultJob>? JobEnqueued;

    /// <summary>
    /// Event raised when a job is completed.
    /// </summary>
    event EventHandler<VaultJob>? JobCompleted;
}

/// <summary>
/// Queue statistics summary.
/// </summary>
public sealed class QueueStatistics
{
    public int QueuedCount { get; init; }
    public int ProcessingCount { get; init; }
    public int CompletedCount { get; init; }
    public int FailedCount { get; init; }
    public int CancelledCount { get; init; }
    public int TotalCount => QueuedCount + ProcessingCount + CompletedCount + FailedCount + CancelledCount;
    public bool IsPaused { get; init; }
    public DateTimeOffset? LastProcessedAt { get; init; }
    public double AverageProcessingTimeMs { get; init; }
}
