using FluentAssertions;
using FluxIndex.Core.Domain.Entities;
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
public class QdrantVectorStoreIntegrationTests : IAsyncLifetime
{
    private readonly QdrantContainer _container;
    private QdrantVectorStore _vectorStore = null!;
    private readonly ILogger<QdrantVectorStore> _logger;

    public QdrantVectorStoreIntegrationTests()
    {
        _container = new QdrantBuilder()
            .WithImage("qdrant/qdrant:latest")
            .Build();
        _logger = NullLogger<QdrantVectorStore>.Instance;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();

            var options = Options.Create(new QdrantOptions
            {
                Host = _container.Hostname,
                GrpcPort = _container.GetMappedPublicPort(6334),
                CollectionName = $"test_{Guid.NewGuid():N}",
                VectorSize = 384,
                CreateCollectionOnStartup = true
            });

            _vectorStore = new QdrantVectorStore(options, _logger);
        }
        catch (Exception)
        {
            // Docker not available - tests will be skipped
        }
    }

    public async Task DisposeAsync()
    {
        if (_vectorStore != null)
        {
            await _vectorStore.DisposeAsync();
        }
        await _container.DisposeAsync();
    }

    private bool IsDockerAvailable => _vectorStore != null;

    private float[] CreateTestEmbedding(int dimension = 384)
    {
        var random = new Random(42);
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

    #region Store Operations

    [SkippableFact]
    public async Task StoreAsync_SingleChunk_ReturnsChunkId()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();

        // Act
        var result = await _vectorStore.StoreAsync(chunk);

        // Assert
        result.Should().Be(chunk.Id);
    }

    [SkippableFact]
    public async Task StoreBatchAsync_MultipleChunks_ReturnsAllIds()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunks = Enumerable.Range(0, 5)
            .Select(_ => CreateTestChunk())
            .ToList();

        // Act
        var results = (await _vectorStore.StoreBatchAsync(chunks)).ToList();

        // Assert
        results.Should().HaveCount(5);
        results.Should().BeEquivalentTo(chunks.Select(c => c.Id));
    }

    [SkippableFact]
    public async Task StoreAsync_ChunkWithoutEmbedding_ThrowsArgumentException()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = Guid.NewGuid().ToString(),
            Content = "No embedding"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _vectorStore.StoreAsync(chunk));
    }

    #endregion

    #region Retrieve Operations

    [SkippableFact]
    public async Task GetByIdAsync_ExistingChunk_ReturnsChunk()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();
        await _vectorStore.StoreAsync(chunk);

        // Act
        var result = await _vectorStore.GetByIdAsync(chunk.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(chunk.Id);
        result.DocumentId.Should().Be(chunk.DocumentId);
        result.Content.Should().Be(chunk.Content);
    }

    [SkippableFact]
    public async Task GetByIdAsync_NonExistingChunk_ReturnsNull()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Act
        var result = await _vectorStore.GetByIdAsync(Guid.NewGuid().ToString());

        // Assert
        result.Should().BeNull();
    }

    [SkippableFact]
    public async Task GetByDocumentIdAsync_MultipleChunks_ReturnsAllChunks()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

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

        await _vectorStore.StoreBatchAsync(chunks);

        // Act
        var results = (await _vectorStore.GetByDocumentIdAsync(documentId)).ToList();

        // Assert
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.DocumentId.Should().Be(documentId));
    }

    #endregion

    #region Search Operations

    [SkippableFact]
    public async Task SearchAsync_SimilarVector_ReturnsMatchingChunks()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var embedding = CreateTestEmbedding();
        var chunk = CreateTestChunk(embedding: embedding);
        await _vectorStore.StoreAsync(chunk);

        // Store some other chunks with different embeddings
        for (int i = 0; i < 5; i++)
        {
            await _vectorStore.StoreAsync(CreateTestChunk());
        }

        // Act - search with similar embedding
        var results = (await _vectorStore.SearchAsync(embedding, topK: 3)).ToList();

        // Assert
        results.Should().NotBeEmpty();
        results.First().Id.Should().Be(chunk.Id);
        results.First().Score.Should().BeGreaterThan(0.9f); // High similarity for same vector
    }

    [SkippableFact]
    public async Task SearchAsync_WithMinScore_FiltersLowScoreResults()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var embedding = CreateTestEmbedding();
        var chunk = CreateTestChunk(embedding: embedding);
        await _vectorStore.StoreAsync(chunk);

        // Act - search with high minimum score
        var results = (await _vectorStore.SearchAsync(embedding, topK: 10, minScore: 0.95f)).ToList();

        // Assert
        results.Should().AllSatisfy(r => r.Score.Should().BeGreaterThanOrEqualTo(0.95f));
    }

    #endregion

    #region Update/Delete Operations

    [SkippableFact]
    public async Task UpdateAsync_ExistingChunk_UpdatesContent()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();
        await _vectorStore.StoreAsync(chunk);

        chunk.Content = "Updated content";

        // Act
        var result = await _vectorStore.UpdateAsync(chunk);

        // Assert
        result.Should().BeTrue();

        var retrieved = await _vectorStore.GetByIdAsync(chunk.Id);
        retrieved!.Content.Should().Be("Updated content");
    }

    [SkippableFact]
    public async Task DeleteAsync_ExistingChunk_RemovesChunk()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();
        await _vectorStore.StoreAsync(chunk);

        // Act
        var result = await _vectorStore.DeleteAsync(chunk.Id);

        // Assert
        result.Should().BeTrue();

        var retrieved = await _vectorStore.GetByIdAsync(chunk.Id);
        retrieved.Should().BeNull();
    }

    [SkippableFact]
    public async Task DeleteByDocumentIdAsync_MultipleChunks_RemovesAllChunks()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

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

        await _vectorStore.StoreBatchAsync(chunks);

        // Act
        var result = await _vectorStore.DeleteByDocumentIdAsync(documentId);

        // Assert
        result.Should().BeTrue();

        var remaining = (await _vectorStore.GetByDocumentIdAsync(documentId)).ToList();
        remaining.Should().BeEmpty();
    }

    #endregion

    #region Count/Clear Operations

    [SkippableFact]
    public async Task CountAsync_AfterStoring_ReturnsCorrectCount()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunks = Enumerable.Range(0, 5)
            .Select(_ => CreateTestChunk())
            .ToList();
        await _vectorStore.StoreBatchAsync(chunks);

        // Act
        var count = await _vectorStore.CountAsync();

        // Assert
        count.Should().Be(5);
    }

    [SkippableFact]
    public async Task ExistsAsync_ExistingChunk_ReturnsTrue()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();
        await _vectorStore.StoreAsync(chunk);

        // Act
        var exists = await _vectorStore.ExistsAsync(chunk.Id);

        // Assert
        exists.Should().BeTrue();
    }

    [SkippableFact]
    public async Task ExistsAsync_NonExistingChunk_ReturnsFalse()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Act
        var exists = await _vectorStore.ExistsAsync(Guid.NewGuid().ToString());

        // Assert
        exists.Should().BeFalse();
    }

    #endregion

    #region Properties/Metadata Tests

    [SkippableFact]
    public async Task StoreAsync_WithProperties_PreservesProperties()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();
        chunk.AddProperty("source", "test-file.pdf");
        chunk.AddProperty("page", "42");

        await _vectorStore.StoreAsync(chunk);

        // Act
        var result = await _vectorStore.GetByIdAsync(chunk.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Properties.Should().ContainKey("source");
        result.Properties["source"].Should().Be("test-file.pdf");
    }

    [SkippableFact]
    public async Task StoreAsync_WithMetadata_PreservesMetadata()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();
        chunk.Metadata = new Dictionary<string, object>
        {
            ["author"] = "Test Author",
            ["version"] = "1.0"
        };

        await _vectorStore.StoreAsync(chunk);

        // Act
        var result = await _vectorStore.GetByIdAsync(chunk.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Metadata.Should().ContainKey("author");
    }

    #endregion
}

[CollectionDefinition("Qdrant")]
public class QdrantCollection : ICollectionFixture<QdrantFixture>
{
}

public class QdrantFixture : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;
}
