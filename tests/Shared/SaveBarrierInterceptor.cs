using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluxIndex.Tests.Shared;

/// <summary>
/// Holds the first <c>parties</c> saves after <see cref="Arm"/> until all of them have reached their save, so writers that
/// each read a row and then save are forced to have all read before any writes — the lost-update window, opened on demand
/// instead of raced. Saves after those (a retry, a later call) pass straight through, so a writer that retries does not
/// wait on a barrier nobody else will reach.
/// </summary>
public sealed class SaveBarrierInterceptor : SaveChangesInterceptor, IDisposable
{
    private Barrier? _barrier;
    private int _parties;
    private int _entered;
    private int _passed;

    /// <summary>Number of armed saves that met at the barrier (each one counts once).</summary>
    public int Passed => Volatile.Read(ref _passed);

    /// <summary>Gates the next <paramref name="parties"/> saves.</summary>
    public void Arm(int parties)
    {
        _barrier?.Dispose();
        _barrier = new Barrier(parties);
        _parties = parties;
        _entered = 0;
        _passed = 0;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var barrier = _barrier;
        if (barrier is not null && Interlocked.Increment(ref _entered) <= _parties
            && await Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(30)), cancellationToken))
        {
            Interlocked.Increment(ref _passed);
        }

        return result;
    }

    public void Dispose() => _barrier?.Dispose();
}
