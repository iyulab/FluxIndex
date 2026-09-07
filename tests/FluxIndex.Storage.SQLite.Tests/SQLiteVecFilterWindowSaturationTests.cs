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
            builder.SetMinimumLevel(LogLevel.Warning);
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

    /// <summary>Captures warning-level messages so a test can assert on what an operator would see.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _warnings = [];
        private readonly Lock _gate = new();

        public IReadOnlyList<string> Warnings
        {
            get { lock (_gate) return [.. _warnings]; }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() { }

        private void Add(string message)
        {
            lock (_gate) _warnings.Add(message);
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning)
                    owner.Add(formatter(state, exception));
            }
        }
    }
}
