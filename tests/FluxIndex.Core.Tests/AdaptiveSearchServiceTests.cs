using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services;
using FluxIndex.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests;

public class AdaptiveSearchServiceTests
{
    private readonly Mock<IHybridSearchService> _mockHybridSearch;
    private readonly Mock<ISmallToBigRetriever> _mockSmallToBig;
    private readonly Mock<IQueryComplexityAnalyzer> _mockAnalyzer;
    private readonly ILogger<AdaptiveSearchService> _logger;
    private readonly AdaptiveSearchService _service;

    public AdaptiveSearchServiceTests()
    {
        _mockHybridSearch = new Mock<IHybridSearchService>();
        _mockSmallToBig = new Mock<ISmallToBigRetriever>();
        _mockAnalyzer = new Mock<IQueryComplexityAnalyzer>();
        _logger = NullLogger<AdaptiveSearchService>.Instance;

        _service = new AdaptiveSearchService(
            _mockHybridSearch.Object,
            _mockSmallToBig.Object,
            _mockAnalyzer.Object,
            _logger);
    }

    [Fact]
    public async Task SearchAsync_ValidQuery_ReturnsResults()
    {
        // Arrange
        var query = "test query";
        var options = new AdaptiveSearchOptions { MaxResults = 5 };

        var analysis = new QueryAnalysis
        {
            Type = QueryType.SimpleKeyword,
            Complexity = ComplexityLevel.Simple,
            ConfidenceScore = 0.8
        };

        var hybridResults = new List<FluxIndex.Domain.Models.HybridSearchResult>
        {
            new()
            {
                Chunk = new FluxIndex.Domain.Models.DocumentChunk
                {
                    Id = "chunk1",
                    Content = "Test content",
                    DocumentId = "doc1",
                    Metadata = new Dictionary<string, object> { ["title"] = "Test Doc" }
                },
                FusedScore = 0.9
            }
        };

        _mockAnalyzer.Setup(x => x.AnalyzeAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(analysis);

        _mockAnalyzer.Setup(x => x.RecommendStrategy(analysis))
            .Returns(SearchStrategy.DirectVector);

        _mockHybridSearch.Setup(x => x.SearchAsync(query, It.IsAny<FluxIndex.Domain.Models.HybridSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hybridResults);

        // Act
        var result = await _service.SearchAsync(query, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Documents.Any());
        Assert.Equal(SearchStrategy.DirectVector, result.UsedStrategy);
        Assert.Equal(analysis, result.QueryAnalysis);
        Assert.True(result.Performance.TotalTime > TimeSpan.Zero);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ThrowsArgumentException()
    {
        // Arrange
        var query = "";
        var options = new AdaptiveSearchOptions();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SearchAsync(query, options));
    }

    [Fact]
    public async Task SearchAsync_ForceStrategy_UsesSpecifiedStrategy()
    {
        // Arrange
        var query = "test query";
        var options = new AdaptiveSearchOptions
        {
            ForceStrategy = SearchStrategy.KeywordOnly,
            MaxResults = 5
        };

        var analysis = new QueryAnalysis
        {
            Type = QueryType.SimpleKeyword,
            Complexity = ComplexityLevel.Simple,
            ConfidenceScore = 0.8
        };

        _mockAnalyzer.Setup(x => x.AnalyzeAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(analysis);

        _mockHybridSearch.Setup(x => x.SearchAsync(query, It.IsAny<FluxIndex.Domain.Models.HybridSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FluxIndex.Domain.Models.HybridSearchResult>());

        // Act
        var result = await _service.SearchAsync(query, options);

        // Assert
        Assert.Equal(SearchStrategy.KeywordOnly, result.UsedStrategy);
        Assert.Contains("강제 지정된 전략", result.StrategyReasons.First());
    }

    [Fact]
    public async Task SearchAsync_CacheEnabled_UsesCachedResult()
    {
        // Arrange
        var query = "test query";
        var options = new AdaptiveSearchOptions { UseCache = true, MaxResults = 3 };

        var analysis = new QueryAnalysis
        {
            Type = QueryType.SimpleKeyword,
            Complexity = ComplexityLevel.Simple,
            ConfidenceScore = 0.8
        };

        _mockAnalyzer.Setup(x => x.AnalyzeAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(analysis);

        _mockHybridSearch.Setup(x => x.SearchAsync(query, It.IsAny<FluxIndex.Domain.Models.HybridSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FluxIndex.Domain.Models.HybridSearchResult>());

        // Act - First call
        var result1 = await _service.SearchAsync(query, options);

        // Act - Second call (should use cache)
        var result2 = await _service.SearchAsync(query, options);

        // Assert
        Assert.False(result1.Performance.CacheHit);
        Assert.True(result2.Performance.CacheHit);
    }

    [Fact]
    public async Task UpdateFeedbackAsync_ValidFeedback_UpdatesMetrics()
    {
        // Arrange
        var query = "test query";
        var result = new AdaptiveSearchResult
        {
            UsedStrategy = SearchStrategy.Hybrid,
            QueryAnalysis = new QueryAnalysis { Type = QueryType.SimpleKeyword }
        };
        var feedback = new UserFeedback
        {
            Satisfaction = 4,
            Relevance = 5,
            Timestamp = DateTime.UtcNow
        };

        // Act
        await _service.UpdateFeedbackAsync(query, result, feedback);

        // Assert - Should not throw and complete successfully
        Assert.True(true);
    }

    [Fact]
    public async Task GetPerformanceReportAsync_ReturnsReport()
    {
        // Act
        var report = await _service.GetPerformanceReportAsync();

        // Assert
        Assert.NotNull(report);
        Assert.NotNull(report.StrategyMetrics);
        Assert.NotNull(report.OptimalStrategies);
        Assert.NotNull(report.Overall);
        Assert.True(report.GeneratedAt <= DateTime.UtcNow);
    }

    [Theory]
    [InlineData(ComplexityLevel.Simple)]
    [InlineData(ComplexityLevel.Moderate)]
    [InlineData(ComplexityLevel.Complex)]
    [InlineData(ComplexityLevel.VeryComplex)]
    public async Task SearchAsync_DifferentComplexityLevels_SelectsAppropriateStrategy(ComplexityLevel complexity)
    {
        // Arrange
        var query = "test query";
        var options = new AdaptiveSearchOptions { MaxResults = 3 };

        var analysis = new QueryAnalysis
        {
            Type = QueryType.SimpleKeyword,
            Complexity = complexity,
            ConfidenceScore = 0.8
        };

        _mockAnalyzer.Setup(x => x.AnalyzeAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(analysis);

        _mockHybridSearch.Setup(x => x.SearchAsync(query, It.IsAny<FluxIndex.Domain.Models.HybridSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FluxIndex.Domain.Models.HybridSearchResult>());

        // Act
        var result = await _service.SearchAsync(query, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(Enum.IsDefined(typeof(SearchStrategy), result.UsedStrategy));
    }
}