using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// 벡터 인덱스 자동 튜닝 서비스 단위 테스트
/// </summary>
public class VectorIndexAutoTunerTests
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<IVectorIndexBenchmark> _mockBenchmark;
    private readonly Mock<ILogger<VectorIndexAutoTuner>> _mockLogger;
    private readonly VectorIndexAutoTuner _autoTuner;

    public VectorIndexAutoTunerTests(ITestOutputHelper output)
    {
        _output = output;
        _mockBenchmark = new Mock<IVectorIndexBenchmark>();
        _mockLogger = new Mock<ILogger<VectorIndexAutoTuner>>();
        _autoTuner = new VectorIndexAutoTuner(_mockBenchmark.Object, _mockLogger.Object);
    }

    /// <summary>
    /// 다단계 자동 튜닝 테스트
    /// </summary>
    [Fact]
    public async Task RunMultiStageAutoTuningAsync_ShouldReturnOptimalParameters()
    {
        // Arrange
        var options = CreateTestAutoTuningOptions(TuningStrategy.BalancedOptimization);

        // Mock benchmark results for initial exploration
        var initialResults = new List<HnswBenchmarkResult>
        {
            CreateMockBenchmarkResult(new HnswParameters { M = 8, EfConstruction = 32, EfSearch = 20 }, 0.75, 15.0, 0.85),
            CreateMockBenchmarkResult(new HnswParameters { M = 16, EfConstruction = 64, EfSearch = 40 }, 0.85, 25.0, 0.90),
            CreateMockBenchmarkResult(new HnswParameters { M = 24, EfConstruction = 96, EfSearch = 60 }, 0.80, 35.0, 0.95)
        };

        _mockBenchmark.Setup(x => x.BenchmarkParameterCombinationsAsync(
            It.IsAny<IReadOnlyList<HnswParameters>>(),
            It.IsAny<HnswBenchmarkOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialResults.AsReadOnly());

        // Mock single benchmark for final validation
        _mockBenchmark.Setup(x => x.BenchmarkHnswIndexAsync(
            It.IsAny<HnswBenchmarkOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMockBenchmarkResult(new HnswParameters { M = 16, EfConstruction = 64, EfSearch = 40 }, 0.85, 25.0, 0.90));

        // Act
        var result = await _autoTuner.RunMultiStageAutoTuningAsync(options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.M >= 4);
        Assert.True(result.EfConstruction >= 16);
        Assert.True(result.EfSearch >= 10);

        _output.WriteLine($"다단계 튜닝 결과: {result.GetIdentifier()}");
    }

    /// <summary>
    /// 적응형 튜닝 테스트
    /// </summary>
    [Fact]
    public async Task RunAdaptiveTuningAsync_ShouldAdaptSearchRadius()
    {
        // Arrange
        var options = CreateTestAutoTuningOptions(TuningStrategy.SpeedOptimization);

        // Mock improving results for first iteration
        var firstIterationResults = new List<HnswBenchmarkResult>
        {
            CreateMockBenchmarkResult(new HnswParameters { M = 8, EfConstruction = 32, EfSearch = 20 }, 0.80, 12.0, 0.80),
            CreateMockBenchmarkResult(new HnswParameters { M = 10, EfConstruction = 40, EfSearch = 25 }, 0.85, 18.0, 0.82),
            CreateMockBenchmarkResult(new HnswParameters { M = 12, EfConstruction = 48, EfSearch = 30 }, 0.82, 22.0, 0.85)
        };

        // Mock non-improving results for second iteration
        var secondIterationResults = new List<HnswBenchmarkResult>
        {
            CreateMockBenchmarkResult(new HnswParameters { M = 6, EfConstruction = 24, EfSearch = 15 }, 0.75, 10.0, 0.75),
            CreateMockBenchmarkResult(new HnswParameters { M = 14, EfConstruction = 56, EfSearch = 35 }, 0.83, 28.0, 0.88)
        };

        _mockBenchmark.SetupSequence(x => x.BenchmarkParameterCombinationsAsync(
            It.IsAny<IReadOnlyList<HnswParameters>>(),
            It.IsAny<HnswBenchmarkOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstIterationResults.AsReadOnly())
            .ReturnsAsync(secondIterationResults.AsReadOnly());

        // Act
        var result = await _autoTuner.RunAdaptiveTuningAsync(options);

        // Assert
        Assert.NotNull(result);

        // 속도 최적화 전략에서는 더 작은 M 값을 선호해야 함
        Assert.True(result.M <= 16, $"속도 최적화에서 M이 16 이하여야 하는데 {result.M}입니다");

        _output.WriteLine($"적응형 튜닝 결과: {result.GetIdentifier()}");
    }

    /// <summary>
    /// 스마트 튜닝 테스트
    /// </summary>
    [Fact]
    public async Task RunSmartTuningAsync_ShouldUseIntelligentSelection()
    {
        // Arrange
        var options = CreateTestAutoTuningOptions(TuningStrategy.AccuracyOptimization);

        // Mock progressive improvement in benchmark results
        var benchmarkResults = new Queue<HnswBenchmarkResult>();
        benchmarkResults.Enqueue(CreateMockBenchmarkResult(new HnswParameters { M = 20, EfConstruction = 100, EfSearch = 60 }, 0.85, 45.0, 0.90));
        benchmarkResults.Enqueue(CreateMockBenchmarkResult(new HnswParameters { M = 28, EfConstruction = 140, EfSearch = 80 }, 0.90, 55.0, 0.95));
        benchmarkResults.Enqueue(CreateMockBenchmarkResult(new HnswParameters { M = 36, EfConstruction = 180, EfSearch = 100 }, 0.88, 65.0, 0.98));

        _mockBenchmark.Setup(x => x.BenchmarkHnswIndexAsync(
            It.IsAny<HnswBenchmarkOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(benchmarkResults.Dequeue()));

        // Act
        var result = await _autoTuner.RunSmartTuningAsync(options);

        // Assert
        Assert.NotNull(result);

        // 정확도 최적화에서는 더 큰 M 값을 선호해야 함
        Assert.True(result.M >= 16, $"정확도 최적화에서 M이 16 이상이어야 하는데 {result.M}입니다");

        _output.WriteLine($"스마트 튜닝 결과: {result.GetIdentifier()}");
    }

    /// <summary>
    /// 메모리 최적화 전략 테스트
    /// </summary>
    [Fact]
    public async Task RunMultiStageAutoTuningAsync_MemoryOptimization_ShouldPreferSmallerM()
    {
        // Arrange
        var options = CreateTestAutoTuningOptions(TuningStrategy.MemoryOptimization);

        var memoryOptimizedResults = new List<HnswBenchmarkResult>
        {
            CreateMockBenchmarkResult(new HnswParameters { M = 4, EfConstruction = 20, EfSearch = 15 }, 0.75, 20.0, 0.80),
            CreateMockBenchmarkResult(new HnswParameters { M = 6, EfConstruction = 30, EfSearch = 20 }, 0.80, 25.0, 0.82),
            CreateMockBenchmarkResult(new HnswParameters { M = 8, EfConstruction = 40, EfSearch = 25 }, 0.82, 30.0, 0.85)
        };

        _mockBenchmark.Setup(x => x.BenchmarkParameterCombinationsAsync(
            It.IsAny<IReadOnlyList<HnswParameters>>(),
            It.IsAny<HnswBenchmarkOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(memoryOptimizedResults.AsReadOnly());

        _mockBenchmark.Setup(x => x.BenchmarkHnswIndexAsync(
            It.IsAny<HnswBenchmarkOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMockBenchmarkResult(new HnswParameters { M = 6, EfConstruction = 30, EfSearch = 20 }, 0.80, 25.0, 0.82));

        // Act
        var result = await _autoTuner.RunMultiStageAutoTuningAsync(options);

        // Assert
        Assert.NotNull(result);

        // 메모리 최적화에서는 더 작은 M 값을 선호해야 함
        Assert.True(result.M <= 12, $"메모리 최적화에서 M이 12 이하여야 하는데 {result.M}입니다");

        _output.WriteLine($"메모리 최적화 튜닝 결과: {result.GetIdentifier()}");
    }

    /// <summary>
    /// 제약 조건 내에서 튜닝 테스트
    /// </summary>
    [Fact]
    public async Task RunAdaptiveTuningAsync_WithConstraints_ShouldRespectLimits()
    {
        // Arrange
        var options = new HnswAutoTuningOptions
        {
            TargetQueryTimeMs = 30.0, // 엄격한 제약
            MinRecallRequired = 0.85, // 높은 Recall 요구
            MaxMemoryUsageMB = 512,   // 낮은 메모리 제한
            MaxBuildTimeMinutes = 2.0, // 짧은 빌드 시간
            Strategy = TuningStrategy.BalancedOptimization,
            MaxTuningAttempts = 5,
            BenchmarkOptions = CreateTestBenchmarkOptions()
        };

        // Mock results that meet and don't meet constraints
        var constraintResults = new List<HnswBenchmarkResult>
        {
            // 제약 조건을 만족하는 결과
            CreateMockBenchmarkResult(new HnswParameters { M = 8, EfConstruction = 40, EfSearch = 25 }, 0.88, 28.0, 0.87),
            // 제약 조건을 만족하지 않는 결과 (쿼리 시간 초과)
            CreateMockBenchmarkResult(new HnswParameters { M = 16, EfConstruction = 80, EfSearch = 50 }, 0.92, 45.0, 0.92),
            // 제약 조건을 만족하는 다른 결과
            CreateMockBenchmarkResult(new HnswParameters { M = 10, EfConstruction = 50, EfSearch = 30 }, 0.86, 32.0, 0.89)
        };

        _mockBenchmark.Setup(x => x.BenchmarkParameterCombinationsAsync(
            It.IsAny<IReadOnlyList<HnswParameters>>(),
            It.IsAny<HnswBenchmarkOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(constraintResults.AsReadOnly());

        // Act
        var result = await _autoTuner.RunAdaptiveTuningAsync(options);

        // Assert
        Assert.NotNull(result);

        // 제약 조건을 만족하는 매개변수가 선택되어야 함
        Assert.True(result.M <= 10, "제약 조건을 고려하여 적절한 M 값이 선택되어야 합니다");

        _output.WriteLine($"제약 조건 내 튜닝 결과: {result.GetIdentifier()}");
    }

    /// <summary>
    /// 벤치마크 실패 시 처리 테스트
    /// </summary>
    [Fact]
    public async Task RunMultiStageAutoTuningAsync_WithBenchmarkFailures_ShouldHandleGracefully()
    {
        // Arrange
        var options = CreateTestAutoTuningOptions(TuningStrategy.BalancedOptimization);

        // Mock some successful and some failed results
        var mixedResults = new List<HnswBenchmarkResult>
        {
            CreateMockBenchmarkResult(new HnswParameters { M = 8, EfConstruction = 32, EfSearch = 20 }, 0.80, 20.0, 0.85),
            CreateFailedBenchmarkResult(new HnswParameters { M = 16, EfConstruction = 64, EfSearch = 40 }, "Out of memory"),
            CreateMockBenchmarkResult(new HnswParameters { M = 12, EfConstruction = 48, EfSearch = 30 }, 0.82, 25.0, 0.87)
        };

        _mockBenchmark.Setup(x => x.BenchmarkParameterCombinationsAsync(
            It.IsAny<IReadOnlyList<HnswParameters>>(),
            It.IsAny<HnswBenchmarkOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mixedResults.AsReadOnly());

        _mockBenchmark.Setup(x => x.BenchmarkHnswIndexAsync(
            It.IsAny<HnswBenchmarkOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMockBenchmarkResult(new HnswParameters { M = 12, EfConstruction = 48, EfSearch = 30 }, 0.82, 25.0, 0.87));

        // Act
        var result = await _autoTuner.RunMultiStageAutoTuningAsync(options);

        // Assert
        Assert.NotNull(result);

        // 실패한 벤치마크를 제외하고 성공한 결과 중에서 선택되어야 함
        Assert.True(result.M == 8 || result.M == 12, "성공한 벤치마크 결과 중에서 선택되어야 합니다");

        _output.WriteLine($"벤치마크 실패 처리 결과: {result.GetIdentifier()}");
    }

    #region Helper Methods

    private HnswAutoTuningOptions CreateTestAutoTuningOptions(TuningStrategy strategy)
    {
        return new HnswAutoTuningOptions
        {
            TargetQueryTimeMs = 50.0,
            MinRecallRequired = 0.80,
            MaxMemoryUsageMB = 1024,
            MaxBuildTimeMinutes = 5.0,
            Strategy = strategy,
            MaxTuningAttempts = 10,
            BenchmarkOptions = CreateTestBenchmarkOptions()
        };
    }

    private HnswBenchmarkOptions CreateTestBenchmarkOptions()
    {
        return new HnswBenchmarkOptions
        {
            TestVectorCount = 1000,
            VectorDimensions = 384,
            QueryCount = 50,
            TopK = 10,
            WarmupQueries = 5,
            RecreateIndex = true,
            Iterations = 1,
            MonitorMemoryUsage = true,
            MeasureAccuracy = true,
            RandomSeed = 42
        };
    }

    private HnswBenchmarkResult CreateMockBenchmarkResult(
        HnswParameters parameters,
        double performanceScore,
        double queryTimeMs,
        double recall)
    {
        return new HnswBenchmarkResult
        {
            Parameters = parameters,
            IsSuccessful = true,
            AverageQueryTimeMs = queryTimeMs,
            P95QueryTimeMs = queryTimeMs * 1.5,
            P99QueryTimeMs = queryTimeMs * 2.0,
            RecallAtK = recall,
            IndexBuildTimeMs = 5000.0,
            IndexSizeBytes = 1024 * 1024, // 1MB
            MemoryUsageBytes = (long)(1024 * 1024 * 1.5), // 1.5MB
            PerformanceScore = performanceScore,
            BenchmarkTime = DateTime.UtcNow
        };
    }

    private HnswBenchmarkResult CreateFailedBenchmarkResult(HnswParameters parameters, string errorMessage)
    {
        return new HnswBenchmarkResult
        {
            Parameters = parameters,
            IsSuccessful = false,
            ErrorMessage = errorMessage,
            BenchmarkTime = DateTime.UtcNow
        };
    }

    #endregion
}