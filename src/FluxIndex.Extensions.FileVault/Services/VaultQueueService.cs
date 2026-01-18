using System.Collections.Concurrent;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;
using FluxIndex.Extensions.FileVault.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// In-memory vault processing queue service.
/// </summary>
public sealed class VaultQueueService : IVaultQueueService
{
    private readonly ILogger<VaultQueueService> _logger;
    private readonly ConcurrentDictionary<Guid, QueuedItem> _items = new();
    private readonly PriorityQueue<Guid, int> _queue = new();
    private readonly object _queueLock = new();
    private readonly List<double> _processingTimes = new();
    private DateTimeOffset? _lastProcessedAt;
    private bool _isPaused;

    public bool IsPaused => _isPaused;

    public VaultQueueService(ILogger<VaultQueueService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<QueuedItem> EnqueueAsync(string filePath, ProcessingPriority priority = ProcessingPriority.Normal, CancellationToken ct = default)
    {
        var item = new QueuedItem
        {
            FilePath = filePath,
            Priority = priority
        };

        EnqueueItem(item);
        _logger.LogDebug("Enqueued file {FilePath} with priority {Priority}", filePath, priority);

        return Task.FromResult(item);
    }

    public Task<IReadOnlyList<QueuedItem>> EnqueueBatchAsync(IEnumerable<string> filePaths, ProcessingPriority priority = ProcessingPriority.Normal, CancellationToken ct = default)
    {
        var items = new List<QueuedItem>();

        foreach (var filePath in filePaths)
        {
            var item = new QueuedItem
            {
                FilePath = filePath,
                Priority = priority
            };
            EnqueueItem(item);
            items.Add(item);
        }

        _logger.LogDebug("Enqueued {Count} files with priority {Priority}", items.Count, priority);
        return Task.FromResult<IReadOnlyList<QueuedItem>>(items);
    }

    public Task<QueuedItem> EnqueueEntryAsync(VaultEntry entry, ProcessingStage fromStage = ProcessingStage.Source, ProcessingPriority priority = ProcessingPriority.Normal, CancellationToken ct = default)
    {
        var item = new QueuedItem
        {
            FilePath = entry.SourcePath,
            VaultEntryId = entry.Id,
            FromStage = fromStage,
            Priority = priority
        };

        EnqueueItem(item);
        _logger.LogDebug("Enqueued entry {EntryId} from stage {Stage} with priority {Priority}", entry.Id, fromStage, priority);

        return Task.FromResult(item);
    }

    public Task<QueuedItem?> DequeueAsync(CancellationToken ct = default)
    {
        if (_isPaused)
        {
            return Task.FromResult<QueuedItem?>(null);
        }

        lock (_queueLock)
        {
            while (_queue.Count > 0)
            {
                if (_queue.TryDequeue(out var itemId, out _))
                {
                    if (_items.TryGetValue(itemId, out var item) && item.Status == QueueItemStatus.Queued)
                    {
                        item.MarkAsProcessing();
                        _logger.LogDebug("Dequeued item {ItemId} for processing", itemId);
                        return Task.FromResult<QueuedItem?>(item);
                    }
                }
            }
        }

        return Task.FromResult<QueuedItem?>(null);
    }

    public Task CompleteAsync(Guid itemId, CancellationToken ct = default)
    {
        if (_items.TryGetValue(itemId, out var item))
        {
            var startedAt = item.StartedAt;
            item.MarkAsCompleted();
            _lastProcessedAt = DateTimeOffset.UtcNow;

            if (startedAt.HasValue)
            {
                var processingTime = (_lastProcessedAt.Value - startedAt.Value).TotalMilliseconds;
                lock (_processingTimes)
                {
                    _processingTimes.Add(processingTime);
                    if (_processingTimes.Count > 100)
                    {
                        _processingTimes.RemoveAt(0);
                    }
                }
            }

            _logger.LogDebug("Completed item {ItemId}", itemId);
        }

        return Task.CompletedTask;
    }

    public Task FailAsync(Guid itemId, string errorMessage, Exception? exception = null, CancellationToken ct = default)
    {
        if (_items.TryGetValue(itemId, out var item))
        {
            item.MarkAsFailed(errorMessage);
            _logger.LogWarning(exception, "Failed item {ItemId}: {ErrorMessage}", itemId, errorMessage);
        }

        return Task.CompletedTask;
    }

    public Task<bool> RetryAsync(Guid itemId, CancellationToken ct = default)
    {
        if (_items.TryGetValue(itemId, out var item) && item.Status == QueueItemStatus.Failed)
        {
            item.IncrementRetry();
            EnqueueItem(item, requeue: true);
            _logger.LogDebug("Retrying item {ItemId}, attempt {RetryCount}", itemId, item.RetryCount);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task CancelAsync(Guid itemId, CancellationToken ct = default)
    {
        if (_items.TryGetValue(itemId, out var item) &&
            (item.Status == QueueItemStatus.Queued || item.Status == QueueItemStatus.Processing))
        {
            item.MarkAsCancelled();
            _logger.LogDebug("Cancelled item {ItemId}", itemId);
        }

        return Task.CompletedTask;
    }

    public Task<QueueStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var items = _items.Values.ToList();
        double avgTime;

        lock (_processingTimes)
        {
            avgTime = _processingTimes.Count > 0 ? _processingTimes.Average() : 0;
        }

        var status = new QueueStatus
        {
            QueuedCount = items.Count(i => i.Status == QueueItemStatus.Queued),
            ProcessingCount = items.Count(i => i.Status == QueueItemStatus.Processing),
            CompletedCount = items.Count(i => i.Status == QueueItemStatus.Completed),
            FailedCount = items.Count(i => i.Status == QueueItemStatus.Failed),
            CancelledCount = items.Count(i => i.Status == QueueItemStatus.Cancelled),
            IsPaused = _isPaused,
            LastProcessedAt = _lastProcessedAt,
            AverageProcessingTimeMs = avgTime
        };

        return Task.FromResult(status);
    }

    public Task<IReadOnlyList<QueuedItem>> GetItemsAsync(QueueItemStatus? statusFilter = null, int? limit = null, CancellationToken ct = default)
    {
        var query = _items.Values.AsEnumerable();

        if (statusFilter.HasValue)
        {
            query = query.Where(i => i.Status == statusFilter.Value);
        }

        query = query.OrderByDescending(i => (int)i.Priority)
                     .ThenBy(i => i.QueuedAt);

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return Task.FromResult<IReadOnlyList<QueuedItem>>(query.ToList());
    }

    public Task<int> ClearCompletedAsync(CancellationToken ct = default)
    {
        var completedIds = _items
            .Where(kv => kv.Value.Status == QueueItemStatus.Completed || kv.Value.Status == QueueItemStatus.Cancelled)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in completedIds)
        {
            _items.TryRemove(id, out _);
        }

        _logger.LogDebug("Cleared {Count} completed items", completedIds.Count);
        return Task.FromResult(completedIds.Count);
    }

    public Task ClearAllAsync(CancellationToken ct = default)
    {
        lock (_queueLock)
        {
            _queue.Clear();
        }
        _items.Clear();

        _logger.LogDebug("Cleared all queue items");
        return Task.CompletedTask;
    }

    public void Pause()
    {
        _isPaused = true;
        _logger.LogInformation("Queue processing paused");
    }

    public void Resume()
    {
        _isPaused = false;
        _logger.LogInformation("Queue processing resumed");
    }

    private void EnqueueItem(QueuedItem item, bool requeue = false)
    {
        if (!requeue)
        {
            _items[item.Id] = item;
        }

        // Priority queue uses negative priority so higher priorities come first
        var priority = -(int)item.Priority;

        lock (_queueLock)
        {
            _queue.Enqueue(item.Id, priority);
        }
    }
}
