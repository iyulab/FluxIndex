using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Services;

/// <summary>
/// 벡터 인덱스 자동 튜닝 서비스
/// </summary>
public partial class VectorIndexAutoTuner
{
    private readonly IVectorIndexBenchmark _benchmark;
    private readonly ILogger<VectorIndexAutoTuner> _logger;

    public VectorIndexAutoTuner(
        IVectorIndexBenchmark benchmark,
        ILogger<VectorIndexAutoTuner> logger)
    {
        _benchmark = benchmark ?? throw new ArgumentNullException(nameof(benchmark));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 다단계 자동 튜닝 실행
    /// </summary>
    public async Task<HnswParameters> RunMultiStageAutoTuningAsync(
        HnswAutoTuningOptions options,
        CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            LogVectorIndexAutoTuner15(_logger, options.Strategy);

        // 1단계: 초기 탐색 (Wide Search)
        var initialCandidates = await InitialExplorationAsync(options, cancellationToken);

        // 2단계: 세밀 조정 (Fine Tuning)
        var refinedCandidates = await FineTuningAsync(initialCandidates, options, cancellationToken);

        // 3단계: 최종 검증 (Final Validation)
        var bestParameters = await FinalValidationAsync(refinedCandidates, options, cancellationToken);

        var bestId = bestParameters.GetIdentifier();
        LogVectorIndexAutoTuner14(_logger, bestId);

        return bestParameters;
    }

    /// <summary>
    /// 적응형 튜닝 실행 - 결과에 따라 동적으로 탐색 범위 조정
    /// </summary>
    public async Task<HnswParameters> RunAdaptiveTuningAsync(
        HnswAutoTuningOptions options,
        CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            LogVectorIndexAutoTuner13(_logger, options.Strategy);

        var bestParameters = HnswParameters.Default;
        var bestScore = 0.0;
        var iteration = 0;
        var searchRadius = GetInitialSearchRadius(options.Strategy);

        while (iteration < options.MaxTuningAttempts / 3) // 반복 횟수 제한
        {
            if (_logger.IsEnabled(LogLevel.Information))
                LogVectorIndexAutoTuner12(_logger, iteration + 1, searchRadius);

            // 현재 최적 매개변수 주변 탐색
            var candidates = GenerateAdaptiveCandidates(bestParameters, searchRadius, options.Strategy);

            var results = await _benchmark.BenchmarkParameterCombinationsAsync(
                candidates, options.BenchmarkOptions, cancellationToken);

            // 최적 결과 찾기
            var iterationBest = FindBestResult(results, options);
            if (iterationBest.score > bestScore)
            {
                bestScore = iterationBest.score;
                bestParameters = iterationBest.parameters;
                searchRadius = Math.Max(1, searchRadius - 1); // 범위 축소
                var bestId2 = bestParameters.GetIdentifier();
                LogVectorIndexAutoTuner11(_logger, bestId2, bestScore);
            }
            else
            {
                searchRadius = Math.Min(5, searchRadius + 1); // 범위 확대
                if (_logger.IsEnabled(LogLevel.Information))
                    LogVectorIndexAutoTuner10(_logger, searchRadius);
            }

            iteration++;

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        return bestParameters;
    }

    /// <summary>
    /// 베이지안 최적화를 모방한 스마트 튜닝
    /// </summary>
    public async Task<HnswParameters> RunSmartTuningAsync(
        HnswAutoTuningOptions options,
        CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            LogVectorIndexAutoTuner9(_logger, options.Strategy);

        var evaluatedCombinations = new List<(HnswParameters parameters, double score)>();
        var maxIterations = Math.Min(options.MaxTuningAttempts, 20);

        // 초기 탐색점들
        var initialPoints = GetSmartInitialPoints(options.Strategy);

        foreach (var parameters in initialPoints)
        {
            var result = await _benchmark.BenchmarkHnswIndexAsync(
                options.BenchmarkOptions, cancellationToken);

            if (result.IsSuccessful && IsWithinConstraints(result, options))
            {
                var score = CalculateTuningScore(result, options);
                evaluatedCombinations.Add((parameters, score));
            }

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        // 반복적 개선
        for (int iteration = initialPoints.Length; iteration < maxIterations; iteration++)
        {
            // 다음 탐색점 선택 (가장 유망한 영역)
            var nextCandidate = SelectNextSmartCandidate(evaluatedCombinations, options.Strategy);

            var result = await _benchmark.BenchmarkHnswIndexAsync(
                options.BenchmarkOptions, cancellationToken);

            if (result.IsSuccessful && IsWithinConstraints(result, options))
            {
                var score = CalculateTuningScore(result, options);
                evaluatedCombinations.Add((nextCandidate, score));

                var candidateId = nextCandidate.GetIdentifier();
                LogVectorIndexAutoTuner8(_logger, iteration + 1, candidateId, score);
            }

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        var bestResult = evaluatedCombinations.OrderByDescending(x => x.score).First();
        var bestResultId = bestResult.parameters.GetIdentifier();
        LogVectorIndexAutoTuner7(_logger, bestResultId, bestResult.score);

        return bestResult.parameters;
    }

    #region Private Methods

    private async Task<IReadOnlyList<HnswParameters>> InitialExplorationAsync(
        HnswAutoTuningOptions options,
        CancellationToken cancellationToken)
    {
        LogVectorIndexAutoTuner6(_logger);

        // 전략별 초기 후보군 생성
        var initialCandidates = GenerateInitialCandidates(options.Strategy);

        var results = await _benchmark.BenchmarkParameterCombinationsAsync(
            initialCandidates, options.BenchmarkOptions, cancellationToken);

        // 상위 30% 선택
        var successfulResults = results
            .Where(r => r.IsSuccessful && IsWithinConstraints(r, options))
            .Select(r => new { Parameters = r.Parameters, Score = CalculateTuningScore(r, options) })
            .OrderByDescending(r => r.Score)
            .Take(Math.Max(1, initialCandidates.Count / 3))
            .Select(r => r.Parameters)
            .ToList();

        LogVectorIndexAutoTuner5(_logger, successfulResults.Count);
        return successfulResults.AsReadOnly();
    }

    private async Task<IReadOnlyList<HnswParameters>> FineTuningAsync(
        IReadOnlyList<HnswParameters> candidates,
        HnswAutoTuningOptions options,
        CancellationToken cancellationToken)
    {
        LogVectorIndexAutoTuner4(_logger, candidates.Count);

        var refinedCandidates = new List<HnswParameters>();

        foreach (var candidate in candidates)
        {
            // 각 후보 주변의 세밀한 변형 생성
            var variations = GenerateFineTuningVariations(candidate);
            refinedCandidates.AddRange(variations);
        }

        var results = await _benchmark.BenchmarkParameterCombinationsAsync(
            refinedCandidates, options.BenchmarkOptions, cancellationToken);

        // 상위 20% 선택
        var bestRefined = results
            .Where(r => r.IsSuccessful && IsWithinConstraints(r, options))
            .Select(r => new { Parameters = r.Parameters, Score = CalculateTuningScore(r, options) })
            .OrderByDescending(r => r.Score)
            .Take(Math.Max(1, refinedCandidates.Count / 5))
            .Select(r => r.Parameters)
            .ToList();

        LogVectorIndexAutoTuner3(_logger, bestRefined.Count);
        return bestRefined.AsReadOnly();
    }

    private async Task<HnswParameters> FinalValidationAsync(
        IReadOnlyList<HnswParameters> candidates,
        HnswAutoTuningOptions options,
        CancellationToken cancellationToken)
    {
        LogVectorIndexAutoTuner2(_logger, candidates.Count);

        var bestParameters = candidates[0];
        var bestScore = 0.0;

        // 더 엄격한 벤치마크 옵션으로 최종 검증
        var validationOptions = CreateValidationBenchmarkOptions(options.BenchmarkOptions);

        foreach (var candidate in candidates)
        {
            var result = await _benchmark.BenchmarkHnswIndexAsync(
                validationOptions, cancellationToken);

            if (result.IsSuccessful && IsWithinConstraints(result, options))
            {
                var score = CalculateTuningScore(result, options);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestParameters = candidate;
                }
            }

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        var finalId = bestParameters.GetIdentifier();
        LogVectorIndexAutoTuner1(_logger, finalId, bestScore);

        return bestParameters;
    }

    private static ReadOnlyCollection<HnswParameters> GenerateInitialCandidates(TuningStrategy strategy)
    {
        var candidates = new List<HnswParameters>();

        // 전략별 기본 후보들 추가
        candidates.Add(HnswParameters.Default);
        candidates.Add(HnswParameters.HighPerformance);
        candidates.Add(HnswParameters.MemoryEfficient);
        candidates.Add(HnswParameters.Balanced);

        // 전략별 추가 후보들
        switch (strategy)
        {
            case TuningStrategy.SpeedOptimization:
                candidates.AddRange(GenerateSpeedOptimizedCandidates());
                break;
            case TuningStrategy.AccuracyOptimization:
                candidates.AddRange(GenerateAccuracyOptimizedCandidates());
                break;
            case TuningStrategy.MemoryOptimization:
                candidates.AddRange(GenerateMemoryOptimizedCandidates());
                break;
            default:
                candidates.AddRange(GenerateBalancedCandidates());
                break;
        }

        return candidates.AsReadOnly();
    }

    private static HnswParameters[] GenerateSpeedOptimizedCandidates()
    {
        return new[]
        {
            new HnswParameters { M = 8, EfConstruction = 32, EfSearch = 16 },
            new HnswParameters { M = 12, EfConstruction = 48, EfSearch = 24 },
            new HnswParameters { M = 16, EfConstruction = 64, EfSearch = 32 }
        };
    }

    private static HnswParameters[] GenerateAccuracyOptimizedCandidates()
    {
        return new[]
        {
            new HnswParameters { M = 24, EfConstruction = 128, EfSearch = 80 },
            new HnswParameters { M = 32, EfConstruction = 256, EfSearch = 120 },
            new HnswParameters { M = 40, EfConstruction = 512, EfSearch = 160 }
        };
    }

    private static HnswParameters[] GenerateMemoryOptimizedCandidates()
    {
        return new[]
        {
            new HnswParameters { M = 4, EfConstruction = 16, EfSearch = 10 },
            new HnswParameters { M = 6, EfConstruction = 24, EfSearch = 15 },
            new HnswParameters { M = 8, EfConstruction = 32, EfSearch = 20 }
        };
    }

    private static HnswParameters[] GenerateBalancedCandidates()
    {
        return new[]
        {
            new HnswParameters { M = 12, EfConstruction = 60, EfSearch = 40 },
            new HnswParameters { M = 20, EfConstruction = 100, EfSearch = 60 },
            new HnswParameters { M = 24, EfConstruction = 120, EfSearch = 70 }
        };
    }

    private static IEnumerable<HnswParameters> GenerateFineTuningVariations(HnswParameters baseParams)
    {
        var variations = new List<HnswParameters>();

        // M 값 변형
        for (int mDelta = -2; mDelta <= 2; mDelta++)
        {
            var newM = Math.Max(4, baseParams.M + mDelta * 2);
            variations.Add(new HnswParameters
            {
                M = newM,
                EfConstruction = baseParams.EfConstruction,
                EfSearch = baseParams.EfSearch
            });
        }

        // EfConstruction 변형
        for (int efcDelta = -1; efcDelta <= 1; efcDelta++)
        {
            var newEfc = Math.Max(16, baseParams.EfConstruction + efcDelta * 16);
            variations.Add(new HnswParameters
            {
                M = baseParams.M,
                EfConstruction = newEfc,
                EfSearch = baseParams.EfSearch
            });
        }

        // EfSearch 변형
        for (int efsDelta = -1; efsDelta <= 1; efsDelta++)
        {
            var newEfs = Math.Max(10, baseParams.EfSearch + efsDelta * 10);
            variations.Add(new HnswParameters
            {
                M = baseParams.M,
                EfConstruction = baseParams.EfConstruction,
                EfSearch = newEfs
            });
        }

        return variations.Distinct();
    }

    private static ReadOnlyCollection<HnswParameters> GenerateAdaptiveCandidates(
        HnswParameters center,
        int radius,
        TuningStrategy strategy)
    {
        var candidates = new List<HnswParameters>();

        for (int mOffset = -radius; mOffset <= radius; mOffset++)
        {
            for (int efcOffset = -radius; efcOffset <= radius; efcOffset++)
            {
                for (int efsOffset = -radius; efsOffset <= radius; efsOffset++)
                {
                    var newM = Math.Max(4, center.M + mOffset * 2);
                    var newEfc = Math.Max(16, center.EfConstruction + efcOffset * 16);
                    var newEfs = Math.Max(10, center.EfSearch + efsOffset * 10);

                    candidates.Add(new HnswParameters
                    {
                        M = newM,
                        EfConstruction = newEfc,
                        EfSearch = newEfs
                    });
                }
            }
        }

        return candidates.AsReadOnly();
    }

    private static int GetInitialSearchRadius(TuningStrategy strategy)
    {
        return strategy switch
        {
            TuningStrategy.SpeedOptimization => 1,
            TuningStrategy.AccuracyOptimization => 3,
            TuningStrategy.MemoryOptimization => 1,
            _ => 2
        };
    }

    private static (HnswParameters parameters, double score) FindBestResult(
        IReadOnlyList<HnswBenchmarkResult> results,
        HnswAutoTuningOptions options)
    {
        var best = results
            .Where(r => r.IsSuccessful && IsWithinConstraints(r, options))
            .Select(r => new { Parameters = r.Parameters, Score = CalculateTuningScore(r, options) })
            .OrderByDescending(r => r.Score)
            .FirstOrDefault();

        return best != null ? (best.Parameters, best.Score) : (HnswParameters.Default, 0.0);
    }

    private static HnswParameters[] GetSmartInitialPoints(TuningStrategy strategy)
    {
        // 전략별 스마트 초기점들 - 경험적으로 좋은 영역들
        return strategy switch
        {
            TuningStrategy.SpeedOptimization => new[]
            {
                new HnswParameters { M = 8, EfConstruction = 32, EfSearch = 20 },
                new HnswParameters { M = 12, EfConstruction = 48, EfSearch = 30 },
                new HnswParameters { M = 16, EfConstruction = 64, EfSearch = 40 }
            },
            TuningStrategy.AccuracyOptimization => new[]
            {
                new HnswParameters { M = 20, EfConstruction = 100, EfSearch = 60 },
                new HnswParameters { M = 28, EfConstruction = 140, EfSearch = 80 },
                new HnswParameters { M = 36, EfConstruction = 180, EfSearch = 100 }
            },
            TuningStrategy.MemoryOptimization => new[]
            {
                new HnswParameters { M = 4, EfConstruction = 20, EfSearch = 15 },
                new HnswParameters { M = 6, EfConstruction = 30, EfSearch = 20 },
                new HnswParameters { M = 8, EfConstruction = 40, EfSearch = 25 }
            },
            _ => new[]
            {
                new HnswParameters { M = 12, EfConstruction = 60, EfSearch = 40 },
                new HnswParameters { M = 18, EfConstruction = 90, EfSearch = 55 },
                new HnswParameters { M = 24, EfConstruction = 120, EfSearch = 70 }
            }
        };
    }

    private static HnswParameters SelectNextSmartCandidate(
        List<(HnswParameters parameters, double score)> evaluatedCombinations,
        TuningStrategy strategy)
    {
        // 간단한 휴리스틱: 최고 점수 지점 주변 탐색
        var bestSoFar = evaluatedCombinations.OrderByDescending(x => x.score).First();

        // 주변 지점 중 아직 평가하지 않은 지점 선택
        var candidates = GenerateAdaptiveCandidates(bestSoFar.parameters, 1, strategy);

        foreach (var candidate in candidates)
        {
            if (!evaluatedCombinations.Any(x => AreParametersEqual(x.parameters, candidate)))
            {
                return candidate;
            }
        }

        // 모든 주변 지점이 평가됐다면 랜덤하게 새로운 지점 선택
        return strategy switch
        {
            TuningStrategy.SpeedOptimization => new HnswParameters
            {
                M = 4 + new Random().Next(0, 16),
                EfConstruction = 32 + new Random().Next(0, 64),
                EfSearch = 16 + new Random().Next(0, 32)
            },
            _ => HnswParameters.Default
        };
    }

    private static bool AreParametersEqual(HnswParameters a, HnswParameters b)
    {
        return a.M == b.M &&
               a.EfConstruction == b.EfConstruction &&
               a.EfSearch == b.EfSearch;
    }

    private static HnswBenchmarkOptions CreateValidationBenchmarkOptions(HnswBenchmarkOptions baseOptions)
    {
        // 최종 검증을 위한 더 엄격한 옵션
        return new HnswBenchmarkOptions
        {
            TestVectorCount = Math.Min(baseOptions.TestVectorCount * 2, 50000), // 더 많은 테스트 벡터
            VectorDimensions = baseOptions.VectorDimensions,
            QueryCount = Math.Min(baseOptions.QueryCount * 2, 1000), // 더 많은 쿼리
            TopK = baseOptions.TopK,
            WarmupQueries = baseOptions.WarmupQueries * 2, // 더 많은 워밍업
            RecreateIndex = baseOptions.RecreateIndex,
            Iterations = Math.Max(baseOptions.Iterations, 5), // 더 많은 반복
            MonitorMemoryUsage = baseOptions.MonitorMemoryUsage,
            MeasureAccuracy = baseOptions.MeasureAccuracy,
            RandomSeed = baseOptions.RandomSeed
        };
    }

    private static bool IsWithinConstraints(HnswBenchmarkResult result, HnswAutoTuningOptions options)
    {
        return result.AverageQueryTimeMs <= options.TargetQueryTimeMs &&
               result.RecallAtK >= options.MinRecallRequired &&
               result.MemoryUsageBytes <= options.MaxMemoryUsageMB * 1024 * 1024 &&
               result.IndexBuildTimeMs <= options.MaxBuildTimeMinutes * 60 * 1000;
    }

    private static double CalculateTuningScore(HnswBenchmarkResult result, HnswAutoTuningOptions options)
    {
        return options.Strategy switch
        {
            TuningStrategy.SpeedOptimization => result.CalculatePerformanceScore(0.2, 0.6, 0.2),
            TuningStrategy.AccuracyOptimization => result.CalculatePerformanceScore(0.7, 0.2, 0.1),
            TuningStrategy.MemoryOptimization => result.CalculatePerformanceScore(0.3, 0.2, 0.5),
            _ => result.CalculatePerformanceScore()
        };
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "다단계 자동 튜닝 시작 - 전략: {Strategy}")]
    private static partial void LogVectorIndexAutoTuner15(ILogger logger, TuningStrategy strategy);
    [LoggerMessage(Level = LogLevel.Information, Message = "다단계 자동 튜닝 완료 - 최적 매개변수: {Identifier}")]
    private static partial void LogVectorIndexAutoTuner14(ILogger logger, string identifier);
    [LoggerMessage(Level = LogLevel.Information, Message = "적응형 튜닝 시작 - 전략: {Strategy}")]
    private static partial void LogVectorIndexAutoTuner13(ILogger logger, TuningStrategy strategy);
    [LoggerMessage(Level = LogLevel.Information, Message = "적응형 튜닝 반복 {Iteration} - 탐색 반경: {Radius}")]
    private static partial void LogVectorIndexAutoTuner12(ILogger logger, int iteration, int radius);
    [LoggerMessage(Level = LogLevel.Information, Message = "개선된 매개변수 발견: {Identifier}, 점수: {Score:F3}")]
    private static partial void LogVectorIndexAutoTuner11(ILogger logger, string identifier, double score);
    [LoggerMessage(Level = LogLevel.Information, Message = "개선 없음 - 탐색 반경 확대: {Radius}")]
    private static partial void LogVectorIndexAutoTuner10(ILogger logger, int radius);
    [LoggerMessage(Level = LogLevel.Information, Message = "스마트 튜닝 시작 - 전략: {Strategy}")]
    private static partial void LogVectorIndexAutoTuner9(ILogger logger, TuningStrategy strategy);
    [LoggerMessage(Level = LogLevel.Information, Message = "스마트 튜닝 반복 {Iteration}: {Identifier}, 점수: {Score:F3}")]
    private static partial void LogVectorIndexAutoTuner8(ILogger logger, int iteration, string identifier, double score);
    [LoggerMessage(Level = LogLevel.Information, Message = "스마트 튜닝 완료 - 최적 매개변수: {Identifier}, 최종 점수: {Score:F3}")]
    private static partial void LogVectorIndexAutoTuner7(ILogger logger, string identifier, double score);
    [LoggerMessage(Level = LogLevel.Information, Message = "1단계: 초기 탐색 시작")]
    private static partial void LogVectorIndexAutoTuner6(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "초기 탐색 완료 - {Count}개 후보 선정")]
    private static partial void LogVectorIndexAutoTuner5(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Information, Message = "2단계: 세밀 조정 시작 - {Count}개 후보")]
    private static partial void LogVectorIndexAutoTuner4(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Information, Message = "세밀 조정 완료 - {Count}개 후보 선정")]
    private static partial void LogVectorIndexAutoTuner3(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Information, Message = "3단계: 최종 검증 시작 - {Count}개 후보")]
    private static partial void LogVectorIndexAutoTuner2(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Information, Message = "최종 검증 완료 - 최적 매개변수: {Identifier}, 점수: {Score:F3}")]
    private static partial void LogVectorIndexAutoTuner1(ILogger logger, string identifier, double score);

    #endregion
}
