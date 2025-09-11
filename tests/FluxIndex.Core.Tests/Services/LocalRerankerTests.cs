using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Reranking;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

public class LocalRerankerTests
{
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;
    private readonly Mock<ILogger<LocalReranker>> _loggerMock;
    private readonly LocalReranker _reranker;

    public LocalRerankerTests()
    {
        _embeddingServiceMock = new Mock<IEmbeddingService>();
        _loggerMock = new Mock<ILogger<LocalReranker>>();
        _reranker = new LocalReranker(
            _embeddingServiceMock.Object,
            logger: _loggerMock.Object);
    }

    [Fact]
    public async Task RerankAsync_WithEmptyCandidates_ReturnsEmpty()
    {
        // Arrange
        var query = "test query";
        var candidates = new List<RetrievalCandidate>();

        // Act
        var results = await _reranker.RerankAsync(query, candidates);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task RerankAsync_WithSingleCandidate_ReturnsSingleResult()
    {
        // Arrange
        var query = "machine learning algorithms";
        var candidates = new List<RetrievalCandidate>
        {
            new RetrievalCandidate
            {
                Id = "1",
                Content = "Machine learning is a subset of artificial intelligence algorithms",
                InitialScore = 0.7f,
                InitialRank = 1
            }
        };

        // Act
        var results = await _reranker.RerankAsync(query, candidates);

        // Assert
        results.Should().HaveCount(1);
        var result = results.First();
        result.Id.Should().Be("1");
        result.NewRank.Should().Be(1);
        result.RerankScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RerankAsync_WithMultipleCandidates_ReranksCorrectly()
    {
        // Arrange
        var query = "machine learning tutorial";
        var candidates = new List<RetrievalCandidate>
        {
            new RetrievalCandidate
            {
                Id = "1",
                Content = "This is about cooking recipes",
                InitialScore = 0.8f,
                InitialRank = 1
            },
            new RetrievalCandidate
            {
                Id = "2", 
                Content = "Machine learning tutorial for beginners with step-by-step guide",
                InitialScore = 0.6f,
                InitialRank = 2
            },
            new RetrievalCandidate
            {
                Id = "3",
                Content = "Introduction to machine learning algorithms and tutorial examples",
                InitialScore = 0.5f,
                InitialRank = 3
            }
        };

        // Act
        var results = await _reranker.RerankAsync(query, candidates);

        // Assert
        results.Should().HaveCount(3);
        
        var resultList = results.ToList();
        
        // The result with better topic match should rank higher
        var topResult = resultList.First();
        topResult.Content.Should().Contain("tutorial");
        
        // Results should be ordered by rerank score
        var scores = resultList.Select(r => r.RerankScore).ToList();
        scores.Should().BeInDescendingOrder();
        
        // New ranks should be assigned correctly
        for (int i = 0; i < resultList.Count; i++)
        {
            resultList[i].NewRank.Should().Be(i + 1);
        }
    }

    [Fact]
    public async Task RerankAsync_WithScoreThreshold_FiltersResults()
    {
        // Arrange
        var query = "specific topic";
        var candidates = new List<RetrievalCandidate>
        {
            new RetrievalCandidate
            {
                Id = "1",
                Content = "Very specific topic discussion with detailed information",
                InitialScore = 0.8f,
                InitialRank = 1
            },
            new RetrievalCandidate
            {
                Id = "2",
                Content = "Completely unrelated content about cooking",
                InitialScore = 0.7f,
                InitialRank = 2
            }
        };

        var options = new RerankOptions
        {
            ScoreThreshold = 0.3f,
            TopN = 5
        };

        // Act
        var results = await _reranker.RerankAsync(query, candidates, options);

        // Assert
        results.Should().NotBeEmpty();
        results.All(r => r.RerankScore >= options.ScoreThreshold).Should().BeTrue();
    }

    [Fact]
    public async Task RerankAsync_WithExplanationEnabled_ProvidesExplanations()
    {
        // Arrange
        var query = "test";
        var candidates = new List<RetrievalCandidate>
        {
            new RetrievalCandidate
            {
                Id = "1",
                Content = "Test content for explanation",
                InitialScore = 0.5f,
                InitialRank = 1
            }
        };

        var options = new RerankOptions
        {
            IncludeExplanation = true
        };

        // Act
        var results = await _reranker.RerankAsync(query, candidates, options);

        // Assert
        var result = results.First();
        result.Explanation.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RerankAsync_WithEmbeddingService_UsesSemanticSimilarity()
    {
        // Arrange
        var query = "artificial intelligence";
        var queryEmbedding = new EmbeddingVector(new[] { 0.1f, 0.2f, 0.3f });
        var contentEmbedding = new EmbeddingVector(new[] { 0.15f, 0.25f, 0.35f });

        _embeddingServiceMock
            .Setup(s => s.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(queryEmbedding);

        _embeddingServiceMock
            .Setup(s => s.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentEmbedding);

        var candidates = new List<RetrievalCandidate>
        {
            new RetrievalCandidate
            {
                Id = "1",
                Content = "AI and machine intelligence research",
                InitialScore = 0.5f,
                InitialRank = 1
            }
        };

        // Act
        var results = await _reranker.RerankAsync(query, candidates);

        // Assert
        results.Should().HaveCount(1);
        _embeddingServiceMock.Verify(s => s.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()), Times.Once);
        _embeddingServiceMock.Verify(s => s.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task RerankAsync_WithTopNLimit_ReturnsLimitedResults()
    {
        // Arrange
        var query = "test";
        var candidates = Enumerable.Range(1, 10)
            .Select(i => new RetrievalCandidate
            {
                Id = i.ToString(),
                Content = $"Test content {i}",
                InitialScore = 0.5f + i * 0.01f,
                InitialRank = i
            })
            .ToList();

        var options = new RerankOptions
        {
            TopN = 3
        };

        // Act
        var results = await _reranker.RerankAsync(query, candidates, options);

        // Assert
        results.Should().HaveCount(3);
    }

    [Fact]
    public void GetModelInfo_ReturnsCorrectInformation()
    {
        // Act
        var modelInfo = _reranker.GetModelInfo();

        // Assert
        modelInfo.Should().NotBeNull();
        modelInfo.Name.Should().Be("Local Similarity Reranker");
        modelInfo.Type.Should().Be(RerankModel.Local);
        modelInfo.SupportsMultilingual.Should().BeTrue();
        modelInfo.RequiresApiKey.Should().BeFalse();
        modelInfo.Capabilities.Should().NotBeNull();
        modelInfo.Capabilities.Should().ContainKey("supports_tf_idf");
        modelInfo.Capabilities.Should().ContainKey("supports_bm25");
    }

    [Fact]
    public async Task RerankAsync_WithCustomOptions_AppliesConfiguration()
    {
        // Arrange
        var options = new LocalRerankOptions
        {
            TfIdfWeight = 0.5f,
            Bm25Weight = 0.3f,
            SemanticWeight = 0.2f,
            MaxContentLength = 256
        };

        var reranker = new LocalReranker(
            embeddingService: null,
            options: options,
            logger: _loggerMock.Object);

        var query = "test query";
        var candidates = new List<RetrievalCandidate>
        {
            new RetrievalCandidate
            {
                Id = "1",
                Content = new string('x', 1000), // Long content to test MaxContentLength
                InitialScore = 0.5f,
                InitialRank = 1
            }
        };

        // Act & Assert (should not throw)
        var results = await reranker.RerankAsync(query, candidates);
        results.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("machine learning", "machine learning algorithms", 0.5f)]
    [InlineData("AI", "artificial intelligence", 0.3f)]
    [InlineData("cooking", "programming tutorial", 0.1f)]
    public async Task RerankAsync_WithDifferentQuerySimilarities_ProducesExpectedRankings(
        string query, string content, float expectedMinScore)
    {
        // Arrange
        var candidates = new List<RetrievalCandidate>
        {
            new RetrievalCandidate
            {
                Id = "1",
                Content = content,
                InitialScore = 0.5f,
                InitialRank = 1
            }
        };

        // Act
        var results = await _reranker.RerankAsync(query, candidates);

        // Assert
        var result = results.First();
        if (expectedMinScore > 0.3f)
        {
            result.RerankScore.Should().BeGreaterThan(expectedMinScore);
        }
        else
        {
            result.RerankScore.Should().BeLessOrEqualTo(expectedMinScore * 2); // Some tolerance for local scoring
        }
    }
}