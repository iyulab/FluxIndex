using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Contract;

/// <summary>
/// Shared filter-contract regression suite for IVectorStore implementations. Derive a concrete
/// class per store (in that store's test project) and implement <see cref="CreateStoreAsync"/> —
/// the inherited facts then run against that store, guaranteeing identical filter semantics
/// across implementations without per-store copy-paste.
/// Container-backed stores (PostgreSQL, Qdrant) cover the same cases in their own integration
/// suites; this suite targets stores constructible in-process.
/// </summary>
public abstract class VectorStoreFilterContractSuite
{
    /// <summary>Creates a fresh, empty store instance.</summary>
    protected abstract Task<IVectorStore> CreateStoreAsync();

    /// <summary>Embedding dimension the store under test expects.</summary>
    protected virtual int Dimensions => 4;

    private DocumentChunk CreateChunk(string documentId, string workspaceId, int chunkIndex = 0)
    {
        var embedding = new float[Dimensions];
        embedding[0] = 1f;
        return new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = documentId,
            ChunkIndex = chunkIndex,
            Content = $"content of {documentId}#{chunkIndex}",
            Embedding = embedding,
            Metadata = new Dictionary<string, object> { ["workspace_id"] = workspaceId }
        };
    }

    private float[] QueryVector()
    {
        var v = new float[Dimensions];
        v[0] = 1f;
        return v;
    }

    [Fact]
    public async Task ScalarFilter_RestrictsToEqualValue()
    {
        var store = await CreateStoreAsync();
        await store.StoreAsync(CreateChunk("doc-1", "ws-a"), TestContext.Current.CancellationToken);
        await store.StoreAsync(CreateChunk("doc-2", "ws-b"), TestContext.Current.CancellationToken);

        var results = await store.SearchAsync(QueryVector(), topK: 10, minScore: -1f, filters: new Dictionary<string, object> { ["workspace_id"] = "ws-a" }, cancellationToken: TestContext.Current.CancellationToken);

        var chunk = Assert.Single(results);
        Assert.Equal("doc-1", chunk.DocumentId);
    }

    [Fact]
    public async Task CollectionFilter_MatchesAnyElement()
    {
        var store = await CreateStoreAsync();
        await store.StoreAsync(CreateChunk("doc-1", "ws-a"), TestContext.Current.CancellationToken);
        await store.StoreAsync(CreateChunk("doc-2", "ws-b"), TestContext.Current.CancellationToken);
        await store.StoreAsync(CreateChunk("doc-3", "ws-c"), TestContext.Current.CancellationToken);

        var results = await store.SearchAsync(QueryVector(), topK: 10, minScore: -1f, filters: new Dictionary<string, object>
            {
                ["workspace_id"] = new List<string> { "ws-a", "ws-c" }
            }, cancellationToken: TestContext.Current.CancellationToken);

        var ids = results.Select(r => r.DocumentId).OrderBy(x => x).ToList();
        Assert.Equal(["doc-1", "doc-3"], ids);
    }

    [Fact]
    public async Task CollectionFilter_NoElementMatches_ReturnsEmpty()
    {
        var store = await CreateStoreAsync();
        await store.StoreAsync(CreateChunk("doc-1", "ws-a"), TestContext.Current.CancellationToken);

        var results = await store.SearchAsync(QueryVector(), topK: 10, minScore: -1f, filters: new Dictionary<string, object>
            {
                ["workspace_id"] = new List<string> { "ws-x", "ws-y" }
            }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task UnsupportedFilterValue_Throws_InsteadOfSilentZeroResults()
    {
        var store = await CreateStoreAsync();
        await store.StoreAsync(CreateChunk("doc-1", "ws-a"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SearchAsync(QueryVector(), topK: 10, minScore: -1f, filters: new Dictionary<string, object> { ["workspace_id"] = new object() }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmptyCollectionFilter_Throws()
    {
        var store = await CreateStoreAsync();
        await store.StoreAsync(CreateChunk("doc-1", "ws-a"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SearchAsync(QueryVector(), topK: 10, minScore: -1f, filters: new Dictionary<string, object> { ["workspace_id"] = new List<string>() }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoFilter_ReturnsAllStoredChunks()
    {
        var store = await CreateStoreAsync();
        await store.StoreAsync(CreateChunk("doc-1", "ws-a"), TestContext.Current.CancellationToken);
        await store.StoreAsync(CreateChunk("doc-2", "ws-b"), TestContext.Current.CancellationToken);

        var results = await store.SearchAsync(QueryVector(), topK: 10, minScore: -1f, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count());
    }

    [Fact]
    public async Task DeleteByFilter_CollectionValue_RemovesAnyMatch_WhenSupported()
    {
        var store = await CreateStoreAsync();
        await store.StoreAsync(CreateChunk("doc-1", "ws-a"), TestContext.Current.CancellationToken);
        await store.StoreAsync(CreateChunk("doc-2", "ws-b"), TestContext.Current.CancellationToken);
        await store.StoreAsync(CreateChunk("doc-3", "ws-c"), TestContext.Current.CancellationToken);

        int deleted;
        try
        {
            deleted = await store.DeleteByFilterAsync(new Dictionary<string, object>
            {
                ["workspace_id"] = new[] { "ws-a", "ws-c" }
            }, TestContext.Current.CancellationToken);
        }
        catch (NotSupportedException)
        {
            // Contract-permitted opt-out — store declares no metadata-scoped deletion.
            return;
        }

        Assert.Equal(2, deleted);
        Assert.Equal(1, await store.CountAsync(TestContext.Current.CancellationToken));
    }
}
