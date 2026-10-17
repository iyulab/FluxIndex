using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// The vector-space revision of each vector on the sqlite-vec store: a re-embed into a new revision in place (same model,
/// same table) can be interrupted, and a fresh instance — a restarted process — still tells which vectors it has not reached.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteVecVectorSpaceTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"fluxindex-vec-space-{Guid.NewGuid():N}.db");
    private readonly List<ServiceProvider> _providers = [];

    private static readonly EmbeddingIdentity Identity = new() { Provider = "test", Model = "model-a", Dimension = 4 };

    private IVectorStore Store()
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
        store.BindIdentity(Identity);
        return store;
    }

    private static DocumentChunk Chunk(int i, string? revision, bool withVector = true) => new()
    {
        Id = $"c{i:00}",
        DocumentId = "doc",
        Content = $"text {i}",
        ChunkIndex = i,
        TotalChunks = 20,
        TokenCount = 1,
        Embedding = withVector ? [0.1f * (i + 1), 0.2f, 0.3f, 0.4f] : null,
        VectorSpaceRevision = revision,
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AfterARestartMidReembed_TheStoreCountsTheVectorsNotYetInTheNewSpace()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        await Store().StoreBatchAsync(Enumerable.Range(0, 10).Select(i => Chunk(i, "A")), Ct);

        // Re-embed four in place (one batch, one single store), then «restart».
        var reembedding = Store();
        await reembedding.StoreBatchAsync(Enumerable.Range(0, 3).Select(i => Chunk(i, "B")), Ct);
        await reembedding.StoreAsync(Chunk(3, "B"), Ct);

        var restarted = Store();
        (await restarted.CountVectorsOutsideSpaceAsync("B", Ct)).Should().Be(6);
        (await restarted.GetChunkIdsOutsideSpaceAsync("B", 4, Ct)).Should().Equal("c04", "c05", "c06", "c07");
        (await restarted.CountVectorsOutsideSpaceAsync("A", Ct)).Should().Be(4);
    }

    [Fact]
    public async Task AVectorWithoutARevision_CountsAsOutside_AndAChunkWithoutAVectorDoesNot()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var store = Store();
        await store.StoreBatchAsync([Chunk(0, "B"), Chunk(1, revision: null), Chunk(2, "B", withVector: false)], Ct);

        (await store.CountVectorsOutsideSpaceAsync("B", Ct)).Should().Be(1);
        (await store.GetChunkIdsOutsideSpaceAsync("B", 10, Ct)).Should().Equal("c01");
    }

    [Fact]
    public async Task Adopt_GivesVectorsWithoutARevisionTheOneTheIndexWasBuiltIn_AndLeavesTheRest()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        await Store().StoreBatchAsync([Chunk(0, revision: null), Chunk(1, revision: null), Chunk(2, "B"), Chunk(3, "A")], Ct);

        (await Store().AdoptUnrecordedVectorsAsync("A", Ct)).Should().Be(2);

        var restarted = Store();
        (await restarted.CountVectorsOutsideSpaceAsync("A", Ct)).Should().Be(1, "only c02 was computed in another revision");
        (await restarted.GetChunkIdsOutsideSpaceAsync("A", 10, Ct)).Should().Equal("c02");
        (await restarted.AdoptUnrecordedVectorsAsync("A", Ct)).Should().Be(0);
    }

    [Fact]
    public async Task ReadingAChunkBack_ReturnsTheRevisionItsVectorWasComputedIn()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        await Store().StoreBatchAsync([Chunk(0, "A"), Chunk(1, "B")], Ct);

        var fresh = Store();
        (await fresh.GetAsync("c00", Ct))!.VectorSpaceRevision.Should().Be("A");
        (await fresh.GetChunksByIdsAsync(["c01"], Ct)).Single().VectorSpaceRevision.Should().Be("B");
        (await fresh.GetByDocumentIdAsync("doc", Ct)).Select(c => c.VectorSpaceRevision).Should().Equal("A", "B");
    }

    [Fact]
    public async Task UpdateWithANewVector_MovesItIntoThatSpace()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var store = Store();
        await store.StoreAsync(Chunk(0, "A"), Ct);

        (await store.UpdateAsync(Chunk(0, "B"), Ct)).Should().BeTrue();

        (await Store().CountVectorsOutsideSpaceAsync("B", Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Clear_LeavesNothingOutside()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var store = Store();
        await store.StoreBatchAsync([Chunk(0, "A"), Chunk(1, "A")], Ct);

        await store.ClearAsync(Ct);
        await store.StoreAsync(Chunk(0, "B"), Ct);

        (await Store().CountVectorsOutsideSpaceAsync("B", Ct)).Should().Be(0);
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
            provider.Dispose();
        FluxIndex.Tests.Shared.SqliteTestPools.Release(_db);
        try { File.Delete(_db); } catch (IOException) { }
    }
}
