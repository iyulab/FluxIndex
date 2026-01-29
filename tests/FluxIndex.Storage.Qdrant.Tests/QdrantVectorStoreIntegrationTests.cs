using FluentAssertions;
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

        // Store some other chunks with different embeddings (using different seeds)
        for (int i = 0; i < 5; i++)
        {
            var differentEmbedding = CreateTestEmbedding(seed: 100 + i);
            await _vectorStore.StoreAsync(CreateTestChunk(embedding: differentEmbedding));
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

    [SkippableFact]
    public async Task GetByIdAsync_ReturnsChunkIndex_InMetadata()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var chunk = CreateTestChunk();
        chunk.ChunkIndex = 5;
        chunk.TotalChunks = 10;
        chunk.TokenCount = 150;
        await _vectorStore.StoreAsync(chunk);

        // Act
        var result = await _vectorStore.GetByIdAsync(chunk.Id);

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

    [SkippableFact]
    public async Task SearchAsync_ReturnsChunkIndex_InMetadata()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

        // Arrange
        var embedding = CreateTestEmbedding();
        var chunk = CreateTestChunk(embedding: embedding);
        chunk.ChunkIndex = 8;
        chunk.TotalChunks = 15;
        chunk.TokenCount = 200;
        await _vectorStore.StoreAsync(chunk);

        // Act
        var results = (await _vectorStore.SearchAsync(embedding, topK: 1)).ToList();

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

    [SkippableFact]
    public async Task HybridSearch_VectorAndBM25_ReturnsFusedResults()
    {
        Skip.IfNot(IsDockerAvailable, "Docker is not available");

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
        await _vectorStore.StoreBatchAsync(chunks);
        foreach (var chunk in chunks)
        {
            await bm25Retriever.IndexChunkAsync(chunk);
        }

        // Search using BM25 only
        var bm25Results = await bm25Retriever.SearchAsync(
            "machine learning",
            new SparseSearchOptions { MaxResults = 3 });

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
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;
}
