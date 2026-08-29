using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Qdrant;
using Xunit;

namespace FluxIndex.Storage.Qdrant.Tests;

/// <summary>
/// Integration tests for QdrantVectorStore using Testcontainers.
/// These tests require Docker to be running.
/// </summary>
[Collection("Qdrant")]
[Trait("Category", "Integration")]
public class QdrantVectorStoreIntegrationTests : IAsyncLifetime
{
    private readonly QdrantContainer _container;
    private QdrantVectorStore _vectorStore = null!;
    private readonly ILogger<QdrantVectorStore> _logger;

    public QdrantVectorStoreIntegrationTests()
    {
        _container = new QdrantBuilder("qdrant/qdrant:latest")
            .Build();
        _logger = NullLogger<QdrantVectorStore>.Instance;
    }

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _container.StartAsync();

            var options = Options.Create(new QdrantOptions
            {
                Host = _container.Hostname,
                GrpcPort = _container.GetMappedPublicPort(6334),
                BaseCollectionName = $"test_{Guid.NewGuid():N}",
                VectorSize = 384,
                NamingStrategy = CollectionNamingStrategy.Fixed,
                CreateCollectionOnStartup = true
            });

            _vectorStore = new QdrantVectorStore(options, _logger);
        }
        catch (Exception)
        {
            // Docker not available - tests will be skipped
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_vectorStore != null)
        {
            await _vectorStore.DisposeAsync();
        }
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private bool IsDockerAvailable => _vectorStore != null;

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

    private DocumentChunk CreateTestChunk(string? id = null, float[]? embedding = null)
    {
        var chunk = new DocumentChunk
        {
            Id = id ?? Guid.NewGuid().ToString(),
            DocumentId = Guid.NewGuid().ToString(),
            Content = "This is test content for the chunk.",
            ChunkIndex = 0,
            TotalChunks = 1,
            TokenCount = 10,
            CreatedAt = DateTime.UtcNow
        };
        chunk.SetEmbedding(embedding ?? CreateTestEmbedding());
        return chunk;
    }

    [Fact]
    public async Task DeleteByFilterAsync_RemovesOnlyChunksMatchingAllMetadataFilters()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange - two chunks tagged desk=A, one tagged desk=B
        var a1 = CreateTestChunk();
        a1.Metadata!["desk"] = "A";
        var a2 = CreateTestChunk();
        a2.Metadata!["desk"] = "A";
        var b1 = CreateTestChunk();
        b1.Metadata!["desk"] = "B";

        a1.Id = await _vectorStore.StoreAsync(a1, TestContext.Current.CancellationToken);
        a2.Id = await _vectorStore.StoreAsync(a2, TestContext.Current.CancellationToken);
        b1.Id = await _vectorStore.StoreAsync(b1, TestContext.Current.CancellationToken);

        // Act - purge everything tagged desk=A in one call
        var deleted = await _vectorStore.DeleteByFilterAsync(new Dictionary<string, object> { ["desk"] = "A" }, TestContext.Current.CancellationToken);

        // Assert - only the two desk=A chunks removed, desk=B survives
        deleted.Should().Be(2);
        (await _vectorStore.GetByIdAsync(a1.Id, TestContext.Current.CancellationToken)).Should().BeNull();
        (await _vectorStore.GetByIdAsync(a2.Id, TestContext.Current.CancellationToken)).Should().BeNull();
        (await _vectorStore.GetByIdAsync(b1.Id, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteByFilterAsync_EmptyFilter_ThrowsRatherThanPurgingEverything()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        var act = async () => await _vectorStore.DeleteByFilterAsync(new Dictionary<string, object>());
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #region Store Operations

    [Fact]
    public async Task StoreAsync_SingleChunk_ReturnsChunkId()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();

        // Act
        var result = await _vectorStore.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(chunk.Id);
    }

    [Fact]
    public async Task StoreBatchAsync_MultipleChunks_ReturnsAllIds()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunks = Enumerable.Range(0, 5)
            .Select(_ => CreateTestChunk())
            .ToList();

        // Act
        var results = (await _vectorStore.StoreBatchAsync(chunks, TestContext.Current.CancellationToken)).ToList();

        // Assert
        results.Should().HaveCount(5);
        results.Should().BeEquivalentTo(chunks.Select(c => c.Id));
    }

    [Fact]
    public async Task StoreAsync_ChunkWithoutEmbedding_ThrowsArgumentException()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = Guid.NewGuid().ToString(),
            Content = "No embedding"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _vectorStore.StoreAsync(chunk, TestContext.Current.CancellationToken));
    }

    #endregion

    #region Retrieve Operations

    [Fact]
    public async Task GetByIdAsync_ExistingChunk_ReturnsChunk()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();
        await _vectorStore.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Act
        var result = await _vectorStore.GetByIdAsync(chunk.Id, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(chunk.Id);
        result.DocumentId.Should().Be(chunk.DocumentId);
        result.Content.Should().Be(chunk.Content);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingChunk_ReturnsNull()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Act
        var result = await _vectorStore.GetByIdAsync(Guid.NewGuid().ToString(), TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByDocumentIdAsync_MultipleChunks_ReturnsAllChunks()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var documentId = Guid.NewGuid().ToString();
        var chunks = Enumerable.Range(0, 3)
            .Select(i =>
            {
                var chunk = CreateTestChunk();
                chunk.DocumentId = documentId;
                chunk.ChunkIndex = i;
                chunk.TotalChunks = 3;
                return chunk;
            })
            .ToList();

        await _vectorStore.StoreBatchAsync(chunks, TestContext.Current.CancellationToken);

        // Act
        var results = (await _vectorStore.GetByDocumentIdAsync(documentId, TestContext.Current.CancellationToken)).ToList();

        // Assert
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.DocumentId.Should().Be(documentId));
    }

    #endregion

    #region Search Operations

    [Fact]
    public async Task SearchAsync_SimilarVector_ReturnsMatchingChunks()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var embedding = CreateTestEmbedding();
        var chunk = CreateTestChunk(embedding: embedding);
        await _vectorStore.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Store some other chunks with different embeddings (using different seeds)
        for (int i = 0; i < 5; i++)
        {
            var differentEmbedding = CreateTestEmbedding(seed: 100 + i);
            await _vectorStore.StoreAsync(CreateTestChunk(embedding: differentEmbedding), TestContext.Current.CancellationToken);
        }

        // Act - search with similar embedding
        var results = (await _vectorStore.SearchAsync(embedding, topK: 3, cancellationToken: TestContext.Current.CancellationToken)).ToList();

        // Assert
        results.Should().NotBeEmpty();
        results.First().Id.Should().Be(chunk.Id);
        results.First().Score.Should().BeGreaterThan(0.9f); // High similarity for same vector
    }

    [Fact]
    public async Task SearchAsync_WithMinScore_FiltersLowScoreResults()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var embedding = CreateTestEmbedding();
        var chunk = CreateTestChunk(embedding: embedding);
        await _vectorStore.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Act - search with high minimum score
        var results = (await _vectorStore.SearchAsync(embedding, topK: 10, minScore: 0.95f, cancellationToken: TestContext.Current.CancellationToken)).ToList();

        // Assert
        results.Should().AllSatisfy(r => r.Score.Should().BeGreaterThanOrEqualTo(0.95f));
    }

    #endregion

    #region Update/Delete Operations

    [Fact]
    public async Task UpdateAsync_ExistingChunk_UpdatesContent()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();
        await _vectorStore.StoreAsync(chunk, TestContext.Current.CancellationToken);

        chunk.Content = "Updated content";

        // Act
        var result = await _vectorStore.UpdateAsync(chunk, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeTrue();

        var retrieved = await _vectorStore.GetByIdAsync(chunk.Id, TestContext.Current.CancellationToken);
        retrieved!.Content.Should().Be("Updated content");
    }

    [Fact]
    public async Task DeleteAsync_ExistingChunk_RemovesChunk()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();
        await _vectorStore.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Act
        var result = await _vectorStore.DeleteAsync(chunk.Id, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeTrue();

        var retrieved = await _vectorStore.GetByIdAsync(chunk.Id, TestContext.Current.CancellationToken);
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteByDocumentIdAsync_MultipleChunks_RemovesAllChunks()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var documentId = Guid.NewGuid().ToString();
        var chunks = Enumerable.Range(0, 3)
            .Select(i =>
            {
                var chunk = CreateTestChunk();
                chunk.DocumentId = documentId;
                return chunk;
            })
            .ToList();

        await _vectorStore.StoreBatchAsync(chunks, TestContext.Current.CancellationToken);

        // Act
        var result = await _vectorStore.DeleteByDocumentIdAsync(documentId, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeTrue();

        var remaining = (await _vectorStore.GetByDocumentIdAsync(documentId, TestContext.Current.CancellationToken)).ToList();
        remaining.Should().BeEmpty();
    }

    #endregion

    #region Count/Clear Operations

    [Fact]
    public async Task CountAsync_AfterStoring_ReturnsCorrectCount()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunks = Enumerable.Range(0, 5)
            .Select(_ => CreateTestChunk())
            .ToList();
        await _vectorStore.StoreBatchAsync(chunks, TestContext.Current.CancellationToken);

        // Act
        var count = await _vectorStore.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        count.Should().Be(5);
    }

    [Fact]
    public async Task ExistsAsync_ExistingChunk_ReturnsTrue()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();
        await _vectorStore.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Act
        var exists = await _vectorStore.ExistsAsync(chunk.Id, TestContext.Current.CancellationToken);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistingChunk_ReturnsFalse()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Act
        var exists = await _vectorStore.ExistsAsync(Guid.NewGuid().ToString(), TestContext.Current.CancellationToken);

        // Assert
        exists.Should().BeFalse();
    }

    #endregion

    #region Properties/Metadata Tests

    [Fact]
    public async Task StoreAsync_WithProperties_PreservesProperties()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();
        chunk.AddProperty("source", "test-file.pdf");
        chunk.AddProperty("page", "42");

        await _vectorStore.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Act
        var result = await _vectorStore.GetByIdAsync(chunk.Id, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result!.Properties.Should().ContainKey("source");
        result.Properties["source"].Should().Be("test-file.pdf");
    }

    [Fact]
    public async Task StoreAsync_WithMetadata_PreservesMetadata()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();
        chunk.Metadata = new Dictionary<string, object>
        {
            ["author"] = "Test Author",
            ["version"] = "1.0"
        };

        await _vectorStore.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Act
        var result = await _vectorStore.GetByIdAsync(chunk.Id, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result!.Metadata.Should().ContainKey("author");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsChunkIndex_InMetadata()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();
        chunk.ChunkIndex = 5;
        chunk.TotalChunks = 10;
        chunk.TokenCount = 150;
        await _vectorStore.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Act
        var result = await _vectorStore.GetByIdAsync(chunk.Id, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result!.ChunkIndex.Should().Be(5);
        result.Metadata.Should().ContainKey("chunkIndex");
        result.Metadata["chunkIndex"].Should().Be(5);
        result.Metadata.Should().ContainKey("totalChunks");
        result.Metadata["totalChunks"].Should().Be(10);
        result.Metadata.Should().ContainKey("tokenCount");
        result.Metadata["tokenCount"].Should().Be(150);
    }

    [Fact]
    public async Task SearchAsync_ReturnsChunkIndex_InMetadata()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var embedding = CreateTestEmbedding();
        var chunk = CreateTestChunk(embedding: embedding);
        chunk.ChunkIndex = 8;
        chunk.TotalChunks = 15;
        chunk.TokenCount = 200;
        await _vectorStore.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Act
        var results = (await _vectorStore.SearchAsync(embedding, topK: 1, cancellationToken: TestContext.Current.CancellationToken)).ToList();

        // Assert
        results.Should().HaveCount(1);
        var result = results.First();
        result.ChunkIndex.Should().Be(8);
        result.Metadata.Should().ContainKey("chunkIndex");
        result.Metadata!["chunkIndex"].Should().Be(8);
        result.Metadata.Should().ContainKey("totalChunks");
        result.Metadata["totalChunks"].Should().Be(15);
        result.Metadata.Should().ContainKey("tokenCount");
        result.Metadata["tokenCount"].Should().Be(200);
    }

    #endregion

    #region Hybrid Search Tests

    [Fact]
    public async Task HybridSearch_VectorAndBM25_ReturnsFusedResults()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");

        // Arrange
        var bm25Logger = NullLogger<BM25SparseRetriever>.Instance;
        var bm25Retriever = new BM25SparseRetriever(bm25Logger);

        // Create test chunks with specific content for keyword matching
        var chunks = new[]
        {
            CreateTestChunk(id: "chunk-1"),
            CreateTestChunk(id: "chunk-2"),
            CreateTestChunk(id: "chunk-3")
        };

        // Modify content for BM25 testing
        chunks[0].Content = "Machine learning algorithms are used in artificial intelligence applications.";
        chunks[1].Content = "Deep learning is a subset of machine learning that uses neural networks.";
        chunks[2].Content = "Natural language processing enables computers to understand human language.";

        // Store chunks in vector store and BM25 index
        await _vectorStore.StoreBatchAsync(chunks, TestContext.Current.CancellationToken);
        foreach (var chunk in chunks)
        {
            await bm25Retriever.IndexChunkAsync(chunk, TestContext.Current.CancellationToken);
        }

        // Search using BM25 only
        var bm25Results = await bm25Retriever.SearchAsync("machine learning", new KeywordSearchOptions { MaxResults = 3 }, TestContext.Current.CancellationToken);

        // Assert
        bm25Results.Should().NotBeEmpty();
        bm25Results.Should().HaveCountGreaterThanOrEqualTo(1);

        // Verify that chunks with "machine learning" content are found
        var foundIds = bm25Results.Select(r => r.Chunk.Id).ToList();
        foundIds.Should().Contain("chunk-1"); // Contains "machine learning"
        foundIds.Should().Contain("chunk-2"); // Contains "machine learning"
    }

    #endregion
}

[CollectionDefinition("Qdrant")]
public class QdrantCollection : ICollectionFixture<QdrantFixture>
{
}

public class QdrantFixture : IAsyncLifetime
{
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }
}
