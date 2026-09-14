using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Contract;

/// <summary>
/// Shared contract suite for <see cref="IGraphStore"/> implementations on the two things the entity
/// graph build stands on when it reuses stored extractions: an entity or relationship id is the row
/// key (storing it again is an update that replaces the stored provenance, not a second row), and
/// <see cref="IGraphStore.GetEntitiesByChunkIdsAsync"/> matches an entity by any of its stored chunk
/// ids, exactly once. Derive a concrete class per store and implement <see cref="CreateStoreAsync"/>.
///
/// <para>
/// Every fact keys its rows under a fresh prefix so that container-backed stores can share one
/// instance across the suite.
/// </para>
/// </summary>
public abstract class GraphStoreChunkProvenanceContractSuite
{
    /// <summary>Returns the store under test (fresh, or shared and isolated by unique ids).</summary>
    protected abstract Task<IGraphStore> CreateStoreAsync();

    private static string Fresh(string label) => $"{label}-{Guid.NewGuid():N}";

    private static GraphEntity Entity(string id, string name, IReadOnlyList<string> chunkIds, IReadOnlyList<string> documentIds, double confidence = 0.9) => new()
    {
        Id = id,
        Name = name,
        NormalizedName = name.ToLowerInvariant(),
        Type = NamedEntityType.Organization,
        SurfaceForms = [name],
        Confidence = confidence,
        MentionCount = 1,
        ChunkIds = chunkIds,
        DocumentIds = documentIds
    };

    private static GraphRelationship Relationship(string id, string sourceId, string targetId, double confidence) => new()
    {
        Id = id,
        SourceEntityId = sourceId,
        TargetEntityId = targetId,
        Type = RelationType.RelatedTo,
        Label = "related",
        Confidence = confidence,
        EvidenceChunkIds = []
    };

    [Fact]
    public async Task StoreEntitiesBatchAsync_SameIdTwice_UpdatesTheEntity_AndReplacesItsProvenance()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var id = Fresh("entity");
        var (c1, c2, c3) = (Fresh("chunk"), Fresh("chunk"), Fresh("chunk"));

        await store.StoreEntitiesBatchAsync([Entity(id, "Globex", [c1], ["doc-a"], 0.5)], ct);
        await store.StoreEntitiesBatchAsync([Entity(id, "Globex Corp", [c2, c3], ["doc-b"], 0.9)], ct);

        var stored = await store.GetEntityByIdAsync(id, ct);
        Assert.NotNull(stored);
        Assert.Equal("Globex Corp", stored.Name);
        Assert.Equal(0.9, stored.Confidence, precision: 6);
        Assert.Equal(new[] { c2, c3 }.Order(), stored.ChunkIds.Order());
        Assert.Equal(["doc-b"], stored.DocumentIds);

        Assert.Empty(await store.GetEntitiesByChunkIdsAsync([c1], ct));
        var byNewChunk = await store.GetEntitiesByChunkIdsAsync([c3], ct);
        Assert.Equal(id, Assert.Single(byNewChunk).Id);
    }

    // Community ids are derived from level and member chunks, so a rebuilt document re-stores the same ids. That must
    // update the row, not add a second community for the same members.
    [Fact]
    public async Task StoreCommunityAsync_SameIdTwice_UpdatesTheCommunity_WithoutASecondRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var entityId = Fresh("entity");
        var (c1, c2) = (Fresh("chunk"), Fresh("chunk"));
        await store.StoreEntitiesBatchAsync([Entity(entityId, "Acme Corp", [c1, c2], ["doc-a"])], ct);
        var communityId = Guid.NewGuid().ToString();

        GraphCommunity Community(string summary) => new()
        {
            Id = communityId,
            Name = "Acme",
            Summary = summary,
            EntityIds = [entityId],
            ChunkIds = [c1, c2],
            Topics = ["partners"],
            ImportanceScore = 0.8,
            Level = 0
        };

        await store.StoreCommunityAsync(Community("first build"), ct);
        await store.StoreCommunityAsync(Community("rebuilt"), ct);

        var byChunks = await store.GetCommunitiesByChunkIdsAsync([c1, c2], ct);
        var only = Assert.Single(byChunks, c => c.Id == communityId);
        Assert.Equal("rebuilt", only.Summary);
        Assert.Single(await store.GetCommunitiesForEntityAsync(entityId, ct), c => c.Id == communityId);
    }

    [Fact]
    public async Task GetEntitiesByChunkIdsAsync_MatchesAnyStoredChunkId_OncePerEntity()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var (e1, e2) = (Fresh("entity"), Fresh("entity"));
        var (c1, c2, c9) = (Fresh("chunk"), Fresh("chunk"), Fresh("chunk"));

        await store.StoreEntitiesBatchAsync(
        [
            Entity(e1, "Acme", [c1, c2], ["doc-a"]),
            Entity(e2, "Initech", [c2], ["doc-a"])
        ], ct);

        var both = await store.GetEntitiesByChunkIdsAsync([c2], ct);
        Assert.Equal(new[] { e1, e2 }.Order(), both.Select(e => e.Id).Order());

        var first = await store.GetEntitiesByChunkIdsAsync([c1], ct);
        Assert.Equal(e1, Assert.Single(first).Id);

        Assert.Empty(await store.GetEntitiesByChunkIdsAsync([c9], ct));

        // An entity matched through two of the requested ids comes back once.
        var overlapping = await store.GetEntitiesByChunkIdsAsync([c1, c2], ct);
        Assert.Equal(2, overlapping.Count);
        Assert.Equal(overlapping.Count, overlapping.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public async Task StoreRelationshipsBatchAsync_SameIdTwice_UpdatesTheRelationship_AndBothEndsSeeItOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var (e1, e2, r1) = (Fresh("entity"), Fresh("entity"), Fresh("rel"));
        var c1 = Fresh("chunk");
        await store.StoreEntitiesBatchAsync(
        [
            Entity(e1, "Acme", [c1], ["doc-a"]),
            Entity(e2, "Globex", [c1], ["doc-a"])
        ], ct);

        await store.StoreRelationshipsBatchAsync([Relationship(r1, e1, e2, 0.5)], ct);
        await store.StoreRelationshipsBatchAsync([Relationship(r1, e1, e2, 0.9)], ct);

        var fromSource = await store.GetRelationshipsAsync(e1, TraversalDirection.Both, ct);
        var relationship = Assert.Single(fromSource, r => r.Id == r1);
        Assert.Equal(0.9, relationship.Confidence, precision: 6);
        Assert.Equal(e1, relationship.SourceEntityId);
        Assert.Equal(e2, relationship.TargetEntityId);

        var fromTarget = await store.GetRelationshipsAsync(e2, TraversalDirection.Both, ct);
        Assert.Single(fromTarget, r => r.Id == r1);
    }

    [Fact]
    public async Task DeleteEntityAsync_RemovesItFromChunkScope()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var id = Fresh("entity");
        var c1 = Fresh("chunk");
        await store.StoreEntitiesBatchAsync([Entity(id, "Acme", [c1], ["doc-a"])], ct);
        Assert.Single(await store.GetEntitiesByChunkIdsAsync([c1], ct));

        Assert.True(await store.DeleteEntityAsync(id, ct));

        Assert.Null(await store.GetEntityByIdAsync(id, ct));
        Assert.Empty(await store.GetEntitiesByChunkIdsAsync([c1], ct));
    }
}
