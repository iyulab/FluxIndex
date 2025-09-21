using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FluxIndex.Tests.SmallToBig;

/// <summary>
/// SmallToBigRetriever 단위 테스트
/// </summary>
public class SmallToBigRetrieverTests
{
    private readonly Mock<IHybridSearchService> _mockHybridSearchService;
    private readonly Mock<IChunkHierarchyRepository> _mockHierarchyRepository;
    private readonly Mock<IMemoryCache> _mockMemoryCache;
    private readonly Mock<ILogger<SmallToBigRetriever>> _mockLogger;
    private readonly SmallToBigRetriever _retriever;

    public SmallToBigRetrieverTests()
    {
        _mockHybridSearchService = new Mock<IHybridSearchService>();
        _mockHierarchyRepository = new Mock<IChunkHierarchyRepository>();
        _mockMemoryCache = new Mock<IMemoryCache>();
        _mockLogger = new Mock<ILogger<SmallToBigRetriever>>();

        _retriever = new SmallToBigRetriever(
            _mockHybridSearchService.Object,
            _mockHierarchyRepository.Object,
            _mockMemoryCache.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task SearchAsync_SimpleQuery_ReturnsSmallToBigResults()
    {
        // Arrange
        var query = "간단한 테스트 쿼리";
        var options = new SmallToBigOptions { MaxResults = 5 };

        var primaryChunk = CreateTestChunk("chunk1", "이것은 테스트 청크입니다.");
        var hybridResults = new List<HybridSearchResult>
        {
            new() { Chunk = primaryChunk, Score = 0.9 }
        };

        var hierarchy = CreateTestHierarchy("chunk1", "chunk2", "chunk3");
        var contextChunk = CreateTestChunk("chunk2", "이것은 컨텍스트 청크입니다.");

        _mockHybridSearchService
            .Setup(x => x.SearchAsync(query, It.IsAny<HybridSearchOptions>(), CancellationToken.None))
            .ReturnsAsync(hybridResults);

        _mockHierarchyRepository
            .Setup(x => x.GetHierarchyAsync("chunk1", CancellationToken.None))
            .ReturnsAsync(hierarchy);

        _mockHybridSearchService
            .Setup(x => x.GetChunkByIdAsync("chunk2", CancellationToken.None))
            .ReturnsAsync(contextChunk);

        // Act
        var results = await _retriever.SearchAsync(query, options, CancellationToken.None);

        // Assert
        Assert.NotEmpty(results);
        var result = results.First();
        Assert.Equal(primaryChunk.Id, result.PrimaryChunk.Id);
        Assert.Contains(result.ContextChunks, c => c.Id == contextChunk.Id);
        Assert.True(result.RelevanceScore > 0);
    }

    [Theory]
    [InlineData("간단한 질문", 0.2, 1)]
    [InlineData("복잡한 기술적 분석이 필요한 질문", 0.6, 4)]
    [InlineData("매우 복잡하고 고도로 전문적인 분석과 추론이 필요한 질문", 0.9, 8)]
    public async Task DetermineOptimalWindowSizeAsync_DifferentComplexity_ReturnsAppropriateWindowSize(
        string query, double expectedComplexity, int expectedMinWindowSize)
    {
        // Act
        var windowSize = await _retriever.DetermineOptimalWindowSizeAsync(query, CancellationToken.None);

        // Assert
        Assert.True(windowSize >= expectedMinWindowSize,
            $"윈도우 크기 {windowSize}는 최소 {expectedMinWindowSize} 이상이어야 합니다.");
        Assert.True(windowSize <= 10, "윈도우 크기는 10을 초과할 수 없습니다.");
    }

    [Fact]
    public async Task AnalyzeQueryComplexityAsync_ComplexQuery_ReturnsDetailedAnalysis()
    {
        // Arrange
        var complexQuery = "AI 기반 자연어 처리에서 트랜스포머 아키텍처의 어텐션 메커니즘이 어떻게 작동하는지 설명하고, BERT와 GPT의 차이점을 분석해주세요.";

        // Act
        var analysis = await _retriever.AnalyzeQueryComplexityAsync(complexQuery, CancellationToken.None);

        // Assert
        Assert.True(analysis.OverallComplexity > 0.5, "복잡한 쿼리의 전체 복잡도는 0.5 이상이어야 합니다.");
        Assert.True(analysis.LexicalComplexity > 0, "어휘적 복잡도가 계산되어야 합니다.");
        Assert.True(analysis.SemanticComplexity > 0, "의미적 복잡도가 계산되어야 합니다.");
        Assert.True(analysis.RecommendedWindowSize > 1, "복잡한 쿼리는 더 큰 윈도우 크기를 권장해야 합니다.");
        Assert.True(analysis.Components.TechnicalTermCount > 0, "전문 용어가 감지되어야 합니다.");
    }

    [Fact]
    public async Task ExpandContextAsync_HierarchicalExpansion_ReturnsExpandedContext()
    {
        // Arrange
        var primaryChunk = CreateTestChunk("chunk1", "메인 청크 내용");
        var parentChunk = CreateTestChunk("parent1", "부모 청크 내용");
        var siblingChunk = CreateTestChunk("sibling1", "형제 청크 내용");

        var hierarchy = CreateTestHierarchy("chunk1", "sibling1");
        hierarchy.ParentChunkId = "parent1";

        var options = new ContextExpansionOptions
        {
            EnableHierarchicalExpansion = true,
            EnableSequentialExpansion = true,
            MaxExpansionDistance = 2
        };

        _mockHierarchyRepository
            .Setup(x => x.GetHierarchyAsync("chunk1", CancellationToken.None))
            .ReturnsAsync(hierarchy);

        _mockHybridSearchService
            .Setup(x => x.GetChunkByIdAsync("parent1", CancellationToken.None))
            .ReturnsAsync(parentChunk);

        _mockHybridSearchService
            .Setup(x => x.GetChunkByIdAsync("sibling1", CancellationToken.None))
            .ReturnsAsync(siblingChunk);

        // Act
        var result = await _retriever.ExpandContextAsync(primaryChunk, 3, options, CancellationToken.None);

        // Assert
        Assert.Equal(primaryChunk.Id, result.OriginalChunk.Id);
        Assert.Contains(result.ExpandedChunks, c => c.Id == parentChunk.Id);
        Assert.Contains(result.ExpandedChunks, c => c.Id == siblingChunk.Id);
        Assert.True(result.ExpansionQuality > 0);
        Assert.Contains(ExpansionMethod.Hierarchical, result.ExpansionBreakdown.Keys);
    }

    [Fact]
    public async Task BuildChunkHierarchyAsync_MultipleChunks_CreatesHierarchy()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            CreateTestChunk("chunk1", "첫 번째 문장입니다.", 0, 10),
            CreateTestChunk("chunk2", "두 번째 문장입니다.", 11, 21),
            CreateTestChunk("chunk3", "세 번째 문장입니다.", 22, 32)
        };

        // Act
        var result = await _retriever.BuildChunkHierarchyAsync(chunks, CancellationToken.None);

        // Assert
        Assert.True(result.HierarchyCount > 0, "계층 구조가 생성되어야 합니다.");
        Assert.True(result.RelationshipCount >= 0, "관계가 생성될 수 있습니다.");
        Assert.True(result.SuccessRate > 0, "성공률이 0보다 커야 합니다.");
        Assert.True(result.QualityScore > 0, "품질 점수가 계산되어야 합니다.");
    }

    [Fact]
    public async Task RecommendExpansionStrategyAsync_SimpleQuery_ReturnsConservativeStrategy()
    {
        // Arrange
        var simpleQuery = "안녕하세요";
        var chunk = CreateTestChunk("chunk1", "간단한 인사말");

        // Act
        var strategy = await _retriever.RecommendExpansionStrategyAsync(simpleQuery, chunk, CancellationToken.None);

        // Assert
        Assert.Equal(ExpansionStrategyType.Conservative, strategy.Type);
        Assert.True(strategy.Confidence > 0.5, "전략 신뢰도가 충분해야 합니다.");
        Assert.NotEmpty(strategy.Methods);
        Assert.NotEmpty(strategy.Reasoning);
    }

    [Fact]
    public async Task RecommendExpansionStrategyAsync_ComplexQuery_ReturnsAggressiveStrategy()
    {
        // Arrange
        var complexQuery = "딥러닝에서 그래디언트 소실 문제를 해결하기 위한 다양한 기법들과 그 원리를 상세히 분석해주세요.";
        var chunk = CreateTestChunk("chunk1", "딥러닝 관련 내용");

        // Act
        var strategy = await _retriever.RecommendExpansionStrategyAsync(complexQuery, chunk, CancellationToken.None);

        // Assert
        Assert.True(strategy.Type == ExpansionStrategyType.Aggressive || strategy.Type == ExpansionStrategyType.Adaptive,
            "복잡한 쿼리는 적극적이거나 적응형 전략을 권장해야 합니다.");
        Assert.True(strategy.Confidence > 0.6, "복잡한 쿼리의 전략 신뢰도가 높아야 합니다.");
        Assert.Contains(ExpansionMethod.Semantic, strategy.Methods);
    }

    [Fact]
    public async Task EvaluatePerformanceAsync_WithTestData_ReturnsMetrics()
    {
        // Arrange
        var testQueries = new List<string>
        {
            "테스트 쿼리 1",
            "테스트 쿼리 2",
            "테스트 쿼리 3"
        };

        var groundTruth = new List<IReadOnlyList<string>>
        {
            new List<string> { "chunk1", "chunk2" },
            new List<string> { "chunk3", "chunk4" },
            new List<string> { "chunk5", "chunk6" }
        };

        // Mock hybrid search results
        var mockResults = testQueries.Select((query, index) =>
            new List<HybridSearchResult>
            {
                new() { Chunk = CreateTestChunk($"chunk{index + 1}", $"내용 {index + 1}"), Score = 0.8 }
            }).ToList();

        for (int i = 0; i < testQueries.Count; i++)
        {
            _mockHybridSearchService
                .Setup(x => x.SearchAsync(testQueries[i], It.IsAny<HybridSearchOptions>(), CancellationToken.None))
                .ReturnsAsync(mockResults[i]);
        }

        // Act
        var metrics = await _retriever.EvaluatePerformanceAsync(testQueries, groundTruth, CancellationToken.None);

        // Assert
        Assert.True(metrics.Precision >= 0 && metrics.Precision <= 1, "정밀도는 0-1 범위여야 합니다.");
        Assert.True(metrics.Recall >= 0 && metrics.Recall <= 1, "재현율은 0-1 범위여야 합니다.");
        Assert.True(metrics.F1Score >= 0 && metrics.F1Score <= 1, "F1 점수는 0-1 범위여야 합니다.");
        Assert.True(metrics.AverageResponseTime > 0, "평균 응답 시간이 측정되어야 합니다.");
    }

    [Fact]
    public async Task SearchAsync_WithCaching_UsesCachedComplexityAnalysis()
    {
        // Arrange
        var query = "캐시 테스트 쿼리";
        var cachedAnalysis = new QueryComplexityAnalysis
        {
            OverallComplexity = 0.5,
            RecommendedWindowSize = 3,
            AnalysisConfidence = 0.9
        };

        object? cacheValue = cachedAnalysis;
        _mockMemoryCache
            .Setup(x => x.TryGetValue($"complexity_{query.GetHashCode()}", out cacheValue))
            .Returns(true);

        var primaryChunk = CreateTestChunk("chunk1", "테스트 내용");
        var hybridResults = new List<HybridSearchResult>
        {
            new() { Chunk = primaryChunk, Score = 0.9 }
        };

        _mockHybridSearchService
            .Setup(x => x.SearchAsync(query, It.IsAny<HybridSearchOptions>(), CancellationToken.None))
            .ReturnsAsync(hybridResults);

        // Act
        var results = await _retriever.SearchAsync(query, null, CancellationToken.None);

        // Assert
        Assert.NotEmpty(results);
        var result = results.First();
        Assert.Equal(3, result.WindowSize); // 캐시된 윈도우 크기 사용
    }

    private static DocumentChunk CreateTestChunk(string id, string content, int startPos = 0, int endPos = 100)
    {
        return new DocumentChunk
        {
            Id = id,
            DocumentId = Guid.NewGuid().ToString(),
            Content = content,
            ChunkIndex = 0,
            Embedding = new float[1536], // 기본 임베딩 차원
            TokenCount = content.Split(' ').Length,
            Metadata = new Dictionary<string, object>
            {
                ["start_position"] = startPos,
                ["end_position"] = endPos
            },
            CreatedAt = DateTime.UtcNow
        };
    }

    private static ChunkHierarchy CreateTestHierarchy(string chunkId, params string[] childIds)
    {
        return new ChunkHierarchy
        {
            ChunkId = chunkId,
            ChildChunkIds = childIds.ToList(),
            HierarchyLevel = 0,
            RecommendedWindowSize = 2,
            Boundary = new ChunkBoundary
            {
                StartPosition = 0,
                EndPosition = 100,
                Type = BoundaryType.Sentence,
                Confidence = 1.0
            },
            Metadata = new HierarchyMetadata
            {
                Depth = 1,
                SiblingCount = childIds.Length,
                QualityScore = 0.9
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}