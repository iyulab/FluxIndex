using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;

namespace FluxIndex.Extensions.FileVault.Interfaces;

/// <summary>
/// Service for managing the vault processing queue.
/// </summary>
public interface IVaultQueueService
{
    /// <summary>
    /// Enqueues a file for processing.
    /// </summary>
    Task<QueuedItem> EnqueueAsync(string filePath, ProcessingPriority priority = ProcessingPriority.Normal, CancellationToken ct = default);

    /// <summary>
    /// Enqueues multiple files for processing.
    /// </summary>
    Task<IReadOnlyList<QueuedItem>> EnqueueBatchAsync(IEnumerable<string> filePaths, ProcessingPriority priority = ProcessingPriority.Normal, CancellationToken ct = default);

    /// <summary>
    /// Enqueues a vault entry for reprocessing.
    /// </summary>
    Task<QueuedItem> EnqueueEntryAsync(VaultEntry entry, ProcessingStage fromStage = ProcessingStage.Source, ProcessingPriority priority = ProcessingPriority.Normal, CancellationToken ct = default);

    /// <summary>
    /// Dequeues the next item for processing.
    /// </summary>
    Task<QueuedItem?> DequeueAsync(CancellationToken ct = default);

    /// <summary>
    /// Marks an item as completed.
    /// </summary>
    Task CompleteAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Marks an item as failed.
    /// </summary>
    Task FailAsync(Guid itemId, string errorMessage, Exception? exception = null, CancellationToken ct = default);

    /// <summary>
    /// Retries a failed item.
    /// </summary>
    Task<bool> RetryAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Cancels a queued item.
    /// </summary>
    Task CancelAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Gets the current queue status.
    /// </summary>
    Task<QueueStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets queued items with optional filters.
    /// </summary>
    Task<IReadOnlyList<QueuedItem>> GetItemsAsync(QueueItemStatus? statusFilter = null, int? limit = null, CancellationToken ct = default);

    /// <summary>
    /// Clears completed items from the queue.
    /// </summary>
    Task<int> ClearCompletedAsync(CancellationToken ct = default);

    /// <summary>
    /// Clears all items from the queue.
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
    /// Gets whether the queue is paused.
    /// </summary>
    bool IsPaused { get; }
}

/// <summary>
/// Processing priority levels.
/// </summary>
public enum ProcessingPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Immediate = 3
}

/// <summary>
/// Queue item status.
/// </summary>
public enum QueueItemStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// Represents an item in the processing queue.
/// </summary>
public sealed class QueuedItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FilePath { get; init; } = string.Empty;
    public Guid? VaultEntryId { get; init; }
    public ProcessingStage FromStage { get; init; } = ProcessingStage.Source;
    public ProcessingPriority Priority { get; init; } = ProcessingPriority.Normal;
    public QueueItemStatus Status { get; private set; } = QueueItemStatus.Queued;
    public DateTimeOffset QueuedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int RetryCount { get; private set; }
    public string? ErrorMessage { get; private set; }

    public void MarkAsProcessing()
    {
        Status = QueueItemStatus.Processing;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAsCompleted()
    {
        Status = QueueItemStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAsFailed(string errorMessage)
    {
        Status = QueueItemStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
        ErrorMessage = errorMessage;
    }

    public void MarkAsCancelled()
    {
        Status = QueueItemStatus.Cancelled;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void IncrementRetry()
    {
        RetryCount++;
        Status = QueueItemStatus.Queued;
        StartedAt = null;
        CompletedAt = null;
        ErrorMessage = null;
    }
}

/// <summary>
/// Queue status summary.
/// </summary>
public sealed class QueueStatus
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
