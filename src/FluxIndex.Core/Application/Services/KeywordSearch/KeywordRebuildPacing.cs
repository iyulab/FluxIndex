namespace FluxIndex.Core.Application.Services.KeywordSearch;

/// <summary>
/// How many chunks the next batch of a keyword-index rebuild re-tokenizes, and how long to wait after a batch fails.
/// </summary>
/// <remarks>
/// <para>
/// A batch writes its postings, field postings, chunk rows and filter rows in a handful of statements, and each statement
/// runs under the connection's command timeout. How long a statement takes depends on the store - a large postings table
/// on a cold page cache took 5-11 s per statement for a few thousand rows - so no fixed batch size is safe everywhere.
/// The size starts small, doubles while a batch finishes well inside the timeout, halves when one comes close to it, and
/// halves again on every failure, down to a single chunk.
/// </para>
/// <para>
/// A failed batch rolls back whole and is tried again from the committed cursor after an exponential delay, without
/// limit: the store may be down for a while, and giving up would leave the index half on each definition until the host
/// restarts. The delay is capped so a store that comes back is noticed within minutes.
/// </para>
/// </remarks>
internal sealed class KeywordRebuildPacing
{
    /// <summary>Chunks in the first batch: small enough to finish inside any sane command timeout on a cold store.</summary>
    internal const int InitialBatchSize = 50;

    /// <summary>Most chunks one batch re-tokenizes.</summary>
    internal const int MaxBatchSize = 500;

    /// <summary>The wait before the first retry; it doubles on each further failure.</summary>
    internal static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>The longest wait between two attempts.</summary>
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    /// <summary>The batch time budget used when the connection sets no command timeout (0 = wait forever).</summary>
    private static readonly TimeSpan BudgetWithoutTimeout = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _budget;

    /// <param name="commandTimeout">The command timeout statements run under; zero or less when there is none.</param>
    internal KeywordRebuildPacing(TimeSpan commandTimeout)
    {
        _budget = commandTimeout > TimeSpan.Zero ? commandTimeout : BudgetWithoutTimeout;
    }

    /// <summary>Chunks the next batch re-tokenizes.</summary>
    internal int BatchSize { get; private set; } = InitialBatchSize;

    /// <summary>Batches that failed in a row since the last one that succeeded.</summary>
    internal int ConsecutiveFailures { get; private set; }

    /// <summary>
    /// A batch committed after <paramref name="elapsed"/>: the size doubles when it took under a quarter of the command
    /// timeout and halves when it took over half of it.
    /// </summary>
    internal void Succeeded(TimeSpan elapsed)
    {
        ConsecutiveFailures = 0;
        if (elapsed < _budget / 4)
            BatchSize = Math.Min(MaxBatchSize, BatchSize * 2);
        else if (elapsed > _budget / 2)
            BatchSize = Math.Max(1, BatchSize / 2);
    }

    /// <summary>A batch failed: halves the size and returns how long to wait before trying again.</summary>
    internal TimeSpan Failed()
    {
        ConsecutiveFailures++;
        BatchSize = Math.Max(1, BatchSize / 2);
        var delay = FirstRetryDelay * Math.Pow(2, Math.Min(ConsecutiveFailures - 1, 16));
        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }
}
