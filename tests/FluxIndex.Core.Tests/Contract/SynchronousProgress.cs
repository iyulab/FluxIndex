namespace FluxIndex.Core.Tests.Contract;

/// <summary>
/// An <see cref="IProgress{T}"/> that invokes its handler synchronously on the reporting thread.
/// </summary>
/// <remarks>
/// <para>
/// Tests that collect progress reports and then assert on them must not use
/// <see cref="Progress{T}"/>. That type captures the ambient <see cref="SynchronizationContext"/>
/// at construction; when there is none — which is the case on xUnit worker threads — it marshals
/// every <c>Report</c> call to the thread pool. Reports therefore arrive <em>asynchronously and in
/// arbitrary order</em>, and may not have arrived at all by the time the awaited operation returns.
/// </para>
/// <para>
/// The resulting failures are load-dependent, so they pass locally and fail on a busy CI runner —
/// typically as "the collection contains the later reports but not the first one". Sleeping before
/// the assertion only widens the race; it does not close it.
/// </para>
/// <para>
/// This implementation makes delivery deterministic: <c>Report</c> runs the handler inline, so every
/// report raised before the operation completes is observable once it does. Reporting code under
/// test sees the same <see cref="IProgress{T}"/> contract either way.
/// </para>
/// </remarks>
public sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SynchronousProgress(Action<T> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public void Report(T value) => _handler(value);
}
