using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Graph;

/// <summary>
/// <see cref="GraphChunkRetriever"/> against a substituted graph store and chunk store: which entities a query seeds, how
/// the walk scores chunks, and how the scope cuts them. The store round trips live in the storage tests.
/// </summary>
public class GraphChunkRetrieverTests
{
    private readonly IGraphStore _graph = Substitute.For<IGraphStore>();
    private readonly IVectorStore _chunks = Substitute.For<IVectorStore>();
    private readonly List<GraphEntity> _entities = [];
    private readonly Dictionary<string, DocumentChunk> _chunkRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GraphStoreTraversalResult> _walks = new(StringComparer.Ordinal);
    private readonly List<IReadOnlyList<string>> _fetched = [];

    public GraphChunkRetrieverTests()
    {
        _graph.GetEntitiesByNormalizedNamesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var names = call.Arg<IEnumerable<string>>().ToHashSet(StringComparer.Ordinal);
                var partition = call.Arg<string>();
                return (IReadOnlyList<GraphEntity>)_entities.Where(e => e.Partition == partition && names.Contains(e.NormalizedName)).ToList();
            });
        _graph.TraverseAsync(Arg.Any<string>(), Arg.Any<GraphStoreTraversalOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var start = call.ArgAt<string>(0);
                return _walks.TryGetValue(start, out var walk)
                    ? walk
                    : new GraphStoreTraversalResult { StartEntity = _entities.Single(e => e.Id == start) };
            });
        _chunks.GetChunksByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var ids = call.Arg<IEnumerable<string>>().ToList();
                _fetched.Add(ids);
                return ids.Where(_chunkRows.ContainsKey).Select(id => _chunkRows[id]);
            });
    }

    private GraphChunkRetriever Retriever() => new(_graph, _chunks, NullLogger<GraphChunkRetriever>.Instance);

    private GraphEntity AddEntity(string id, string normalizedName, string[] chunkIds, string[]? documentIds = null, int mentions = 1)
    {
        var entity = new GraphEntity
        {
            Id = id,
            Name = normalizedName,
            NormalizedName = normalizedName,
            ChunkIds = chunkIds,
            DocumentIds = documentIds ?? [],
            MentionCount = mentions,
        };
        _entities.Add(entity);
        return entity;
    }

    private void AddChunk(string id, string documentId, Dictionary<string, object>? metadata = null) =>
        _chunkRows[id] = new DocumentChunk { Id = id, DocumentId = documentId, Content = $"content of {id}", Metadata = metadata };

    private void Walk(string from, params (GraphEntity Entity, int Hops)[] reached) =>
        _walks[from] = new GraphStoreTraversalResult
        {
            StartEntity = _entities.Single(e => e.Id == from),
            Entities = reached.Select(r => r.Entity).ToList(),
            Paths = reached.ToDictionary(
                r => r.Entity.Id,
                r => new GraphPath { EntityIds = Enumerable.Repeat(from, r.Hops).Append(r.Entity.Id).ToList() }),
        };

    [Fact]
    public async Task A_particle_suffixed_Korean_query_seeds_the_stored_entity()
    {
        AddEntity("samsung", "삼성전자", ["c1"]);
        AddEntity("jeonja", "전자", ["c9"]);
        AddChunk("c1", "d1");
        AddChunk("c9", "d9");

        var hits = await Retriever().RetrieveAsync("삼성전자의 협력사는?", new GraphRetrievalOptions { MaxHops = 0 }, TestContext.Current.CancellationToken);

        var hit = Assert.Single(hits);
        Assert.Equal("c1", hit.Chunk.Id);
        Assert.Equal(["samsung"], hit.SeedEntityIds);
        Assert.Equal(0, hit.Hops);
    }

    [Fact]
    public async Task A_shorter_name_the_query_also_names_on_its_own_stays_a_seed()
    {
        AddEntity("samsung", "삼성전자", ["c1"]);
        AddEntity("jeonja", "전자", ["c9"]);
        AddChunk("c1", "d1");
        AddChunk("c9", "d9");

        var hits = await Retriever().RetrieveAsync("삼성전자와 전자 부품", new GraphRetrievalOptions { MaxHops = 0 }, TestContext.Current.CancellationToken);

        Assert.Equal(["c1", "c9"], hits.Select(h => h.Chunk.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_multi_word_name_with_a_possessive_and_any_casing_is_seeded()
    {
        AddEntity("apple", "apple inc", ["c1"]);
        AddEntity("tim", "Tim Cook", ["c2"]); // written as extracted, not lower-cased
        AddChunk("c1", "d1");
        AddChunk("c2", "d1");

        var hits = await Retriever().RetrieveAsync("What did Apple Inc's board ask Tim Cook?", new GraphRetrievalOptions { MaxHops = 0 }, TestContext.Current.CancellationToken);

        Assert.Equal(["c1", "c2"], hits.Select(h => h.Chunk.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_chunk_two_seeds_reach_ranks_above_one_seed_and_a_neighbour_decays_per_hop()
    {
        var a = AddEntity("a", "alpha", ["c1", "c2"]);
        AddEntity("b", "beta", ["c2"]);
        var n = AddEntity("n", "gamma", ["c3"]);
        AddChunk("c1", "d1");
        AddChunk("c2", "d1");
        AddChunk("c3", "d1");
        Walk("a", (n, 1));

        var hits = await Retriever().RetrieveAsync("alpha and beta", new GraphRetrievalOptions(), TestContext.Current.CancellationToken);

        Assert.Equal(["c2", "c1", "c3"], hits.Select(h => h.Chunk.Id));
        Assert.Equal([2.0, 1.0, 0.5], hits.Select(h => h.Score));
        Assert.Equal([1, 2, 3], hits.Select(h => h.Rank));
        Assert.Equal(["a", "b"], hits[0].SeedEntityIds);
        Assert.Equal(1, hits[2].Hops);
        Assert.Equal(["a"], hits[2].SeedEntityIds);
    }

    [Fact]
    public async Task No_hops_reads_the_seeds_own_chunks_without_walking()
    {
        AddEntity("a", "alpha", ["c1"]);
        AddChunk("c1", "d1");

        await Retriever().RetrieveAsync("alpha", new GraphRetrievalOptions { MaxHops = 0 }, TestContext.Current.CancellationToken);

        await _graph.DidNotReceive().TraverseAsync(Arg.Any<string>(), Arg.Any<GraphStoreTraversalOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_walk_is_bounded_and_goes_both_ways()
    {
        AddEntity("a", "alpha", ["c1"]);
        AddChunk("c1", "d1");

        await Retriever().RetrieveAsync("alpha", new GraphRetrievalOptions { MaxHops = 2, MaxNodesPerSeed = 7 }, TestContext.Current.CancellationToken);

        await _graph.Received(1).TraverseAsync(
            "a",
            Arg.Is<GraphStoreTraversalOptions>(o => o.MaxDepth == 2 && o.MaxNodes == 7 && o.Direction == TraversalDirection.Both),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_document_scope_keeps_out_of_scope_entities_chunks_from_being_fetched()
    {
        AddEntity("a", "alpha", ["c1"], documentIds: ["d1"]);
        AddEntity("b", "beta", ["c5"], documentIds: ["d2"]);
        AddChunk("c1", "d1");
        AddChunk("c5", "d2");

        var hits = await Retriever().RetrieveAsync(
            "alpha beta",
            new GraphRetrievalOptions { MaxHops = 0, Filters = new Dictionary<string, object> { [FilterKeys.DocumentId] = "d1" } },
            TestContext.Current.CancellationToken);

        Assert.Equal("c1", Assert.Single(hits).Chunk.Id);
        Assert.DoesNotContain("c5", Assert.Single(_fetched));
    }

    [Fact]
    public async Task Metadata_scope_is_matched_on_the_fetched_chunks()
    {
        AddEntity("a", "alpha", ["c1", "c2"]);
        AddChunk("c1", "d1", new Dictionary<string, object> { ["tenant"] = "x" });
        AddChunk("c2", "d2", new Dictionary<string, object> { ["tenant"] = "y" });

        var hits = await Retriever().RetrieveAsync(
            "alpha",
            new GraphRetrievalOptions { MaxHops = 0, Filters = new Dictionary<string, object> { ["tenant"] = new[] { "x", "z" } } },
            TestContext.Current.CancellationToken);

        Assert.Equal("c1", Assert.Single(hits).Chunk.Id);
    }

    [Fact]
    public async Task A_malformed_scope_throws_before_the_store_is_read()
    {
        var options = new GraphRetrievalOptions { Filters = new Dictionary<string, object> { ["tenant"] = new object() } };

        await Assert.ThrowsAnyAsync<ArgumentException>(() => Retriever().RetrieveAsync("alpha", options, TestContext.Current.CancellationToken));
        await _graph.DidNotReceive().GetEntitiesByNormalizedNamesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_query_naming_no_stored_entity_returns_nothing_and_fetches_no_chunk()
    {
        AddEntity("a", "alpha", ["c1"]);

        var hits = await Retriever().RetrieveAsync("nothing here", new GraphRetrievalOptions(), TestContext.Current.CancellationToken);

        Assert.Empty(hits);
        await _chunks.DidNotReceive().GetChunksByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_the_requested_partition_is_read()
    {
        AddEntity("a", "alpha", ["c1"]);
        AddChunk("c1", "d1");

        var hits = await Retriever().RetrieveAsync("alpha", new GraphRetrievalOptions { Partition = "other", MaxHops = 0 }, TestContext.Current.CancellationToken);

        Assert.Empty(hits);
        await _graph.Received().GetEntitiesByNormalizedNamesAsync(Arg.Any<IEnumerable<string>>(), "other", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seeds_and_results_are_capped()
    {
        AddEntity("a", "alpha", ["c1", "c2", "c3"]);
        AddEntity("b", "beta", ["c4"]);
        foreach (var id in new[] { "c1", "c2", "c3", "c4" })
            AddChunk(id, "d1");

        var hits = await Retriever().RetrieveAsync(
            "alpha beta", new GraphRetrievalOptions { MaxHops = 0, MaxSeedEntities = 1, MaxResults = 2 }, TestContext.Current.CancellationToken);

        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Equal(["a"], h.SeedEntityIds));
    }

    [Theory]
    [InlineData(0, 1, 50, 0.5, 20)]
    [InlineData(8, -1, 50, 0.5, 20)]
    [InlineData(8, 1, 0, 0.5, 20)]
    [InlineData(8, 1, 50, 0.0, 20)]
    [InlineData(8, 1, 50, 1.5, 20)]
    [InlineData(8, 1, 50, 0.5, 0)]
    public async Task Out_of_range_options_throw(int seeds, int hops, int nodes, double decay, int results)
    {
        var options = new GraphRetrievalOptions { MaxSeedEntities = seeds, MaxHops = hops, MaxNodesPerSeed = nodes, HopDecay = decay, MaxResults = results };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Retriever().RetrieveAsync("alpha", options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Name_candidates_cover_runs_possessives_and_Korean_substrings_in_both_casings()
    {
        var names = GraphChunkRetriever.NameCandidates("(Apple Inc's) 삼성전자의");

        Assert.Contains("Apple Inc", names);
        Assert.Contains("apple inc", names);
        Assert.Contains("삼성전자", names);
        Assert.Contains("전자의", names);
        Assert.DoesNotContain("삼", names); // no single-letter names
    }
}
