using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Services;

/// <summary>
/// 벡터 인덱스 자동 튜닝 서비스
/// </summary>
public class VectorIndexAutoTuner
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
        _logger.LogInformation("다단계 자동 튜닝 시작 - 전략: {Strategy}", options.Strategy);

        // 1단계: 초기 탐색 (Wide Search)
        var initialCandidates = await InitialExplorationAsync(options, cancellationToken);

        // 2단계: 세밀 조정 (Fine Tuning)
        var refinedCandidates = await FineTuningAsync(initialCandidates, options, cancellationToken);

        // 3단계: 최종 검증 (Final Validation)
        var bestParameters = await FinalValidationAsync(refinedCandidates, options, cancellationToken);

        _logger.LogInformation("다단계 자동 튜닝 완료 - 최적 매개변수: {Identifier}",
            bestParameters.GetIdentifier());

        return bestParameters;
    }

    /// <summary>
    /// 적응형 튜닝 실행 - 결과에 따라 동적으로 탐색 범위 조정
    /// </summary>
    public async Task<HnswParameters> RunAdaptiveTuningAsync(
        HnswAutoTuningOptions options,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("적응형 튜닝 시작 - 전략: {Strategy}", options.Strategy);

        var bestParameters = HnswParameters.Default;
        var bestScore = 0.0;
        var iteration = 0;
        var searchRadius = GetInitialSearchRadius(options.Strategy);

        while (iteration < options.MaxTuningAttempts / 3) // 반복 횟수 제한
        {
            _logger.LogInformation("적응형 튜닝 반복 {Iteration} - 탐색 반경: {Radius}",
                iteration + 1, searchRadius);

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
                _logger.LogInformation("개선된 매개변수 발견: {Identifier}, 점수: {Score:F3}",
                    bestParameters.GetIdentifier(), bestScore);
            }
            else
            {
                searchRadius = Math.Min(5, searchRadius + 1); // 범위 확대
                _logger.LogInformation("개선 없음 - 탐색 반경 확대: {Radius}", searchRadius);
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
        _logger.LogInformation("스마트 튜닝 시작 - 전략: {Strategy}", options.Strategy);

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
        for (int iteration = initialPoints.Count; iteration < maxIterations; iteration++)
        {
            // 다음 탐색점 선택 (가장 유망한 영역)
            var nextCandidate = SelectNextSmartCandidate(evaluatedCombinations, options.Strategy);

            var result = await _benchmark.BenchmarkHnswIndexAsync(
                options.BenchmarkOptions, cancellationToken);

            if (result.IsSuccessful && IsWithinConstraints(result, options))
            {
                var score = CalculateTuningScore(result, options);
                evaluatedCombinations.Add((nextCandidate, score));

                _logger.LogInformation("스마트 튜닝 반복 {Iteration}: {Identifier}, 점수: {Score:F3}",
                    iteration + 1, nextCandidate.GetIdentifier(), score);
            }

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        var bestResult = evaluatedCombinations.OrderByDescending(x => x.score).First();
        _logger.LogInformation("스마트 튜닝 완료 - 최적 매개변수: {Identifier}, 최종 점수: {Score:F3}",
            bestResult.parameters.GetIdentifier(), bestResult.score);

        return bestResult.parameters;
    }

    #region Private Methods

    private async Task<IReadOnlyList<HnswParameters>> InitialExplorationAsync(
        HnswAutoTuningOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("1단계: 초기 탐색 시작");

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

        _logger.LogInformation("초기 탐색 완료 - {Count}개 후보 선정", successfulResults.Count);
        return successfulResults.AsReadOnly();
    }

    private async Task<IReadOnlyList<HnswParameters>> FineTuningAsync(
        IReadOnlyList<HnswParameters> candidates,
        HnswAutoTuningOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("2단계: 세밀 조정 시작 - {Count}개 후보", candidates.Count);

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

        _logger.LogInformation("세밀 조정 완료 - {Count}개 후보 선정", bestRefined.Count);
        return bestRefined.AsReadOnly();
    }

    private async Task<HnswParameters> FinalValidationAsync(
        IReadOnlyList<HnswParameters> candidates,
        HnswAutoTuningOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("3단계: 최종 검증 시작 - {Count}개 후보", candidates.Count);

        var bestParameters = candidates.First();
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

        _logger.LogInformation("최종 검증 완료 - 최적 매개변수: {Identifier}, 점수: {Score:F3}",
            bestParameters.GetIdentifier(), bestScore);

        return bestParameters;
    }

    private IReadOnlyList<HnswParameters> GenerateInitialCandidates(TuningStrategy strategy)
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

    private IEnumerable<HnswParameters> GenerateSpeedOptimizedCandidates()
    {
        return new[]
        {
            new HnswParameters { M = 8, EfConstruction = 32, EfSearch = 16 },
            new HnswParameters { M = 12, EfConstruction = 48, EfSearch = 24 },
            new HnswParameters { M = 16, EfConstruction = 64, EfSearch = 32 }
        };
    }

    private IEnumerable<HnswParameters> GenerateAccuracyOptimizedCandidates()
    {
        return new[]
        {
            new HnswParameters { M = 24, EfConstruction = 128, EfSearch = 80 },
            new HnswParameters { M = 32, EfConstruction = 256, EfSearch = 120 },
            new HnswParameters { M = 40, EfConstruction = 512, EfSearch = 160 }
        };
    }

    private IEnumerable<HnswParameters> GenerateMemoryOptimizedCandidates()
    {
        return new[]
        {
            new HnswParameters { M = 4, EfConstruction = 16, EfSearch = 10 },
            new HnswParameters { M = 6, EfConstruction = 24, EfSearch = 15 },
            new HnswParameters { M = 8, EfConstruction = 32, EfSearch = 20 }
        };
    }

    private IEnumerable<HnswParameters> GenerateBalancedCandidates()
    {
        return new[]
        {
            new HnswParameters { M = 12, EfConstruction = 60, EfSearch = 40 },
            new HnswParameters { M = 20, EfConstruction = 100, EfSearch = 60 },
            new HnswParameters { M = 24, EfConstruction = 120, EfSearch = 70 }
        };
    }

    private IEnumerable<HnswParameters> GenerateFineTuningVariations(HnswParameters baseParams)
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

    private IReadOnlyList<HnswParameters> GenerateAdaptiveCandidates(
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

    private int GetInitialSearchRadius(TuningStrategy strategy)
    {
        return strategy switch
        {
            TuningStrategy.SpeedOptimization => 1,
            TuningStrategy.AccuracyOptimization => 3,
            TuningStrategy.MemoryOptimization => 1,
            _ => 2
        };
    }

    private (HnswParameters parameters, double score) FindBestResult(
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

    private IReadOnlyList<HnswParameters> GetSmartInitialPoints(TuningStrategy strategy)
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

    private HnswParameters SelectNextSmartCandidate(
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

    private bool AreParametersEqual(HnswParameters a, HnswParameters b)
    {
        return a.M == b.M &&
               a.EfConstruction == b.EfConstruction &&
               a.EfSearch == b.EfSearch;
    }

    private HnswBenchmarkOptions CreateValidationBenchmarkOptions(HnswBenchmarkOptions baseOptions)
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

    private bool IsWithinConstraints(HnswBenchmarkResult result, HnswAutoTuningOptions options)
    {
        return result.AverageQueryTimeMs <= options.TargetQueryTimeMs &&
               result.RecallAtK >= options.MinRecallRequired &&
               result.MemoryUsageBytes <= options.MaxMemoryUsageMB * 1024 * 1024 &&
               result.IndexBuildTimeMs <= options.MaxBuildTimeMinutes * 60 * 1000;
    }

    private double CalculateTuningScore(HnswBenchmarkResult result, HnswAutoTuningOptions options)
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
}