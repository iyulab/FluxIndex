using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Contract;

/// <summary>
/// Shared contract suite for <see cref="IVectorStore.ReassignDocumentAsync"/>: a document's chunks move to a new
/// document id and new chunk ids with their vectors intact, metadata updates apply, and every rejected call leaves the
/// store exactly as it was. Derive a concrete class per store and implement <see cref="CreateStoreAsync"/>.
/// </summary>
/// <remarks>
/// Vectors are checked through search, not by reading them back: several stores do not return embeddings on reads.
/// Each chunk sits on its own axis, so a query on that axis with a similarity floor near 1 finds exactly that chunk —
/// only if the moved row still carries the vector it was stored with.
/// </remarks>
public abstract class VectorStoreReassignContractSuite
{
    private const string OldDocument = "doc-old";
    private const string NewDocument = "doc-new";
    private const string OtherDocument = "doc-other";

    /// <summary>Creates a fresh, empty store instance.</summary>
    protected abstract Task<IVectorStore> CreateStoreAsync();

    /// <summary>Embedding dimension the store under test expects.</summary>
    protected virtual int Dimensions => 4;

    private static readonly Dictionary<string, string> Map = new()
    {
        ["old-a"] = "new-a",
        ["old-b"] = "new-b",
        ["old-c"] = "new-c",
    };

    private float[] Axis(int axis)
    {
        var v = new float[Dimensions];
        v[axis % Dimensions] = 1f;
        return v;
    }

    private DocumentChunk Chunk(string id, string documentId, int axis, int chunkIndex) => new()
    {
        Id = id,
        DocumentId = documentId,
        ChunkIndex = chunkIndex,
        TotalChunks = 3,
        Content = $"passage {id}",
        TokenCount = 2,
        Embedding = Axis(axis),
        Metadata = new Dictionary<string, object>
        {
            ["origin"] = "contract",
            ["source_path"] = "old/place.txt",
            ["obsolete"] = "remove me"
        }
    };

    private async Task<IVectorStore> SeededStoreAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        await store.StoreBatchAsync(
        [
            Chunk("old-a", OldDocument, 0, 0),
            Chunk("old-b", OldDocument, 1, 1),
            Chunk("old-c", OldDocument, 2, 2),
            Chunk("other-a", OtherDocument, 3, 0),
        ], ct);
        return store;
    }

    private static Dictionary<string, object> Scope(string documentId) =>
        new() { [FilterKeys.DocumentId] = documentId };

    private static async Task<List<string>> IdsOf(IVectorStore store, string documentId) =>
        (await store.GetChunkIdsByDocumentIdAsync(documentId, TestContext.Current.CancellationToken))
            .Order(StringComparer.Ordinal).ToList();

    [Fact]
    public async Task Reassign_MovesEveryChunkUnderItsNewId_AndLeavesTheOldDocumentEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeededStoreAsync();

        var moved = await store.ReassignDocumentAsync(OldDocument, NewDocument, Map, cancellationToken: ct);

        Assert.Equal(3, moved);
        Assert.Empty(await IdsOf(store, OldDocument));
        Assert.Equal(["new-a", "new-b", "new-c"], await IdsOf(store, NewDocument));
        Assert.Null(await store.GetAsync("old-b", ct));
        var fetched = await store.GetAsync("new-b", ct);
        Assert.NotNull(fetched);
        Assert.Equal(NewDocument, fetched.DocumentId);
        Assert.Equal("passage old-b", fetched.Content);
        Assert.Equal(1, fetched.ChunkIndex);
        Assert.Equal(["other-a"], await IdsOf(store, OtherDocument));
    }

    [Fact]
    public async Task Reassign_KeepsEachVector_SoTheNewScopeFindsTheSameChunkAtFullSimilarity()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeededStoreAsync();

        await store.ReassignDocumentAsync(OldDocument, NewDocument, Map, cancellationToken: ct);

        for (var axis = 0; axis < 3; axis++)
        {
            var hits = (await store.SearchAsync(Axis(axis), topK: 5, minScore: 0.9f, filters: Scope(NewDocument), cancellationToken: ct)).ToList();
            var hit = Assert.Single(hits);
            Assert.Equal(Map[$"old-{(char)('a' + axis)}"], hit.Id);
        }

        Assert.Empty(await store.SearchAsync(Axis(1), topK: 5, minScore: -1f, filters: Scope(OldDocument), cancellationToken: ct));
    }

    [Fact]
    public async Task Reassign_AppliesMetadataUpdates_NullRemovesTheKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeededStoreAsync();

        await store.ReassignDocumentAsync(
            OldDocument,
            NewDocument,
            Map,
            new Dictionary<string, object?> { ["source_path"] = "new/place.txt", ["obsolete"] = null },
            ct);

        var fetched = await store.GetAsync("new-a", ct);
        Assert.NotNull(fetched);
        Assert.NotNull(fetched.Metadata);
        Assert.Equal("new/place.txt", fetched.Metadata["source_path"]?.ToString());
        Assert.Equal("contract", fetched.Metadata["origin"]?.ToString());
        Assert.False(fetched.Metadata.ContainsKey("obsolete"));

        // The updated value is what a metadata filter sees, too.
        var hits = await store.SearchAsync(
            Axis(0), topK: 5, minScore: -1f,
            filters: new Dictionary<string, object> { ["source_path"] = "new/place.txt" },
            cancellationToken: ct);
        Assert.Equal(["new-a", "new-b", "new-c"], hits.Select(h => h.Id).Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task Reassign_ExtraMapEntries_AreIgnored()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeededStoreAsync();
        var map = new Dictionary<string, string>(Map) { ["held-elsewhere"] = "new-elsewhere" };

        Assert.Equal(3, await store.ReassignDocumentAsync(OldDocument, NewDocument, map, cancellationToken: ct));
        Assert.Equal(["new-a", "new-b", "new-c"], await IdsOf(store, NewDocument));
    }

    [Fact]
    public async Task Reassign_AMissingMapEntry_Throws_AndChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeededStoreAsync();
        var partial = new Dictionary<string, string> { ["old-a"] = "new-a", ["old-b"] = "new-b" };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => store.ReassignDocumentAsync(OldDocument, NewDocument, partial, cancellationToken: ct));

        Assert.Contains("old-c", ex.Message, StringComparison.Ordinal);
        Assert.Equal(["old-a", "old-b", "old-c"], await IdsOf(store, OldDocument));
        Assert.Empty(await IdsOf(store, NewDocument));
    }

    [Fact]
    public async Task Reassign_ToADocumentThatHasChunks_Throws_AndChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeededStoreAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.ReassignDocumentAsync(OldDocument, OtherDocument, Map, cancellationToken: ct));

        Assert.Equal(["old-a", "old-b", "old-c"], await IdsOf(store, OldDocument));
        Assert.Equal(["other-a"], await IdsOf(store, OtherDocument));
    }

    [Fact]
    public async Task Reassign_OntoAChunkIdThatIsTaken_Throws_AndChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeededStoreAsync();
        var map = new Dictionary<string, string> { ["old-a"] = "new-a", ["old-b"] = "other-a", ["old-c"] = "new-c" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.ReassignDocumentAsync(OldDocument, NewDocument, map, cancellationToken: ct));

        Assert.Equal(["old-a", "old-b", "old-c"], await IdsOf(store, OldDocument));
        Assert.Equal(["other-a"], await IdsOf(store, OtherDocument));
        Assert.Empty(await IdsOf(store, NewDocument));
    }

    [Fact]
    public async Task Reassign_AnUnknownDocument_MovesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeededStoreAsync();

        Assert.Equal(0, await store.ReassignDocumentAsync("doc-missing", NewDocument, Map, cancellationToken: ct));
        Assert.Empty(await IdsOf(store, NewDocument));
    }

    [Fact]
    public async Task Reassign_InvalidArguments_ThrowBeforeAnythingIsRead()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeededStoreAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.ReassignDocumentAsync(OldDocument, OldDocument, Map, cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.ReassignDocumentAsync(OldDocument, NewDocument, new Dictionary<string, string>(), cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.ReassignDocumentAsync(
                OldDocument, NewDocument,
                new Dictionary<string, string> { ["old-a"] = "same", ["old-b"] = "same", ["old-c"] = "new-c" },
                cancellationToken: ct));

        Assert.Equal(["old-a", "old-b", "old-c"], await IdsOf(store, OldDocument));
    }
}
