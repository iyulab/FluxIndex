using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Graph;

/// <summary>
/// A stored entity's <see cref="GraphEntity.NormalizedName"/> is case-folded whichever extractor produced it, and a graph
/// written before names were folded joins and heals instead of growing a second node. The rule-based extractor and custom
/// patterns hand back <see cref="ExtractedEntity.NormalizedText"/> in the text's own casing; every reader compares lower-case.
/// </summary>
public class EntityNameCaseFoldTests
{
    private readonly IAdvancedEntityExtractionService _extractor = Substitute.For<IAdvancedEntityExtractionService>();
    private readonly IGraphStore _store = Substitute.For<IGraphStore>();
    private readonly List<GraphEntity> _stored = [];
    private readonly List<IReadOnlyList<string>> _nameLookups = [];

    public EntityNameCaseFoldTests()
    {
        _store.StoreEntitiesBatchAsync(Arg.Any<IEnumerable<GraphEntity>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var batch = ci.Arg<IEnumerable<GraphEntity>>().ToList();
                foreach (var entity in batch)
                {
                    _stored.RemoveAll(e => e.Id == entity.Id);
                    _stored.Add(entity);
                }
                return Task.FromResult<IReadOnlyList<string>>(batch.Select(e => e.Id).ToList());
            });
        _store.StoreRelationshipsBatchAsync(Arg.Any<IEnumerable<GraphRelationship>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyList<string>>(ci.Arg<IEnumerable<GraphRelationship>>().Select(r => r.Id).ToList()));
        _store.GetEntitiesByChunkIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<GraphEntity>>([]));
        _store.GetEntitiesByNormalizedNamesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var names = ci.Arg<IEnumerable<string>>().ToList();
                _nameLookups.Add(names);
                var set = names.ToHashSet(StringComparer.Ordinal);
                return Task.FromResult<IReadOnlyList<GraphEntity>>(_stored.Where(e => set.Contains(e.NormalizedName)).ToList());
            });
        _store.GetRelationshipsAsync(Arg.Any<string>(), Arg.Any<TraversalDirection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<GraphRelationship>>([]));

        // Like the rule-based extractor: NormalizedText keeps the text's casing.
        _extractor.ExtractBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyList<EntityGraph>>(ci.Arg<IEnumerable<string>>()
                .Select(content => new EntityGraph
                {
                    SourceId = content,
                    Entities = [new ExtractedEntity { Id = Guid.NewGuid().ToString(), Text = "Apple Inc", NormalizedText = "Apple Inc", Type = NamedEntityType.Organization, Confidence = 0.6 }],
                    Relations = [],
                })
                .ToList()));
    }

    private EntityGraphService Service() => new(_extractor, null, _store, NullLogger<EntityGraphService>.Instance);

    private static DocumentChunk Chunk(string id) => new() { Id = id, DocumentId = "doc", Content = $"Apple Inc in {id}.", ChunkIndex = 0 };

    [Fact]
    public async Task An_entity_extracted_with_its_own_casing_is_stored_folded()
    {
        var graph = await Service().BuildEntityGraphAsync([Chunk("c1")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("apple inc", Assert.Single(graph.Entities).NormalizedName);
        Assert.Equal("apple inc", Assert.Single(_stored).NormalizedName);
        Assert.Equal("Apple Inc", _stored[0].Name);
    }

    [Fact]
    public async Task A_node_stored_before_folding_is_joined_and_healed_not_duplicated()
    {
        _stored.Add(new GraphEntity
        {
            Id = "legacy",
            Name = "Apple Inc",
            NormalizedName = "Apple Inc", // as written before 0.97.0
            Type = NamedEntityType.Organization,
            SurfaceForms = ["Apple Inc"],
            ChunkIds = ["c0"],
            DocumentIds = ["doc"],
            MentionCount = 1,
            Confidence = 0.6,
        });

        var graph = await Service().BuildEntityGraphAsync([Chunk("c1")], cancellationToken: TestContext.Current.CancellationToken);

        var node = Assert.Single(graph.Entities);
        Assert.Equal("legacy", node.Id);
        var stored = Assert.Single(_stored);
        Assert.Equal("legacy", stored.Id);
        Assert.Equal("apple inc", stored.NormalizedName);
        Assert.Contains("c1", stored.ChunkIds);
        Assert.Contains(_nameLookups, names => names.Contains("Apple Inc") && names.Contains("apple inc"));
    }

    [Fact]
    public void A_stored_node_read_back_keys_folded()
    {
        var node = StoredGraphConversions.ToEntityNode(new GraphEntity { Id = "x", Name = "Apple Inc", NormalizedName = " Apple Inc " });

        Assert.Equal("apple inc", node.NormalizedName);
    }
}
