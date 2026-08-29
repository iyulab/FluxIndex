using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests;

public class AdaptiveSearchServiceTests
{
    private readonly IHybridSearchService _mockHybridSearch;
    private readonly ISmallToBigRetriever _mockSmallToBig;
    private readonly IQueryComplexityAnalyzer _mockAnalyzer;
    private readonly ISemanticCacheService _mockSemanticCache;
    private readonly ILogger<AdaptiveSearchService> _logger;
    private readonly AdaptiveSearchService _service;

    public AdaptiveSearchServiceTests()
    {
        _mockHybridSearch = Substitute.For<IHybridSearchService>();
        _mockSmallToBig = Substitute.For<ISmallToBigRetriever>();
        _mockAnalyzer = Substitute.For<IQueryComplexityAnalyzer>();
        _mockSemanticCache = Substitute.For<ISemanticCacheService>();
        _logger = NullLogger<AdaptiveSearchService>.Instance;

        _service = new AdaptiveSearchService(
            _mockHybridSearch,
            _mockSmallToBig,
            _mockAnalyzer,
            _logger,
            dynamicFusion: null,
            semanticCache: _mockSemanticCache);
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

        var hybridResults = new List<FluxIndex.Core.Domain.Models.HybridSearchResult>
        {
            new()
            {
                Chunk = new DocumentChunk
                {
                    Id = "chunk1",
                    Content = "Test content",
                    DocumentId = "doc1",
                    ChunkIndex = 0,
                    TotalChunks = 1,
                    Metadata = new Dictionary<string, object> { ["title"] = "Test Doc" }
                },
                FusedScore = 0.9
            }
        };

        _mockAnalyzer.AnalyzeAsync(query, Arg.Any<CancellationToken>()).Returns(analysis);

        _mockAnalyzer.RecommendStrategy(analysis).Returns(SearchStrategy.DirectVector);

        _mockHybridSearch.SearchAsync(query, Arg.Any<FluxIndex.Core.Domain.Models.HybridSearchOptions>(), Arg.Any<CancellationToken>()).Returns(hybridResults);

        // Act
        var result = await _service.SearchAsync(query, options, TestContext.Current.CancellationToken);

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
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SearchAsync(query, options, TestContext.Current.CancellationToken));
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

        _mockAnalyzer.AnalyzeAsync(query, Arg.Any<CancellationToken>()).Returns(analysis);

        _mockHybridSearch.SearchAsync(query, Arg.Any<FluxIndex.Core.Domain.Models.HybridSearchOptions>(), Arg.Any<CancellationToken>()).Returns(new List<FluxIndex.Core.Domain.Models.HybridSearchResult>());

        // Act
        var result = await _service.SearchAsync(query, options, TestContext.Current.CancellationToken);

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

        var hybridResults = new List<FluxIndex.Core.Domain.Models.HybridSearchResult>
        {
            new()
            {
                Chunk = new DocumentChunk
                {
                    Id = "chunk1",
                    Content = "Test content",
                    DocumentId = "doc1",
                    ChunkIndex = 0,
                    TotalChunks = 1,
                    Metadata = new Dictionary<string, object> { ["title"] = "Test Doc" }
                },
                FusedScore = 0.9
            }
        };

        _mockAnalyzer.AnalyzeAsync(query, Arg.Any<CancellationToken>()).Returns(analysis);

        _mockHybridSearch.SearchAsync(query, Arg.Any<FluxIndex.Core.Domain.Models.HybridSearchOptions>(), Arg.Any<CancellationToken>()).Returns(hybridResults);

        // Setup semantic cache: first call returns null (miss), second call returns cached result (hit)
        var cacheChunks = hybridResults.Select(r => new FluxIndex.Core.Domain.Models.CacheDocumentChunk
        {
            Id = r.Chunk.Id,
            DocumentId = r.Chunk.DocumentId,
            Content = r.Chunk.Content,
            ChunkIndex = r.Chunk.ChunkIndex,
            Score = (float)r.FusedScore,
            Metadata = r.Chunk.Metadata ?? new Dictionary<string, object>()
        }).ToList();

        var cachedResult = new FluxIndex.Core.Application.Interfaces.CachedSearchResult
        {
            OriginalQuery = query,
            CachedQuery = query,
            SimilarityScore = 1.0f,
            Results = cacheChunks.AsReadOnly(),
            CachedAt = DateTime.UtcNow,
            Metadata = new FluxIndex.Core.Application.Interfaces.SearchMetadata
            {
                SearchAlgorithm = "DirectVector",
                TotalDocuments = 1,
                SearchTimeMs = 10
            }
        };

        _mockSemanticCache.GetCachedResultAsync(query, Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(
                (FluxIndex.Core.Application.Interfaces.CachedSearchResult?)null,  // First call: cache miss
                cachedResult);  // Second call: cache hit

        // Setup SetCachedResultAsync to complete successfully
        _mockSemanticCache.SetCachedResultAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<FluxIndex.Core.Domain.Models.CacheDocumentChunk>>(),
            Arg.Any<FluxIndex.Core.Application.Interfaces.SearchMetadata>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // Act - First call
        var result1 = await _service.SearchAsync(query, options, TestContext.Current.CancellationToken);

        // Act - Second call (should use cache)
        var result2 = await _service.SearchAsync(query, options, TestContext.Current.CancellationToken);

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
        await _service.UpdateFeedbackAsync(query, result, feedback, TestContext.Current.CancellationToken);

        // Assert - Should not throw and complete successfully
        Assert.True(true);
    }

    [Fact]
    public async Task GetPerformanceReportAsync_ReturnsReport()
    {
        // Act
        var report = await _service.GetPerformanceReportAsync(TestContext.Current.CancellationToken);

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

        _mockAnalyzer.AnalyzeAsync(query, Arg.Any<CancellationToken>()).Returns(analysis);

        _mockHybridSearch.SearchAsync(query, Arg.Any<FluxIndex.Core.Domain.Models.HybridSearchOptions>(), Arg.Any<CancellationToken>()).Returns(new List<FluxIndex.Core.Domain.Models.HybridSearchResult>());

        // Act
        var result = await _service.SearchAsync(query, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(Enum.IsDefined(typeof(SearchStrategy), result.UsedStrategy));
    }
}