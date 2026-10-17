using AwesomeAssertions;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.SDK.Services;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// <see cref="InMemoryVectorStore.AdoptUnrecordedVectorsAsync"/>: vectors written without a revision take the one the index
/// was built in, without re-embedding; recorded revisions stay, and a persisted store keeps the adoption across a reload.
/// </summary>
public sealed class InMemoryVectorStoreVectorSpaceAdoptTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fluxindex-mem-adopt-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { File.Delete(_path); } catch (IOException) { }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DocumentChunk Chunk(int i, string? revision, bool withVector = true) => new()
    {
        Id = $"c{i:00}", DocumentId = "doc", Content = $"text {i}", ChunkIndex = i, TotalChunks = 5, TokenCount = 1,
        Embedding = withVector ? [1f, 0.1f * i, 0f, 0f] : null,
        VectorSpaceRevision = revision,
    };

    [Fact]
    public async Task Adopt_GivesVectorsWithoutARevisionTheOneTheIndexWasBuiltIn_AndSurvivesAReload()
    {
        var store = new InMemoryVectorStore(_path, autoSave: true);
        await store.StoreBatchAsync(
            [Chunk(0, revision: null), Chunk(1, revision: null), Chunk(2, "B"), Chunk(3, "A"), Chunk(4, revision: null, withVector: false)], Ct);

        (await store.AdoptUnrecordedVectorsAsync("A", Ct)).Should().Be(2);
        (await store.CountVectorsOutsideSpaceAsync("A", Ct)).Should().Be(1, "only c02 was computed in another revision");
        (await store.AdoptUnrecordedVectorsAsync("A", Ct)).Should().Be(0);

        var reloaded = new InMemoryVectorStore(_path, autoSave: true);
        (await reloaded.CountVectorsOutsideSpaceAsync("A", Ct)).Should().Be(1);
        (await reloaded.GetAsync("c00", Ct))!.VectorSpaceRevision.Should().Be("A");
        (await reloaded.GetAsync("c04", Ct))!.VectorSpaceRevision.Should().BeNull("a chunk without a vector has no space");
    }
}
