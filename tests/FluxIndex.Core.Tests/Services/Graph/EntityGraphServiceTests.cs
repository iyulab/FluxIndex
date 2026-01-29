using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Graph;

/// <summary>
/// Tests for EntityGraphService
/// </summary>
public class EntityGraphServiceTests
{
    private readonly Mock<IAdvancedEntityExtractionService> _mockEntityService;
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly ILogger<EntityGraphService> _logger;

    public EntityGraphServiceTests()
    {
        _mockEntityService = new Mock<IAdvancedEntityExtractionService>();
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _logger = NullLogger<EntityGraphService>.Instance;
    }

    private EntityGraphService CreateService(
        bool withEntityService = true,
        bool withEmbeddingService = false)
    {
        return new EntityGraphService(
            withEntityService ? _mockEntityService.Object : null,
            withEmbeddingService ? _mockEmbeddingService.Object : null,
            graphStore: null, // No graph store for unit tests
            _logger);
    }

    private DocumentChunk CreateChunk(string id, string content)
    {
        return new DocumentChunk
        {
            Id = id,
            DocumentId = $"doc_{id}",
            Content = content,
            ChunkIndex = 0
        };
    }

    private EntityGraph CreateMockEntityGraph(string sourceId, List<ExtractedEntity> entities, List<EntityRelation> relations)
    {
        return new EntityGraph
        {
            SourceId = sourceId,
            Entities = entities,
            Relations = relations
        };
    }

    #region BuildEntityGraphAsync Tests

    [Fact]
    public async Task BuildEntityGraphAsync_WithChunks_ExtractsEntitiesAndRelations()
    {
        // Arrange
        var service = CreateService(withEntityService: true);
        var chunks = new List<DocumentChunk>
        {
            CreateChunk("1", "Microsoft was founded by Bill Gates and Paul Allen in 1975."),
            CreateChunk("2", "Bill Gates attended Harvard University before starting Microsoft.")
        };

        var entity1 = new ExtractedEntity { Id = "e1", Text = "Microsoft", Type = NamedEntityType.Organization, Confidence = 0.95 };
        var entity2 = new ExtractedEntity { Id = "e2", Text = "Bill Gates", Type = NamedEntityType.Person, Confidence = 0.9 };
        var entity3 = new ExtractedEntity { Id = "e3", Text = "Paul Allen", Type = NamedEntityType.Person, Confidence = 0.85 };
        var relation = new EntityRelation
        {
            SourceEntityId = "e2",
            TargetEntityId = "e1",
            Type = RelationType.FoundedBy,
            Label = "founded",
            Confidence = 0.8
        };

        _mockEntityService
            .Setup(x => x.ExtractBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EntityExtractionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityGraph>
            {
                CreateMockEntityGraph("1", new List<ExtractedEntity> { entity1, entity2, entity3 }, new List<EntityRelation> { relation }),
                CreateMockEntityGraph("2", new List<ExtractedEntity> { entity2 }, new List<EntityRelation>())
            });

        // Act
        var result = await service.BuildEntityGraphAsync(chunks);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Entities);
        Assert.Equal(2, result.SourceChunkIds.Count);
        Assert.True(result.Stats.TotalEntities > 0);
    }

    [Fact]
    public async Task BuildEntityGraphAsync_WithoutEntityService_ReturnsEmptyGraph()
    {
        // Arrange
        var service = CreateService(withEntityService: false);
        var chunks = new List<DocumentChunk>
        {
            CreateChunk("1", "Test content")
        };

        // Act
        var result = await service.BuildEntityGraphAsync(chunks);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Entities);
        Assert.Single(result.SourceChunkIds);
    }

    [Fact]
    public async Task BuildEntityGraphAsync_WithLinkingEnabled_MergesDuplicateEntities()
    {
        // Arrange
        var service = CreateService(withEntityService: true);
        var chunks = new List<DocumentChunk>
        {
            CreateChunk("1", "Bill Gates is a philanthropist."),
            CreateChunk("2", "Bill Gates founded the Bill & Melinda Gates Foundation.")
        };

        var entity1 = new ExtractedEntity { Id = "e1", Text = "Bill Gates", Type = NamedEntityType.Person, Confidence = 0.9 };
        var entity2 = new ExtractedEntity { Id = "e2", Text = "Bill Gates", Type = NamedEntityType.Person, Confidence = 0.85 };

        _mockEntityService
            .Setup(x => x.ExtractBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EntityExtractionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityGraph>
            {
                CreateMockEntityGraph("1", new List<ExtractedEntity> { entity1 }, new List<EntityRelation>()),
                CreateMockEntityGraph("2", new List<ExtractedEntity> { entity2 }, new List<EntityRelation>())
            });

        var options = new EntityGraphBuildOptions { LinkEntitiesAcrossChunks = true };

        // Act
        var result = await service.BuildEntityGraphAsync(chunks, options);

        // Assert
        Assert.NotNull(result);
        // With linking, duplicate entities should be merged
        var entity = Assert.Single(result.Entities);
        Assert.Contains("Bill Gates", entity.SurfaceForms);
    }

    #endregion

    #region SearchByEntitiesAsync Tests

    [Fact]
    public async Task SearchByEntitiesAsync_WithMatchingEntities_ReturnsRankedResults()
    {
        // Arrange
        var service = CreateService(withEntityService: true);

        // Create a pre-built entity graph
        var entityGraph = new EntityGraphResult
        {
            Entities = new List<EntityNode>
            {
                new() { Id = "e1", Name = "Microsoft", NormalizedName = "microsoft", Type = NamedEntityType.Organization },
                new() { Id = "e2", Name = "Bill Gates", NormalizedName = "bill gates", Type = NamedEntityType.Person }
            },
            Relations = new List<EntityEdge>
            {
                new() { SourceEntityId = "e2", TargetEntityId = "e1", RelationType = RelationType.FoundedBy, Weight = 0.9 }
            },
            ChunkMappings = new List<EntityChunkMapping>
            {
                new() { EntityId = "e1", ChunkId = "c1", RelevanceScore = 0.9 },
                new() { EntityId = "e2", ChunkId = "c1", RelevanceScore = 0.85 },
                new() { EntityId = "e2", ChunkId = "c2", RelevanceScore = 0.7 }
            }
        };

        _mockEntityService
            .Setup(x => x.ExtractEntitiesAsync(It.IsAny<string>(), It.IsAny<EntityExtractionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedEntity>
            {
                new() { Id = "q1", Text = "Microsoft", Type = NamedEntityType.Organization, Confidence = 0.9 }
            });

        // Act
        var result = await service.SearchByEntitiesAsync("Who founded Microsoft?", entityGraph);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.QueryEntities);
        Assert.NotEmpty(result.Hits);
    }

    [Fact]
    public async Task SearchByEntitiesAsync_WithNoMatchingEntities_ReturnsEmptyResults()
    {
        // Arrange
        var service = CreateService(withEntityService: true);
        var entityGraph = new EntityGraphResult
        {
            Entities = new List<EntityNode>
            {
                new() { Id = "e1", Name = "Apple", NormalizedName = "apple", Type = NamedEntityType.Organization }
            },
            ChunkMappings = new List<EntityChunkMapping>()
        };

        _mockEntityService
            .Setup(x => x.ExtractEntitiesAsync(It.IsAny<string>(), It.IsAny<EntityExtractionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedEntity>());

        // Act
        var result = await service.SearchByEntitiesAsync("Who founded Microsoft?", entityGraph);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.QueryEntities);
        Assert.Empty(result.Hits);
    }

    #endregion

    #region TraverseEntityRelationsAsync Tests

    [Fact]
    public async Task TraverseEntityRelationsAsync_WithConnectedEntities_ReturnsTraversalPaths()
    {
        // Arrange
        var service = CreateService();
        var entityGraph = new EntityGraphResult
        {
            Entities = new List<EntityNode>
            {
                new() { Id = "e1", Name = "Microsoft", NormalizedName = "microsoft", Type = NamedEntityType.Organization },
                new() { Id = "e2", Name = "Bill Gates", NormalizedName = "bill gates", Type = NamedEntityType.Person },
                new() { Id = "e3", Name = "Harvard", NormalizedName = "harvard", Type = NamedEntityType.Organization }
            },
            Relations = new List<EntityEdge>
            {
                new() { SourceEntityId = "e2", TargetEntityId = "e1", RelationType = RelationType.FoundedBy, Weight = 0.9 },
                new() { SourceEntityId = "e2", TargetEntityId = "e3", RelationType = RelationType.RelatedTo, Weight = 0.7 }
            },
            ChunkMappings = new List<EntityChunkMapping>
            {
                new() { EntityId = "e1", ChunkId = "c1", RelevanceScore = 0.9 },
                new() { EntityId = "e2", ChunkId = "c1", RelevanceScore = 0.85 }
            }
        };

        // Act
        var result = await service.TraverseEntityRelationsAsync(new[] { "e2" }, entityGraph);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.StartEntities);
        Assert.True(result.Stats.EntitiesVisited >= 1);
    }

    [Fact]
    public async Task TraverseEntityRelationsAsync_RespectsMaxHops()
    {
        // Arrange
        var service = CreateService();
        var entityGraph = new EntityGraphResult
        {
            Entities = new List<EntityNode>
            {
                new() { Id = "e1", Name = "A", NormalizedName = "a", Type = NamedEntityType.Concept },
                new() { Id = "e2", Name = "B", NormalizedName = "b", Type = NamedEntityType.Concept },
                new() { Id = "e3", Name = "C", NormalizedName = "c", Type = NamedEntityType.Concept },
                new() { Id = "e4", Name = "D", NormalizedName = "d", Type = NamedEntityType.Concept }
            },
            Relations = new List<EntityEdge>
            {
                new() { SourceEntityId = "e1", TargetEntityId = "e2", RelationType = RelationType.RelatedTo, Weight = 0.9 },
                new() { SourceEntityId = "e2", TargetEntityId = "e3", RelationType = RelationType.RelatedTo, Weight = 0.8 },
                new() { SourceEntityId = "e3", TargetEntityId = "e4", RelationType = RelationType.RelatedTo, Weight = 0.7 }
            },
            ChunkMappings = new List<EntityChunkMapping>()
        };

        var options = new EntityTraversalOptions { MaxHops = 2 };

        // Act
        var result = await service.TraverseEntityRelationsAsync(new[] { "e1" }, entityGraph, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Stats.MaxHopReached <= 2);
    }

    #endregion

    #region ComputeEntityImportanceAsync Tests

    [Fact]
    public async Task ComputeEntityImportanceAsync_WithConnectedGraph_ReturnsImportanceScores()
    {
        // Arrange
        var service = CreateService();
        var entityGraph = new EntityGraphResult
        {
            Entities = new List<EntityNode>
            {
                new() { Id = "e1", Name = "Hub", NormalizedName = "hub", Type = NamedEntityType.Concept },
                new() { Id = "e2", Name = "Spoke1", NormalizedName = "spoke1", Type = NamedEntityType.Concept },
                new() { Id = "e3", Name = "Spoke2", NormalizedName = "spoke2", Type = NamedEntityType.Concept }
            },
            Relations = new List<EntityEdge>
            {
                new() { SourceEntityId = "e2", TargetEntityId = "e1", RelationType = RelationType.RelatedTo, Weight = 0.9 },
                new() { SourceEntityId = "e3", TargetEntityId = "e1", RelationType = RelationType.RelatedTo, Weight = 0.8 }
            }
        };

        // Act
        var scores = await service.ComputeEntityImportanceAsync(entityGraph);

        // Assert
        Assert.NotNull(scores);
        Assert.Equal(3, scores.Count);
        Assert.All(scores.Values, score => Assert.True(score >= 0 && score <= 1));
    }

    [Fact]
    public async Task ComputeEntityImportanceAsync_WithSeedEntities_BiasesTowardsSeeds()
    {
        // Arrange
        var service = CreateService();
        var entityGraph = new EntityGraphResult
        {
            Entities = new List<EntityNode>
            {
                new() { Id = "e1", Name = "A", NormalizedName = "a", Type = NamedEntityType.Concept },
                new() { Id = "e2", Name = "B", NormalizedName = "b", Type = NamedEntityType.Concept },
                new() { Id = "e3", Name = "C", NormalizedName = "c", Type = NamedEntityType.Concept }
            },
            Relations = new List<EntityEdge>
            {
                new() { SourceEntityId = "e1", TargetEntityId = "e2", RelationType = RelationType.RelatedTo, Weight = 0.9 },
                new() { SourceEntityId = "e2", TargetEntityId = "e3", RelationType = RelationType.RelatedTo, Weight = 0.8 }
            }
        };

        // Act
        var scoresWithSeed = await service.ComputeEntityImportanceAsync(
            entityGraph, new[] { "e1" });
        var scoresWithoutSeed = await service.ComputeEntityImportanceAsync(
            entityGraph, null);

        // Assert
        Assert.NotNull(scoresWithSeed);
        Assert.NotNull(scoresWithoutSeed);
        // With seed on e1, e1 should have higher importance than without
        Assert.True(scoresWithSeed["e1"] >= scoresWithoutSeed["e1"] * 0.8);
    }

    #endregion

    #region MergeEntityGraphsAsync Tests

    [Fact]
    public async Task MergeEntityGraphsAsync_WithOverlappingEntities_MergesCorrectly()
    {
        // Arrange
        var service = CreateService();
        var graph1 = new EntityGraphResult
        {
            Entities = new List<EntityNode>
            {
                new() { Id = "g1e1", Name = "Microsoft", NormalizedName = "microsoft", Type = NamedEntityType.Organization, MentionCount = 5 }
            },
            Relations = new List<EntityEdge>(),
            ChunkMappings = new List<EntityChunkMapping>
            {
                new() { EntityId = "g1e1", ChunkId = "c1", RelevanceScore = 0.9 }
            },
            SourceChunkIds = new List<string> { "c1" }
        };

        var graph2 = new EntityGraphResult
        {
            Entities = new List<EntityNode>
            {
                new() { Id = "g2e1", Name = "Microsoft", NormalizedName = "microsoft", Type = NamedEntityType.Organization, MentionCount = 3 }
            },
            Relations = new List<EntityEdge>(),
            ChunkMappings = new List<EntityChunkMapping>
            {
                new() { EntityId = "g2e1", ChunkId = "c2", RelevanceScore = 0.85 }
            },
            SourceChunkIds = new List<string> { "c2" }
        };

        // Act
        var merged = await service.MergeEntityGraphsAsync(new[] { graph1, graph2 });

        // Assert
        Assert.NotNull(merged);
        Assert.Single(merged.Entities); // Should be merged into one
        Assert.Equal(8, merged.Entities.First().MentionCount); // 5 + 3
        Assert.Equal(2, merged.ChunkMappings.Count);
        Assert.Equal(2, merged.SourceChunkIds.Count);
    }

    [Fact]
    public async Task MergeEntityGraphsAsync_WithEmptyGraphs_ReturnsEmptyResult()
    {
        // Arrange
        var service = CreateService();
        var graphs = Array.Empty<EntityGraphResult>();

        // Act
        var result = await service.MergeEntityGraphsAsync(graphs);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Entities);
    }

    #endregion

    #region GetChunksForEntitiesAsync Tests

    [Fact]
    public async Task GetChunksForEntitiesAsync_WithValidEntityIds_ReturnsMappings()
    {
        // Arrange
        var service = CreateService();
        var entityGraph = new EntityGraphResult
        {
            ChunkMappings = new List<EntityChunkMapping>
            {
                new() { EntityId = "e1", ChunkId = "c1", RelevanceScore = 0.9 },
                new() { EntityId = "e1", ChunkId = "c2", RelevanceScore = 0.7 },
                new() { EntityId = "e2", ChunkId = "c3", RelevanceScore = 0.8 }
            }
        };

        // Act
        var result = await service.GetChunksForEntitiesAsync(new[] { "e1" }, entityGraph);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.All(result, m => Assert.Equal("e1", m.EntityId));
    }

    #endregion

    #region FindBridgeEntitiesAsync Tests

    [Fact]
    public async Task FindBridgeEntitiesAsync_WithBridgeEntity_IdentifiesBridge()
    {
        // Arrange
        var service = CreateService();
        var entityGraph = new EntityGraphResult
        {
            Entities = new List<EntityNode>
            {
                new() { Id = "bridge", Name = "Bridge", NormalizedName = "bridge", Type = NamedEntityType.Concept },
                new() { Id = "cluster1a", Name = "C1A", NormalizedName = "c1a", Type = NamedEntityType.Concept },
                new() { Id = "cluster1b", Name = "C1B", NormalizedName = "c1b", Type = NamedEntityType.Concept },
                new() { Id = "cluster2a", Name = "C2A", NormalizedName = "c2a", Type = NamedEntityType.Concept },
                new() { Id = "cluster2b", Name = "C2B", NormalizedName = "c2b", Type = NamedEntityType.Concept }
            },
            Relations = new List<EntityEdge>
            {
                // Cluster 1 connected to bridge (bidirectional)
                new() { SourceEntityId = "cluster1a", TargetEntityId = "bridge", RelationType = RelationType.RelatedTo, Weight = 0.9, IsDirectional = false },
                new() { SourceEntityId = "cluster1b", TargetEntityId = "bridge", RelationType = RelationType.RelatedTo, Weight = 0.8, IsDirectional = false },
                // Cluster 2 connected to bridge (bidirectional)
                new() { SourceEntityId = "cluster2a", TargetEntityId = "bridge", RelationType = RelationType.RelatedTo, Weight = 0.85, IsDirectional = false },
                new() { SourceEntityId = "cluster2b", TargetEntityId = "bridge", RelationType = RelationType.RelatedTo, Weight = 0.75, IsDirectional = false },
                // Intra-cluster connections
                new() { SourceEntityId = "cluster1a", TargetEntityId = "cluster1b", RelationType = RelationType.RelatedTo, Weight = 0.9, IsDirectional = false },
                new() { SourceEntityId = "cluster2a", TargetEntityId = "cluster2b", RelationType = RelationType.RelatedTo, Weight = 0.9, IsDirectional = false }
            }
        };

        var options = new BridgeEntityOptions { MinConnections = 3, TopN = 5 };

        // Act
        var bridges = await service.FindBridgeEntitiesAsync(entityGraph, options);

        // Assert
        Assert.NotNull(bridges);
        Assert.NotEmpty(bridges);
        Assert.Equal("bridge", bridges.First().Entity.Id);
    }

    #endregion

    #region DI Registration Tests

    [Fact]
    public void ServiceCanBeCreatedWithMinimalDependencies()
    {
        // Arrange & Act
        var service = new EntityGraphService(
            entityExtractionService: null,
            embeddingService: null,
            logger: null);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void ServiceCanBeCreatedWithAllDependencies()
    {
        // Arrange & Act
        var service = CreateService(withEntityService: true, withEmbeddingService: true);

        // Assert
        Assert.NotNull(service);
    }

    #endregion
}
