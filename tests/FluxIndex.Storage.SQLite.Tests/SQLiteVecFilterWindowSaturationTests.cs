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
/// A metadata filter on this store is applied <em>after</em> the KNN step, over an over-fetched
/// window of <c>topK * 3</c> candidates — vec0 cannot filter on metadata as the table is currently
/// declared, because the metadata lives in <c>vector_chunks</c>.
/// <para>
/// So a scope narrow enough relative to the store still loses matches that never enter the window:
/// higher-scoring non-matching chunks fill it first. That recall loss used to be completely silent
/// — the caller received a short result with no way to tell it apart from "there was nothing else".
/// These tests fix the point at which the store must say so.
/// </para>
/// </summary>
[Collection("SQLite Tests")]
public class SQLiteVecFilterWindowSaturationTests : IAsyncLifetime
{
    private const int Dimension = 8;

    private readonly ServiceProvider _serviceProvider;
    private readonly CapturingLoggerProvider _logs = new();
    private readonly string _testDatabasePath;

    public SQLiteVecFilterWindowSaturationTests()
    {
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"fluxindex_saturation_{Guid.NewGuid()}.db");

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
    /// Fills the candidate window with chunks that score better than the one match, so the filter
    /// has nothing left to return. The store must warn rather than answer "no results" as though
    /// the store held none.
    /// </summary>
    [Fact]
    public async Task SearchAsync_FilterStarvedByASaturatedWindow_Warns()
    {
        if (CITestHelper.ShouldSkipSqliteVec())
            return;

        var store = _serviceProvider.GetRequiredService<IVectorStore>();
        const int topK = 3;

        // The window is topK * 3 = 9. Store more than that many near-exact matches that the filter
        // will reject, plus one accepted chunk placed further away so it cannot make the cut.
        for (var i = 0; i < 12; i++)
        {
            await store.StoreAsync(Chunk($"noise-{i}", "other-tenant", Near(1f, i * 0.0001f)));
        }
        await store.StoreAsync(Chunk("wanted", "wanted-tenant", Far()));

        var results = await store.SearchAsync(
            Near(1f, 0f), topK, minScore: 0f,
            filters: new Dictionary<string, object> { ["tenant"] = "wanted-tenant" },
            TestContext.Current.CancellationToken);

        results.Should().HaveCountLessThan(topK, "the accepted chunk sits outside the candidate window");
        _logs.Warnings.Should().Contain(m => m.Contains("applied after the KNN step"),
            "a short result caused by post-KNN filtering must be observable, not silent");
    }

    /// <summary>
    /// The counterpart: when the filter is satisfied from inside the window, nothing is lost and
    /// the store must stay quiet. A warning that fires on healthy searches is noise, and noise is
    /// how a real one gets ignored.
    /// </summary>
    [Fact]
    public async Task SearchAsync_FilterSatisfiedWithinWindow_DoesNotWarn()
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
        _logs.Warnings.Should().NotContain(m => m.Contains("applied after the KNN step"));
    }

    /// <summary>
    /// vec0 rejects a KNN k above its compile-time ceiling, and the store widens the caller's topK
    /// on its own (x3 under a filter, x2 more on the hybrid path). A caller could not pick a safe
    /// topK without knowing those multipliers, so a window past the ceiling must be clamped — the
    /// search answers from what the clamped window holds and says it was clamped, never throws.
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
    /// The clamp becomes an operator's problem only once the clamped window comes back full: past
    /// that point the answer really is drawn from the nearest 4,096 chunks alone, and a filter
    /// applied after the KNN step can starve inside it. This is the case the warning is for, and it
    /// is the one a small vault must not be able to reach.
    /// </summary>
    [Fact]
    public async Task SearchAsync_ClampedWindowFilledByTheStore_WarnsThatResultsMayStarve()
    {
        if (CITestHelper.ShouldSkipSqliteVec())
            return;

        var store = _serviceProvider.GetRequiredService<IVectorStore>();

        // One row past the ceiling is enough for the clamped window to come back full.
        const int rows = 4_100;
        await store.StoreBatchAsync(
            Enumerable.Range(0, rows).Select(i => Chunk($"noise-{i}", "other-tenant", Near(1f, i * 0.0001f))),
            TestContext.Current.CancellationToken);
        await store.StoreAsync(Chunk("wanted", "wanted-tenant", Near(1f, 0f)), TestContext.Current.CancellationToken);

        var results = await store.SearchAsync(
            Near(1f, 0f), topK: 1_366, minScore: 0f,
            filters: new Dictionary<string, object> { ["tenant"] = "wanted-tenant" },
            TestContext.Current.CancellationToken);

        results.Should().NotBeNull();
        _logs.Warnings.Should().Contain(m => m.Contains("4098") && m.Contains("was clamped to 4096"));
        _logs.Debugs.Should().NotContain(m => m.Contains("no candidate was lost"));
    }

    private static DocumentChunk Chunk(string documentId, string tenant, float[] embedding) => new()
    {
        DocumentId = documentId,
        ChunkIndex = 0,
        Content = $"content for {documentId}",
        Embedding = embedding,
        Metadata = new Dictionary<string, object> { ["tenant"] = tenant }
    };

    private static float[] Near(float lead, float jitter)
    {
        var v = new float[Dimension];
        v[0] = lead - jitter;
        return v;
    }

    private static float[] Far()
    {
        var v = new float[Dimension];
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
