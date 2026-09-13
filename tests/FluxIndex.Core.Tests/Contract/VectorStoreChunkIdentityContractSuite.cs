using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Contract;

/// <summary>
/// Shared chunk-identity contract suite for IVectorStore implementations (docs/REFERENCE.md,
/// "Chunk identity"): <c>DocumentChunk.Id</c> is a free string the store keys on — what you store
/// under is what you read back and delete by — and re-storing the same id is an update, not a
/// second row. Derive a concrete class per store and implement <see cref="CreateStoreAsync"/>.
///
/// <para>
/// This suite exists because three implementations disagreed on the same call: one upserted, one
/// added a duplicate row (and threw on the second write), one discarded the caller's id and minted
/// its own. Each was green in its own tests. Container-backed stores (PostgreSQL, Qdrant) cover the
/// same cases in their own integration suites; this suite targets stores constructible in-process.
/// </para>
/// </summary>
public abstract class VectorStoreChunkIdentityContractSuite
{
    /// <summary>Creates a fresh, empty store instance.</summary>
    protected abstract Task<IVectorStore> CreateStoreAsync();

    /// <summary>Embedding dimension the store under test expects.</summary>
    protected virtual int Dimensions => 4;

    private DocumentChunk CreateChunk(string id, string content, int axis, int chunkIndex = 0, int totalChunks = 3)
    {
        var embedding = new float[Dimensions];
        embedding[axis % Dimensions] = 1f;
        return new DocumentChunk
        {
            Id = id,
            DocumentId = "doc-1",
            ChunkIndex = chunkIndex,
            TotalChunks = totalChunks,
            Content = content,
            TokenCount = 2,
            Embedding = embedding,
            Metadata = new Dictionary<string, object> { ["origin"] = "contract" }
        };
    }

    private float[] Axis(int axis)
    {
        var v = new float[Dimensions];
        v[axis % Dimensions] = 1f;
        return v;
    }

    [Fact]
    public async Task StoreAsync_KeepsTheCallerId_AndReadsBackUnderIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();

        var returned = await store.StoreAsync(CreateChunk("chunk-1", "the quick brown fox", 0), ct);

        Assert.Equal("chunk-1", returned);
        var fetched = await store.GetAsync("chunk-1", ct);
        Assert.NotNull(fetched);
        Assert.Equal("chunk-1", fetched.Id);
        Assert.Equal("the quick brown fox", fetched.Content);
        Assert.True(await store.ExistsAsync("chunk-1", ct));
        Assert.True(await store.DeleteAsync("chunk-1", ct));
        Assert.Null(await store.GetAsync("chunk-1", ct));
    }

    [Fact]
    public async Task StoreBatchAsync_KeepsEveryCallerId_InOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();

        var ids = (await store.StoreBatchAsync(
        [
            CreateChunk("batch-a", "alpha", 0, chunkIndex: 0),
            CreateChunk("batch-b", "bravo", 1, chunkIndex: 1),
        ], ct)).ToList();

        Assert.Equal(["batch-a", "batch-b"], ids);
        Assert.Equal("alpha", (await store.GetAsync("batch-a", ct))?.Content);
        Assert.Equal("bravo", (await store.GetAsync("batch-b", ct))?.Content);
    }

    [Fact]
    public async Task StoreAsync_SameIdTwice_UpdatesTheRow_NotADuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();

        await store.StoreAsync(CreateChunk("dup", "first wording", 0), ct);
        var returned = await store.StoreAsync(CreateChunk("dup", "second wording", 1), ct);

        Assert.Equal("dup", returned);
        var rows = (await store.GetByDocumentIdAsync("doc-1", ct)).ToList();
        var row = Assert.Single(rows);
        Assert.Equal("second wording", row.Content);

        // The vector follows the row: the second embedding finds it exactly once.
        var hits = (await store.SearchAsync(Axis(1), topK: 10, minScore: -1f, cancellationToken: ct)).ToList();
        Assert.Single(hits, c => c.Id == "dup");
    }

    [Fact]
    public async Task StoreBatchAsync_WithAnAlreadyStoredId_UpdatesInsteadOfDuplicating()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();

        await store.StoreAsync(CreateChunk("keep", "original", 0, chunkIndex: 0), ct);
        await store.StoreBatchAsync(
        [
            CreateChunk("keep", "rewritten", 2, chunkIndex: 0),
            CreateChunk("new", "added", 1, chunkIndex: 1),
        ], ct);

        var rows = (await store.GetByDocumentIdAsync("doc-1", ct)).ToList();
        Assert.Equal(["keep", "new"], rows.Select(r => r.Id).OrderBy(x => x).ToList());
        Assert.Equal("rewritten", rows.Single(r => r.Id == "keep").Content);
    }

    [Fact]
    public async Task StoreAsync_EmptyId_GetsOneFromTheStore()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();

        var returned = await store.StoreAsync(CreateChunk(string.Empty, "no id given", 0), ct);

        Assert.False(string.IsNullOrWhiteSpace(returned));
        Assert.NotNull(await store.GetAsync(returned, ct));
    }

    // An id the store generated is the chunk's identity from then on, so the instance the caller
    // handed over carries it too — not only the return value. A caller that keeps the object (to
    // roll back, to tie provenance to it) must not be left holding one with no id.
    [Fact]
    public async Task StoreAsync_EmptyId_FillsTheChunkItWasGiven()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var chunk = CreateChunk(string.Empty, "no id given", 0);

        var returned = await store.StoreAsync(chunk, ct);

        Assert.Equal(returned, chunk.Id);
    }

    [Fact]
    public async Task StoreBatchAsync_EmptyIds_FillTheChunksTheyWereGiven()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var chunks = new[]
        {
            CreateChunk(string.Empty, "first without id", 0, chunkIndex: 0),
            CreateChunk(string.Empty, "second without id", 1, chunkIndex: 1),
        };

        var returned = await store.StoreBatchAsync(chunks, ct);

        Assert.Equal(returned, chunks.Select(c => c.Id).ToList());
        Assert.NotEqual(chunks[0].Id, chunks[1].Id);
    }

    /// <summary>
    /// The position a chunk was stored with is the position it reads back with — on every read path,
    /// not only the metadata convenience keys some stores add. Four relational stores had no column for
    /// <c>TotalChunks</c> at all, so every "chunk i of N" citation read "of 0" while their own suites were
    /// green: nothing here ever set the field before this fact.
    /// </summary>
    [Fact]
    public async Task StoreAsync_RoundTripsChunkPosition_OnEveryReadPath()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();

        await store.StoreBatchAsync(
        [
            CreateChunk("pos-0", "first of three", 0, chunkIndex: 0, totalChunks: 3),
            CreateChunk("pos-1", "second of three", 1, chunkIndex: 1, totalChunks: 3),
            CreateChunk("pos-2", "third of three", 2, chunkIndex: 2, totalChunks: 3),
        ], ct);

        var byId = await store.GetAsync("pos-1", ct);
        Assert.NotNull(byId);
        Assert.Equal((1, 3), (byId.ChunkIndex, byId.TotalChunks));

        var byDocument = (await store.GetByDocumentIdAsync("doc-1", ct)).OrderBy(c => c.ChunkIndex).ToList();
        Assert.Equal([0, 1, 2], byDocument.Select(c => c.ChunkIndex).ToList());
        Assert.All(byDocument, c => Assert.Equal(3, c.TotalChunks));

        var hit = Assert.Single(await store.SearchAsync(Axis(2), topK: 1, minScore: -1f, cancellationToken: ct));
        Assert.Equal(("pos-2", 2, 3), (hit.Id, hit.ChunkIndex, hit.TotalChunks));

        // Re-storing the row with a new position updates it — the field follows the write like Content does.
        await store.StoreAsync(CreateChunk("pos-1", "second of four", 1, chunkIndex: 1, totalChunks: 4), ct);
        Assert.Equal(4, (await store.GetAsync("pos-1", ct))?.TotalChunks);
    }
}
