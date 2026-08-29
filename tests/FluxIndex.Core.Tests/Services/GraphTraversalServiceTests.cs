using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

public class GraphTraversalServiceTests
{
    private readonly IChunkHierarchyRepository _hierarchyRepositoryMock;
    private readonly ILogger<GraphTraversalService> _loggerMock;
    private readonly GraphTraversalService _service;

    public GraphTraversalServiceTests()
    {
        _hierarchyRepositoryMock = Substitute.For<IChunkHierarchyRepository>();
        _loggerMock = Substitute.For<ILogger<GraphTraversalService>>();
        _service = new GraphTraversalService(_hierarchyRepositoryMock, _loggerMock);
    }

    #region BFS Tests

    [Fact]
    public async Task TraverseBfsAsync_EmptyGraph_ReturnsStartNodeOnly()
    {
        // Arrange
        SetupEmptyRelationships();

        // Act
        var result = await _service.TraverseBfsAsync("chunk1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.VisitedChunkIds);
        Assert.Equal("chunk1", result.VisitedChunkIds[0]);
    }

    [Fact]
    public async Task TraverseBfsAsync_LinearPath_TraversesInOrder()
    {
        // Arrange: chunk1 -> chunk2 -> chunk3
        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["chunk1"] = new() { CreateRelationship("chunk1", "chunk2") },
            ["chunk2"] = new() { CreateRelationship("chunk2", "chunk3") },
            ["chunk3"] = new()
        };

        SetupRelationships(relationships);

        // Act
        var result = await _service.TraverseBfsAsync("chunk1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.VisitedChunkIds.Count);
        Assert.Contains("chunk1", result.VisitedChunkIds);
        Assert.Contains("chunk2", result.VisitedChunkIds);
        Assert.Contains("chunk3", result.VisitedChunkIds);
        // BFS visits level by level
        Assert.True(result.ChunksByLevel.ContainsKey(0));
        Assert.True(result.ChunksByLevel.ContainsKey(1));
        Assert.True(result.ChunksByLevel.ContainsKey(2));
    }

    [Fact]
    public async Task TraverseBfsAsync_RespectsMaxDepth()
    {
        // Arrange: chunk1 -> chunk2 -> chunk3 -> chunk4
        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["chunk1"] = new() { CreateRelationship("chunk1", "chunk2") },
            ["chunk2"] = new() { CreateRelationship("chunk2", "chunk3") },
            ["chunk3"] = new() { CreateRelationship("chunk3", "chunk4") },
            ["chunk4"] = new()
        };

        SetupRelationships(relationships);

        var options = new GraphTraversalOptions { MaxDepth = 2 };

        // Act
        var result = await _service.TraverseBfsAsync("chunk1", options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.VisitedChunkIds.Count); // chunk1 (depth 0) + chunk2 (depth 1) + chunk3 (depth 2)
        Assert.DoesNotContain("chunk4", result.VisitedChunkIds);
    }

    [Fact]
    public async Task TraverseBfsAsync_FiltersRelationshipType()
    {
        // Arrange: chunk1 has both Sequential and Semantic relationships
        var sequentialRel = CreateRelationship("chunk1", "chunk2", RelationshipType.Sequential);

        // Setup to return only sequential when filtered
        _hierarchyRepositoryMock.GetRelationshipsAsync(
            "chunk1",
            Arg.Is<IEnumerable<RelationshipType>?>(types => types != null && types.Contains(RelationshipType.Sequential)),
            Arg.Any<CancellationToken>()).Returns(new List<ChunkRelationshipExtended> { sequentialRel });

        _hierarchyRepositoryMock.GetRelationshipsAsync(
            "chunk2",
            Arg.Any<IEnumerable<RelationshipType>?>(),
            Arg.Any<CancellationToken>()).Returns(new List<ChunkRelationshipExtended>());

        var options = new GraphTraversalOptions
        {
            RelationshipTypes = new[] { RelationshipType.Sequential }
        };

        // Act
        var result = await _service.TraverseBfsAsync("chunk1", options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.VisitedChunkIds.Count);
        Assert.Contains("chunk2", result.VisitedChunkIds);
        Assert.DoesNotContain("chunk3", result.VisitedChunkIds);
    }

    [Fact]
    public async Task TraverseBfsAsync_RespectsMaxNodes()
    {
        // Arrange: Star topology with many nodes
        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["center"] = new()
            {
                CreateRelationship("center", "node1"),
                CreateRelationship("center", "node2"),
                CreateRelationship("center", "node3"),
                CreateRelationship("center", "node4"),
                CreateRelationship("center", "node5")
            },
            ["node1"] = new(),
            ["node2"] = new(),
            ["node3"] = new(),
            ["node4"] = new(),
            ["node5"] = new()
        };

        SetupRelationships(relationships);

        var options = new GraphTraversalOptions { MaxNodes = 3 };

        // Act
        var result = await _service.TraverseBfsAsync("center", options, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.VisitedChunkIds.Count <= 3);
    }

    #endregion

    #region DFS Tests

    [Fact]
    public async Task TraverseDfsAsync_LinearPath_TraversesDeepFirst()
    {
        // Arrange: chunk1 -> chunk2 -> chunk3
        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["chunk1"] = new() { CreateRelationship("chunk1", "chunk2") },
            ["chunk2"] = new() { CreateRelationship("chunk2", "chunk3") },
            ["chunk3"] = new()
        };

        SetupRelationships(relationships);

        // Act
        var result = await _service.TraverseDfsAsync("chunk1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.VisitedChunkIds.Count);
        Assert.Contains("chunk3", result.VisitedChunkIds);
    }

    [Fact]
    public async Task TraverseDfsAsync_RespectsMaxDepth()
    {
        // Arrange
        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["a"] = new() { CreateRelationship("a", "b") },
            ["b"] = new() { CreateRelationship("b", "c") },
            ["c"] = new() { CreateRelationship("c", "d") },
            ["d"] = new()
        };

        SetupRelationships(relationships);

        var options = new GraphTraversalOptions { MaxDepth = 1 };

        // Act
        var result = await _service.TraverseDfsAsync("a", options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.VisitedChunkIds.Count);
        Assert.DoesNotContain("c", result.VisitedChunkIds);
    }

    #endregion

    #region Shortest Path Tests

    [Fact]
    public async Task FindShortestPathAsync_DirectConnection_ReturnsPath()
    {
        // Arrange: chunk1 -> chunk2
        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["chunk1"] = new() { CreateRelationship("chunk1", "chunk2") },
            ["chunk2"] = new()
        };

        SetupRelationships(relationships);

        // Act
        var result = await _service.FindShortestPathAsync("chunk1", "chunk2", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.PathExists);
        Assert.Equal(2, result.Path.Count);
        Assert.Equal("chunk1", result.Path[0]);
        Assert.Equal("chunk2", result.Path[1]);
    }

    [Fact]
    public async Task FindShortestPathAsync_NoPath_ReturnsNotFound()
    {
        // Arrange: chunk1 and chunk2 are not connected
        SetupEmptyRelationships();

        // Act
        var result = await _service.FindShortestPathAsync("chunk1", "chunk2", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.PathExists);
        Assert.Empty(result.Path);
    }

    [Fact]
    public async Task FindShortestPathAsync_SameSourceAndTarget_ReturnsPath()
    {
        // Arrange
        SetupEmptyRelationships();

        // Act
        var result = await _service.FindShortestPathAsync("chunk1", "chunk1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.PathExists);
        Assert.Single(result.Path);
        Assert.Equal("chunk1", result.Path[0]);
    }

    [Fact]
    public async Task FindShortestPathAsync_MultiplePaths_ReturnsShortestPath()
    {
        // Arrange: chunk1 -> chunk2 -> chunk4 (short path, 2 hops)
        //          chunk1 -> chunk3 -> chunk5 -> chunk4 (long path, 3 hops)
        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["chunk1"] = new()
            {
                CreateRelationship("chunk1", "chunk2"),
                CreateRelationship("chunk1", "chunk3")
            },
            ["chunk2"] = new() { CreateRelationship("chunk2", "chunk4") },
            ["chunk3"] = new() { CreateRelationship("chunk3", "chunk5") },
            ["chunk4"] = new(),
            ["chunk5"] = new() { CreateRelationship("chunk5", "chunk4") }
        };

        SetupRelationships(relationships);

        // Act
        var result = await _service.FindShortestPathAsync("chunk1", "chunk4", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.PathExists);
        Assert.Equal(3, result.Path.Count); // chunk1 -> chunk2 -> chunk4
        Assert.Equal("chunk1", result.Path[0]);
        Assert.Equal("chunk2", result.Path[1]);
        Assert.Equal("chunk4", result.Path[2]);
    }

    #endregion

    #region Neighborhood Tests

    [Fact]
    public async Task GetNeighborhoodAsync_SingleHop_ReturnsDirectNeighbors()
    {
        // Arrange: chunk1 -> chunk2, chunk3
        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["chunk1"] = new()
            {
                CreateRelationship("chunk1", "chunk2"),
                CreateRelationship("chunk1", "chunk3")
            },
            ["chunk2"] = new(),
            ["chunk3"] = new()
        };

        SetupRelationships(relationships);

        // Act
        var result = await _service.GetNeighborhoodAsync("chunk1", 1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.TotalNeighbors); // chunk2 and chunk3
        Assert.True(result.NeighborsByHop.ContainsKey(1));
        Assert.Equal(2, result.NeighborsByHop[1].Count);
    }

    [Fact]
    public async Task GetNeighborhoodAsync_MultipleHops_ReturnsAllWithinRange()
    {
        // Arrange: A -> B -> C
        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["A"] = new() { CreateRelationship("A", "B") },
            ["B"] = new() { CreateRelationship("B", "C") },
            ["C"] = new()
        };

        SetupRelationships(relationships);

        // Act
        var result = await _service.GetNeighborhoodAsync("A", 2, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.TotalNeighbors); // B and C
        Assert.True(result.NeighborsByHop.ContainsKey(1)); // B at hop 1
        Assert.True(result.NeighborsByHop.ContainsKey(2)); // C at hop 2
    }

    #endregion

    #region Connected Components Tests

    [Fact]
    public async Task FindConnectedComponentsAsync_SingleComponent_ReturnsOne()
    {
        // Arrange: All chunks connected
        SetupChunksByLevel(new List<string> { "chunk1", "chunk2", "chunk3" });

        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["chunk1"] = new() { CreateRelationship("chunk1", "chunk2") },
            ["chunk2"] = new() { CreateRelationship("chunk2", "chunk3") },
            ["chunk3"] = new()
        };

        SetupRelationships(relationships);

        // Act
        var result = await _service.FindConnectedComponentsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        Assert.Equal(3, result[0].ChunkIds.Count);
    }

    [Fact]
    public async Task FindConnectedComponentsAsync_TwoComponents_ReturnsTwo()
    {
        // Arrange: chunk1-chunk2 and chunk3-chunk4 are separate components
        SetupChunksByLevel(new List<string> { "chunk1", "chunk2", "chunk3", "chunk4" });

        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["chunk1"] = new() { CreateRelationship("chunk1", "chunk2") },
            ["chunk2"] = new(),
            ["chunk3"] = new() { CreateRelationship("chunk3", "chunk4") },
            ["chunk4"] = new()
        };

        SetupRelationships(relationships);

        // Act
        var result = await _service.FindConnectedComponentsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
    }

    #endregion

    #region Cycle Detection Tests

    [Fact]
    public async Task DetectCyclesAsync_NoCycle_ReturnsEmpty()
    {
        // Arrange: Linear path, no cycle
        SetupChunksByLevel(new List<string> { "chunk1", "chunk2", "chunk3" });

        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["chunk1"] = new() { CreateRelationship("chunk1", "chunk2", direction: RelationshipDirection.Unidirectional) },
            ["chunk2"] = new() { CreateRelationship("chunk2", "chunk3", direction: RelationshipDirection.Unidirectional) },
            ["chunk3"] = new()
        };

        SetupRelationships(relationships);

        // Act
        var result = await _service.DetectCyclesAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectCyclesAsync_WithCycle_ReturnsCyclePath()
    {
        // Arrange: chunk1 -> chunk2 -> chunk3 -> chunk1 (cycle)
        SetupChunksByLevel(new List<string> { "chunk1", "chunk2", "chunk3" });

        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["chunk1"] = new() { CreateRelationship("chunk1", "chunk2", direction: RelationshipDirection.Unidirectional) },
            ["chunk2"] = new() { CreateRelationship("chunk2", "chunk3", direction: RelationshipDirection.Unidirectional) },
            ["chunk3"] = new() { CreateRelationship("chunk3", "chunk1", direction: RelationshipDirection.Unidirectional) }
        };

        SetupRelationships(relationships);

        // Act
        var result = await _service.DetectCyclesAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains(result, c => c.ChunkIds.Count >= 3);
    }

    #endregion

    #region Importance Calculation Tests

    [Fact]
    public async Task ComputeChunkImportanceAsync_EmptyGraph_ReturnsEmpty()
    {
        // Arrange
        SetupChunksByLevel(new List<string>());

        // Act
        var result = await _service.ComputeChunkImportanceAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ComputeChunkImportanceAsync_SingleNode_ReturnsNormalizedScore()
    {
        // Arrange
        SetupChunksByLevel(new List<string> { "chunk1" });
        SetupEmptyRelationships();

        // Act
        var result = await _service.ComputeChunkImportanceAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        Assert.True(result["chunk1"] > 0);
    }

    [Fact]
    public async Task ComputeChunkImportanceAsync_HubChunk_HasImportanceScore()
    {
        // Arrange: hub is connected to many chunks
        SetupChunksByLevel(new List<string> { "hub", "leaf1", "leaf2", "leaf3" });

        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["hub"] = new()
            {
                CreateRelationship("hub", "leaf1"),
                CreateRelationship("hub", "leaf2"),
                CreateRelationship("hub", "leaf3")
            },
            ["leaf1"] = new() { CreateRelationship("leaf1", "hub") },
            ["leaf2"] = new() { CreateRelationship("leaf2", "hub") },
            ["leaf3"] = new() { CreateRelationship("leaf3", "hub") }
        };

        SetupRelationships(relationships);

        // Act
        var result = await _service.ComputeChunkImportanceAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, result.Count);
        Assert.All(result.Values, v => Assert.True(v >= 0));
    }

    #endregion

    #region Consistency Check Tests

    [Fact]
    public async Task CheckConsistencyAsync_ValidGraph_ReturnsConsistent()
    {
        // Arrange
        SetupChunksByLevel(new List<string> { "chunk1", "chunk2" });

        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["chunk1"] = new() { CreateRelationship("chunk1", "chunk2") },
            ["chunk2"] = new()
        };

        SetupRelationships(relationships);

        // Act
        var result = await _service.CheckConsistencyAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsConsistent);
    }

    #endregion

    #region Transitive Closure Tests

    [Fact]
    public async Task ComputeTransitiveClosureAsync_LinearPath_ComputesAllReachable()
    {
        // Arrange: A -> B -> C
        var relationships = new Dictionary<string, List<ChunkRelationshipExtended>>
        {
            ["A"] = new() { CreateRelationship("A", "B") },
            ["B"] = new() { CreateRelationship("B", "C") },
            ["C"] = new()
        };

        SetupRelationships(relationships);

        // Act
        var result = await _service.ComputeTransitiveClosureAsync("A", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("B", result.DirectlyConnected);
        Assert.Contains(result.TransitiveConnections, t => t.TargetChunkId == "C");
    }

    #endregion

    #region Helper Methods

    private void SetupEmptyRelationships()
    {
        _hierarchyRepositoryMock.GetRelationshipsAsync(
            Arg.Any<string>(),
            Arg.Any<IEnumerable<RelationshipType>?>(),
            Arg.Any<CancellationToken>()).Returns(new List<ChunkRelationshipExtended>());
    }

    private void SetupRelationships(Dictionary<string, List<ChunkRelationshipExtended>> relationships)
    {
        _hierarchyRepositoryMock.GetRelationshipsAsync(
            Arg.Any<string>(),
            Arg.Any<IEnumerable<RelationshipType>?>(),
            Arg.Any<CancellationToken>()).Returns(callInfo => { var chunkId = callInfo.ArgAt<string>(0); return Task.FromResult<IReadOnlyList<ChunkRelationshipExtended>>(
                    relationships.GetValueOrDefault(chunkId) ?? new List<ChunkRelationshipExtended>()); });
    }

    private void SetupChunksByLevel(List<string> chunkIds)
    {
        // Setup GetChunksByLevelAsync to return chunks at level 0 only
        var hierarchies = chunkIds.Select(id => new ChunkHierarchy { ChunkId = id }).ToList();

        _hierarchyRepositoryMock.GetChunksByLevelAsync(
            Arg.Any<string>(), 0, Arg.Any<CancellationToken>()).Returns(hierarchies);

        // Return empty for other levels
        _hierarchyRepositoryMock.GetChunksByLevelAsync(
            Arg.Any<string>(), Arg.Is<int>(level => level > 0), Arg.Any<CancellationToken>()).Returns(new List<ChunkHierarchy>());
    }

    private static ChunkRelationshipExtended CreateRelationship(
        string sourceId,
        string targetId,
        RelationshipType type = RelationshipType.Sequential,
        RelationshipDirection direction = RelationshipDirection.Bidirectional,
        double strength = 0.8)
    {
        return new ChunkRelationshipExtended
        {
            SourceChunkId = sourceId,
            TargetChunkId = targetId,
            Type = type,
            Direction = direction,
            Strength = strength
        };
    }

    #endregion
}
