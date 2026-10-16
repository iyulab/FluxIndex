using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// <see cref="IVectorStore.GetRecordedIdentityAsync"/> on the sqlite-vec store: a fresh instance — a restarted process,
/// a copied data folder — reads which identity (model and revision) the index was last written with, so a consumer can
/// compare it with its configured model without keeping a side file.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteVecRecordedIdentityTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"fluxindex-vec-identity-{Guid.NewGuid():N}.db");
    private readonly List<ServiceProvider> _providers = [];

    private IVectorStore Store(EmbeddingIdentity? identity)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSQLiteVecVectorStore(o =>
        {
            o.DatabasePath = _db;
            o.UseSQLiteVec = true;
            o.VectorDimension = 4;
            o.FallbackToInMemoryOnError = false;
        });
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        var store = provider.CreateScope().ServiceProvider.GetRequiredService<IVectorStore>();
        if (identity is not null)
            store.BindIdentity(identity);
        return store;
    }

    private static EmbeddingIdentity Model(string model, string? revision = null) =>
        new() { Provider = "test", Model = model, Dimension = 4, Revision = revision };

    private static DocumentChunk Chunk() => new()
    {
        Id = Guid.NewGuid().ToString(),
        DocumentId = "doc",
        Content = "text",
        ChunkIndex = 0,
        TotalChunks = 1,
        TokenCount = 1,
        Embedding = [0.1f, 0.2f, 0.3f, 0.4f],
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFreshInstance_ReadsTheIdentityTheIndexWasWrittenWith()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        await Store(Model("model-a", "r2")).StoreAsync(Chunk(), Ct);

        var recorded = await Store(identity: null).GetRecordedIdentityAsync(Ct);

        recorded.Should().Be(Model("model-a", "r2"));
    }

    [Fact]
    public async Task BeforeAnyWrite_ReadsNull()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        (await Store(Model("model-a")).GetRecordedIdentityAsync(Ct)).Should().BeNull();
    }

    [Fact]
    public async Task ABatchWrite_RecordsTooAndASwitchedModelReplacesTheRecord()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        await Store(Model("model-a")).StoreBatchAsync([Chunk(), Chunk()], Ct);
        (await Store(identity: null).GetRecordedIdentityAsync(Ct)).Should().Be(Model("model-a"));

        // A switched model gets its own fingerprinted table (not refused); the record now names it.
        await Store(Model("model-b")).StoreBatchAsync([Chunk()], Ct);

        (await Store(identity: null).GetRecordedIdentityAsync(Ct)).Should().Be(Model("model-b"));
    }

    [Fact]
    public async Task Clear_ForgetsTheRecord()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var store = Store(Model("model-a"));
        await store.StoreAsync(Chunk(), Ct);

        await store.ClearAsync(Ct);

        (await Store(identity: null).GetRecordedIdentityAsync(Ct)).Should().BeNull();
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
            provider.Dispose();
        FluxIndex.Tests.Shared.SqliteTestPools.Release(_db);
        try { File.Delete(_db); } catch (IOException) { }
    }
}
