using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using EvaluationThresholds = FluxIndex.Core.Domain.Models.QualityThresholds;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Services;

/// <summary>
/// 품질 게이트 서비스 - CI/CD 통합용
/// </summary>
public partial class QualityGateService : IQualityGateService
{
    private readonly IRAGEvaluationService _evaluationService;
    private readonly IGoldenDatasetManager _datasetManager;
    private readonly ILogger<QualityGateService> _logger;
    private readonly IEvaluationResultCache? _resultCache;

    public QualityGateService(
        IRAGEvaluationService evaluationService,
        IGoldenDatasetManager datasetManager,
        ILogger<QualityGateService> logger,
        IEvaluationResultCache? resultCache = null)
    {
        _evaluationService = evaluationService ?? throw new ArgumentNullException(nameof(evaluationService));
        _datasetManager = datasetManager ?? throw new ArgumentNullException(nameof(datasetManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resultCache = resultCache;
    }

    /// <summary>
    /// 품질 게이트 실행
    /// </summary>
    public async Task<QualityGateResult> ExecuteQualityGateAsync(
        string systemVersion,
        string datasetId,
        EvaluationThresholds thresholds,
        CancellationToken cancellationToken = default)
    {
        var result = new QualityGateResult
        {
            SystemVersion = systemVersion,
            AppliedThresholds = thresholds,
            ExecutedAt = DateTime.UtcNow
        };

        try
        {
            if (_logger.IsEnabled(LogLevel.Information))
                LogQualityGate12(_logger, systemVersion, datasetId);

            // 1. 골든 데이터셋 로드
            var dataset = await _datasetManager.LoadDatasetAsync(datasetId, cancellationToken);
            if (!dataset.Any())
            {
                throw new InvalidOperationException($"데이터셋이 비어있습니다: {datasetId}");
            }

            // 2. 배치 평가 실행
            var evaluationConfig = new EvaluationConfiguration
            {
                EnableFaithfulnessEvaluation = true,
                EnableAnswerRelevancyEvaluation = true,
                EnableContextEvaluation = true,
                Timeout = TimeSpan.FromMinutes(30)
            };

            result.EvaluationResult = await _evaluationService.EvaluateBatchAsync(
                dataset, evaluationConfig, cancellationToken);

            // 3. 품질 임계값 검증
            var passedValidation = await _evaluationService.ValidateQualityThresholdsAsync(
                result.EvaluationResult, thresholds, cancellationToken);

            result.Passed = passedValidation;

            // 4. 실패한 기준 식별
            if (!passedValidation)
            {
                result.FailedCriteria = IdentifyFailedCriteria(result.EvaluationResult, thresholds);
            }

            // 5. 요약 정보 생성
            result.Summary = GenerateQualityGateSummary(result.EvaluationResult, thresholds, passedValidation);

            if (_logger.IsEnabled(LogLevel.Information))
                LogQualityGate11(_logger, systemVersion, result.Passed, result.FailedCriteria.Count);

            return result;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Information))
                LogQualityGate10(_logger, ex, systemVersion);
            result.Passed = false;
            result.FailedCriteria.Add($"실행 오류: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 이전 버전과 성능 비교
    /// </summary>
    public async Task<PerformanceComparisonResult> CompareWithBaselineAsync(
        string currentVersion,
        string baselineVersion,
        string datasetId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_logger.IsEnabled(LogLevel.Information))
                LogQualityGate9(_logger, currentVersion, baselineVersion);

            // 현재 버전 평가
            var currentResult = await EvaluateVersion(currentVersion, datasetId, cancellationToken);

            // 기준선 버전 평가 (캐시에서 로드하거나 새로 평가)
            var baselineResult = await EvaluateVersion(baselineVersion, datasetId, cancellationToken);

            // 성능 비교 수행
            var comparison = await _evaluationService.CompareEvaluationResultsAsync(
                baselineResult, currentResult, cancellationToken);

            var result = new PerformanceComparisonResult
            {
                CurrentVersion = currentVersion,
                BaselineVersion = baselineVersion,
                CurrentMetrics = ExtractMetrics(currentResult),
                BaselineMetrics = ExtractMetrics(baselineResult),
                OverallImprovement = (double)comparison["overall_improvement"],
                HasSignificantRegression = DetectSignificantRegression(comparison)
            };

            // 개선사항과 회귀사항 분류
            foreach (var metric in comparison)
            {
                if (metric.Key.EndsWith("_improvement", StringComparison.Ordinal) && metric.Value is double improvement)
                {
                    var metricName = metric.Key.Replace("_improvement", "");
                    if (improvement > 0)
                    {
                        result.Improvements[metricName] = improvement;
                    }
                    else if (improvement < 0)
                    {
                        result.Regressions[metricName] = Math.Abs(improvement);
                    }
                }
            }

            if (_logger.IsEnabled(LogLevel.Information))
                LogQualityGate8(_logger, result.OverallImprovement, result.Regressions.Count);

            return result;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Information))
                LogQualityGate7(_logger, ex, currentVersion, baselineVersion);
            throw;
        }
    }

    /// <summary>
    /// 품질 회귀 감지
    /// </summary>
    public async Task<bool> DetectQualityRegressionAsync(
        BatchEvaluationResult currentResult,
        BatchEvaluationResult baselineResult,
        double regressionThreshold = 0.05,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(1, cancellationToken); // 비동기 시뮬레이션

            var regressions = new List<(string metric, double regression)>();

            // 주요 지표들의 회귀 확인
            var metricComparisons = new[]
            {
                ("Precision", currentResult.AveragePrecision - baselineResult.AveragePrecision),
                ("Recall", currentResult.AverageRecall - baselineResult.AverageRecall),
                ("F1Score", currentResult.AverageF1Score - baselineResult.AverageF1Score),
                ("MRR", currentResult.AverageMRR - baselineResult.AverageMRR),
                ("NDCG", currentResult.AverageNDCG - baselineResult.AverageNDCG),
                ("HitRate", currentResult.AverageHitRate - baselineResult.AverageHitRate),
                ("Faithfulness", currentResult.AverageFaithfulness - baselineResult.AverageFaithfulness),
                ("AnswerRelevancy", currentResult.AverageAnswerRelevancy - baselineResult.AverageAnswerRelevancy),
                ("ContextRelevancy", currentResult.AverageContextRelevancy - baselineResult.AverageContextRelevancy)
            };

            foreach (var (metric, difference) in metricComparisons)
            {
                if (difference < -regressionThreshold)
                {
                    regressions.Add((metric, Math.Abs(difference)));
                    if (_logger.IsEnabled(LogLevel.Information))
                        LogQualityGate6(_logger, metric, Math.Abs(difference), regressionThreshold);
                }
            }

            var hasRegression = regressions.Count != 0;

            if (hasRegression)
            {
                LogQualityGate5(_logger, regressions.Count);
            }
            else
            {
                LogQualityGate4(_logger);
            }

            return hasRegression;
        }
        catch (Exception ex)
        {
            LogQualityGate3(_logger, ex);
            throw;
        }
    }

    #region Private Helper Methods

    private async Task<BatchEvaluationResult> EvaluateVersion(
        string version,
        string datasetId,
        CancellationToken cancellationToken)
    {
        // Check cache first
        if (_resultCache != null)
        {
            var cachedResult = await _resultCache.GetAsync(version, datasetId, cancellationToken);
            if (cachedResult != null)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                    LogQualityGate2(_logger, version, datasetId);
                return cachedResult;
            }
        }

        // Evaluate if not cached
        var dataset = await _datasetManager.LoadDatasetAsync(datasetId, cancellationToken);

        var evaluationConfig = new EvaluationConfiguration
        {
            EnableFaithfulnessEvaluation = true,
            EnableAnswerRelevancyEvaluation = true,
            EnableContextEvaluation = true
        };

        var result = await _evaluationService.EvaluateBatchAsync(dataset, evaluationConfig, cancellationToken);

        // Cache the result
        if (_resultCache != null)
        {
            await _resultCache.SetAsync(version, datasetId, result, TimeSpan.FromHours(24), cancellationToken);
            if (_logger.IsEnabled(LogLevel.Information))
                LogQualityGate1(_logger, version, datasetId);
        }

        return result;
    }

    private static List<string> IdentifyFailedCriteria(BatchEvaluationResult result, EvaluationThresholds thresholds)
    {
        var failedCriteria = new List<string>();

        if (result.AveragePrecision < thresholds.MinPrecision)
            failedCriteria.Add($"Precision: {result.AveragePrecision:F3} < {thresholds.MinPrecision:F3}");

        if (result.AverageRecall < thresholds.MinRecall)
            failedCriteria.Add($"Recall: {result.AverageRecall:F3} < {thresholds.MinRecall:F3}");

        if (result.AverageF1Score < thresholds.MinF1Score)
            failedCriteria.Add($"F1Score: {result.AverageF1Score:F3} < {thresholds.MinF1Score:F3}");

        if (result.AverageMRR < thresholds.MinMRR)
            failedCriteria.Add($"MRR: {result.AverageMRR:F3} < {thresholds.MinMRR:F3}");

        if (result.AverageNDCG < thresholds.MinNDCG)
            failedCriteria.Add($"NDCG: {result.AverageNDCG:F3} < {thresholds.MinNDCG:F3}");

        if (result.AverageHitRate < thresholds.MinHitRate)
            failedCriteria.Add($"HitRate: {result.AverageHitRate:F3} < {thresholds.MinHitRate:F3}");

        if (result.AverageFaithfulness < thresholds.MinFaithfulness)
            failedCriteria.Add($"Faithfulness: {result.AverageFaithfulness:F3} < {thresholds.MinFaithfulness:F3}");

        if (result.AverageAnswerRelevancy < thresholds.MinAnswerRelevancy)
            failedCriteria.Add($"AnswerRelevancy: {result.AverageAnswerRelevancy:F3} < {thresholds.MinAnswerRelevancy:F3}");

        if (result.AverageContextRelevancy < thresholds.MinContextRelevancy)
            failedCriteria.Add($"ContextRelevancy: {result.AverageContextRelevancy:F3} < {thresholds.MinContextRelevancy:F3}");

        if (result.AverageQueryDuration > thresholds.MaxAcceptableLatency)
            failedCriteria.Add($"Latency: {result.AverageQueryDuration:F1}ms > {thresholds.MaxAcceptableLatency:F1}ms");

        return failedCriteria;
    }

    private static Dictionary<string, object> GenerateQualityGateSummary(
        BatchEvaluationResult result,
        EvaluationThresholds thresholds,
        bool passed)
    {
        return new Dictionary<string, object>
        {
            ["status"] = passed ? "PASSED" : "FAILED",
            ["total_queries"] = result.TotalQueries,
            ["success_rate"] = result.SuccessRate,
            ["evaluation_summary"] = new
            {
                precision = new { value = result.AveragePrecision, threshold = thresholds.MinPrecision, passed = result.AveragePrecision >= thresholds.MinPrecision },
                recall = new { value = result.AverageRecall, threshold = thresholds.MinRecall, passed = result.AverageRecall >= thresholds.MinRecall },
                f1_score = new { value = result.AverageF1Score, threshold = thresholds.MinF1Score, passed = result.AverageF1Score >= thresholds.MinF1Score },
                mrr = new { value = result.AverageMRR, threshold = thresholds.MinMRR, passed = result.AverageMRR >= thresholds.MinMRR },
                faithfulness = new { value = result.AverageFaithfulness, threshold = thresholds.MinFaithfulness, passed = result.AverageFaithfulness >= thresholds.MinFaithfulness },
                latency = new { value = result.AverageQueryDuration, threshold = thresholds.MaxAcceptableLatency, passed = result.AverageQueryDuration <= thresholds.MaxAcceptableLatency }
            },
            ["execution_time"] = result.TotalDuration.TotalSeconds,
            ["timestamp"] = DateTime.UtcNow.ToString("O")
        };
    }

    private static Dictionary<string, double> ExtractMetrics(BatchEvaluationResult result)
    {
        return new Dictionary<string, double>
        {
            ["precision"] = result.AveragePrecision,
            ["recall"] = result.AverageRecall,
            ["f1_score"] = result.AverageF1Score,
            ["mrr"] = result.AverageMRR,
            ["ndcg"] = result.AverageNDCG,
            ["hit_rate"] = result.AverageHitRate,
            ["faithfulness"] = result.AverageFaithfulness,
            ["answer_relevancy"] = result.AverageAnswerRelevancy,
            ["context_relevancy"] = result.AverageContextRelevancy,
            ["latency"] = result.AverageQueryDuration
        };
    }

    private static bool DetectSignificantRegression(Dictionary<string, object> comparison)
    {
        var significantRegressionThreshold = -0.05; // 5% 이상 성능 저하

        foreach (var metric in comparison)
        {
            if (metric.Key.EndsWith("_improvement", StringComparison.Ordinal) && metric.Value is double improvement)
            {
                if (improvement < significantRegressionThreshold)
                {
                    return true;
                }
            }
        }

        return false;
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "품질 게이트 실행 시작: Version={Version}, Dataset={Dataset}")]
    private static partial void LogQualityGate12(ILogger logger, string version, string dataset);
    [LoggerMessage(Level = LogLevel.Information, Message = "품질 게이트 실행 완료: Version={Version}, Passed={Passed}, FailedCriteria={FailedCount}")]
    private static partial void LogQualityGate11(ILogger logger, string version, bool passed, int failedCount);
    [LoggerMessage(Level = LogLevel.Error, Message = "품질 게이트 실행 중 오류 발생: Version={Version}")]
    private static partial void LogQualityGate10(ILogger logger, Exception exception, string version);
    [LoggerMessage(Level = LogLevel.Information, Message = "성능 비교 시작: Current={Current}, Baseline={Baseline}")]
    private static partial void LogQualityGate9(ILogger logger, string current, string baseline);
    [LoggerMessage(Level = LogLevel.Information, Message = "성능 비교 완료: OverallImprovement={Improvement:F3}, Regressions={RegressionCount}")]
    private static partial void LogQualityGate8(ILogger logger, double improvement, double regressionCount);
    [LoggerMessage(Level = LogLevel.Error, Message = "성능 비교 중 오류 발생: Current={Current}, Baseline={Baseline}")]
    private static partial void LogQualityGate7(ILogger logger, Exception exception, string current, string baseline);
    [LoggerMessage(Level = LogLevel.Warning, Message = "품질 회귀 감지: {Metric} = {Regression:F3} (임계값: {Threshold:F3})")]
    private static partial void LogQualityGate6(ILogger logger, string metric, double regression, double threshold);
    [LoggerMessage(Level = LogLevel.Warning, Message = "품질 회귀 감지됨: RegressionCount={Count}")]
    private static partial void LogQualityGate5(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Information, Message = "품질 회귀 없음: 모든 지표가 임계값 내에 있습니다.")]
    private static partial void LogQualityGate4(ILogger logger);
    [LoggerMessage(Level = LogLevel.Error, Message = "품질 회귀 감지 중 오류 발생")]
    private static partial void LogQualityGate3(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Using cached evaluation result for version={Version}, dataset={DatasetId}")]
    private static partial void LogQualityGate2(ILogger logger, string version, string datasetId);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Cached evaluation result for version={Version}, dataset={DatasetId}")]
    private static partial void LogQualityGate1(ILogger logger, string version, string datasetId);

    #endregion
}
