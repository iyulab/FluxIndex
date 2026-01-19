namespace FluxIndex.Extensions.FileVault.Domain.Entities;

/// <summary>
/// Represents a processing job in the vault queue.
/// Persisted to SQLite for crash recovery.
/// </summary>
public sealed class VaultJob
{
    /// <summary>
    /// Unique job identifier.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Source file path.
    /// </summary>
    public string FilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Filepath hash (entry identifier).
    /// </summary>
    public string FilepathHash { get; private set; } = string.Empty;

    /// <summary>
    /// Type of job to perform.
    /// </summary>
    public VaultJobType JobType { get; private set; }

    /// <summary>
    /// Current job status.
    /// </summary>
    public VaultJobStatus Status { get; private set; }

    /// <summary>
    /// Processing priority.
    /// </summary>
    public VaultJobPriority Priority { get; private set; }

    /// <summary>
    /// When the job was queued.
    /// </summary>
    public DateTimeOffset QueuedAt { get; private set; }

    /// <summary>
    /// When processing started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>
    /// When processing completed (success or failure).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// Number of retry attempts.
    /// </summary>
    public int RetryCount { get; private set; }

    /// <summary>
    /// Maximum retry attempts allowed.
    /// </summary>
    public int MaxRetries { get; private set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    private VaultJob() { }

    /// <summary>
    /// Creates a new job.
    /// </summary>
    public static VaultJob Create(
        string filePath,
        string filepathHash,
        VaultJobType jobType,
        VaultJobPriority priority = VaultJobPriority.Normal,
        int maxRetries = 3)
    {
        return new VaultJob
        {
            Id = Guid.NewGuid(),
            FilePath = filePath,
            FilepathHash = filepathHash,
            JobType = jobType,
            Status = VaultJobStatus.Queued,
            Priority = priority,
            QueuedAt = DateTimeOffset.UtcNow,
            MaxRetries = maxRetries
        };
    }

    /// <summary>
    /// Restores a job from DB.
    /// </summary>
    public static VaultJob Restore(
        Guid id,
        string filePath,
        string filepathHash,
        VaultJobType jobType,
        VaultJobStatus status,
        VaultJobPriority priority,
        DateTimeOffset queuedAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        int retryCount,
        int maxRetries,
        string? errorMessage)
    {
        return new VaultJob
        {
            Id = id,
            FilePath = filePath,
            FilepathHash = filepathHash,
            JobType = jobType,
            Status = status,
            Priority = priority,
            QueuedAt = queuedAt,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            RetryCount = retryCount,
            MaxRetries = maxRetries,
            ErrorMessage = errorMessage
        };
    }

    /// <summary>
    /// Marks the job as processing.
    /// </summary>
    public bool TryStart()
    {
        if (Status != VaultJobStatus.Queued)
            return false;

        Status = VaultJobStatus.Processing;
        StartedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// Marks the job as completed successfully.
    /// </summary>
    public void Complete()
    {
        Status = VaultJobStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        ErrorMessage = null;
    }

    /// <summary>
    /// Marks the job as failed.
    /// </summary>
    public void Fail(string errorMessage)
    {
        Status = VaultJobStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Attempts to retry the job.
    /// Returns true if retry is allowed, false if max retries exceeded.
    /// </summary>
    public bool TryRetry()
    {
        if (Status != VaultJobStatus.Failed)
            return false;

        if (RetryCount >= MaxRetries)
            return false;

        RetryCount++;
        Status = VaultJobStatus.Queued;
        StartedAt = null;
        CompletedAt = null;
        ErrorMessage = null;
        return true;
    }

    /// <summary>
    /// Resets a stuck processing job back to queued.
    /// Used during recovery after crash.
    /// </summary>
    public bool TryRecover()
    {
        if (Status != VaultJobStatus.Processing)
            return false;

        Status = VaultJobStatus.Queued;
        StartedAt = null;
        return true;
    }

    /// <summary>
    /// Cancels the job.
    /// </summary>
    public bool TryCancel()
    {
        if (Status != VaultJobStatus.Queued && Status != VaultJobStatus.Processing)
            return false;

        Status = VaultJobStatus.Cancelled;
        CompletedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// Processing duration if completed.
    /// </summary>
    public TimeSpan? Duration => StartedAt.HasValue && CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt.Value
        : null;

    /// <summary>
    /// Whether retry is still possible.
    /// </summary>
    public bool CanRetry => Status == VaultJobStatus.Failed && RetryCount < MaxRetries;
}

/// <summary>
/// Type of vault job.
/// </summary>
public enum VaultJobType
{
    /// <summary>
    /// Full memorize: extract → chunk → embed → commit.
    /// </summary>
    Memorize = 0,

    /// <summary>
    /// Refresh: chunk → embed → commit (skip extraction).
    /// </summary>
    Refresh = 1,

    /// <summary>
    /// Remove: delete chunks from vector store.
    /// </summary>
    Remove = 2
}

/// <summary>
/// Job status.
/// </summary>
public enum VaultJobStatus
{
    /// <summary>
    /// Waiting to be processed.
    /// </summary>
    Queued = 0,

    /// <summary>
    /// Currently being processed.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// Successfully completed.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Failed with error.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Cancelled by user.
    /// </summary>
    Cancelled = 4
}

/// <summary>
/// Job priority.
/// </summary>
public enum VaultJobPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Immediate = 3
}
