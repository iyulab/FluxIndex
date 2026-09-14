using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Contract;

/// <summary>
/// Shared chunk-identity contract suite for <see cref="IKeywordSearchService"/> implementations, the
/// keyword-index counterpart of <see cref="VectorStoreChunkIdentityContractSuite"/>: a chunk id is the
/// row key, and indexing an id that is already indexed replaces its postings rather than adding a
/// second set. Derive a concrete class per implementation and implement <see cref="CreateServiceAsync"/>.
///
/// <para>
/// This suite exists because the relational implementations replaced (delete postings, upsert row,
/// re-post) while the in-memory BM25 index appended a second posting per term and counted the chunk
/// twice — the same call, two meanings, each green in its own tests.
/// </para>
/// </summary>
public abstract class KeywordSearchChunkIdentityContractSuite
{
    /// <summary>Creates a fresh, empty keyword index.</summary>
    protected abstract Task<IKeywordSearchService> CreateServiceAsync();

    private static DocumentChunk CreateChunk(string id, string content, int chunkIndex = 0, string documentId = "doc-1") => new()
    {
        Id = id,
        DocumentId = documentId,
        ChunkIndex = chunkIndex,
        Content = content,
        TokenCount = 3
    };

    [Fact]
    public async Task IndexChunksAsync_SameIdTwice_ReplacesThePostings()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync();

        await service.IndexChunksAsync([CreateChunk("dup", "alpha beta gamma")], ct);
        await service.IndexChunksAsync([CreateChunk("dup", "delta epsilon zeta")], ct);

        var newWording = await service.SearchAsync("delta", cancellationToken: ct);
        var hit = Assert.Single(newWording);
        Assert.Equal("dup", hit.Chunk.Id);
        Assert.Equal("delta epsilon zeta", hit.Chunk.Content);

        var oldWording = await service.SearchAsync("alpha", cancellationToken: ct);
        Assert.Empty(oldWording);

        var stats = await service.GetStatisticsAsync(ct);
        Assert.Equal(1, stats.TotalDocuments);
    }

    [Fact]
    public async Task IndexChunksAsync_SameIdTwiceWithTheSameContent_IsOneRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync();

        await service.IndexChunksAsync([CreateChunk("same", "alpha beta gamma")], ct);
        await service.IndexChunksAsync([CreateChunk("same", "alpha beta gamma")], ct);

        var hits = await service.SearchAsync("alpha", cancellationToken: ct);
        Assert.Single(hits);
        Assert.Equal(1, (await service.GetStatisticsAsync(ct)).TotalDocuments);
    }

    [Fact]
    public async Task DeleteChunkAsync_RemovesTheChunkFromSearch()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync();

        await service.IndexChunksAsync(
        [
            CreateChunk("keep", "alpha beta", 0),
            CreateChunk("drop", "alpha gamma", 1)
        ], ct);
        await service.DeleteChunkAsync("drop", ct);

        var hits = await service.SearchAsync("alpha", cancellationToken: ct);
        var hit = Assert.Single(hits);
        Assert.Equal("keep", hit.Chunk.Id);
        Assert.Equal(1, (await service.GetStatisticsAsync(ct)).TotalDocuments);
    }

    [Fact]
    public async Task GetChunkIdsByDocumentIdAsync_ReturnsThisIndexesOwnIdsForTheDocumentOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync();

        await service.IndexChunksAsync(
        [
            CreateChunk("a-0", "alpha beta", 0, "doc-a"),
            CreateChunk("a-1", "alpha gamma", 1, "doc-a"),
            CreateChunk("b-0", "alpha delta", 0, "doc-b")
        ], ct);

        var ids = await service.GetChunkIdsByDocumentIdAsync("doc-a", ct);
        Assert.Equal(["a-0", "a-1"], ids.Order());

        Assert.Empty(await service.GetChunkIdsByDocumentIdAsync("doc-none", ct));
    }

    [Fact]
    public async Task GetChunkIdsByDocumentIdAsync_AfterReindexingAnIdAndDeletingAnother_ReflectsTheIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync();

        await service.IndexChunksAsync(
        [
            CreateChunk("a-0", "alpha beta", 0, "doc-a"),
            CreateChunk("a-1", "alpha gamma", 1, "doc-a")
        ], ct);
        await service.IndexChunksAsync([CreateChunk("a-0", "alpha epsilon", 0, "doc-a")], ct);
        await service.DeleteChunkAsync("a-1", ct);

        // A generation swap deletes previous-minus-attempted by these ids: a duplicate would delete a
        // row that was just written, a stale id would delete nothing and leave the row behind.
        var ids = await service.GetChunkIdsByDocumentIdAsync("doc-a", ct);
        Assert.Equal(["a-0"], ids);
    }
}
