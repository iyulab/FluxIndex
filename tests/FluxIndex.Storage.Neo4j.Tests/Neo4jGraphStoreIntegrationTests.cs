using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Neo4j;
using Xunit;

namespace FluxIndex.Storage.Neo4j.Tests;

/// <summary>
/// Integration tests for Neo4jGraphStore using Testcontainers.
/// These tests require Docker to be running.
/// </summary>
[Collection("Neo4j")]
[Trait("Category", "Integration")]
public class Neo4jGraphStoreIntegrationTests : IAsyncLifetime
{
    private readonly Neo4jContainer _container;
    private Neo4jGraphStore _graphStore = null!;
    private readonly ILogger<Neo4jGraphStore> _logger;

    public Neo4jGraphStoreIntegrationTests()
    {
        _container = new Neo4jBuilder("neo4j:5-community")
            .Build();
        _logger = NullLogger<Neo4jGraphStore>.Instance;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();

            var options = Options.Create(new Neo4jOptions
            {
                Uri = _container.GetConnectionString(),
                Username = "neo4j",
                Password = "neo4j", // Default password for testcontainers
                Database = "neo4j"
            });

            _graphStore = new Neo4jGraphStore(options, _logger);
        }
        catch (Exception)
        {
            // Docker not available - tests will be skipped
        }
    }

    public async Task DisposeAsync()
    {
        if (_graphStore != null)
        {
            await _graphStore.DisposeAsync();
        }
        await _container.DisposeAsync();
    }

    private bool IsDockerAvailable => _graphStore != null;

    private GraphEntity CreateTestEntity(string? id = null, string? name = null, NamedEntityType type = NamedEntityType.Unknown)
    {
        return new GraphEntity
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Name = name ?? $"TestEntity_{Guid.NewGuid():N}",
            NormalizedName = (name ?? "testentity").ToLowerInvariant(),
            Type = type,
            Description = "Test entity for integration testing",
            Confidence = 0.95,
            ImportanceScore = 0.5,
            MentionCount = 1,
            ChunkIds = [$"chunk_{Guid.NewGuid():N}"],
            DocumentIds = [$"doc_{Guid.NewGuid():N}"],
            SurfaceForms = [name ?? "TestEntity"]
        };
    }

    private GraphRelationship CreateTestRelationship(
        string sourceId,
        string targetId,
        RelationType type = RelationType.RelatedTo)
    {
        return new GraphRelationship
        {
            Id = Guid.NewGuid().ToString(),
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Type = type,
            Label = type.ToString(),
            Confidence = 0.9,
            Weight = 1.0,
            IsDirectional = true,
            EvidenceChunkIds = ["chunk_1"],
            EvidenceTexts = ["Test evidence text"]
        };
    }

    #region Entity Operations

    [SkippableFact]
    public async Task StoreEntityAsync_ValidEntity_ReturnsEntityId()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var entity = CreateTestEntity();

        // Act
        var result = await _graphStore.StoreEntityAsync(entity);

        // Assert
        result.Should().Be(entity.Id);
    }

    [SkippableFact]
    public async Task StoreEntitiesBatchAsync_MultipleEntities_ReturnsAllIds()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var entities = Enumerable.Range(0, 5)
            .Select(_ => CreateTestEntity())
            .ToList();

        // Act
        var results = await _graphStore.StoreEntitiesBatchAsync(entities);

        // Assert
        results.Should().HaveCount(5);
        results.Should().BeEquivalentTo(entities.Select(e => e.Id));
    }

    [SkippableFact]
    public async Task GetEntityByIdAsync_ExistingEntity_ReturnsEntity()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var entity = CreateTestEntity();
        await _graphStore.StoreEntityAsync(entity);

        // Act
        var result = await _graphStore.GetEntityByIdAsync(entity.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(entity.Id);
        result.Name.Should().Be(entity.Name);
        result.Type.Should().Be(entity.Type);
    }

    [SkippableFact]
    public async Task GetEntityByIdAsync_NonExistingEntity_ReturnsNull()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Act
        var result = await _graphStore.GetEntityByIdAsync(Guid.NewGuid().ToString());

        // Assert
        result.Should().BeNull();
    }

    [SkippableFact]
    public async Task GetEntitiesByNameAsync_ExactMatch_ReturnsMatchingEntities()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var uniqueName = $"UniqueTestEntity_{Guid.NewGuid():N}";
        var entity = CreateTestEntity(name: uniqueName);
        await _graphStore.StoreEntityAsync(entity);

        // Act
        var results = await _graphStore.GetEntitiesByNameAsync(uniqueName, fuzzyMatch: false);

        // Assert
        results.Should().HaveCount(1);
        results[0].Name.Should().Be(uniqueName);
    }

    [SkippableFact]
    public async Task GetEntitiesByTypeAsync_ValidType_ReturnsMatchingEntities()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        await _graphStore.ClearAsync();
        var personEntities = Enumerable.Range(0, 3)
            .Select(_ => CreateTestEntity(type: NamedEntityType.Person))
            .ToList();
        await _graphStore.StoreEntitiesBatchAsync(personEntities);

        var orgEntity = CreateTestEntity(type: NamedEntityType.Organization);
        await _graphStore.StoreEntityAsync(orgEntity);

        // Act
        var results = await _graphStore.GetEntitiesByTypeAsync(NamedEntityType.Person);

        // Assert
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(e => e.Type.Should().Be(NamedEntityType.Person));
    }

    [SkippableFact]
    public async Task UpdateEntityAsync_ExistingEntity_UpdatesSuccessfully()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var entity = CreateTestEntity();
        await _graphStore.StoreEntityAsync(entity);

        var updatedEntity = entity with
        {
            Description = "Updated description",
            ImportanceScore = 0.9
        };

        // Act
        var result = await _graphStore.UpdateEntityAsync(updatedEntity);

        // Assert
        result.Should().BeTrue();

        var retrieved = await _graphStore.GetEntityByIdAsync(entity.Id);
        retrieved!.Description.Should().Be("Updated description");
        retrieved.ImportanceScore.Should().Be(0.9);
    }

    [SkippableFact]
    public async Task DeleteEntityAsync_ExistingEntity_RemovesEntity()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var entity = CreateTestEntity();
        await _graphStore.StoreEntityAsync(entity);

        // Act
        var result = await _graphStore.DeleteEntityAsync(entity.Id);

        // Assert
        result.Should().BeTrue();

        var retrieved = await _graphStore.GetEntityByIdAsync(entity.Id);
        retrieved.Should().BeNull();
    }

    #endregion

    #region Relationship Operations

    [SkippableFact]
    public async Task StoreRelationshipAsync_ValidRelationship_ReturnsRelationshipId()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var entity1 = CreateTestEntity();
        var entity2 = CreateTestEntity();
        await _graphStore.StoreEntitiesBatchAsync([entity1, entity2]);

        var relationship = CreateTestRelationship(entity1.Id, entity2.Id);

        // Act
        var result = await _graphStore.StoreRelationshipAsync(relationship);

        // Assert
        result.Should().Be(relationship.Id);
    }

    [SkippableFact]
    public async Task GetRelationshipsAsync_ExistingRelationships_ReturnsRelationships()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var entity1 = CreateTestEntity();
        var entity2 = CreateTestEntity();
        var entity3 = CreateTestEntity();
        await _graphStore.StoreEntitiesBatchAsync([entity1, entity2, entity3]);

        var rel1 = CreateTestRelationship(entity1.Id, entity2.Id);
        var rel2 = CreateTestRelationship(entity1.Id, entity3.Id);
        await _graphStore.StoreRelationshipsBatchAsync([rel1, rel2]);

        // Act
        var results = await _graphStore.GetRelationshipsAsync(entity1.Id, TraversalDirection.Outgoing);

        // Assert
        results.Should().HaveCount(2);
    }

    [SkippableFact]
    public async Task GetRelationshipsByTypeAsync_ValidType_ReturnsMatchingRelationships()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        await _graphStore.ClearAsync();
        var entities = Enumerable.Range(0, 4)
            .Select(_ => CreateTestEntity())
            .ToList();
        await _graphStore.StoreEntitiesBatchAsync(entities);

        var relatedToRel = CreateTestRelationship(entities[0].Id, entities[1].Id, RelationType.RelatedTo);
        var worksForRel = CreateTestRelationship(entities[2].Id, entities[3].Id, RelationType.WorksFor);
        await _graphStore.StoreRelationshipsBatchAsync([relatedToRel, worksForRel]);

        // Act
        var results = await _graphStore.GetRelationshipsByTypeAsync(RelationType.WorksFor);

        // Assert
        results.Should().HaveCount(1);
        results[0].Type.Should().Be(RelationType.WorksFor);
    }

    [SkippableFact]
    public async Task DeleteRelationshipAsync_ExistingRelationship_RemovesRelationship()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var entity1 = CreateTestEntity();
        var entity2 = CreateTestEntity();
        await _graphStore.StoreEntitiesBatchAsync([entity1, entity2]);

        var relationship = CreateTestRelationship(entity1.Id, entity2.Id);
        await _graphStore.StoreRelationshipAsync(relationship);

        // Act
        var result = await _graphStore.DeleteRelationshipAsync(relationship.Id);

        // Assert
        result.Should().BeTrue();

        var relationships = await _graphStore.GetRelationshipsAsync(entity1.Id);
        relationships.Should().BeEmpty();
    }

    #endregion

    #region Traversal Operations

    [SkippableFact]
    public async Task TraverseAsync_ValidStartEntity_ReturnsConnectedEntities()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange - Create a simple graph: A -> B -> C
        var entityA = CreateTestEntity(name: "EntityA");
        var entityB = CreateTestEntity(name: "EntityB");
        var entityC = CreateTestEntity(name: "EntityC");
        await _graphStore.StoreEntitiesBatchAsync([entityA, entityB, entityC]);

        var relAB = CreateTestRelationship(entityA.Id, entityB.Id);
        var relBC = CreateTestRelationship(entityB.Id, entityC.Id);
        await _graphStore.StoreRelationshipsBatchAsync([relAB, relBC]);

        var options = new GraphStoreTraversalOptions
        {
            MaxDepth = 2,
            Direction = TraversalDirection.Outgoing
        };

        // Act
        var result = await _graphStore.TraverseAsync(entityA.Id, options);

        // Assert
        result.Should().NotBeNull();
        result.Entities.Should().HaveCountGreaterThanOrEqualTo(2); // B and C
    }

    [SkippableFact]
    public async Task FindShortestPathAsync_ConnectedEntities_ReturnsPath()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange - Create graph: A -> B -> C
        var entityA = CreateTestEntity(name: "PathA");
        var entityB = CreateTestEntity(name: "PathB");
        var entityC = CreateTestEntity(name: "PathC");
        await _graphStore.StoreEntitiesBatchAsync([entityA, entityB, entityC]);

        var relAB = CreateTestRelationship(entityA.Id, entityB.Id);
        var relBC = CreateTestRelationship(entityB.Id, entityC.Id);
        await _graphStore.StoreRelationshipsBatchAsync([relAB, relBC]);

        // Act
        var path = await _graphStore.FindShortestPathAsync(entityA.Id, entityC.Id, maxDepth: 5);

        // Assert
        path.Should().NotBeNull();
        path!.EntityIds.Should().HaveCount(3); // A -> B -> C
        path.Length.Should().Be(2);
    }

    [SkippableFact]
    public async Task FindShortestPathAsync_UnconnectedEntities_ReturnsNull()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange - Create two disconnected entities
        var entityA = CreateTestEntity(name: "DisconnectedA");
        var entityB = CreateTestEntity(name: "DisconnectedB");
        await _graphStore.StoreEntitiesBatchAsync([entityA, entityB]);

        // Act
        var path = await _graphStore.FindShortestPathAsync(entityA.Id, entityB.Id, maxDepth: 5);

        // Assert
        path.Should().BeNull();
    }

    [SkippableFact]
    public async Task GetNeighborsAsync_EntityWithNeighbors_ReturnsNeighbors()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var center = CreateTestEntity(name: "Center");
        var neighbor1 = CreateTestEntity(name: "Neighbor1");
        var neighbor2 = CreateTestEntity(name: "Neighbor2");
        await _graphStore.StoreEntitiesBatchAsync([center, neighbor1, neighbor2]);

        var rel1 = CreateTestRelationship(center.Id, neighbor1.Id);
        var rel2 = CreateTestRelationship(center.Id, neighbor2.Id);
        await _graphStore.StoreRelationshipsBatchAsync([rel1, rel2]);

        // Act
        var neighbors = await _graphStore.GetNeighborsAsync(center.Id, depth: 1);

        // Assert
        neighbors.Should().HaveCount(2);
    }

    #endregion

    #region Statistics Operations

    [SkippableFact]
    public async Task GetStatisticsAsync_AfterStoringData_ReturnsCorrectCounts()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        await _graphStore.ClearAsync();
        var entities = Enumerable.Range(0, 5)
            .Select(_ => CreateTestEntity())
            .ToList();
        await _graphStore.StoreEntitiesBatchAsync(entities);

        var rel = CreateTestRelationship(entities[0].Id, entities[1].Id);
        await _graphStore.StoreRelationshipAsync(rel);

        // Act
        var stats = await _graphStore.GetStatisticsAsync();

        // Assert
        stats.EntityCount.Should().Be(5);
        stats.RelationshipCount.Should().Be(1);
    }

    [SkippableFact]
    public async Task ClearAsync_WithExistingData_RemovesAllData()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var entities = Enumerable.Range(0, 3)
            .Select(_ => CreateTestEntity())
            .ToList();
        await _graphStore.StoreEntitiesBatchAsync(entities);

        // Act
        await _graphStore.ClearAsync();

        // Assert
        var stats = await _graphStore.GetStatisticsAsync();
        stats.EntityCount.Should().Be(0);
    }

    #endregion

    #region Community Operations

    [SkippableFact]
    public async Task StoreCommunityAsync_ValidCommunity_ReturnsCommunityId()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var entities = Enumerable.Range(0, 3)
            .Select(_ => CreateTestEntity())
            .ToList();
        await _graphStore.StoreEntitiesBatchAsync(entities);

        var community = new GraphCommunity
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test Community",
            Summary = "A test community for integration testing",
            EntityIds = entities.Select(e => e.Id).ToList(),
            Topics = ["testing", "integration"],
            ImportanceScore = 0.8,
            Level = 0
        };

        // Act
        var result = await _graphStore.StoreCommunityAsync(community);

        // Assert
        result.Should().Be(community.Id);
    }

    [SkippableFact]
    public async Task GetCommunityByIdAsync_ExistingCommunity_ReturnsCommunity()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var community = new GraphCommunity
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Retrievable Community",
            Summary = "A community to retrieve",
            EntityIds = [],
            Topics = ["test"],
            ImportanceScore = 0.5,
            Level = 0
        };
        await _graphStore.StoreCommunityAsync(community);

        // Act
        var result = await _graphStore.GetCommunityByIdAsync(community.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(community.Id);
        result.Name.Should().Be("Retrievable Community");
    }

    [SkippableFact]
    public async Task GetTopCommunitiesAsync_WithCommunities_ReturnsOrderedByImportance()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        await _graphStore.ClearAsync();
        var communities = new[]
        {
            new GraphCommunity { Id = Guid.NewGuid().ToString(), Name = "Low", ImportanceScore = 0.2, Level = 0 },
            new GraphCommunity { Id = Guid.NewGuid().ToString(), Name = "High", ImportanceScore = 0.9, Level = 0 },
            new GraphCommunity { Id = Guid.NewGuid().ToString(), Name = "Medium", ImportanceScore = 0.5, Level = 0 }
        };

        foreach (var community in communities)
        {
            await _graphStore.StoreCommunityAsync(community);
        }

        // Act
        var results = await _graphStore.GetTopCommunitiesAsync(limit: 3);

        // Assert
        results.Should().HaveCount(3);
        results[0].Name.Should().Be("High");
    }

    #endregion

    #region EntityGraphService Integration

    [SkippableFact]
    public async Task EntityGraphService_WithNeo4jStore_PersistsEntities()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        await _graphStore.ClearAsync();

        var entityGraphService = new EntityGraphService(
            entityExtractionService: null,
            embeddingService: null,
            graphStore: _graphStore,
            logger: NullLogger<EntityGraphService>.Instance);

        // Create manual entities for test
        var manualEntities = new[]
        {
            new EntityNode
            {
                Id = "entity-apple",
                Name = "Apple Inc.",
                NormalizedName = "apple inc",
                Type = NamedEntityType.Organization,
                Confidence = 0.95,
                ImportanceScore = 0.8,
                MentionCount = 1
            },
            new EntityNode
            {
                Id = "entity-steve",
                Name = "Steve Jobs",
                NormalizedName = "steve jobs",
                Type = NamedEntityType.Person,
                Confidence = 0.95,
                ImportanceScore = 0.9,
                MentionCount = 1
            }
        };

        var manualRelations = new[]
        {
            new EntityEdge
            {
                Id = "rel-1",
                SourceEntityId = "entity-steve",
                TargetEntityId = "entity-apple",
                RelationType = RelationType.FoundedBy, // Steve Jobs founded Apple
                Label = "founded",
                Confidence = 0.9,
                Weight = 1.0,
                IsDirectional = true,
                EvidenceChunkIds = ["chunk-1"]
            }
        };

        var manualGraph = new EntityGraphResult
        {
            Entities = manualEntities.ToList(),
            Relations = manualRelations.ToList(),
            ChunkMappings = new[]
            {
                new EntityChunkMapping { EntityId = "entity-apple", ChunkId = "chunk-1", RelevanceScore = 1.0 },
                new EntityChunkMapping { EntityId = "entity-steve", ChunkId = "chunk-1", RelevanceScore = 1.0 }
            }
        };

        // Act - Persist manual graph
        await entityGraphService.PersistGraphAsync(manualGraph);

        // Assert - Verify entities were stored in Neo4j
        var stats = await _graphStore.GetStatisticsAsync();
        stats.EntityCount.Should().BeGreaterThanOrEqualTo(2);
        stats.RelationshipCount.Should().BeGreaterThanOrEqualTo(1);

        // Verify specific entity
        var appleEntity = await _graphStore.GetEntityByIdAsync("entity-apple");
        appleEntity.Should().NotBeNull();
        appleEntity!.Name.Should().Be("Apple Inc.");
        appleEntity.Type.Should().Be(NamedEntityType.Organization);

        // Verify relationship
        var relationships = await _graphStore.GetRelationshipsAsync("entity-steve", TraversalDirection.Outgoing);
        relationships.Should().HaveCount(1);
        relationships[0].TargetEntityId.Should().Be("entity-apple");
    }

    #endregion
}

[CollectionDefinition("Neo4j")]
public class Neo4jCollection : ICollectionFixture<Neo4jFixture>
{
}

public class Neo4jFixture : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;
}
