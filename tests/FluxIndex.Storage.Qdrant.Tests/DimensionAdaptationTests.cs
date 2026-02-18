using FluentAssertions;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Testcontainers.Qdrant;
using Xunit;

namespace FluxIndex.Storage.Qdrant.Tests;

/// <summary>
/// Tests for dynamic vector dimension adaptation in QdrantVectorStore.
/// Verifies that collections are automatically created with dimension suffixes.
/// </summary>
[Collection("Qdrant")]
[Trait("Category", "Integration")]
public class DimensionAdaptationTests : IAsyncLifetime
{
    private readonly QdrantContainer _container;
    private readonly ILogger<QdrantVectorStore> _logger;
    private QdrantClient? _qdrantClient;

    public DimensionAdaptationTests()
    {
        _container = new QdrantBuilder("qdrant/qdrant:latest")
            .Build();
        _logger = NullLogger<QdrantVectorStore>.Instance;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
            _qdrantClient = new QdrantClient(
                host: _container.Hostname,
                port: _container.GetMappedPublicPort(6334));
        }
        catch (Exception)
        {
            // Docker not available - tests will be skipped
        }
    }

    public async Task DisposeAsync()
    {
        _qdrantClient?.Dispose();
        await _container.DisposeAsync();
    }

    private bool IsDockerAvailable => _qdrantClient != null;

    private QdrantVectorStore CreateVectorStore(
        string baseCollectionName,
        CollectionNamingStrategy strategy = CollectionNamingStrategy.DimensionSuffix,
        int vectorSize = 1536)
    {
        var options = Options.Create(new QdrantOptions
        {
            Host = _container.Hostname,
            GrpcPort = _container.GetMappedPublicPort(6334),
            BaseCollectionName = baseCollectionName,
            NamingStrategy = strategy,
            VectorSize = vectorSize,
            CreateCollectionOnStartup = true
        });

        return new QdrantVectorStore(options, _logger);
    }

    private static float[] CreateTestEmbedding(int dimension, int seed = 42)
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

    private static DocumentChunk CreateTestChunk(int dimension, int seed = 42)
    {
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = Guid.NewGuid().ToString(),
            Content = $"Test content with {dimension}-dimensional embedding.",
            ChunkIndex = 0,
            TotalChunks = 1,
            TokenCount = 10,
            CreatedAt = DateTime.UtcNow
        };
        chunk.SetEmbedding(CreateTestEmbedding(dimension, seed));
        return chunk;
    }

    #region DimensionSuffix Strategy Tests

    [SkippableFact]
    public async Task Store_WithDimensionSuffix_CreatesCorrectCollection()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var baseName = $"test_{Guid.NewGuid():N}";
        await using var vectorStore = CreateVectorStore(baseName, CollectionNamingStrategy.DimensionSuffix);
        var chunk = CreateTestChunk(384);

        // Act
        await vectorStore.StoreAsync(chunk);

        // Assert
        var resolvedName = vectorStore.GetResolvedCollectionName();
        resolvedName.Should().Be($"{baseName}_384");

        var detectedDim = vectorStore.GetDetectedDimension();
        detectedDim.Should().Be(384);

        // Verify collection exists in Qdrant
        var collections = await _qdrantClient!.ListCollectionsAsync();
        collections.Should().Contain($"{baseName}_384");
    }

    [SkippableFact]
    public async Task Store_DifferentDimensions_CreatesSeparateCollections()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var baseName = $"test_{Guid.NewGuid():N}";

        // Store 384-dim chunk
        await using var vectorStore384 = CreateVectorStore(baseName, CollectionNamingStrategy.DimensionSuffix);
        var chunk384 = CreateTestChunk(384);
        await vectorStore384.StoreAsync(chunk384);

        // Store 1024-dim chunk (new vector store instance)
        await using var vectorStore1024 = CreateVectorStore(baseName, CollectionNamingStrategy.DimensionSuffix);
        var chunk1024 = CreateTestChunk(1024, seed: 100);
        await vectorStore1024.StoreAsync(chunk1024);

        // Assert
        var collections = await _qdrantClient!.ListCollectionsAsync();
        collections.Should().Contain($"{baseName}_384");
        collections.Should().Contain($"{baseName}_1024");

        // Verify each collection has correct count
        var info384 = await _qdrantClient.GetCollectionInfoAsync($"{baseName}_384");
        info384.PointsCount.Should().Be(1);

        var info1024 = await _qdrantClient.GetCollectionInfoAsync($"{baseName}_1024");
        info1024.PointsCount.Should().Be(1);
    }

    [SkippableFact]
    public async Task Search_MatchesDimensionCollection()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var baseName = $"test_{Guid.NewGuid():N}";
        await using var vectorStore = CreateVectorStore(baseName, CollectionNamingStrategy.DimensionSuffix);

        var embedding = CreateTestEmbedding(384);
        var chunk = CreateTestChunk(384);
        chunk.SetEmbedding(embedding);
        await vectorStore.StoreAsync(chunk);

        // Act - search with same dimension
        var results = (await vectorStore.SearchAsync(embedding, topK: 5)).ToList();

        // Assert
        results.Should().HaveCount(1);
        results.First().Id.Should().Be(chunk.Id);
        results.First().Score.Should().BeGreaterThan(0.99f); // Nearly identical vector
    }

    [SkippableFact]
    public async Task StoreBatch_UsesFirstChunkDimension()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var baseName = $"test_{Guid.NewGuid():N}";
        await using var vectorStore = CreateVectorStore(baseName, CollectionNamingStrategy.DimensionSuffix);

        var chunks = Enumerable.Range(0, 5)
            .Select(i => CreateTestChunk(512, seed: 100 + i))
            .ToList();

        // Act
        var ids = (await vectorStore.StoreBatchAsync(chunks)).ToList();

        // Assert
        ids.Should().HaveCount(5);
        vectorStore.GetResolvedCollectionName().Should().Be($"{baseName}_512");
        vectorStore.GetDetectedDimension().Should().Be(512);
    }

    #endregion

    #region Fixed Strategy Tests (Legacy Compatibility)

    [SkippableFact]
    public async Task Store_WithFixedStrategy_UsesExactName()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var collectionName = $"fixed_test_{Guid.NewGuid():N}";
        await using var vectorStore = CreateVectorStore(collectionName, CollectionNamingStrategy.Fixed, vectorSize: 256);
        var chunk = CreateTestChunk(256);

        // Act
        await vectorStore.StoreAsync(chunk);

        // Assert
        var resolvedName = vectorStore.GetResolvedCollectionName();
        resolvedName.Should().Be(collectionName); // No dimension suffix

        // Verify collection exists with exact name
        var collections = await _qdrantClient!.ListCollectionsAsync();
        collections.Should().Contain(collectionName);
        collections.Should().NotContain($"{collectionName}_256");
    }

    [SkippableFact]
    public async Task Store_WithFixedStrategy_UsesConfiguredVectorSize()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var collectionName = $"fixed_vectorsize_{Guid.NewGuid():N}";
        await using var vectorStore = CreateVectorStore(collectionName, CollectionNamingStrategy.Fixed, vectorSize: 768);
        var chunk = CreateTestChunk(768);

        // Act
        await vectorStore.StoreAsync(chunk);

        // Assert - verify the collection exists and chunk was stored
        var collections = await _qdrantClient!.ListCollectionsAsync();
        collections.Should().Contain(collectionName);

        // Verify chunk count
        var info = await _qdrantClient.GetCollectionInfoAsync(collectionName);
        info.PointsCount.Should().Be(1);
    }

    #endregion

    #region Collection Name Accessors Tests

    [SkippableFact]
    public async Task GetResolvedCollectionName_BeforeStore_ReturnsNull()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var baseName = $"test_{Guid.NewGuid():N}";
        await using var vectorStore = CreateVectorStore(baseName);

        // Assert
        vectorStore.GetResolvedCollectionName().Should().BeNull();
        vectorStore.GetDetectedDimension().Should().BeNull();
    }

    [SkippableFact]
    public async Task GetDetectedDimension_AfterStore_ReturnsCorrectValue()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var baseName = $"test_{Guid.NewGuid():N}";
        await using var vectorStore = CreateVectorStore(baseName);
        var chunk = CreateTestChunk(1536);

        // Act
        await vectorStore.StoreAsync(chunk);

        // Assert
        vectorStore.GetDetectedDimension().Should().Be(1536);
    }

    #endregion

    #region Thread Safety Tests

    [SkippableFact]
    public async Task ConcurrentStores_SameDimension_ThreadSafe()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var baseName = $"concurrent_{Guid.NewGuid():N}";
        await using var vectorStore = CreateVectorStore(baseName);

        var tasks = Enumerable.Range(0, 10)
            .Select(i => Task.Run(async () =>
            {
                var chunk = CreateTestChunk(384, seed: 1000 + i);
                await vectorStore.StoreAsync(chunk);
                return chunk.Id;
            }));

        // Act
        var ids = await Task.WhenAll(tasks);

        // Assert
        ids.Should().HaveCount(10);
        ids.Distinct().Should().HaveCount(10); // All unique IDs

        var count = await vectorStore.CountAsync();
        count.Should().Be(10);

        vectorStore.GetResolvedCollectionName().Should().Be($"{baseName}_384");
    }

    #endregion

    #region Clear/Reset Tests

    [SkippableFact]
    public async Task Clear_ResetsCollectionState()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var baseName = $"clear_{Guid.NewGuid():N}";
        await using var vectorStore = CreateVectorStore(baseName);
        var chunk = CreateTestChunk(384);
        await vectorStore.StoreAsync(chunk);

        // Verify initial state
        vectorStore.GetResolvedCollectionName().Should().Be($"{baseName}_384");
        (await vectorStore.CountAsync()).Should().Be(1);

        // Act
        await vectorStore.ClearAsync();

        // Assert
        // After clear, the collection should be recreated
        (await vectorStore.CountAsync()).Should().Be(0);
    }

    #endregion
}
