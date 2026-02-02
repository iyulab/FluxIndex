using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.Neo4j;
using FluxIndex.Storage.Qdrant;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Neo4j;
using Testcontainers.Qdrant;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// End-to-end integration tests for GraphRAG pipeline with Neo4j and Qdrant.
/// These tests require Docker to be running.
/// </summary>
[Collection("GraphRAG")]
public class GraphRAGEndToEndTests : IAsyncLifetime
{
    private readonly Neo4jContainer _neo4jContainer;
    private readonly QdrantContainer _qdrantContainer;
    private Neo4jGraphStore? _graphStore;
    private QdrantVectorStore? _vectorStore;

    public GraphRAGEndToEndTests()
    {
        _neo4jContainer = new Neo4jBuilder()
            .WithImage("neo4j:5-community")
            .Build();

        _qdrantContainer = new QdrantBuilder()
            .WithImage("qdrant/qdrant:latest")
            .Build();
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Start containers in parallel
            await Task.WhenAll(
                _neo4jContainer.StartAsync(),
                _qdrantContainer.StartAsync()
            );

            // Initialize Neo4j graph store
            var neo4jOptions = Options.Create(new Neo4jOptions
            {
                Uri = _neo4jContainer.GetConnectionString(),
                Username = "neo4j",
                Password = "neo4j",
                Database = "neo4j"
            });
            _graphStore = new Neo4jGraphStore(neo4jOptions, NullLogger<Neo4jGraphStore>.Instance);

            // Initialize Qdrant vector store (Fixed strategy for explicit dimension control)
            var qdrantOptions = Options.Create(new QdrantOptions
            {
                Host = _qdrantContainer.Hostname,
                GrpcPort = _qdrantContainer.GetMappedPublicPort(6334),
                BaseCollectionName = $"graphrag_test_{Guid.NewGuid():N}",
                VectorSize = 384,
                NamingStrategy = CollectionNamingStrategy.Fixed,
                CreateCollectionOnStartup = true
            });
            _vectorStore = new QdrantVectorStore(qdrantOptions, NullLogger<QdrantVectorStore>.Instance);
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
        if (_vectorStore != null)
        {
            await _vectorStore.DisposeAsync();
        }
        await Task.WhenAll(
            _neo4jContainer.DisposeAsync().AsTask(),
            _qdrantContainer.DisposeAsync().AsTask()
        );
    }

    private bool IsDockerAvailable => _graphStore != null && _vectorStore != null;

    private float[] CreateTestEmbedding(int dimension = 384, int seed = 42)
    {
        var random = new Random(seed);
        var embedding = new float[dimension];
        for (int i = 0; i < dimension; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }
        // Normalize
        var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
        for (int i = 0; i < dimension; i++)
        {
            embedding[i] /= magnitude;
        }
        return embedding;
    }

    [SkippableFact]
    public async Task FullPipeline_WithNeo4jAndQdrant_StoresAndRetrievesData()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange - Create test document chunks (using GUIDs for Qdrant compatibility)
        var chunkId1 = Guid.NewGuid().ToString();
        var chunkId2 = Guid.NewGuid().ToString();
        var chunkId3 = Guid.NewGuid().ToString();
        var docId = Guid.NewGuid().ToString();

        var chunks = new[]
        {
            new DocumentChunk
            {
                Id = chunkId1,
                DocumentId = docId,
                Content = "OpenAI developed GPT-4, a large language model. Microsoft invested heavily in OpenAI.",
                ChunkIndex = 0,
                TotalChunks = 3
            },
            new DocumentChunk
            {
                Id = chunkId2,
                DocumentId = docId,
                Content = "Google created BERT and later developed Gemini for multimodal AI applications.",
                ChunkIndex = 1,
                TotalChunks = 3
            },
            new DocumentChunk
            {
                Id = chunkId3,
                DocumentId = docId,
                Content = "Anthropic, founded by former OpenAI researchers, developed Claude AI assistant.",
                ChunkIndex = 2,
                TotalChunks = 3
            }
        };

        // Set embeddings
        for (int i = 0; i < chunks.Length; i++)
        {
            chunks[i].SetEmbedding(CreateTestEmbedding(seed: 42 + i));
        }

        // Act - Store chunks in Qdrant
        await _vectorStore!.StoreBatchAsync(chunks);

        // Store entities in Neo4j
        var entities = new[]
        {
            new GraphEntity
            {
                Id = "entity-openai",
                Name = "OpenAI",
                NormalizedName = "openai",
                Type = NamedEntityType.Organization,
                Confidence = 0.95,
                ImportanceScore = 0.9,
                MentionCount = 2
            },
            new GraphEntity
            {
                Id = "entity-microsoft",
                Name = "Microsoft",
                NormalizedName = "microsoft",
                Type = NamedEntityType.Organization,
                Confidence = 0.95,
                ImportanceScore = 0.85,
                MentionCount = 1
            },
            new GraphEntity
            {
                Id = "entity-google",
                Name = "Google",
                NormalizedName = "google",
                Type = NamedEntityType.Organization,
                Confidence = 0.95,
                ImportanceScore = 0.85,
                MentionCount = 1
            },
            new GraphEntity
            {
                Id = "entity-anthropic",
                Name = "Anthropic",
                NormalizedName = "anthropic",
                Type = NamedEntityType.Organization,
                Confidence = 0.95,
                ImportanceScore = 0.8,
                MentionCount = 1
            }
        };

        await _graphStore!.StoreEntitiesBatchAsync(entities);

        // Store relationships
        var relationships = new[]
        {
            new GraphRelationship
            {
                Id = "rel-ms-openai",
                SourceEntityId = "entity-microsoft",
                TargetEntityId = "entity-openai",
                Type = RelationType.Owns, // Microsoft invested in/owns stake in OpenAI
                Label = "invested in",
                Confidence = 0.9,
                Weight = 1.0,
                IsDirectional = true
            },
            new GraphRelationship
            {
                Id = "rel-anthropic-openai",
                SourceEntityId = "entity-anthropic",
                TargetEntityId = "entity-openai",
                Type = RelationType.RelatedTo, // Anthropic is related to OpenAI (founded by former employees)
                Label = "founded by former employees of",
                Confidence = 0.85,
                Weight = 0.8,
                IsDirectional = true
            }
        };

        await _graphStore!.StoreRelationshipsBatchAsync(relationships);

        // Assert - Verify Qdrant storage
        var vectorCount = await _vectorStore!.CountAsync();
        vectorCount.Should().Be(3);

        // Verify Neo4j storage
        var graphStats = await _graphStore!.GetStatisticsAsync();
        graphStats.EntityCount.Should().Be(4);
        graphStats.RelationshipCount.Should().Be(2);

        // Verify vector search
        // Note: Each chunk has different embedding (seed: 42, 43, 44), so only similar ones are returned
        var searchEmbedding = CreateTestEmbedding(seed: 42); // Same as first chunk
        var searchResults = (await _vectorStore!.SearchAsync(searchEmbedding, topK: 3, minScore: 0.0f)).ToList();
        searchResults.Should().HaveCountGreaterThanOrEqualTo(1, "At least one result should be returned");
        searchResults.First().Id.Should().Be(chunkId1, "First chunk should be most similar (same seed)");

        // Verify graph traversal
        var traversalResult = await _graphStore!.TraverseAsync("entity-openai", new GraphStoreTraversalOptions
        {
            MaxDepth = 2,
            Direction = TraversalDirection.Incoming
        });
        traversalResult.Entities.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [SkippableFact]
    public async Task FluxIndexContext_WithNeo4jAndQdrant_BuildsSuccessfully()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange & Act - Build context with both Neo4j and Qdrant (Fixed strategy)
        var builder = FluxIndexContext.CreateBuilder()
            .UseQdrant(options =>
            {
                options.Host = _qdrantContainer.Hostname;
                options.GrpcPort = _qdrantContainer.GetMappedPublicPort(6334);
                options.BaseCollectionName = $"context_test_{Guid.NewGuid():N}";
                options.VectorSize = 384;
                options.NamingStrategy = CollectionNamingStrategy.Fixed;
                options.CreateCollectionOnStartup = true;
            })
            .UseNeo4j(
                uri: _neo4jContainer.GetConnectionString(),
                username: "neo4j",
                password: "neo4j",
                database: "neo4j");

        var context = builder.Build();

        // Assert
        context.Should().NotBeNull();

        // Verify services are registered
        var vectorStore = context.ServiceProvider.GetService<IVectorStore>();
        vectorStore.Should().NotBeNull();

        var graphStore = context.ServiceProvider.GetService<IGraphStore>();
        graphStore.Should().NotBeNull();

        // Cleanup - IFluxIndexContext doesn't have DisposeAsync, dispose through ServiceProvider if it implements IAsyncDisposable
        if (context is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
    }
}

[CollectionDefinition("GraphRAG")]
public class GraphRAGCollection : ICollectionFixture<GraphRAGFixture>
{
}

public class GraphRAGFixture : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;
}
