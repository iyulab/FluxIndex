using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests;

public class HybridSearchServiceTests
{
    private readonly IVectorStore _mockVectorStore;
    private readonly IKeywordSearchService _mockKeywordSearchService;
    private readonly IEmbeddingService _mockEmbeddingService;
    private readonly ILogger<HybridSearchService> _logger;
    private readonly HybridSearchService _service;

    public HybridSearchServiceTests()
    {
        _mockVectorStore = Substitute.For<IVectorStore>();
        _mockKeywordSearchService = Substitute.For<IKeywordSearchService>();
        _mockEmbeddingService = Substitute.For<IEmbeddingService>();
        _logger = NullLogger<HybridSearchService>.Instance;

        _service = new HybridSearchService(
            _mockVectorStore,
            _mockKeywordSearchService,
            _mockEmbeddingService,
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

        var vectorChunks = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = "chunk1",
                Content = "Test content 1",
                DocumentId = "doc1",
                Metadata = new Dictionary<string, object> { ["title"] = "Test Doc 1" }
            },
            new DocumentChunk
            {
                Id = "chunk2",
                Content = "Test content 2",
                DocumentId = "doc2",
                Metadata = new Dictionary<string, object> { ["title"] = "Test Doc 2" }
            }
        };

        var sparseResults = new List<KeywordSearchResult>
        {
            new KeywordSearchResult
            {
                Chunk = new DocumentChunk
                {
                    Id = "chunk1",
                    Content = "Test content 1"
                },
                Score = 0.7,
                MatchedTerms = new[] { "test" }
            },
            new KeywordSearchResult
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

        _mockEmbeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>()).Returns(embedding);

        _mockVectorStore.SearchAsync(embedding, 10, 0.0f, Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>()).Returns(vectorChunks.AsEnumerable());

        _mockKeywordSearchService.SearchAsync(query, Arg.Any<KeywordSearchOptions>(), Arg.Any<CancellationToken>()).Returns(sparseResults);

        // Act
        var results = await _service.SearchAsync(query, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(results);
        Assert.True(results.Any());

        // Verify that embedding service was called
        await _mockEmbeddingService.Received(1).GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>());

        // Verify that both vector and sparse searches were called
        await _mockVectorStore.Received(1).SearchAsync(embedding, 10, 0.0f, Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>());
        await _mockKeywordSearchService.Received(1).SearchAsync(query, Arg.Any<KeywordSearchOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyResult()
    {
        // Arrange
        var query = "";
        var options = new HybridSearchOptions();

        // Act
        var results = await _service.SearchAsync(query, options, TestContext.Current.CancellationToken);

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

        _mockEmbeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>()).Returns(embedding);

        _mockVectorStore.SearchAsync(embedding, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<DocumentChunk>());

        _mockKeywordSearchService.SearchAsync(query, Arg.Any<KeywordSearchOptions>(), Arg.Any<CancellationToken>()).Returns(new List<KeywordSearchResult>());

        // Act
        var results = await _service.SearchAsync(query, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(results);
        await _mockVectorStore.Received(1).SearchAsync(embedding, 10, 0.0f, Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>());
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

        _mockEmbeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>()).Returns(embedding);

        _mockVectorStore.SearchAsync(embedding, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<DocumentChunk>());

        _mockKeywordSearchService.SearchAsync(query, Arg.Any<KeywordSearchOptions>(), Arg.Any<CancellationToken>()).Returns(new List<KeywordSearchResult>());

        // Act
        await _service.SearchAsync(query, options, TestContext.Current.CancellationToken);

        // Assert
        await _mockEmbeddingService.Received(1).GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>());
        await _mockVectorStore.Received(1).SearchAsync(embedding, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>());
        await _mockKeywordSearchService.Received(1).SearchAsync(query, Arg.Any<KeywordSearchOptions>(), Arg.Any<CancellationToken>());
    }
}