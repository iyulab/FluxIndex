using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Graph;

/// <summary>
/// <see cref="IGraphRAGService.LoadIndexAsync"/> rebuilds a queryable index from what the graph store
/// holds, scoped to the chunks the caller passes. These tests pin the scope rules against a substituted
/// store; the real round trip lives in the SQLite storage tests.
/// </summary>
public class GraphRAGServiceLoadIndexTests
{
    private readonly IGraphStore _store = Substitute.For<IGraphStore>();

    private GraphRAGService CreateService(bool withStore = true) => new(
        Substitute.For<IEntityGraphService>(),
        Substitute.For<ILeidenCommunityService>(),
        Substitute.For<IHierarchicalSummarizationService>(),
        graphStore: withStore ? _store : null,
        logger: NullLogger<GraphRAGService>.Instance);

    private static DocumentChunk Chunk(string id, string documentId) => new() { Id = id, DocumentId = documentId, Content = $"content of {id}", ChunkIndex = 0 };

    private static GraphEntity Entity(string id, string name, params string[] chunkIds) => new()
    {
        Id = id,
        Name = name,
        NormalizedName = name.ToLowerInvariant(),
        Type = NamedEntityType.Organization,
        Confidence = 0.8,
        ChunkIds = chunkIds,
        DocumentIds = ["doc-a"]
    };

    private static GraphRelationship Relation(string id, string source, string target) => new()
    {
        Id = id,
        SourceEntityId = source,
        TargetEntityId = target,
        Type = RelationType.RelatedTo,
        Label = "partner",
        Confidence = 0.7,
        Weight = 1.0
    };

    [Fact]
    public async Task WithoutAGraphStore_Throws()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(withStore: false).LoadIndexAsync([Chunk("c1", "doc-a")], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("IGraphStore", ex.Message);
    }

    [Fact]
    public async Task LoadsEntitiesOfTheScopeChunks_AndKeepsOnlyInScopeProvenance()
    {
        var chunks = new[] { Chunk("c1", "doc-a"), Chunk("c2", "doc-a") };
        _store.GetEntitiesByChunkIdsAsync(Arg.Is<IEnumerable<string>>(ids => ids.Order().SequenceEqual(new[] { "c1", "c2" })), Arg.Any<CancellationToken>())
            .Returns([Entity("acme", "Acme Corp", "c1", "c9"), Entity("globex", "Globex", "c2")]);
        _store.GetRelationshipsAsync(Arg.Any<string>(), Arg.Any<TraversalDirection>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var index = await CreateService().LoadIndexAsync(chunks, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["c1", "c2"], index.Chunks.Keys.Order());
        Assert.Equal(["acme", "globex"], index.EntityGraph.Entities.Select(e => e.Id).Order());
        Assert.Equal(["c1", "c2"], index.EntityGraph.SourceChunkIds.Order());

        // c9 is outside the scope: the entity is loaded, its out-of-scope provenance is not.
        var acmeMappings = index.EntityGraph.ChunkMappings.Where(m => m.EntityId == "acme").ToList();
        var only = Assert.Single(acmeMappings);
        Assert.Equal("c1", only.ChunkId);
        Assert.Equal("doc-a", only.DocumentId);
        Assert.True(only.RelevanceScore > 0, "reloaded mappings must keep a non-zero relevance so chunk ranking works");

        Assert.Equal(2, index.Stats.TotalEntities);
        Assert.Empty(index.CommunityHierarchy.Levels);
    }

    [Fact]
    public async Task LoadsOnlyRelationshipsBetweenLoadedEntities_Deduplicated()
    {
        var chunks = new[] { Chunk("c1", "doc-a") };
        _store.GetEntitiesByChunkIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([Entity("acme", "Acme Corp", "c1"), Entity("globex", "Globex", "c1")]);
        var inScope = Relation("r1", "acme", "globex");
        var dangling = Relation("r2", "acme", "initech"); // initech was not loaded
        _store.GetRelationshipsAsync("acme", Arg.Any<TraversalDirection>(), Arg.Any<CancellationToken>()).Returns([inScope, dangling]);
        _store.GetRelationshipsAsync("globex", Arg.Any<TraversalDirection>(), Arg.Any<CancellationToken>()).Returns([inScope]);

        var index = await CreateService().LoadIndexAsync(chunks, cancellationToken: TestContext.Current.CancellationToken);

        var edge = Assert.Single(index.EntityGraph.Relations);
        Assert.Equal("r1", edge.Id);
        Assert.Equal(RelationType.RelatedTo, edge.RelationType);
    }

    [Fact]
    public async Task LoadRelationshipsFalse_SkipsTheRelationshipReads()
    {
        _store.GetEntitiesByChunkIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([Entity("acme", "Acme Corp", "c1")]);

        var index = await CreateService().LoadIndexAsync([Chunk("c1", "doc-a")], new GraphRAGLoadOptions { LoadRelationships = false }, TestContext.Current.CancellationToken);

        Assert.Empty(index.EntityGraph.Relations);
        await _store.DidNotReceive().GetRelationshipsAsync(Arg.Any<string>(), Arg.Any<TraversalDirection>(), Arg.Any<CancellationToken>());
    }
}
