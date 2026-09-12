using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Graph;

/// <summary>
/// The graph store's document/chunk scope queries (<see cref="IGraphStore.GetEntitiesByChunkIdsAsync"/>)
/// stand on <see cref="GraphEntity.ChunkIds"/> and <see cref="GraphEntity.DocumentIds"/>. These tests pin
/// that the persistence path fills both from the provenance the build already holds
/// (<see cref="EntityGraphResult.ChunkMappings"/>) — an entity stored without its sources can never be
/// matched by a scoped query, and nothing else fails when that happens.
/// </summary>
public class EntityGraphServicePersistenceTests
{
    private readonly IAdvancedEntityExtractionService _extractor = Substitute.For<IAdvancedEntityExtractionService>();
    private readonly IGraphStore _store = Substitute.For<IGraphStore>();

    private static DocumentChunk Chunk(string id, string documentId, string content) => new()
    {
        Id = id,
        DocumentId = documentId,
        Content = content,
        ChunkIndex = 0
    };

    private static ExtractedEntity Entity(string id, string text, NamedEntityType type) => new()
    {
        Id = id,
        Text = text,
        Type = type,
        Confidence = 0.9
    };

    private EntityGraphService CreateService() => new(_extractor, null, _store, NullLogger<EntityGraphService>.Instance);

    private void ExtractorReturns(params EntityGraph[] graphs)
        => _extractor.ExtractBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(graphs.ToList());

    private List<GraphEntity> CaptureStoredEntities()
    {
        var stored = new List<GraphEntity>();
        _store.StoreEntitiesBatchAsync(Arg.Do<IEnumerable<GraphEntity>>(e => stored.AddRange(e)), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyList<string>>(ci.Arg<IEnumerable<GraphEntity>>().Select(e => e.Id).ToList()));
        return stored;
    }

    [Fact]
    public async Task PersistedEntities_CarryTheChunkAndDocumentTheyWereExtractedFrom()
    {
        var chunks = new List<DocumentChunk>
        {
            Chunk("c1", "doc-a", "Acme Corp partners with Globex."),
            Chunk("c2", "doc-b", "Globex opened a new office.")
        };
        ExtractorReturns(
            new EntityGraph { SourceId = "c1", Entities = [Entity("acme", "Acme Corp", NamedEntityType.Organization), Entity("globex", "Globex", NamedEntityType.Organization)], Relations = [] },
            new EntityGraph { SourceId = "c2", Entities = [Entity("globex", "Globex", NamedEntityType.Organization)], Relations = [] });
        var stored = CaptureStoredEntities();

        await CreateService().BuildEntityGraphAsync(chunks, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(stored);
        Assert.All(stored, e => Assert.NotEmpty(e.ChunkIds));
        Assert.All(stored, e => Assert.NotEmpty(e.DocumentIds));

        var acme = Assert.Single(stored, e => e.Name == "Acme Corp");
        Assert.Equal(["c1"], acme.ChunkIds);
        Assert.Equal(["doc-a"], acme.DocumentIds);

        // An entity linked across chunks keeps every source, not just the first one seen.
        var globex = Assert.Single(stored, e => e.Name == "Globex");
        Assert.Equal(new[] { "c1", "c2" }, globex.ChunkIds.Order());
        Assert.Equal(new[] { "doc-a", "doc-b" }, globex.DocumentIds.Order());
    }

    [Fact]
    public async Task RelationsFollowTheLinkedEntityIds_SoNoEdgeDangles()
    {
        // Linking replaces per-chunk entity ids with one canonical id per entity. Relations were
        // extracted against the per-chunk ids, so they must be re-keyed too — otherwise every edge
        // points at ids no node has, and a store with referential integrity refuses to persist them.
        var chunks = new List<DocumentChunk> { Chunk("c1", "doc-a", "Acme Corp partners with Globex."), Chunk("c2", "doc-a", "Globex again.") };
        ExtractorReturns(
            new EntityGraph
            {
                SourceId = "c1",
                Entities = [Entity("e-acme-1", "Acme Corp", NamedEntityType.Organization), Entity("e-globex-1", "Globex", NamedEntityType.Organization)],
                Relations = [new EntityRelation { SourceEntityId = "e-acme-1", TargetEntityId = "e-globex-1", Type = RelationType.RelatedTo, Label = "partners with", Confidence = 0.8 }]
            },
            new EntityGraph
            {
                SourceId = "c2",
                Entities = [Entity("e-globex-2", "Globex", NamedEntityType.Organization)],
                // Two surface forms of the same entity: after linking this is a self-loop and must be dropped.
                Relations = [new EntityRelation { SourceEntityId = "e-globex-2", TargetEntityId = "e-globex-2", Type = RelationType.RelatedTo, Label = "self", Confidence = 0.5 }]
            });
        var stored = CaptureStoredEntities();
        var storedRelations = new List<GraphRelationship>();
        _store.StoreRelationshipsBatchAsync(Arg.Do<IEnumerable<GraphRelationship>>(r => storedRelations.AddRange(r)), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyList<string>>(ci.Arg<IEnumerable<GraphRelationship>>().Select(r => r.Id).ToList()));

        var result = await CreateService().BuildEntityGraphAsync(chunks, new EntityGraphBuildOptions { LinkEntitiesAcrossChunks = true }, TestContext.Current.CancellationToken);

        var nodeIds = result.Entities.Select(e => e.Id).ToHashSet();
        var edge = Assert.Single(result.Relations);
        Assert.Contains(edge.SourceEntityId, nodeIds);
        Assert.Contains(edge.TargetEntityId, nodeIds);
        Assert.NotEqual(edge.SourceEntityId, edge.TargetEntityId);

        var persisted = Assert.Single(storedRelations);
        Assert.Contains(persisted.SourceEntityId, stored.Select(e => e.Id));
        Assert.Contains(persisted.TargetEntityId, stored.Select(e => e.Id));
    }

    [Fact]
    public async Task ChunkMappings_CarryTheDocumentIdOfTheirChunk()
    {
        var chunks = new List<DocumentChunk> { Chunk("c1", "doc-a", "Acme Corp."), Chunk("c2", "doc-b", "Globex.") };
        ExtractorReturns(
            new EntityGraph { SourceId = "c1", Entities = [Entity("acme", "Acme Corp", NamedEntityType.Organization)], Relations = [] },
            new EntityGraph { SourceId = "c2", Entities = [Entity("globex", "Globex", NamedEntityType.Organization)], Relations = [] });
        CaptureStoredEntities();

        var result = await CreateService().BuildEntityGraphAsync(chunks, cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(result.ChunkMappings, m => Assert.False(string.IsNullOrEmpty(m.DocumentId), $"mapping for chunk {m.ChunkId} has no document id"));
        Assert.Equal("doc-a", Assert.Single(result.ChunkMappings, m => m.ChunkId == "c1").DocumentId);
        Assert.Equal("doc-b", Assert.Single(result.ChunkMappings, m => m.ChunkId == "c2").DocumentId);
    }
}
