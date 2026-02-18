using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Vault.Services;

/// <summary>
/// Service for debouncing rapid file system events.
/// Uses MemoryCache to coalesce multiple events within the debounce interval.
/// </summary>
public partial class DebounceService : IDisposable
{
    private readonly ILogger<DebounceService> _logger;
    private readonly MemoryCache _cache;
    private readonly TimeSpan _debounceInterval;
    private bool _disposed;

    public DebounceService(
        ILogger<DebounceService> logger,
        TimeSpan? debounceInterval = null)
    {
        _logger = logger;
        _debounceInterval = debounceInterval ?? TimeSpan.FromMilliseconds(500);
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    /// <summary>
    /// Debounces an action with the given key.
    /// If multiple calls are made with the same key within the debounce interval,
    /// only the last action will be executed.
    /// </summary>
    public async Task DebounceAsync(string key, Func<Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(action);

        // Cancel any existing pending action for this key
        if (_cache.TryGetValue(key, out CancellationTokenSource? existing))
        {
            existing?.Cancel();
            existing?.Dispose();
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var entryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(_debounceInterval * 2)
            .RegisterPostEvictionCallback((k, v, reason, state) =>
            {
                if (v is CancellationTokenSource tokenSource)
                {
                    tokenSource.Dispose();
                }
            });

        _cache.Set(key, cts, entryOptions);

        try
        {
            await Task.Delay(_debounceInterval, cts.Token);

            // If we weren't cancelled, execute the action
            if (!cts.Token.IsCancellationRequested)
            {
                _cache.Remove(key);
                await action();
            }
        }
        catch (OperationCanceledException)
        {
            // Debounced - a newer event will handle this
            LogDebouncedEvent(_logger, key);
        }
    }

    /// <summary>
    /// Debounces an action and returns a result.
    /// </summary>
    public async Task<T?> DebounceAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        T? result = default;
        await DebounceAsync(key, async () =>
        {
            result = await action();
        }, cancellationToken);
        return result;
    }

    /// <summary>
    /// Cancels any pending debounced action for the given key.
    /// </summary>
    public void Cancel(string key)
    {
        if (_cache.TryGetValue(key, out CancellationTokenSource? existing))
        {
            existing?.Cancel();
            _cache.Remove(key);
        }
    }

    /// <summary>
    /// Cancels all pending debounced actions.
    /// </summary>
    public void CancelAll()
    {
        _cache.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Trace, Message = "Debounced event for key: {Key}")]
    private static partial void LogDebouncedEvent(ILogger logger, string key);

    #endregion
}
