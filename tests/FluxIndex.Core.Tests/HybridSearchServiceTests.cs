using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services;
using FluxIndex.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using DomainEntities = FluxIndex.Domain.Entities;

namespace FluxIndex.Core.Tests;

public class HybridSearchServiceTests
{
    private readonly Mock<IVectorStore> _mockVectorStore;
    private readonly Mock<ISparseRetriever> _mockSparseRetriever;
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly ILogger<HybridSearchService> _logger;
    private readonly HybridSearchService _service;

    public HybridSearchServiceTests()
    {
        _mockVectorStore = new Mock<IVectorStore>();
        _mockSparseRetriever = new Mock<ISparseRetriever>();
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _logger = NullLogger<HybridSearchService>.Instance;

        _service = new HybridSearchService(
            _mockVectorStore.Object,
            _mockSparseRetriever.Object,
            _mockEmbeddingService.Object,
            _logger);
    }

    [Fact]
    public async Task SearchAsync_ValidQuery_ReturnsResults()
    {
        // Arrange
        var query = "test query";
        var options = new HybridSearchOptions
        {
            MaxResults = 5,
            VectorWeight = 0.7f,
            SparseWeight = 0.3f
        };

        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        var vectorChunks = new List<DomainEntities.DocumentChunk>
        {
            new DomainEntities.DocumentChunk
            {
                Id = "chunk1",
                Content = "Test content 1",
                DocumentId = "doc1",
                Metadata = new Dictionary<string, object> { ["title"] = "Test Doc 1" }
            },
            new DomainEntities.DocumentChunk
            {
                Id = "chunk2",
                Content = "Test content 2",
                DocumentId = "doc2",
                Metadata = new Dictionary<string, object> { ["title"] = "Test Doc 2" }
            }
        };

        var sparseResults = new List<SparseSearchResult>
        {
            new SparseSearchResult
            {
                Chunk = new DocumentChunk
                {
                    Id = "chunk1",
                    Content = "Test content 1"
                },
                Score = 0.7,
                MatchedTerms = new[] { "test" }
            },
            new SparseSearchResult
            {
                Chunk = new DocumentChunk
                {
                    Id = "chunk3",
                    Content = "Test content 3"
                },
                Score = 0.6,
                MatchedTerms = new[] { "query" }
            }
        };

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockVectorStore.Setup(x => x.SearchAsync(embedding, 10, 0.0f, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorChunks.AsEnumerable());

        _mockSparseRetriever.Setup(x => x.SearchAsync(query, It.IsAny<SparseSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sparseResults);

        // Act
        var results = await _service.SearchAsync(query, options);

        // Assert
        Assert.NotNull(results);
        Assert.True(results.Any());

        // Verify that embedding service was called
        _mockEmbeddingService.Verify(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()), Times.Once);

        // Verify that both vector and sparse searches were called
        _mockVectorStore.Verify(x => x.SearchAsync(embedding, 10, 0.0f, It.IsAny<CancellationToken>()), Times.Once);
        _mockSparseRetriever.Verify(x => x.SearchAsync(query, It.IsAny<SparseSearchOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyResult()
    {
        // Arrange
        var query = "";
        var options = new HybridSearchOptions();

        // Act
        var results = await _service.SearchAsync(query, options);

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_NullOptions_UsesDefaults()
    {
        // Arrange
        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockVectorStore.Setup(x => x.SearchAsync(embedding, It.IsAny<int>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<DomainEntities.DocumentChunk>());

        _mockSparseRetriever.Setup(x => x.SearchAsync(query, It.IsAny<SparseSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SparseSearchResult>());

        // Act
        var results = await _service.SearchAsync(query, null);

        // Assert
        Assert.NotNull(results);
        _mockVectorStore.Verify(x => x.SearchAsync(embedding, 10, 0.0f, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0.0f, 1.0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1.0f, 0.0f)]
    public async Task SearchAsync_DifferentWeights_CallsCorrectServices(float vectorWeight, float sparseWeight)
    {
        // Arrange
        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var options = new HybridSearchOptions
        {
            VectorWeight = vectorWeight,
            SparseWeight = sparseWeight
        };

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockVectorStore.Setup(x => x.SearchAsync(embedding, It.IsAny<int>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<DomainEntities.DocumentChunk>());

        _mockSparseRetriever.Setup(x => x.SearchAsync(query, It.IsAny<SparseSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SparseSearchResult>());

        // Act
        await _service.SearchAsync(query, options);

        // Assert
        _mockEmbeddingService.Verify(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()), Times.Once);
        _mockVectorStore.Verify(x => x.SearchAsync(embedding, It.IsAny<int>(), It.IsAny<float>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockSparseRetriever.Verify(x => x.SearchAsync(query, It.IsAny<SparseSearchOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}