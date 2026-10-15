using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Contract;

/// <summary>
/// Shared contract suite for <see cref="GraphChunkRetriever"/> over a real <see cref="IGraphStore"/>: the store's exact name
/// lookup, its traversal in both directions and its partitions are what the retriever stands on, and each store implements
/// them itself. Derive a concrete class per store and implement <see cref="CreateStoreAsync"/>.
///
/// <para>
/// Every fact writes into a fresh partition so container-backed stores can share one instance across the suite. The chunk
/// store is a stand-in that returns the chunks it is asked for — the graph store is what is under test.
/// </para>
/// </summary>
public abstract class GraphChunkRetrieverContractSuite
{
    /// <summary>Returns the store under test (fresh, or shared and isolated by partitions).</summary>
    protected abstract Task<IGraphStore> CreateStoreAsync();

    private static string Fresh(string label) => $"{label}-{Guid.NewGuid():N}";

    private static GraphEntity Entity(string partition, string name, string chunkId, string documentId, params string[] moreChunks) => new()
    {
        Id = Fresh("entity"),
        Name = name,
        NormalizedName = name.ToLowerInvariant(),
        Partition = partition,
        Type = NamedEntityType.Organization,
        SurfaceForms = [name],
        Confidence = 0.9,
        MentionCount = 1,
        ChunkIds = [chunkId, .. moreChunks],
        DocumentIds = [documentId],
    };

    private static GraphRelationship Relationship(GraphEntity source, GraphEntity target) => new()
    {
        Id = Fresh("rel"),
        SourceEntityId = source.Id,
        TargetEntityId = target.Id,
        Type = RelationType.RelatedTo,
        Label = "supplies",
        Confidence = 0.9,
        Weight = 1.0,
        EvidenceChunkIds = [],
    };

    private static GraphChunkRetriever Retriever(IGraphStore store)
    {
        var chunks = Substitute.For<IVectorStore>();
        chunks.GetChunksByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<string>>()
                .Select(id => new DocumentChunk { Id = id, DocumentId = id.Split(':')[0], Content = id })
                .ToList());
        return new GraphChunkRetriever(store, chunks, NullLogger<GraphChunkRetriever>.Instance);
    }

    [Fact]
    public async Task A_particle_suffixed_Korean_query_finds_the_stored_entity()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var p = Fresh("p");
        var samsung = Entity(p, "삼성전자", "d1:c1", "d1");
        await store.StoreEntitiesBatchAsync([samsung], ct);

        var hits = await Retriever(store).RetrieveAsync("삼성전자의 협력사는?", new GraphRetrievalOptions { Partition = p, MaxHops = 0 }, ct);

        Assert.Equal("d1:c1", Assert.Single(hits).Chunk.Id);
        Assert.Equal([samsung.Id], hits[0].SeedEntityIds);
    }

    [Fact]
    public async Task A_relationship_is_walked_in_both_directions_and_decays_per_hop()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var p = Fresh("p");
        var acme = Entity(p, "Acme Corp", "d1:c1", "d1");
        var globex = Entity(p, "Globex", "d2:c2", "d2");
        await store.StoreEntitiesBatchAsync([acme, globex], ct);
        await store.StoreRelationshipsBatchAsync([Relationship(acme, globex)], ct);
        var retriever = Retriever(store);

        var fromSource = await retriever.RetrieveAsync("who supplies acme corp?", new GraphRetrievalOptions { Partition = p }, ct);
        var fromTarget = await retriever.RetrieveAsync("who does globex supply?", new GraphRetrievalOptions { Partition = p }, ct);
        var noHops = await retriever.RetrieveAsync("who supplies acme corp?", new GraphRetrievalOptions { Partition = p, MaxHops = 0 }, ct);

        Assert.Equal(["d1:c1", "d2:c2"], fromSource.Select(h => h.Chunk.Id));
        Assert.Equal([1.0, 0.5], fromSource.Select(h => h.Score));
        Assert.Equal([0, 1], fromSource.Select(h => h.Hops));
        Assert.Equal(["d2:c2", "d1:c1"], fromTarget.Select(h => h.Chunk.Id));
        Assert.Equal(["d1:c1"], noHops.Select(h => h.Chunk.Id));
    }

    [Fact]
    public async Task A_chunk_that_mentions_two_named_entities_ranks_first()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var p = Fresh("p");
        await store.StoreEntitiesBatchAsync(
            [Entity(p, "Acme Corp", "d1:c1", "d1", "d3:both"), Entity(p, "Globex", "d2:c2", "d2", "d3:both")], ct);

        var hits = await Retriever(store).RetrieveAsync("acme corp and globex", new GraphRetrievalOptions { Partition = p, MaxHops = 0 }, ct);

        Assert.Equal("d3:both", hits[0].Chunk.Id);
        Assert.Equal(2.0, hits[0].Score);
    }

    [Fact]
    public async Task Another_partitions_entity_of_the_same_name_is_not_read()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var (mine, theirs) = (Fresh("p"), Fresh("p"));
        await store.StoreEntitiesBatchAsync([Entity(mine, "Acme Corp", "d1:c1", "d1"), Entity(theirs, "Acme Corp", "d9:c9", "d9")], ct);

        var hits = await Retriever(store).RetrieveAsync("acme corp", new GraphRetrievalOptions { Partition = mine, MaxHops = 0 }, ct);

        Assert.Equal(["d1:c1"], hits.Select(h => h.Chunk.Id));
    }

    [Fact]
    public async Task A_document_scope_keeps_entities_of_other_documents_out()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var p = Fresh("p");
        await store.StoreEntitiesBatchAsync([Entity(p, "Acme Corp", "d1:c1", "d1"), Entity(p, "Globex", "d2:c2", "d2")], ct);

        var hits = await Retriever(store).RetrieveAsync(
            "acme corp globex",
            new GraphRetrievalOptions { Partition = p, MaxHops = 0, Filters = new Dictionary<string, object> { [FilterKeys.DocumentId] = "d2" } },
            ct);

        Assert.Equal(["d2:c2"], hits.Select(h => h.Chunk.Id));
    }
}
