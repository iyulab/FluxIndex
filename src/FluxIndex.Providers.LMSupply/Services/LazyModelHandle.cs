using LMSupply;

namespace FluxIndex.Providers.LMSupply.Services;

/// <summary>
/// Something whose model loads on first use and can be warmed up explicitly.
/// </summary>
public interface ILazilyLoadedModel
{
    /// <summary>Whether the model has finished loading.</summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Loads the model now (downloading it first when it is not cached) if it is not loaded yet.
    /// Concurrent callers share one load; a failed load is retried by the next call.
    /// </summary>
    Task EnsureLoadedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Loads a model once, on first request, sharing the load between concurrent callers. The load
/// itself runs under the configured timeout and the handle's lifetime — not under the first
/// caller's token, so one caller cancelling does not abort a load others are waiting on; each
/// caller's token only abandons that caller's wait.
/// </summary>
internal sealed class LazyModelHandle<TModel> : IAsyncDisposable where TModel : class, IAsyncDisposable
{
    private readonly Func<IProgress<DownloadProgress>?, CancellationToken, Task<TModel>> _loader;
    private readonly IProgress<DownloadProgress>? _progress;
    private readonly TimeSpan? _loadTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task<TModel>? _loading;
    private TModel? _model;
    private bool _disposed;

    public LazyModelHandle(
        Func<IProgress<DownloadProgress>?, CancellationToken, Task<TModel>> loader,
        IProgress<DownloadProgress>? progress,
        TimeSpan? loadTimeout)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _progress = progress;
        _loadTimeout = loadTimeout;
    }

    /// <summary>The loaded model, or <c>null</c> until the first successful load.</summary>
    public TModel? LoadedModel => Volatile.Read(ref _model);

    public bool IsLoaded => LoadedModel is not null;

    public async Task<TModel> GetAsync(CancellationToken cancellationToken)
    {
        if (LoadedModel is { } ready)
            return ready;

        ObjectDisposedException.ThrowIf(_disposed, this);

        Task<TModel> loading;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (LoadedModel is { } readyNow)
                return readyNow;

            // A previous attempt that faulted is replaced, so a transient download failure does
            // not poison the handle for the rest of the process.
            if (_loading is null || _loading.IsFaulted || _loading.IsCanceled)
                _loading = LoadAsync();

            loading = _loading;
        }
        finally
        {
            _gate.Release();
        }

        // The caller's token abandons this wait only; the load keeps running for the others.
        return await loading.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<TModel> LoadAsync()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        if (_loadTimeout is { } timeout)
            cts.CancelAfter(timeout);

        try
        {
            var model = await _loader(_progress, cts.Token).ConfigureAwait(false);
            Volatile.Write(ref _model, model);
            return model;
        }
        catch (OperationCanceledException) when (_loadTimeout is { } elapsedTimeout && !_lifetime.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Loading the model did not complete within {elapsedTimeout.TotalSeconds:0.#}s (LoadTimeout). " +
                "A first run downloads the model; raise LoadTimeout, warm the cache up front, or report progress to see where it stands.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Abort an in-flight load, then dispose whatever finished loading.
        await _lifetime.CancelAsync().ConfigureAwait(false);
        var loading = _loading;
        if (loading is not null)
        {
            try { await loading.ConfigureAwait(false); }
            catch (Exception) { /* cancelled or failed load: nothing to dispose */ }
        }

        if (LoadedModel is { } model)
            await model.DisposeAsync().ConfigureAwait(false);

        _lifetime.Dispose();
        _gate.Dispose();
    }
}
