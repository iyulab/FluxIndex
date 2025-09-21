using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace FluxIndex.Tests.SmallToBig;

/// <summary>
/// Small-to-Big 성능 테스트
/// </summary>
public class SmallToBigPerformanceTests
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<IHybridSearchService> _mockHybridSearchService;
    private readonly Mock<IChunkHierarchyRepository> _mockHierarchyRepository;
    private readonly IMemoryCache _memoryCache;
    private readonly Mock<ILogger<SmallToBigRetriever>> _mockLogger;
    private readonly SmallToBigRetriever _retriever;

    public SmallToBigPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _mockHybridSearchService = new Mock<IHybridSearchService>();
        _mockHierarchyRepository = new Mock<IChunkHierarchyRepository>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _mockLogger = new Mock<ILogger<SmallToBigRetriever>>();

        _retriever = new SmallToBigRetriever(
            _mockHybridSearchService.Object,
            _mockHierarchyRepository.Object,
            _memoryCache,
            _mockLogger.Object);

        SetupMockServices();
    }

    [Fact]
    public async Task SearchAsync_SingleQuery_MeetsPerformanceTarget()
    {
        // Arrange
        var query = "머신러닝 알고리즘의 성능 최적화 방법";
        var options = new SmallToBigOptions { MaxResults = 10 };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var results = await _retriever.SearchAsync(query, options, CancellationToken.None);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 500,
            $"검색 시간({stopwatch.ElapsedMilliseconds}ms)이 500ms 목표를 초과했습니다.");

        Assert.NotEmpty(results);
        _output.WriteLine($"검색 시간: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"결과 수: {results.Count}");
    }

    [Fact]
    public async Task SearchAsync_ConcurrentRequests_HandlesLoad()
    {
        // Arrange
        var queries = Enumerable.Range(1, 10)
            .Select(i => $"테스트 쿼리 {i}")
            .ToList();

        var options = new SmallToBigOptions { MaxResults = 5 };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var tasks = queries.Select(query =>
            _retriever.SearchAsync(query, options, CancellationToken.None));

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var averageTime = stopwatch.ElapsedMilliseconds / (double)queries.Count;
        Assert.True(averageTime < 1000,
            $"평균 검색 시간({averageTime:F2}ms)이 1초 목표를 초과했습니다.");

        Assert.All(results, result => Assert.NotEmpty(result));

        _output.WriteLine($"총 실행 시간: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"평균 검색 시간: {averageTime:F2}ms");
        _output.WriteLine($"처리량: {queries.Count / (stopwatch.ElapsedMilliseconds / 1000.0):F2} queries/sec");
    }

    [Theory]
    [InlineData(1, 50)]    // 최소 윈도우
    [InlineData(5, 200)]   // 중간 윈도우
    [InlineData(10, 500)]  // 최대 윈도우
    public async Task ExpandContextAsync_DifferentWindowSizes_ScalesLinear(int windowSize, int maxExpectedMs)
    {
        // Arrange
        var primaryChunk = CreateTestChunk("main", "메인 청크 내용");
        var options = new ContextExpansionOptions
        {
            EnableHierarchicalExpansion = true,
            EnableSequentialExpansion = true,
            EnableSemanticExpansion = true
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await _retriever.ExpandContextAsync(primaryChunk, windowSize, options, CancellationToken.None);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < maxExpectedMs,
            $"윈도우 크기 {windowSize}의 확장 시간({stopwatch.ElapsedMilliseconds}ms)이 " +
            $"목표({maxExpectedMs}ms)를 초과했습니다.");

        Assert.NotNull(result);
        _output.WriteLine($"윈도우 크기 {windowSize}: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task QueryComplexityAnalysis_WithCaching_ImprovePerformance()
    {
        // Arrange
        var query = "복잡한 자연어 처리 알고리즘의 성능 분석";

        // Act 1: First analysis (no cache)
        var stopwatch1 = Stopwatch.StartNew();
        var analysis1 = await _retriever.AnalyzeQueryComplexityAsync(query, CancellationToken.None);
        stopwatch1.Stop();

        // Act 2: Second analysis (with cache)
        var stopwatch2 = Stopwatch.StartNew();
        var analysis2 = await _retriever.AnalyzeQueryComplexityAsync(query, CancellationToken.None);
        stopwatch2.Stop();

        // Assert
        Assert.True(stopwatch2.ElapsedMilliseconds < stopwatch1.ElapsedMilliseconds,
            "캐시된 분석이 초기 분석보다 빨라야 합니다.");

        Assert.Equal(analysis1.OverallComplexity, analysis2.OverallComplexity);

        _output.WriteLine($"초기 분석: {stopwatch1.ElapsedMilliseconds}ms");
        _output.WriteLine($"캐시된 분석: {stopwatch2.ElapsedMilliseconds}ms");
        _output.WriteLine($"성능 향상: {((double)(stopwatch1.ElapsedMilliseconds - stopwatch2.ElapsedMilliseconds) / stopwatch1.ElapsedMilliseconds * 100):F1}%");
    }

    [Fact]
    public async Task BatchProcessing_MultipleQueries_OptimizedPerformance()
    {
        // Arrange
        var queries = new List<string>
        {
            "머신러닝 기초",
            "딥러닝 아키텍처",
            "자연어 처리 기법",
            "컴퓨터 비전 알고리즘",
            "강화학습 응용"
        };

        // Act: Sequential processing
        var sequentialStopwatch = Stopwatch.StartNew();
        var sequentialResults = new List<IReadOnlyList<SmallToBigResult>>();
        foreach (var query in queries)
        {
            var result = await _retriever.SearchAsync(query, new SmallToBigOptions { MaxResults = 5 }, CancellationToken.None);
            sequentialResults.Add(result);
        }
        sequentialStopwatch.Stop();

        // Act: Parallel processing
        var parallelStopwatch = Stopwatch.StartNew();
        var parallelTasks = queries.Select(query =>
            _retriever.SearchAsync(query, new SmallToBigOptions { MaxResults = 5 }, CancellationToken.None));
        var parallelResults = await Task.WhenAll(parallelTasks);
        parallelStopwatch.Stop();

        // Assert
        var speedup = (double)sequentialStopwatch.ElapsedMilliseconds / parallelStopwatch.ElapsedMilliseconds;
        Assert.True(speedup > 1.5, $"병렬 처리 성능 향상({speedup:F2}x)이 1.5배 미만입니다.");

        Assert.Equal(sequentialResults.Count, parallelResults.Length);

        _output.WriteLine($"순차 처리: {sequentialStopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"병렬 처리: {parallelStopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"성능 향상: {speedup:F2}x");
    }

    [Theory]
    [InlineData(100, 1000)]   // 중간 규모
    [InlineData(500, 3000)]   // 대규모
    [InlineData(1000, 5000)]  // 초대규모
    public async Task BuildChunkHierarchy_LargeScale_PerformanceTarget(int chunkCount, int maxTimeMs)
    {
        // Arrange
        var chunks = Enumerable.Range(1, chunkCount)
            .Select(i => CreateTestChunk($"chunk_{i}", $"청크 {i}의 내용입니다."))
            .ToList();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await _retriever.BuildChunkHierarchyAsync(chunks, CancellationToken.None);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < maxTimeMs,
            $"{chunkCount}개 청크 계층 구축 시간({stopwatch.ElapsedMilliseconds}ms)이 " +
            $"목표({maxTimeMs}ms)를 초과했습니다.");

        Assert.True(result.SuccessRate > 0.8, "계층 구축 성공률이 80% 미만입니다.");

        _output.WriteLine($"{chunkCount}개 청크 처리: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"처리량: {chunkCount / (stopwatch.ElapsedMilliseconds / 1000.0):F2} chunks/sec");
    }

    [Fact]
    public async Task MemoryUsage_LongRunningOperations_DoesNotLeak()
    {
        // Arrange
        var initialMemory = GC.GetTotalMemory(true);
        var queries = Enumerable.Range(1, 50)
            .Select(i => $"메모리 테스트 쿼리 {i}")
            .ToList();

        // Act
        foreach (var query in queries)
        {
            await _retriever.SearchAsync(query, new SmallToBigOptions { MaxResults = 3 }, CancellationToken.None);
            await _retriever.AnalyzeQueryComplexityAsync(query, CancellationToken.None);

            // Periodic garbage collection
            if (queries.IndexOf(query) % 10 == 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncrease = finalMemory - initialMemory;

        // Assert
        Assert.True(memoryIncrease < 50 * 1024 * 1024, // 50MB 이하
            $"메모리 증가량({memoryIncrease / 1024 / 1024:F2}MB)이 허용 범위를 초과했습니다.");

        _output.WriteLine($"초기 메모리: {initialMemory / 1024 / 1024:F2}MB");
        _output.WriteLine($"최종 메모리: {finalMemory / 1024 / 1024:F2}MB");
        _output.WriteLine($"메모리 증가: {memoryIncrease / 1024 / 1024:F2}MB");
    }

    [Fact]
    public async Task PerformanceEvaluation_ComprehensiveMetrics_AcceptableTime()
    {
        // Arrange
        var testQueries = Enumerable.Range(1, 20)
            .Select(i => $"성능 평가 테스트 쿼리 {i}")
            .ToList();

        var groundTruth = testQueries.Select(query =>
            new List<string> { "expected_chunk_1", "expected_chunk_2" } as IReadOnlyList<string>)
            .ToList();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var metrics = await _retriever.EvaluatePerformanceAsync(testQueries, groundTruth, CancellationToken.None);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 10000, // 10초 이하
            $"성능 평가 시간({stopwatch.ElapsedMilliseconds}ms)이 10초를 초과했습니다.");

        Assert.True(metrics.AverageResponseTime > 0);
        Assert.InRange(metrics.Precision, 0.0, 1.0);
        Assert.InRange(metrics.Recall, 0.0, 1.0);

        _output.WriteLine($"평가 시간: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"평균 응답 시간: {metrics.AverageResponseTime:F2}ms");
        _output.WriteLine($"정밀도: {metrics.Precision:F3}");
        _output.WriteLine($"재현율: {metrics.Recall:F3}");
    }

    private void SetupMockServices()
    {
        // Setup mock hybrid search service
        _mockHybridSearchService
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<HybridSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string query, HybridSearchOptions? options, CancellationToken ct) =>
            {
                var results = new List<HybridSearchResult>();
                var count = options?.MaxResults ?? 5;

                for (int i = 0; i < count; i++)
                {
                    results.Add(new HybridSearchResult
                    {
                        Chunk = CreateTestChunk($"chunk_{i}", $"쿼리 '{query}'에 대한 결과 {i}"),
                        Score = 0.9 - (i * 0.1)
                    });
                }

                return results;
            });

        _mockHybridSearchService
            .Setup(x => x.GetChunkByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string chunkId, CancellationToken ct) =>
                CreateTestChunk(chunkId, $"청크 {chunkId}의 내용"));

        // Setup mock hierarchy repository
        _mockHierarchyRepository
            .Setup(x => x.GetHierarchyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string chunkId, CancellationToken ct) =>
                CreateTestHierarchy(chunkId));

        _mockHierarchyRepository
            .Setup(x => x.SaveHierarchyAsync(It.IsAny<ChunkHierarchy>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static DocumentChunk CreateTestChunk(string id, string content)
    {
        return new DocumentChunk
        {
            Id = id,
            DocumentId = Guid.NewGuid().ToString(),
            Content = content,
            ChunkIndex = 0,
            Embedding = new float[1536],
            TokenCount = content.Split(' ').Length,
            Metadata = new Dictionary<string, object>
            {
                ["start_position"] = 0,
                ["end_position"] = content.Length
            },
            CreatedAt = DateTime.UtcNow
        };
    }

    private static ChunkHierarchy CreateTestHierarchy(string chunkId)
    {
        return new ChunkHierarchy
        {
            ChunkId = chunkId,
            ParentChunkId = null,
            ChildChunkIds = new List<string> { $"{chunkId}_child1", $"{chunkId}_child2" },
            HierarchyLevel = 0,
            RecommendedWindowSize = 3,
            Boundary = new ChunkBoundary
            {
                StartPosition = 0,
                EndPosition = 100,
                Type = BoundaryType.Sentence,
                Confidence = 0.95
            },
            Metadata = new HierarchyMetadata
            {
                Depth = 1,
                SiblingCount = 2,
                QualityScore = 0.9
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}