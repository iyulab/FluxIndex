using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// The hosted initializer registered by <c>AddSQLiteVecVectorStore</c> starts before any consumer can
/// call <see cref="IVectorStore.BindIdentity"/>. It must therefore tolerate an unbound identity at host
/// start — deferring the fingerprint-dependent work (vec0 table, legacy migration, warmup) to the
/// first bound access — instead of failing the host (<c>FallbackToInMemoryOnError = false</c>) or
/// logging a spurious initialization error (default). Regression coverage for the FluxFeed README
/// default stack, where the store is bound by the pipeline that resolves the embedder.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteVecDeferredIdentityStartupTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fluxindex_deferred_{Guid.NewGuid():N}.db");
    private ServiceProvider? _sp;

    private static EmbeddingIdentity Identity() =>
        new() { Provider = "test-deferred", Model = "model-d", Dimension = 4 };

    private ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSQLiteVecVectorStore(o =>
        {
            o.DatabasePath = _dbPath;
            o.VectorDimension = 4;
            // Deliberately no EmbeddingFingerprint: the consumer binds later via BindIdentity.
            o.UseSQLiteVec = true;
            o.FallbackToInMemoryOnError = false;
        });
        return _sp = services.BuildServiceProvider();
    }

    [Fact]
    public async Task HostStart_WithoutBoundIdentity_DoesNotThrow_AndStoreWorksAfterBind()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var sp = Build();

        // Host start with no identity bound must succeed even with fallback disabled.
        foreach (var hosted in sp.GetServices<IHostedService>())
            await hosted.StartAsync(TestContext.Current.CancellationToken);

        using var scope = sp.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IVectorStore>();
        store.BindIdentity(Identity());

        var chunk = new DocumentChunk
        {
            DocumentId = "doc-deferred",
            Content = "deferred identity startup",
            ChunkIndex = 0,
            Embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f }
        };

        var id = await store.StoreAsync(chunk, TestContext.Current.CancellationToken);
        var read = await store.GetAsync(id, TestContext.Current.CancellationToken);

        read.Should().NotBeNull();
        read!.DocumentId.Should().Be("doc-deferred");
    }

    [Fact]
    public async Task NoHostedServices_StoreCreatesItsOwnSchemaOnFirstBoundAccess()
    {
        // A consumer that never starts the hosted initializer (plain ServiceCollection, inline
        // processing) must still get a working store: the EF tables have to exist before the vec0
        // table is created, otherwise EnsureCreated sees "a database with tables" and skips them.
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var sp = Build();

        using var scope = sp.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IVectorStore>();
        store.BindIdentity(Identity());

        var chunk = new DocumentChunk
        {
            DocumentId = "doc-no-host",
            Content = "store self-initializes",
            ChunkIndex = 0,
            Embedding = new float[] { 0.4f, 0.3f, 0.2f, 0.1f }
        };

        var id = await store.StoreAsync(chunk, TestContext.Current.CancellationToken);
        var read = await store.GetAsync(id, TestContext.Current.CancellationToken);

        read.Should().NotBeNull();
        read!.DocumentId.Should().Be("doc-no-host");
    }

    public async ValueTask DisposeAsync()
    {
        if (_sp is not null)
        {
            foreach (var hosted in _sp.GetServices<IHostedService>().Reverse())
                await hosted.StopAsync(CancellationToken.None);
            await _sp.DisposeAsync();
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_dbPath}"));
        try { File.Delete(_dbPath); } catch (IOException) { }
    }
}
