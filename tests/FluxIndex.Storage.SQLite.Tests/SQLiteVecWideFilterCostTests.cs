using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// What a vault-wide scope costs on the vector leg. A consumer that owns many vaults in one store
/// (FluxFeed's <c>VaultManager</c>) limits even an unscoped search to "the documents of this vault",
/// so the filter carries one allowed value per document — thousands of them. On the keyword leg that
/// shape was quadratic until it was fixed (9.4 s to 78 ms over 6,000 entries); this fixes what the
/// same shape costs here, because the two legs filter by entirely different mechanisms.
///
/// <para>
/// This store cannot push a metadata filter into the KNN — metadata lives in <c>vector_chunks</c>,
/// not in the vec0 table. It over-fetches a window of <c>topK * 3</c> and matches in memory, and only
/// when that window cannot fill <c>topK</c> does it scan every vector exactly. So the cost here is
/// governed by how *selective* the filter is, not by how many values it carries — which is the
/// opposite of the keyword leg, where the value count drove the cost.
/// </para>
///
/// <para>
/// Measured over 6,000 chunks of 384 dimensions, <c>topK</c> 10: unscoped 6 ms, scoped to every
/// document (6,000 allowed values) 6 ms, scoped to 100 documents 277 ms before 0.46.0 and 63 ms
/// after. A vault-wide scope is free and a narrow one is not, because only the narrow one leaves the
/// window short and pays for the exact walk.
/// </para>
///
/// <para>
/// Where that time went is worth recording, because two plausible answers were measured and were
/// both wrong. It was not the filter matching (compiling the filter once instead of per row moved
/// 277 ms to 298 ms — nothing), and it was not deserializing each row's metadata (6,000 of these
/// small JSON documents deserialize and match in 9 ms). It was the scan binding the query vector as
/// text: a scalar distance function is called per row, so every row re-parsed the whole 384-value
/// query vector. Binding a float32 blob took it to 63 ms. The rest is the paged metadata reads.
/// </para>
/// </summary>
[Collection("SQLite Tests")]
[Trait("Category", "Performance")]
public class SQLiteVecWideFilterCostTests : IAsyncLifetime
{
    private const int Dimension = 384;
    private const int Entries = 6000;

    private readonly ServiceProvider _serviceProvider;
    private readonly CapturingLoggerProvider _logs = new();
    private readonly string _testDatabasePath;

    public SQLiteVecWideFilterCostTests()
    {
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"fluxindex_vecwide_{Guid.NewGuid()}.db");

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(_logs);
        });
        services.AddSQLiteVecVectorStore(options =>
        {
            options.DatabasePath = _testDatabasePath;
            options.VectorDimension = Dimension;
            options.EmbeddingFingerprint = "testmodel384";
            options.UseSQLiteVec = true;
            options.FallbackToInMemoryOnError = false;
            options.AutoMigrate = true;
        });

        _serviceProvider = services.BuildServiceProvider();
    }

    public async ValueTask InitializeAsync()
    {
        if (CITestHelper.ShouldSkipSqliteVec())
            return;

        foreach (var service in _serviceProvider.GetServices<IHostedService>())
            await service.StartAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var service in _serviceProvider.GetServices<IHostedService>().Reverse())
            await service.StopAsync(CancellationToken.None);

        await _serviceProvider.DisposeAsync();

        try
        {
            if (File.Exists(_testDatabasePath))
                File.Delete(_testDatabasePath);
        }
        catch (IOException)
        {
            // A leftover test database is not a test failure.
        }
    }

    [Fact]
    public async Task ScopingASearchToEveryDocument_CostsAboutWhatTheUnscopedSearchCosts()
    {
        if (CITestHelper.ShouldSkipSqliteVec())
            return;

        var ct = TestContext.Current.CancellationToken;
        var store = _serviceProvider.GetRequiredService<IVectorStore>();
        var random = new Random(11);

        for (var i = 0; i < Entries; i++)
            await store.StoreAsync(Chunk($"doc{i}", RandomVector(random)), ct);

        var query = RandomVector(random);
        var everyDocument = Filter(Enumerable.Range(0, Entries).Select(i => $"doc{i}"));
        var narrowScope = Filter(Enumerable.Range(0, 100).Select(i => $"doc{i}"));

        async Task<(TimeSpan Elapsed, int Count, bool Scanned)> MeasureAsync(Dictionary<string, object>? filters)
        {
            await store.SearchAsync(query, 10, -1f, filters, ct);      // warm the page cache
            var before = _logs.Debugs.Count(m => m.Contains("exact scan", StringComparison.Ordinal));
            var started = Stopwatch.GetTimestamp();
            var results = await store.SearchAsync(query, 10, -1f, filters, ct);
            var elapsed = Stopwatch.GetElapsedTime(started);
            var after = _logs.Debugs.Count(m => m.Contains("exact scan", StringComparison.Ordinal));
            return (elapsed, results.Count(), after > before);
        }

        var unscoped = await MeasureAsync(null);
        var everyDoc = await MeasureAsync(everyDocument);
        var narrow = await MeasureAsync(narrowScope);

        unscoped.Count.Should().Be(10);
        everyDoc.Count.Should().Be(10, "a filter that allows every document must not change the answer");
        narrow.Count.Should().Be(10, "100 of 6,000 documents still hold more than topK matches");

        // Why the two filters cost differently, asserted rather than inferred from the timings: the
        // wide one fills the window on the first pass, the narrow one cannot and pays for the walk.
        everyDoc.Scanned.Should().BeFalse("every document passes, so the topK*3 window already fills topK");
        narrow.Scanned.Should().BeTrue("100 of 6,000 documents cannot fill a window of 30, so the store walks every vector");

        // The measurement this fixes: a vault-wide scope carries one value per document, and on this
        // leg that costs about what no filter costs — the window fills on the first pass, so only the
        // window's rows are matched in memory. Contrast the keyword leg, where the same shape was
        // ~300x before the fix.
        var report =
            $"unscoped {unscoped.Elapsed.TotalMilliseconds:F0} ms, scoped to every document "
            + $"{everyDoc.Elapsed.TotalMilliseconds:F0} ms, scoped to 100 documents "
            + $"{narrow.Elapsed.TotalMilliseconds:F0} ms";

        everyDoc.Elapsed.Should().BeLessThan(unscoped.Elapsed * 25, report);

        // The narrow scope still costs more — it walks every vector, and paging that metadata is
        // what 0.46.0 did not address. The bound is set where the text-bound query vector would put
        // it back (it was ~46x), so a return of that defect fails here rather than only showing up
        // as a consumer's latency report.
        narrow.Elapsed.Should().BeLessThan(unscoped.Elapsed * 25, report);
    }

    private static Dictionary<string, object> Filter(IEnumerable<string> documentIds) =>
        new() { ["documentId"] = documentIds.ToArray() };

    private static DocumentChunk Chunk(string documentId, float[] embedding) => new()
    {
        DocumentId = documentId,
        ChunkIndex = 0,
        Content = $"content for {documentId}",
        Embedding = embedding,
        Metadata = new Dictionary<string, object> { ["documentId"] = documentId }
    };

    private static float[] RandomVector(Random random)
    {
        var v = new float[Dimension];
        for (var i = 0; i < Dimension; i++)
            v[i] = (float)(random.NextDouble() * 2 - 1);
        return v;
    }
}
