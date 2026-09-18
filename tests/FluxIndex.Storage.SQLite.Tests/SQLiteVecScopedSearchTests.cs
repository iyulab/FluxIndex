using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// A metadata filter on this store cannot be part of the KNN — the metadata lives in
/// <c>vector_chunks</c>, not in the vec0 table — so the KNN runs over a window (over-fetched under a
/// filter, capped at sqlite-vec's k ceiling of 4,096) and the filter is applied to it in distance order.
/// <para>
/// A scope narrow enough relative to the store used to lose matches that never entered the window:
/// on a 6,205-chunk vault a scoped question returned nothing at any k (docket iyulab/FluxIndex#346).
/// When the window comes back full and the filter cannot fill topK, the store now scans every vector
/// exactly and keeps walking in distance order. These tests fix that the answer is exact — not merely
/// that the loss is reported.
/// </para>
/// </summary>
[Collection("SQLite Tests")]
public class SQLiteVecScopedSearchTests : IAsyncLifetime
{
    private const int Dimension = 8;

    private readonly ServiceProvider _serviceProvider;
    private readonly CapturingLoggerProvider _logs = new();
    private readonly string _testDatabasePath;

    public SQLiteVecScopedSearchTests()
    {
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"fluxindex_scoped_{Guid.NewGuid()}.db");

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
            options.EmbeddingFingerprint = "testmodel8";
            options.UseSQLiteVec = true;
            options.FallbackToInMemoryOnError = false;
            options.AutoMigrate = true;
        });

        _serviceProvider = services.BuildServiceProvider();
    }

    public async ValueTask InitializeAsync()
    {
        if (CITestHelper.ShouldSkipSqliteVec())
        {
            return;
        }

        foreach (var service in _serviceProvider.GetServices<IHostedService>())
        {
            await service.StartAsync(CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var service in _serviceProvider.GetServices<IHostedService>().Reverse())
        {
            await service.StopAsync(CancellationToken.None);
        }

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

    /// <summary>
    /// Fills the candidate window with chunks that score better than the one match. The match lies
    /// outside the window, and the search must still return it.
    /// </summary>
    [Fact]
    public async Task SearchAsync_ScopedMatchOutsideTheWindow_IsFound()
    {
        if (CITestHelper.ShouldSkipSqliteVec())
            return;

        var store = _serviceProvider.GetRequiredService<IVectorStore>();
        const int topK = 3;

        // The window is topK * 3 = 9. Store more than that many near-exact matches that the filter
        // will reject, plus one accepted chunk placed further away so it cannot make the window.
        for (var i = 0; i < 12; i++)
        {
            await store.StoreAsync(Chunk($"noise-{i}", "other-tenant", Near(1f, i * 0.0001f)));
        }
        await store.StoreAsync(Chunk("wanted", "wanted-tenant", Far()));

        var results = await store.SearchAsync(
            Near(1f, 0f), topK, minScore: 0f,
            filters: new Dictionary<string, object> { ["tenant"] = "wanted-tenant" },
            TestContext.Current.CancellationToken);

        results.Select(r => r.DocumentId).Should().Equal("wanted");
        _logs.Debugs.Should().Contain(m => m.Contains("exact scan"));
        _logs.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// The counterpart: when the filter is satisfied from inside the window, the KNN answer is already
    /// exact and no scan is owed.
    /// </summary>
    [Fact]
    public async Task SearchAsync_FilterSatisfiedWithinWindow_DoesNotScan()
    {
        if (CITestHelper.ShouldSkipSqliteVec())
            return;

        var store = _serviceProvider.GetRequiredService<IVectorStore>();

        for (var i = 0; i < 3; i++)
        {
            await store.StoreAsync(Chunk($"wanted-{i}", "wanted-tenant", Near(1f, i * 0.0001f)));
        }
        await store.StoreAsync(Chunk("noise", "other-tenant", Far()));

        var results = await store.SearchAsync(
            Near(1f, 0f), topK: 3, minScore: 0f,
            filters: new Dictionary<string, object> { ["tenant"] = "wanted-tenant" },
            TestContext.Current.CancellationToken);

        results.Should().HaveCount(3);
        _logs.Debugs.Should().NotContain(m => m.Contains("exact scan"), "a window that fills topK needs no scan");
    }

    /// <summary>
    /// vec0 rejects a KNN k above its compile-time ceiling, and the store widens the caller's topK
    /// on its own (x3 under a filter, x2 more on the hybrid path). A caller could not pick a safe
    /// topK without knowing those multipliers, so a window past the ceiling must be clamped — the
    /// search answers without throwing, and records the clamp.
    /// <para>
    /// A store this small is <em>not</em> an operator warning, though: three chunks come back whole
    /// whether the window is 4,096 or 4,098, so nothing starved. The clamp is still recorded, one
    /// level down.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(1_366, false)] // 1,366 x 3 = 4,098 — two past the ceiling
    [InlineData(1_365, true)]  // 1,365 x 3 = 4,095 — inside it
    public async Task SearchAsync_FilteredWindowAtTheKnnCeiling_ClampsInsteadOfThrowing(int topK, bool withinCeiling)
    {
        if (CITestHelper.ShouldSkipSqliteVec())
            return;

        var store = _serviceProvider.GetRequiredService<IVectorStore>();
        for (var i = 0; i < 3; i++)
        {
            await store.StoreAsync(Chunk($"wanted-{i}", "wanted-tenant", Near(1f, i * 0.0001f)));
        }

        var results = await store.SearchAsync(
            Near(1f, 0f), topK, minScore: 0f,
            filters: new Dictionary<string, object> { ["tenant"] = "wanted-tenant" },
            TestContext.Current.CancellationToken);

        results.Should().HaveCount(3);
        _logs.Warnings.Should().NotContain(m => m.Contains("was clamped"));
        if (withinCeiling)
            _logs.Debugs.Should().NotContain(m => m.Contains("was clamped"));
        else
            _logs.Debugs.Should().Contain(m => m.Contains("4098") && m.Contains("was clamped to 4096"));
    }

    /// <summary>
    /// The consumer-measured shape: a document-scoped hybrid search whose widened window lands far
    /// past the ceiling (topK 800 → 1,600 on the vector leg → 4,800 under the filter).
    /// </summary>
    [Fact]
    public async Task HybridSearchAsync_ScopedWindowPastTheKnnCeiling_ReturnsHits()
    {
        if (CITestHelper.ShouldSkipSqliteVec())
            return;

        var store = _serviceProvider.GetRequiredService<IVectorStore>();
        var hybrid = (INativeHybridSearch)store;
        for (var i = 0; i < 3; i++)
        {
            await store.StoreAsync(Chunk($"wanted-{i}", "wanted-tenant", Near(1f, i * 0.0001f)));
        }
        await store.StoreAsync(Chunk("noise", "other-tenant", Near(1f, 0.00005f)));

        var results = await hybrid.HybridSearchAsync(
            Near(1f, 0f), "content", topK: 800, minScore: 0f,
            filters: new Dictionary<string, object> { ["tenant"] = "wanted-tenant" },
            cancellationToken: TestContext.Current.CancellationToken);

        results.Select(r => r.Chunk.DocumentId).Should().BeEquivalentTo(["wanted-0", "wanted-1", "wanted-2"]);
        _logs.Warnings.Should().NotContain(m => m.Contains("was clamped"));
        _logs.Debugs.Should().Contain(m => m.Contains("was clamped to 4096"));
    }

    /// <summary>
    /// The consumer-measured shape of docket iyulab/FluxIndex#346: more chunks than the KNN ceiling,
    /// all of them nearer the query than the in-scope ones. The clamped window holds only out-of-scope
    /// chunks, and before the exact scan a scoped search returned nothing at any topK.
    /// </summary>
    [Fact]
    public async Task SearchAsync_ScopeRankedPastTheKnnCeiling_IsFound()
    {
        if (CITestHelper.ShouldSkipSqliteVec())
            return;

        var store = _serviceProvider.GetRequiredService<IVectorStore>();

        const int rows = 4_200;
        await store.StoreBatchAsync(
            Enumerable.Range(0, rows).Select(i => Chunk($"noise-{i}", "other-tenant", Near(1f, i * 0.0001f))),
            TestContext.Current.CancellationToken);
        await store.StoreAsync(Chunk("wanted-a", "wanted-tenant", Far()), TestContext.Current.CancellationToken);
        await store.StoreAsync(Chunk("wanted-b", "wanted-tenant", Far(-0.5f)), TestContext.Current.CancellationToken);

        var results = (await store.SearchAsync(
            Near(1f, 0f), topK: 5, minScore: 0f,
            filters: new Dictionary<string, object> { ["tenant"] = "wanted-tenant" },
            TestContext.Current.CancellationToken)).ToList();

        results.Select(r => r.DocumentId).Should().Equal(["wanted-a", "wanted-b"], "the exact scan keeps distance order");
        _logs.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// A full window that stops at minScore has nothing further to find: candidates are in distance
    /// order, so everything past the floor is below it too. No scan is owed there.
    /// </summary>
    [Fact]
    public async Task SearchAsync_WindowEndingAtTheScoreFloor_DoesNotScan()
    {
        if (CITestHelper.ShouldSkipSqliteVec())
            return;

        var store = _serviceProvider.GetRequiredService<IVectorStore>();
        for (var i = 0; i < 12; i++)
        {
            await store.StoreAsync(Chunk($"noise-{i}", "other-tenant", i < 6 ? Near(1f, i * 0.0001f) : Far()));
        }
        await store.StoreAsync(Chunk("wanted", "wanted-tenant", Far()));

        var results = await store.SearchAsync(
            Near(1f, 0f), topK: 3, minScore: 0.9f,
            filters: new Dictionary<string, object> { ["tenant"] = "wanted-tenant" },
            TestContext.Current.CancellationToken);

        results.Should().BeEmpty("the one in-scope chunk scores below the floor");
        _logs.Debugs.Should().NotContain(m => m.Contains("exact scan"));
    }

    /// <summary>
    /// The text leg had the same shape: a filtered FTS query read LIMIT topK * 3 rows and filtered
    /// them, so an in-scope match ranked past that was dropped. It must reach the hybrid result.
    /// </summary>
    [Fact]
    public async Task HybridSearchAsync_ScopedTextMatchRankedPastTheLimit_IsFoundByTheTextLeg()
    {
        if (CITestHelper.ShouldSkipSqliteVec())
            return;

        var store = (SQLiteVecVectorStore)_serviceProvider.GetRequiredService<IVectorStore>();
        for (var i = 0; i < 40; i++)
        {
            await store.StoreAsync(Chunk($"noise-{i}", "other-tenant", Near(1f, i * 0.0001f), "harbor harbor harbor"));
        }
        await store.StoreAsync(Chunk("wanted", "wanted-tenant", Far(),
            "harbor appears once in this much longer passage about unrelated shipping schedules and weather"));

        var results = await store.HybridSearchAsync(
            Near(1f, 0f), "harbor", topK: 3, minScore: 0f,
            filters: new Dictionary<string, object> { ["tenant"] = "wanted-tenant" },
            cancellationToken: TestContext.Current.CancellationToken);

        var wanted = results.Should().ContainSingle(r => r.Chunk.DocumentId == "wanted").Subject;
        wanted.FoundInTextSearch.Should().BeTrue("the text leg must not drop an in-scope match ranked past its limit");
    }

    private static DocumentChunk Chunk(string documentId, string tenant, float[] embedding, string? content = null) => new()
    {
        DocumentId = documentId,
        ChunkIndex = 0,
        Content = content ?? $"content for {documentId}",
        Embedding = embedding,
        Metadata = new Dictionary<string, object> { ["tenant"] = tenant }
    };

    private static float[] Near(float lead, float jitter)
    {
        var v = new float[Dimension];
        v[0] = lead - jitter;
        return v;
    }

    // Orthogonal to the query by default; a positive lead tilts it toward the query, a negative one away.
    private static float[] Far(float lead = 0f)
    {
        var v = new float[Dimension];
        v[0] = lead;
        v[Dimension - 1] = 1f;
        return v;
    }

    /// <summary>
    /// Captures messages so a test can assert on what an operator would see — and, separately, on
    /// what was recorded but kept below the operator's attention. A fact the store still notes at
    /// Debug is a different outcome from one it never records, so both are captured and the level
    /// is part of the assertion.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Message)> _messages = [];
        private readonly Lock _gate = new();

        public IReadOnlyList<string> Warnings => At(l => l >= LogLevel.Warning);

        public IReadOnlyList<string> Debugs => At(l => l == LogLevel.Debug);

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() { }

        private IReadOnlyList<string> At(Func<LogLevel, bool> predicate)
        {
            lock (_gate) return [.. _messages.Where(m => predicate(m.Level)).Select(m => m.Message)];
        }

        private void Add(LogLevel level, string message)
        {
            lock (_gate) _messages.Add((level, message));
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Debug)
                    owner.Add(logLevel, formatter(state, exception));
            }
        }
    }
}
