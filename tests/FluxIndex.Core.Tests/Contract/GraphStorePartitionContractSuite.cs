using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Contract;

/// <summary>
/// Shared contract suite for <see cref="IGraphStore"/> partitions: one store instance holds several tenants' graphs,
/// and every multi-result read returns exactly one partition's rows — including when two partitions hold the same chunk
/// ids, which is what two tenants indexing the same content produce. A read that names no partition reads the default
/// partition, never every partition. Derive a concrete class per store and implement <see cref="CreateStoreAsync"/>.
///
/// <para>
/// Every fact works in freshly named partitions so container-backed stores can share one instance across the suite;
/// the default partition is only ever checked for absence.
/// </para>
/// </summary>
public abstract class GraphStorePartitionContractSuite
{
    /// <summary>Returns the store under test (fresh, or shared and isolated by unique ids and partitions).</summary>
    protected abstract Task<IGraphStore> CreateStoreAsync();

    private static string Fresh(string label) => $"{label}-{Guid.NewGuid():N}";

    private static GraphEntity Entity(string partition, string name, string chunkId, NamedEntityType type = NamedEntityType.Organization) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Partition = partition,
        Name = name,
        NormalizedName = name.ToLowerInvariant(),
        Type = type,
        SurfaceForms = [name],
        Confidence = 0.9,
        ImportanceScore = 0.5,
        MentionCount = 1,
        ChunkIds = [chunkId],
        DocumentIds = ["doc"]
    };

    private static GraphCommunity Community(string partition, string chunkId, string entityId) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Partition = partition,
        Name = "community",
        Summary = $"summary of {partition}",
        EntityIds = [entityId],
        ChunkIds = [chunkId],
        ImportanceScore = 0.7,
        Level = 0
    };

    [Fact]
    public async Task Partition_RoundTrips_OnEntitiesAndCommunities()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var partition = Fresh("tenant");
        var chunk = Fresh("chunk");
        var entity = Entity(partition, Fresh("Acme"), chunk);
        await store.StoreEntitiesBatchAsync([entity], ct);
        var community = Community(partition, chunk, entity.Id);
        await store.StoreCommunityAsync(community, ct);

        Assert.Equal(partition, (await store.GetEntityByIdAsync(entity.Id, ct))!.Partition);
        Assert.Equal(partition, (await store.GetCommunityByIdAsync(community.Id, ct))!.Partition);
    }

    [Fact]
    public async Task GetEntitiesByChunkIdsAsync_SameChunkIdInTwoPartitions_ReturnsOnlyTheNamedPartition()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var (x, y) = (Fresh("tenant-x"), Fresh("tenant-y"));
        var chunk = Fresh("chunk");
        var inX = Entity(x, "Acme", chunk);
        var inY = Entity(y, "Acme", chunk);
        await store.StoreEntitiesBatchAsync([inX, inY], ct);

        Assert.Equal(inX.Id, Assert.Single(await store.GetEntitiesByChunkIdsAsync([chunk], x, ct)).Id);
        Assert.Equal(inY.Id, Assert.Single(await store.GetEntitiesByChunkIdsAsync([chunk], y, ct)).Id);
        Assert.Empty(await store.GetEntitiesByChunkIdsAsync([chunk], ct: ct));
    }

    [Fact]
    public async Task GetEntitiesByNormalizedNamesAsync_MatchesExactNames_InThePartitionOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var (x, y) = (Fresh("tenant-x"), Fresh("tenant-y"));
        var name = Fresh("Globex");
        var other = Fresh("Initech");
        var inX = Entity(x, name, Fresh("chunk"));
        var otherInX = Entity(x, other, Fresh("chunk"));
        var prefixInX = Entity(x, name + " Holdings", Fresh("chunk"));
        var inY = Entity(y, name, Fresh("chunk"));
        await store.StoreEntitiesBatchAsync([inX, otherInX, prefixInX, inY], ct);

        var found = await store.GetEntitiesByNormalizedNamesAsync([name.ToLowerInvariant(), other.ToLowerInvariant()], x, ct);
        Assert.Equal(new[] { inX.Id, otherInX.Id }.Order(), found.Select(e => e.Id).Order());

        Assert.Equal(inY.Id, Assert.Single(await store.GetEntitiesByNormalizedNamesAsync([name.ToLowerInvariant()], y, ct)).Id);
        Assert.Empty(await store.GetEntitiesByNormalizedNamesAsync([name.ToLowerInvariant()], ct: ct));
        Assert.Empty(await store.GetEntitiesByNormalizedNamesAsync([], x, ct));
    }

    [Fact]
    public async Task GetEntitiesByNameAsync_ExactAndFuzzy_ReadOnePartition()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var (x, y) = (Fresh("tenant-x"), Fresh("tenant-y"));
        var name = Fresh("Umbrella");
        var inX = Entity(x, name, Fresh("chunk"));
        var inY = Entity(y, name, Fresh("chunk"));
        await store.StoreEntitiesBatchAsync([inX, inY], ct);

        Assert.Equal(inX.Id, Assert.Single(await store.GetEntitiesByNameAsync(name, fuzzyMatch: false, partition: x, ct: ct)).Id);
        Assert.Equal(inY.Id, Assert.Single(await store.GetEntitiesByNameAsync(name[..^4], fuzzyMatch: true, partition: y, ct: ct)).Id);
        Assert.Empty(await store.GetEntitiesByNameAsync(name, ct: ct));
    }

    [Fact]
    public async Task GetEntitiesByTypeAsync_ReadsOnePartition()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var (x, y) = (Fresh("tenant-x"), Fresh("tenant-y"));
        var inX = Entity(x, Fresh("Ada"), Fresh("chunk"), NamedEntityType.Person);
        var inY = Entity(y, Fresh("Grace"), Fresh("chunk"), NamedEntityType.Person);
        await store.StoreEntitiesBatchAsync([inX, inY], ct);

        Assert.Equal(inX.Id, Assert.Single(await store.GetEntitiesByTypeAsync(NamedEntityType.Person, 100, x, ct)).Id);
        Assert.Equal(inY.Id, Assert.Single(await store.GetEntitiesByTypeAsync(NamedEntityType.Person, 100, y, ct)).Id);
    }

    [Fact]
    public async Task GetRelationshipsByTypeAsync_ReadsTheRelationshipsOfOnePartitionsEntities()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var (x, y) = (Fresh("tenant-x"), Fresh("tenant-y"));
        var chunk = Fresh("chunk");
        var (ax, bx) = (Entity(x, "Acme", chunk), Entity(x, "Globex", chunk));
        var (ay, by) = (Entity(y, "Acme", chunk), Entity(y, "Globex", chunk));
        await store.StoreEntitiesBatchAsync([ax, bx, ay, by], ct);
        var rx = new GraphRelationship { Id = Fresh("rel"), SourceEntityId = ax.Id, TargetEntityId = bx.Id, Type = RelationType.PartOf, Label = "part of", Confidence = 0.9 };
        var ry = new GraphRelationship { Id = Fresh("rel"), SourceEntityId = ay.Id, TargetEntityId = by.Id, Type = RelationType.PartOf, Label = "part of", Confidence = 0.9 };
        await store.StoreRelationshipsBatchAsync([rx, ry], ct);

        Assert.Equal(rx.Id, Assert.Single(await store.GetRelationshipsByTypeAsync(RelationType.PartOf, 100, x, ct)).Id);
        Assert.Equal(ry.Id, Assert.Single(await store.GetRelationshipsByTypeAsync(RelationType.PartOf, 100, y, ct)).Id);
    }

    [Fact]
    public async Task Communities_SameChunkIdInTwoPartitions_ByChunkAndTop_ReadOnePartition()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var (x, y) = (Fresh("tenant-x"), Fresh("tenant-y"));
        var chunk = Fresh("chunk");
        var (ex, ey) = (Entity(x, "Acme", chunk), Entity(y, "Acme", chunk));
        await store.StoreEntitiesBatchAsync([ex, ey], ct);
        var (cx, cy) = (Community(x, chunk, ex.Id), Community(y, chunk, ey.Id));
        await store.StoreCommunityAsync(cx, ct);
        await store.StoreCommunityAsync(cy, ct);

        var byChunkX = Assert.Single(await store.GetCommunitiesByChunkIdsAsync([chunk], x, ct));
        Assert.Equal(cx.Id, byChunkX.Id);
        Assert.Equal($"summary of {x}", byChunkX.Summary);
        Assert.Equal(cy.Id, Assert.Single(await store.GetCommunitiesByChunkIdsAsync([chunk], y, ct)).Id);
        Assert.Empty(await store.GetCommunitiesByChunkIdsAsync([chunk], ct: ct));

        Assert.Equal(cx.Id, Assert.Single(await store.GetTopCommunitiesAsync(10, x, ct)).Id);
        Assert.Equal(cy.Id, Assert.Single(await store.GetTopCommunitiesAsync(10, y, ct)).Id);
    }

    [Fact]
    public async Task GetStatisticsAsync_CountsOnePartition()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var (x, y) = (Fresh("tenant-x"), Fresh("tenant-y"));
        var chunk = Fresh("chunk");
        var (ax, bx) = (Entity(x, "Acme", chunk), Entity(x, "Globex", chunk));
        var ay = Entity(y, "Acme", chunk);
        await store.StoreEntitiesBatchAsync([ax, bx, ay], ct);
        await store.StoreRelationshipsBatchAsync(
            [new GraphRelationship { Id = Fresh("rel"), SourceEntityId = ax.Id, TargetEntityId = bx.Id, Type = RelationType.RelatedTo, Label = "related", Confidence = 0.9 }], ct);
        await store.StoreCommunityAsync(Community(x, chunk, ax.Id), ct);

        var statsX = await store.GetStatisticsAsync(x, ct);
        Assert.Equal(2, statsX.EntityCount);
        Assert.Equal(1, statsX.RelationshipCount);
        Assert.Equal(1, statsX.CommunityCount);

        var statsY = await store.GetStatisticsAsync(y, ct);
        Assert.Equal(1, statsY.EntityCount);
        Assert.Equal(0, statsY.RelationshipCount);
        Assert.Equal(0, statsY.CommunityCount);
    }

    [Fact]
    public async Task AnEntityStoredWithoutAPartition_ReadsAsTheDefaultPartition()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateStoreAsync();
        var chunk = Fresh("chunk");
        var entity = new GraphEntity { Id = Guid.NewGuid().ToString(), Name = "Hooli", NormalizedName = "hooli", ChunkIds = [chunk] };
        await store.StoreEntitiesBatchAsync([entity], ct);

        var found = Assert.Single(await store.GetEntitiesByChunkIdsAsync([chunk], ct: ct));
        Assert.Equal(GraphPartition.Default, found.Partition);
        Assert.Empty(await store.GetEntitiesByChunkIdsAsync([chunk], Fresh("tenant"), ct));
    }
}
