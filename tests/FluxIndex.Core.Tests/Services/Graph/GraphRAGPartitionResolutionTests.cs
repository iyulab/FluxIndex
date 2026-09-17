using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Application.Utilities;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Graph;

/// <summary>
/// <see cref="GraphRAGBuildOptions.Partition"/> is the one place a GraphRAG build's partition is set: it reaches the
/// entity graph and the community ids, a sub-option naming another partition is refused, and the default partition
/// derives the community ids it always has — an unpartitioned store keeps its rows.
/// </summary>
public class GraphRAGPartitionResolutionTests
{
    [Fact]
    public void ThePartition_ReachesTheEntityGraphOptions_AndTheCommunityOptions_WithoutTouchingTheConsumersObjects()
    {
        var graphOptions = new EntityGraphBuildOptions { BatchSize = 5 };
        var communityOptions = new LeidenOptions { Resolution = 2.0 };
        var options = new GraphRAGBuildOptions { Partition = "desk-1", EntityGraphOptions = graphOptions, CommunityOptions = communityOptions };

        var entity = GraphRAGService.ResolveEntityGraphOptions(options, options.Partition)!;
        var community = GraphRAGService.ResolveCommunityOptions(options, options.Partition)!;

        Assert.Equal("desk-1", entity.Partition);
        Assert.Equal(5, entity.BatchSize);
        Assert.Equal("desk-1", community.GraphPartition);
        Assert.Equal(2.0, community.Resolution);
        Assert.Equal(GraphPartition.Default, graphOptions.Partition);
        Assert.Equal(GraphPartition.Default, communityOptions.GraphPartition);
    }

    [Fact]
    public void ThePartition_ReachesBothPhases_WhenTheConsumerSetNoSubOptions()
    {
        var options = new GraphRAGBuildOptions { Partition = "desk-1" };

        Assert.Equal("desk-1", GraphRAGService.ResolveEntityGraphOptions(options, options.Partition)!.Partition);
        Assert.Equal("desk-1", GraphRAGService.ResolveCommunityOptions(options, options.Partition)!.GraphPartition);
    }

    [Fact]
    public void TheDefaultPartition_LeavesUnsetSubOptionsUnset()
    {
        var options = new GraphRAGBuildOptions();

        Assert.Null(GraphRAGService.ResolveEntityGraphOptions(options, options.Partition));
        Assert.Null(GraphRAGService.ResolveCommunityOptions(options, options.Partition));
    }

    [Fact]
    public void SubOptionsNamingAnotherPartition_AreRefused()
    {
        var entityConflict = new GraphRAGBuildOptions { Partition = "desk-1", EntityGraphOptions = new EntityGraphBuildOptions { Partition = "desk-2" } };
        var communityConflict = new GraphRAGBuildOptions { Partition = "desk-1", CommunityOptions = new LeidenOptions { GraphPartition = "desk-2" } };

        Assert.Throws<ArgumentException>(() => GraphRAGService.ResolveEntityGraphOptions(entityConflict, entityConflict.Partition));
        Assert.Throws<ArgumentException>(() => GraphRAGService.ResolveCommunityOptions(communityConflict, communityConflict.Partition));
    }

    [Fact]
    public void CommunityIds_DifferPerPartition_AndTheDefaultPartitionKeepsTheIdsItHad()
    {
        string[] chunks = ["c2", "c1"];

        var legacy = ChunkStorageId.CreateNameBasedUuid(new Guid("8c1d4e7a-2f36-4b95-9a0c-5d7e3b1f6a24"), "level|0|c1\nc2").ToString();
        Assert.Equal(legacy, CommunityIdentity.For(0, chunks));
        Assert.Equal(legacy, CommunityIdentity.For(0, chunks, GraphPartition.Default));

        var desk1 = CommunityIdentity.For(0, chunks, "desk-1");
        Assert.NotEqual(legacy, desk1);
        Assert.NotEqual(desk1, CommunityIdentity.For(0, chunks, "desk-2"));
        Assert.Equal(desk1, CommunityIdentity.For(0, ["c1", "c2"], "desk-1"));
    }
}
