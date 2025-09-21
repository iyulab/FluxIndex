using FluxIndex.Core.Domain.Models;
using FluxIndex.Storage.PostgreSQL;
using FluxIndex.Storage.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FluxIndex.Tests.SmallToBig;

/// <summary>
/// ChunkHierarchyRepository 통합 테스트
/// </summary>
public class ChunkHierarchyRepositoryTests : IDisposable
{
    private readonly FluxIndexDbContext _context;
    private readonly ChunkHierarchyRepository _repository;
    private readonly Mock<ILogger<ChunkHierarchyRepository>> _mockLogger;

    public ChunkHierarchyRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<FluxIndexDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new FluxIndexDbContext(options);
        _mockLogger = new Mock<ILogger<ChunkHierarchyRepository>>();
        _repository = new ChunkHierarchyRepository(_context, _mockLogger.Object);
    }

    [Fact]
    public async Task SaveHierarchyAsync_NewHierarchy_SavesSuccessfully()
    {
        // Arrange
        var hierarchy = CreateTestHierarchy("chunk1", "parent1", new[] { "child1", "child2" });

        // Act
        await _repository.SaveHierarchyAsync(hierarchy, CancellationToken.None);

        // Assert
        var saved = await _repository.GetHierarchyAsync("chunk1", CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(hierarchy.ChunkId, saved.ChunkId);
        Assert.Equal(hierarchy.ParentChunkId, saved.ParentChunkId);
        Assert.Equal(hierarchy.ChildChunkIds.Count, saved.ChildChunkIds.Count);
        Assert.Equal(hierarchy.HierarchyLevel, saved.HierarchyLevel);
    }

    [Fact]
    public async Task SaveHierarchyAsync_UpdateExisting_UpdatesSuccessfully()
    {
        // Arrange
        var hierarchy = CreateTestHierarchy("chunk1", "parent1", new[] { "child1" });
        await _repository.SaveHierarchyAsync(hierarchy, CancellationToken.None);

        // Update hierarchy
        hierarchy.ChildChunkIds.Add("child2");
        hierarchy.HierarchyLevel = 1;
        hierarchy.RecommendedWindowSize = 5;

        // Act
        await _repository.SaveHierarchyAsync(hierarchy, CancellationToken.None);

        // Assert
        var updated = await _repository.GetHierarchyAsync("chunk1", CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(2, updated.ChildChunkIds.Count);
        Assert.Equal(1, updated.HierarchyLevel);
        Assert.Equal(5, updated.RecommendedWindowSize);
        Assert.Contains("child2", updated.ChildChunkIds);
    }

    [Fact]
    public async Task GetHierarchyAsync_NonExistentChunk_ReturnsNull()
    {
        // Act
        var result = await _repository.GetHierarchyAsync("nonexistent", CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetChildrenAsync_WithChildren_ReturnsChildren()
    {
        // Arrange
        var parent = CreateTestHierarchy("parent1", null, new[] { "child1", "child2" });
        var child1 = CreateTestHierarchy("child1", "parent1", Array.Empty<string>(), 1);
        var child2 = CreateTestHierarchy("child2", "parent1", Array.Empty<string>(), 1);

        await _repository.SaveHierarchyAsync(parent, CancellationToken.None);
        await _repository.SaveHierarchyAsync(child1, CancellationToken.None);
        await _repository.SaveHierarchyAsync(child2, CancellationToken.None);

        // Act
        var children = await _repository.GetChildrenAsync("parent1", CancellationToken.None);

        // Assert
        Assert.Equal(2, children.Count);
        Assert.Contains(children, c => c.ChunkId == "child1");
        Assert.Contains(children, c => c.ChunkId == "child2");
        Assert.All(children, c => Assert.Equal("parent1", c.ParentChunkId));
    }

    [Fact]
    public async Task GetChunksByLevelAsync_WithDocumentChunks_ReturnsCorrectLevel()
    {
        // Arrange
        var documentId = Guid.NewGuid().ToString();

        // Create document chunks in database
        var chunk1 = new DocumentChunk { Id = Guid.NewGuid().ToString(), DocumentId = documentId, Content = "Content 1" };
        var chunk2 = new DocumentChunk { Id = Guid.NewGuid().ToString(), DocumentId = documentId, Content = "Content 2" };
        var chunk3 = new DocumentChunk { Id = Guid.NewGuid().ToString(), DocumentId = documentId, Content = "Content 3" };

        _context.Chunks.AddRange(chunk1, chunk2, chunk3);
        await _context.SaveChangesAsync();

        // Create hierarchies
        var hierarchy1 = CreateTestHierarchy(chunk1.Id, null, Array.Empty<string>(), 0);
        var hierarchy2 = CreateTestHierarchy(chunk2.Id, null, Array.Empty<string>(), 0);
        var hierarchy3 = CreateTestHierarchy(chunk3.Id, null, Array.Empty<string>(), 1);

        await _repository.SaveHierarchyAsync(hierarchy1, CancellationToken.None);
        await _repository.SaveHierarchyAsync(hierarchy2, CancellationToken.None);
        await _repository.SaveHierarchyAsync(hierarchy3, CancellationToken.None);

        // Act
        var level0Chunks = await _repository.GetChunksByLevelAsync(documentId, 0, CancellationToken.None);
        var level1Chunks = await _repository.GetChunksByLevelAsync(documentId, 1, CancellationToken.None);

        // Assert
        Assert.Equal(2, level0Chunks.Count);
        Assert.Single(level1Chunks);
        Assert.Contains(level0Chunks, c => c.ChunkId == chunk1.Id);
        Assert.Contains(level0Chunks, c => c.ChunkId == chunk2.Id);
        Assert.Contains(level1Chunks, c => c.ChunkId == chunk3.Id);
    }

    [Fact]
    public async Task SaveRelationshipAsync_NewRelationship_SavesSuccessfully()
    {
        // Arrange
        var relationship = CreateTestRelationship("rel1", "chunk1", "chunk2", RelationshipType.Sequential, 0.8);

        // Act
        await _repository.SaveRelationshipAsync(relationship, CancellationToken.None);

        // Assert
        var relationships = await _repository.GetRelationshipsAsync("chunk1", null, CancellationToken.None);
        Assert.Single(relationships);
        var saved = relationships.First();
        Assert.Equal(relationship.Id, saved.Id);
        Assert.Equal(relationship.SourceChunkId, saved.SourceChunkId);
        Assert.Equal(relationship.TargetChunkId, saved.TargetChunkId);
        Assert.Equal(relationship.Type, saved.Type);
        Assert.Equal(relationship.Strength, saved.Strength);
    }

    [Fact]
    public async Task GetRelationshipsAsync_WithTypeFilter_ReturnsFilteredResults()
    {
        // Arrange
        var rel1 = CreateTestRelationship("rel1", "chunk1", "chunk2", RelationshipType.Sequential, 0.8);
        var rel2 = CreateTestRelationship("rel2", "chunk1", "chunk3", RelationshipType.Semantic, 0.7);
        var rel3 = CreateTestRelationship("rel3", "chunk1", "chunk4", RelationshipType.Hierarchical, 0.9);

        await _repository.SaveRelationshipAsync(rel1, CancellationToken.None);
        await _repository.SaveRelationshipAsync(rel2, CancellationToken.None);
        await _repository.SaveRelationshipAsync(rel3, CancellationToken.None);

        // Act
        var semanticRels = await _repository.GetRelationshipsAsync("chunk1",
            new[] { RelationshipType.Semantic }, CancellationToken.None);

        var sequentialRels = await _repository.GetRelationshipsAsync("chunk1",
            new[] { RelationshipType.Sequential }, CancellationToken.None);

        // Assert
        Assert.Single(semanticRels);
        Assert.Equal(RelationshipType.Semantic, semanticRels.First().Type);

        Assert.Single(sequentialRels);
        Assert.Equal(RelationshipType.Sequential, sequentialRels.First().Type);
    }

    [Fact]
    public async Task GetHierarchyStatisticsAsync_WithCompleteHierarchy_ReturnsCorrectStats()
    {
        // Arrange
        var documentId = Guid.NewGuid().ToString();

        // Create document chunks
        var chunks = Enumerable.Range(1, 5)
            .Select(i => new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = documentId,
                Content = $"Content {i}"
            })
            .ToList();

        _context.Chunks.AddRange(chunks);
        await _context.SaveChangesAsync();

        // Create hierarchies: root -> 2 children -> 2 grandchildren
        var root = CreateTestHierarchy(chunks[0].Id, null, new[] { chunks[1].Id, chunks[2].Id }, 0);
        var child1 = CreateTestHierarchy(chunks[1].Id, chunks[0].Id, new[] { chunks[3].Id }, 1);
        var child2 = CreateTestHierarchy(chunks[2].Id, chunks[0].Id, new[] { chunks[4].Id }, 1);
        var grandchild1 = CreateTestHierarchy(chunks[3].Id, chunks[1].Id, Array.Empty<string>(), 2);
        var grandchild2 = CreateTestHierarchy(chunks[4].Id, chunks[2].Id, Array.Empty<string>(), 2);

        await _repository.SaveHierarchyAsync(root, CancellationToken.None);
        await _repository.SaveHierarchyAsync(child1, CancellationToken.None);
        await _repository.SaveHierarchyAsync(child2, CancellationToken.None);
        await _repository.SaveHierarchyAsync(grandchild1, CancellationToken.None);
        await _repository.SaveHierarchyAsync(grandchild2, CancellationToken.None);

        // Create relationships
        var rel1 = CreateTestRelationship("rel1", chunks[0].Id, chunks[1].Id, RelationshipType.Hierarchical, 0.9);
        var rel2 = CreateTestRelationship("rel2", chunks[1].Id, chunks[2].Id, RelationshipType.Sequential, 0.8);

        await _repository.SaveRelationshipAsync(rel1, CancellationToken.None);
        await _repository.SaveRelationshipAsync(rel2, CancellationToken.None);

        // Act
        var stats = await _repository.GetHierarchyStatisticsAsync(documentId, CancellationToken.None);

        // Assert
        Assert.Equal(5, stats.TotalChunks);
        Assert.Equal(2, stats.MaxDepth); // grandchildren at depth 2
        Assert.Equal(1, stats.OrphanChunks); // only root has no parent
        Assert.Equal(2, stats.LeafChunks); // two grandchildren
        Assert.True(stats.AverageBranchingFactor > 0);

        // Level distribution
        Assert.Equal(1, stats.LevelDistribution[0]); // 1 root
        Assert.Equal(2, stats.LevelDistribution[1]); // 2 children
        Assert.Equal(2, stats.LevelDistribution[2]); // 2 grandchildren

        // Relationship statistics
        Assert.Contains(RelationshipType.Hierarchical, stats.RelationshipStatistics.Keys);
        Assert.Contains(RelationshipType.Sequential, stats.RelationshipStatistics.Keys);
    }

    [Fact]
    public async Task GetHierarchyStatisticsAsync_EmptyDocument_ReturnsEmptyStats()
    {
        // Arrange
        var emptyDocumentId = Guid.NewGuid().ToString();

        // Act
        var stats = await _repository.GetHierarchyStatisticsAsync(emptyDocumentId, CancellationToken.None);

        // Assert
        Assert.Equal(0, stats.TotalChunks);
        Assert.Equal(0, stats.MaxDepth);
        Assert.Equal(0, stats.OrphanChunks);
        Assert.Equal(0, stats.LeafChunks);
        Assert.Equal(0, stats.AverageBranchingFactor);
        Assert.Empty(stats.LevelDistribution);
        Assert.Empty(stats.RelationshipStatistics);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetHierarchyAsync_InvalidChunkId_ThrowsArgumentException(string chunkId)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _repository.GetHierarchyAsync(chunkId, CancellationToken.None));
    }

    [Fact]
    public async Task SaveHierarchyAsync_NullHierarchy_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _repository.SaveHierarchyAsync(null!, CancellationToken.None));
    }

    private static ChunkHierarchy CreateTestHierarchy(
        string chunkId,
        string? parentId,
        string[] childIds,
        int level = 0)
    {
        return new ChunkHierarchy
        {
            ChunkId = chunkId,
            ParentChunkId = parentId,
            ChildChunkIds = childIds.ToList(),
            HierarchyLevel = level,
            RecommendedWindowSize = 2,
            Boundary = new ChunkBoundary
            {
                StartPosition = 0,
                EndPosition = 100,
                Type = BoundaryType.Sentence,
                Confidence = 1.0,
                DetectionMethod = "test_method"
            },
            Metadata = new HierarchyMetadata
            {
                Depth = level,
                SiblingCount = childIds.Length,
                DescendantCount = childIds.Length,
                HierarchyWeight = 1.0,
                QualityScore = 0.9,
                Properties = new Dictionary<string, object>
                {
                    ["test_property"] = "test_value"
                }
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static ChunkRelationshipExtended CreateTestRelationship(
        string id,
        string sourceId,
        string targetId,
        RelationshipType type,
        double strength)
    {
        return new ChunkRelationshipExtended
        {
            Id = id,
            SourceChunkId = sourceId,
            TargetChunkId = targetId,
            Type = type,
            Strength = strength,
            Direction = RelationshipDirection.Bidirectional,
            Description = $"Test relationship {type}",
            Metadata = new Dictionary<string, object>
            {
                ["test_meta"] = "test_value"
            },
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}