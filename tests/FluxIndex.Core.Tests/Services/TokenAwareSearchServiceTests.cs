using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

// Use types from Models namespace for token-aware search
using QueryAnalysis = FluxIndex.Core.Application.Models.QueryAnalysis;
using QueryIntent = FluxIndex.Core.Application.Models.QueryIntent;
using QueryComplexityLevel = FluxIndex.Core.Application.Models.QueryComplexityLevel;
using SearchStrategy = FluxIndex.Core.Application.Models.SearchStrategy;
using EntityType = FluxIndex.Core.Application.Models.EntityType;

namespace FluxIndex.Core.Tests.Services;

public class TokenAwareSearchServiceTests
{
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;
    private readonly Mock<ILogger<QueryAnalysisService>> _queryLoggerMock;
    private readonly Mock<ILogger<TokenAwareSearchService>> _searchLoggerMock;
    private readonly ITokenCounter _tokenCounter;
    private readonly IQueryAnalysisService _queryAnalysisService;

    public TokenAwareSearchServiceTests()
    {
        _vectorStoreMock = new Mock<IVectorStore>();
        _embeddingServiceMock = new Mock<IEmbeddingService>();
        _queryLoggerMock = new Mock<ILogger<QueryAnalysisService>>();
        _searchLoggerMock = new Mock<ILogger<TokenAwareSearchService>>();
        _tokenCounter = new SimpleTokenCounter();
        _queryAnalysisService = new QueryAnalysisService(_tokenCounter, _queryLoggerMock.Object);
    }

    #region QueryAnalysisService Tests

    [Theory]
    [InlineData("how to implement RAG", QueryIntent.HowTo)]
    [InlineData("RAG 구현 방법", QueryIntent.HowTo)]
    [InlineData("React vs Vue", QueryIntent.Comparison)]
    [InlineData("what is machine learning", QueryIntent.Definition)]
    [InlineData("fix authentication error", QueryIntent.Troubleshooting)]
    public async Task AnalyzeAsync_DetectsIntent(string query, QueryIntent expectedIntent)
    {
        // Act
        var result = await _queryAnalysisService.AnalyzeAsync(query);

        // Assert
        Assert.Equal(expectedIntent, result.Intent);
    }

    [Theory]
    [InlineData("AI", QueryComplexityLevel.Simple)]
    [InlineData("machine learning basics", QueryComplexityLevel.Moderate)]
    [InlineData("how to implement distributed caching with Redis", QueryComplexityLevel.Complex)]
    public async Task AnalyzeAsync_DetectsComplexity(string query, QueryComplexityLevel expectedComplexity)
    {
        // Act
        var result = await _queryAnalysisService.AnalyzeAsync(query);

        // Assert
        Assert.Equal(expectedComplexity, result.Complexity);
    }

    [Theory]
    [InlineData("한글 텍스트입니다", "ko")]
    [InlineData("This is English text", "en")]
    [InlineData("RAG 시스템 구현", "ko")]
    public async Task AnalyzeAsync_DetectsLanguage(string query, string expectedLanguage)
    {
        // Act
        var result = await _queryAnalysisService.AnalyzeAsync(query);

        // Assert
        Assert.Equal(expectedLanguage, result.Language);
    }

    [Fact]
    public async Task AnalyzeAsync_ExtractsKeywords()
    {
        // Arrange
        var query = "implement vector search with PostgreSQL";

        // Act
        var result = await _queryAnalysisService.AnalyzeAsync(query);

        // Assert
        Assert.Contains("implement", result.Keywords);
        Assert.Contains("vector", result.Keywords);
        Assert.Contains("search", result.Keywords);
        Assert.Contains("postgresql", result.Keywords);
    }

    [Fact]
    public async Task AnalyzeAsync_ExtractsTechEntities()
    {
        // Arrange
        var query = "How to use RAG with GPT and PostgreSQL";

        // Act
        var result = await _queryAnalysisService.AnalyzeAsync(query);

        // Assert
        Assert.Contains(result.Entities, e => e.Text == "RAG");
        Assert.Contains(result.Entities, e => e.Text == "GPT");
        Assert.Contains(result.Entities, e => e.Text == "PostgreSQL");
        Assert.All(result.Entities, e => Assert.Equal(EntityType.Technology, e.Type));
    }

    [Fact]
    public async Task AnalyzeAsync_RecommendsStrategy()
    {
        // Arrange
        var query = "RAG implementation with vector database";

        // Act
        var result = await _queryAnalysisService.AnalyzeAsync(query);

        // Assert
        Assert.True(result.RecommendedStrategy == SearchStrategy.Hybrid ||
                   result.RecommendedStrategy == SearchStrategy.Vector);
    }

    #endregion

    #region TokenAwareSearchService Tests

    [Fact]
    public async Task SearchAsync_ReturnsChunksWithinBudget()
    {
        // Arrange
        var service = CreateSearchService();
        SetupMocks(GenerateTestChunks(10, 500)); // 10 chunks, 500 tokens each

        // Act
        var result = await service.SearchAsync("test query", 2000);

        // Assert
        Assert.True(result.UsedTokens <= 2000);
        Assert.Equal(2000, result.RequestedTokens);
        Assert.True(result.Chunks.Count > 0);
        Assert.True(result.Chunks.Count <= 4); // At most 4 chunks of 500 tokens
    }

    [Fact]
    public async Task SearchAsync_RespectsMinScore()
    {
        // Arrange
        var service = CreateSearchService();
        var chunks = GenerateTestChunks(10, 100);
        // Set some low scores
        chunks[5].Score = 0.3;
        chunks[6].Score = 0.2;
        chunks[7].Score = 0.1;
        SetupMocks(chunks);

        var request = new TokenAwareSearchRequest
        {
            Query = "test query",
            MaxTokens = 10000,
            MinScore = 0.5
        };

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        Assert.All(result.Chunks, c => Assert.True(c.Score >= 0.5));
    }

    [Fact]
    public async Task SearchAsync_IncludesQueryAnalysis()
    {
        // Arrange
        var service = CreateSearchService();
        SetupMocks(GenerateTestChunks(5, 100));

        // Act
        var result = await service.SearchAsync("how to implement RAG", 1000);

        // Assert
        Assert.NotNull(result.Analysis);
        Assert.Equal("how to implement RAG", result.Analysis.OriginalQuery);
        Assert.Equal(QueryIntent.HowTo, result.Analysis.Intent);
    }

    [Fact]
    public async Task SearchAsync_CalculatesCumulativeTokens()
    {
        // Arrange
        var service = CreateSearchService();
        SetupMocks(GenerateTestChunks(5, 200));

        // Act
        var result = await service.SearchAsync("test query", 1000);

        // Assert
        int cumulative = 0;
        foreach (var chunk in result.Chunks)
        {
            cumulative += chunk.TokenCount;
            Assert.Equal(cumulative, chunk.CumulativeTokens);
        }
    }

    [Fact]
    public async Task SearchAsync_TracksElapsedTime()
    {
        // Arrange
        var service = CreateSearchService();
        SetupMocks(GenerateTestChunks(5, 100));

        // Act
        var result = await service.SearchAsync("test query", 1000);

        // Assert
        Assert.True(result.ElapsedMilliseconds >= 0);
    }

    [Fact]
    public async Task SearchAsync_RespectsForceStrategy()
    {
        // Arrange
        var service = CreateSearchService();
        SetupMocks(GenerateTestChunks(5, 100));

        var request = new TokenAwareSearchRequest
        {
            Query = "test query",
            MaxTokens = 1000,
            ForceStrategy = SearchStrategy.Keyword
        };

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        Assert.Equal(SearchStrategy.Keyword, result.Strategy);
    }

    [Fact]
    public async Task SearchAsync_LimitsSameDocumentChunks()
    {
        // Arrange
        var service = CreateSearchService();
        var chunks = new List<TestSearchResult>();

        // Add 10 chunks from same document
        for (int i = 0; i < 10; i++)
        {
            chunks.Add(new TestSearchResult
            {
                ChunkId = $"chunk-{i}",
                DocumentId = "same-doc",
                Content = $"Content {i}",
                Score = 0.9 - (i * 0.01),
                TokenCount = 100
            });
        }
        SetupMocks(chunks);

        var request = new TokenAwareSearchRequest
        {
            Query = "test query",
            MaxTokens = 10000,
            DiversityWeight = 0.5
        };

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        var sameDocChunks = result.Chunks.Count(c => c.DocumentId == "same-doc");
        Assert.True(sameDocChunks <= 3); // Max 3 from same document
    }

    [Fact]
    public async Task SearchAsync_HandlesEmptyResults()
    {
        // Arrange
        var service = CreateSearchService();
        SetupMocks(new List<TestSearchResult>());

        // Act
        var result = await service.SearchAsync("test query", 1000);

        // Assert
        Assert.Empty(result.Chunks);
        Assert.Equal(0, result.UsedTokens);
        Assert.Equal(0, result.TotalRetrieved);
    }

    [Fact]
    public async Task SearchAsync_ReturnsCorrectTruncatedCount()
    {
        // Arrange
        var service = CreateSearchService();
        SetupMocks(GenerateTestChunks(20, 500)); // 20 chunks, 500 tokens each = 10000 tokens

        // Act
        var result = await service.SearchAsync("test query", 2000);

        // Assert
        Assert.Equal(20, result.TotalRetrieved);
        Assert.True(result.TruncatedCount > 0);
        Assert.Equal(result.TotalRetrieved - result.Chunks.Count, result.TruncatedCount);
    }

    #endregion

    #region SimpleTokenCounter Tests

    [Theory]
    [InlineData("hello world", 3)] // ~12 chars / 4 = 3
    [InlineData("한글 테스트", 4)] // 5 korean chars / 2 + 1 space / 4 = ~3
    [InlineData("", 0)]
    public void CountTokens_ReturnsExpectedCount(string text, int expectedApprox)
    {
        // Act
        var count = _tokenCounter.CountTokens(text);

        // Assert - Allow some variance for approximation
        Assert.InRange(count, expectedApprox - 2, expectedApprox + 2);
    }

    #endregion

    #region Helper Methods

    private TokenAwareSearchService CreateSearchService()
    {
        return new TokenAwareSearchService(
            _vectorStoreMock.Object,
            _embeddingServiceMock.Object,
            _queryAnalysisService,
            _tokenCounter,
            _searchLoggerMock.Object);
    }

    private void SetupMocks(List<TestSearchResult> results)
    {
        _embeddingServiceMock
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[384]);

        _vectorStoreMock
            .Setup(x => x.SearchAsync(
                It.IsAny<float[]>(),
                It.IsAny<int>(),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(results.Select(r => new DocumentChunk
            {
                Id = r.ChunkId,
                DocumentId = r.DocumentId,
                Content = r.Content,
                ChunkIndex = r.ChunkIndex,
                TokenCount = r.TokenCount,
                Score = (float)r.Score,
                Metadata = r.Metadata
            }).ToList());
    }

    private static List<TestSearchResult> GenerateTestChunks(int count, int tokensPerChunk)
    {
        return Enumerable.Range(0, count).Select(i => new TestSearchResult
        {
            ChunkId = $"chunk-{i}",
            DocumentId = $"doc-{i % 5}",
            Content = new string('x', tokensPerChunk * 4), // Approximate
            Score = 0.9 - (i * 0.02),
            TokenCount = tokensPerChunk,
            ChunkIndex = i
        }).ToList();
    }

    private class TestSearchResult
    {
        public string ChunkId { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public double Score { get; set; }
        public int TokenCount { get; set; }
        public int ChunkIndex { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    #endregion
}
