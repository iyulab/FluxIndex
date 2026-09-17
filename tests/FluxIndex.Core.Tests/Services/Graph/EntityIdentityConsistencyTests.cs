using System.Text.Json;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Graph;

/// <summary>
/// One definition of "the same entity", shared by every stage that needs it.
/// </summary>
/// <remarks>
/// The stages used to disagree. In-build linking grouped by the normalized text alone, so one name
/// carried by two types collapsed into a single node; the merge against stored extractions keyed on
/// (normalized name, type), so the same pair stayed apart. Whether the extractor's own
/// <see cref="ExtractedEntity.NormalizedText"/> was honoured depended on which node-building path ran,
/// which is to say on an option. Nothing failed when they disagreed - the graph simply came out
/// differently on a first index than on a re-index, and no assertion in this suite looked at that.
/// These facts look at it.
/// </remarks>
public class EntityIdentityConsistencyTests
{
    private readonly IAdvancedEntityExtractionService _extractor = Substitute.For<IAdvancedEntityExtractionService>();
    private readonly IGraphStore _store = Substitute.For<IGraphStore>();
    private readonly List<GraphEntity> _storedEntities = [];
    private readonly Dictionary<string, List<ExtractedEntity>> _extractionByContent = [];

    public EntityIdentityConsistencyTests()
    {
        _store.StoreEntitiesBatchAsync(Arg.Any<IEnumerable<GraphEntity>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var batch = ci.Arg<IEnumerable<GraphEntity>>().ToList();
                foreach (var entity in batch)
                {
                    _storedEntities.RemoveAll(e => e.Id == entity.Id);
                    _storedEntities.Add(entity);
                }
                return Task.FromResult<IReadOnlyList<string>>(batch.Select(e => e.Id).ToList());
            });
        _store.StoreRelationshipsBatchAsync(Arg.Any<IEnumerable<GraphRelationship>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyList<string>>(ci.Arg<IEnumerable<GraphRelationship>>().Select(r => r.Id).ToList()));
        _store.GetEntitiesByChunkIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var ids = ci.Arg<IEnumerable<string>>().ToHashSet();
                return Task.FromResult<IReadOnlyList<GraphEntity>>(
                    _storedEntities.Where(e => e.ChunkIds.Any(ids.Contains)).Select(RoundTripped).ToList());
            });
        _store.GetRelationshipsAsync(Arg.Any<string>(), Arg.Any<TraversalDirection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<GraphRelationship>>([]));

        _extractor.ExtractBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyList<EntityGraph>>(
                ci.Arg<IEnumerable<string>>()
                  .Select(c => new EntityGraph
                  {
                      SourceId = c,
                      Entities = _extractionByContent.TryGetValue(c, out var e)
                          ? e.Select(Clone).ToList()
                          : [],
                      Relations = []
                  })
                  .ToList()));
    }

    // A fresh id per call: the pipeline maps old ids to new ones, and reusing an id across builds
    // would let a test pass for the wrong reason.
    private static ExtractedEntity Clone(ExtractedEntity e) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Text = e.Text,
        NormalizedText = e.NormalizedText,
        Type = e.Type,
        Subtype = e.Subtype,
        Confidence = e.Confidence,
        OccurrenceCount = e.OccurrenceCount
    };

    // What every real store does to Properties: serialize to JSON on write, deserialize to
    // Dictionary<string, object> on read - so a string comes back as a JsonElement, not a string.
    // Handing the build the same object it stored would leave that path unexercised, and it is the
    // path on which a stored node's subtype is read.
    private static GraphEntity RoundTripped(GraphEntity stored) => stored with
    {
        Properties = JsonSerializer.Deserialize<Dictionary<string, object>>(
            JsonSerializer.Serialize(stored.Properties)) ?? []
    };

    private static DocumentChunk Chunk(string id, string content) => new()
    {
        Id = id,
        DocumentId = "doc-a",
        Content = content,
        ChunkIndex = 0
    };

    private EntityGraphService CreateService(IGraphStore? store) =>
        new(_extractor, null, store, NullLogger<EntityGraphService>.Instance);

    private static ExtractedEntity Entity(string text, NamedEntityType type, double confidence, string? normalized = null, string? subtype = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Text = text,
        NormalizedText = normalized!,   // null on purpose: the service must treat "not supplied" as absent
        Type = type,
        Subtype = subtype,
        Confidence = confidence,
        OccurrenceCount = 1
    };

    [Fact]
    public async Task OneNameCarriedByTwoTypes_StaysTwoEntities()
    {
        // "Apple" the organisation and "Apple" the product are not the same thing. Grouping by the
        // normalized text alone merged them and kept whichever type scored higher.
        _extractionByContent["Apple ships Apple."] =
        [
            Entity("Apple", NamedEntityType.Organization, 0.9),
            Entity("Apple", NamedEntityType.Product, 0.6)
        ];
        var service = CreateService(store: null);

        var graph = await service.BuildEntityGraphAsync(
            [Chunk("c1", "Apple ships Apple.")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, graph.Entities.Count);
        Assert.Contains(graph.Entities, e => e.Type == NamedEntityType.Organization);
        Assert.Contains(graph.Entities, e => e.Type == NamedEntityType.Product);
    }

    [Fact]
    public async Task ReIndexingTheSameCorpus_ProducesTheSameNodes_WhenConfidenceOrderFlips()
    {
        // The failure this class of defect actually produced: the in-build stage collapsed the pair
        // and named the survivor after the higher-confidence member, while the merge against stored
        // extractions looked the pair up by (name, type). Flip the confidences between two builds and
        // the second build's key stopped matching the first build's node.
        _extractionByContent["Apple ships Apple."] =
        [
            Entity("Apple", NamedEntityType.Organization, 0.9),
            Entity("Apple", NamedEntityType.Product, 0.6)
        ];
        var service = CreateService(_store);

        var first = await service.BuildEntityGraphAsync(
            [Chunk("c1", "Apple ships Apple.")],
            cancellationToken: TestContext.Current.CancellationToken);

        // Re-extract with the ordering reversed. The second build has to carry a chunk the store
        // already knows (c1): the merge against stored extractions is reached through the chunk-scoped
        // lookup, so a build over only-new chunks never gets there at all - it writes fresh nodes and
        // the assertion below would fail for a reason that has nothing to do with identity.
        _extractionByContent["Apple ships Apple too."] =
        [
            Entity("Apple", NamedEntityType.Organization, 0.5),
            Entity("Apple", NamedEntityType.Product, 0.95)
        ];
        var second = await service.BuildEntityGraphAsync(
            [Chunk("c1", "Apple ships Apple."), Chunk("c2", "Apple ships Apple too.")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, second.Entities.Count);
        // Same identities on both sides, and the store did not accumulate a third node.
        Assert.Equal(
            first.Entities.Select(e => (e.NormalizedName, e.Type)).Order(),
            second.Entities.Select(e => (e.NormalizedName, e.Type)).Order());
        Assert.Equal(2, _storedEntities.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheExtractorsNormalizedText_IsHonoured_WhicheverNodePathRuns(bool linkAcrossChunks)
    {
        // Two node-building paths exist and an option picks between them. They used to disagree about
        // NormalizedText - the linking path recomputed the name and dropped it - so toggling the
        // option changed the identity that got stored.
        _extractionByContent["IBM"] = [Entity("IBM", NamedEntityType.Organization, 0.9, normalized: "international business machines")];
        var service = CreateService(store: null);

        var graph = await service.BuildEntityGraphAsync(
            [Chunk("c1", "IBM")],
            new EntityGraphBuildOptions { LinkEntitiesAcrossChunks = linkAcrossChunks },
            cancellationToken: TestContext.Current.CancellationToken);

        var entity = Assert.Single(graph.Entities);
        Assert.Equal("international business machines", entity.NormalizedName);
    }

    [Fact]
    public async Task OneNameCarriedByTwoSubtypes_StaysTwoEntities_EachKeepingItsLabel()
    {
        // A consumer that extracts with its own vocabulary - two Custom subtypes sharing a name -
        // needs both to reach the store. Keyed on name and type alone, the second merged into the
        // first and its label was gone.
        _extractionByContent["Zeus the desk and Zeus the project."] =
        [
            Entity("Zeus", NamedEntityType.Custom, 0.9, subtype: "desk"),
            Entity("Zeus", NamedEntityType.Custom, 0.8, subtype: "project")
        ];
        var service = CreateService(store: null);

        var graph = await service.BuildEntityGraphAsync(
            [Chunk("c1", "Zeus the desk and Zeus the project.")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, graph.Entities.Count);
        Assert.Equal(
            new[] { "desk", "project" },
            graph.Entities.Select(e => e.Properties["subtype"]).OfType<string>().Order());
    }

    [Fact]
    public async Task ReIndexingSubtypedEntities_MatchesTheStoredNodes_ThroughTheStoresJsonRoundTrip()
    {
        // Every store hands Properties back as JSON elements. If the stored node's subtype were read
        // as one of those, no stored node would have a subtype, none would match the build, and a
        // re-index would write a second node beside each. The double round-trips Properties through
        // JSON for exactly that reason - assert the premise before relying on it.
        _extractionByContent["Zeus the desk and Zeus the project."] =
        [
            Entity("Zeus", NamedEntityType.Custom, 0.9, subtype: "desk"),
            Entity("Zeus", NamedEntityType.Custom, 0.8, subtype: "project")
        ];
        var service = CreateService(_store);

        var first = await service.BuildEntityGraphAsync(
            [Chunk("c1", "Zeus the desk and Zeus the project.")],
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, first.Entities.Count);

        var reread = await _store.GetEntitiesByChunkIdsAsync(["c1"], ct: TestContext.Current.CancellationToken);
        Assert.All(reread, e => Assert.IsType<JsonElement>(e.Properties["subtype"]));   // fixture premise

        _extractionByContent["Zeus again."] =
        [
            Entity("Zeus", NamedEntityType.Custom, 0.7, subtype: "desk"),
            Entity("Zeus", NamedEntityType.Custom, 0.95, subtype: "project")
        ];
        var second = await service.BuildEntityGraphAsync(
            [Chunk("c1", "Zeus the desk and Zeus the project."), Chunk("c2", "Zeus again.")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, second.Entities.Count);
        Assert.Equal(2, _storedEntities.Count);
        // The labels survived the round trip as strings, on the nodes the build handed back.
        Assert.Equal(new[] { "desk", "project" }, second.Entities.Select(e => e.Properties["subtype"]).OfType<string>().Order());
    }

    [Fact]
    public async Task AQueryEntityWithoutASubtype_MatchesEverySubtypeOfThatName()
    {
        // The query extractor sees a few words, not the vocabulary the index was built with. A query
        // that declares no subtype has to reach every subtype of that name - the index holds two
        // Zeus nodes and taking the first would silently drop the other from the search.
        _extractionByContent["Zeus the desk and Zeus the project."] =
        [
            Entity("Zeus", NamedEntityType.Custom, 0.9, subtype: "desk"),
            Entity("Zeus", NamedEntityType.Custom, 0.8, subtype: "project")
        ];
        _extractor.ExtractEntitiesAsync("tell me about Zeus", Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExtractedEntity>>([Entity("Zeus", NamedEntityType.Custom, 0.9)]));
        var service = CreateService(store: null);
        var graph = await service.BuildEntityGraphAsync(
            [Chunk("c1", "Zeus the desk and Zeus the project.")],
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, graph.Entities.Count);   // fixture premise: there are two to find

        var result = await service.SearchByEntitiesAsync("tell me about Zeus", graph, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "desk", "project" },
            result.QueryEntities.Select(e => e.Properties["subtype"]).OfType<string>().Order());
    }

    [Fact]
    public async Task AnUndeclaredSubtype_KeysAsNone_AndMergesWithTheStoredNode()
    {
        // Whitespace is not a declaration. An extractor that leaves the field blank on a re-index
        // must still find the node it wrote with null.
        _extractionByContent["Acme"] = [Entity("Acme", NamedEntityType.Organization, 0.9, subtype: null)];
        var service = CreateService(_store);
        await service.BuildEntityGraphAsync([Chunk("c1", "Acme")], cancellationToken: TestContext.Current.CancellationToken);

        _extractionByContent["Acme again"] = [Entity("Acme", NamedEntityType.Organization, 0.9, subtype: "  ")];
        var second = await service.BuildEntityGraphAsync(
            [Chunk("c1", "Acme"), Chunk("c2", "Acme again")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(second.Entities);
        Assert.Single(_storedEntities);
    }
}
