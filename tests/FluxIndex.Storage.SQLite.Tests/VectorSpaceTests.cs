using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// The plain SQLite store keeps the vector-space revision of each vector in its row, so a fresh instance — a restarted
/// process — tells which vectors an in-place re-embed into a new revision has not reached.
/// </summary>
[Collection("SQLite Tests")]
public sealed class VectorSpaceTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"fluxindex-space-{Guid.NewGuid():N}.db");
    private readonly List<ServiceProvider> _providers = [];

    private IVectorStore Store()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSQLiteVectorStore(new SQLiteOptions { DatabasePath = _db, AutoMigrate = false });
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.CreateScope().ServiceProvider.GetRequiredService<IVectorStore>();
    }

    private static DocumentChunk Chunk(int i, string? revision, bool withVector = true) => new()
    {
        Id = $"c{i:00}", DocumentId = "doc", Content = $"text {i}", ChunkIndex = i, TotalChunks = 20, TokenCount = 1,
        CreatedAt = DateTime.UtcNow,
        Embedding = withVector ? [1f, 0.1f * i, 0f, 0f] : null,
        VectorSpaceRevision = revision,
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AfterARestartMidReembed_TheStoreCountsTheVectorsNotYetInTheNewSpace()
    {
        await Store().StoreBatchAsync(Enumerable.Range(0, 10).Select(i => Chunk(i, "A")), Ct);
        var reembedding = Store();
        await reembedding.StoreBatchAsync(Enumerable.Range(0, 3).Select(i => Chunk(i, "B")), Ct);
        await reembedding.UpdateAsync(Chunk(3, "B"), Ct);

        var restarted = Store();
        (await restarted.CountVectorsOutsideSpaceAsync("B", Ct)).Should().Be(6);
        (await restarted.GetChunkIdsOutsideSpaceAsync("B", 4, Ct)).Should().Equal("c04", "c05", "c06", "c07");
        (await restarted.GetAsync("c02", Ct))!.VectorSpaceRevision.Should().Be("B");
    }

    [Fact]
    public async Task AVectorWithoutARevision_CountsAsOutside_AndAChunkWithoutAVectorDoesNot()
    {
        var store = Store();
        await store.StoreBatchAsync([Chunk(0, "B"), Chunk(1, revision: null), Chunk(2, "B", withVector: false)], Ct);

        (await store.CountVectorsOutsideSpaceAsync("B", Ct)).Should().Be(1);
        (await store.GetChunkIdsOutsideSpaceAsync("B", 10, Ct)).Should().Equal("c01");
    }

    [Fact]
    public async Task Adopt_GivesVectorsWithoutARevisionTheOneTheIndexWasBuiltIn_AndLeavesTheRest()
    {
        await Store().StoreBatchAsync(
            [Chunk(0, revision: null), Chunk(1, revision: null), Chunk(2, "B"), Chunk(3, "A"), Chunk(4, revision: null, withVector: false)], Ct);

        (await Store().AdoptUnrecordedVectorsAsync("A", Ct)).Should().Be(2);

        var restarted = Store();
        (await restarted.CountVectorsOutsideSpaceAsync("A", Ct)).Should().Be(1, "only c02 was computed in another revision");
        (await restarted.GetAsync("c00", Ct))!.VectorSpaceRevision.Should().Be("A");
        (await restarted.GetAsync("c02", Ct))!.VectorSpaceRevision.Should().Be("B", "a recorded revision is not changed");
        (await restarted.GetAsync("c04", Ct))!.VectorSpaceRevision.Should().BeNull("a chunk without a vector has no space");
        (await restarted.AdoptUnrecordedVectorsAsync("A", Ct)).Should().Be(0);
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
            provider.Dispose();
        FluxIndex.Tests.Shared.SqliteTestPools.Release(_db);
        try { File.Delete(_db); } catch (IOException) { }
    }
}
