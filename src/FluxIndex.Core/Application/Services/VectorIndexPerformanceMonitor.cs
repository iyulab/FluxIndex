using FluxIndex.Core.Interfaces;
using FluxIndex.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Services;

/// <summary>
/// 벡터 인덱스 성능 모니터링 서비스
/// </summary>
public class VectorIndexPerformanceMonitor
{
    private readonly IVectorIndexBenchmark _benchmark;
    private readonly ILogger<VectorIndexPerformanceMonitor> _logger;
    private readonly Dictionary<string, PerformanceBaseline> _baselines;
    private readonly object _lockObject = new();

    public VectorIndexPerformanceMonitor(
        IVectorIndexBenchmark benchmark,
        ILogger<VectorIndexPerformanceMonitor> logger)
    {
        _benchmark = benchmark ?? throw new ArgumentNullException(nameof(benchmark));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _baselines = new Dictionary<string, PerformanceBaseline>();
    }

    /// <summary>
    /// 성능 기준선 설정
    /// </summary>
    public async Task<PerformanceBaseline> EstablishBaselineAsync(
        string indexName,
        HnswBenchmarkOptions options,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("성능 기준선 설정 시작: {IndexName}", indexName);

        var benchmarkResult = await _benchmark.BenchmarkHnswIndexAsync(options, cancellationToken);

        var baseline = new PerformanceBaseline
        {
            IndexName = indexName,
            EstablishedAt = DateTime.UtcNow,
            BaselineQueryTimeMs = benchmarkResult.AverageQueryTimeMs,
            BaselineRecall = benchmarkResult.RecallAtK,
            BaselineMemoryUsageBytes = benchmarkResult.MemoryUsageBytes,
            BaselineQPS = benchmarkResult.QueriesPerSecond,
            BenchmarkOptions = options
        };

        lock (_lockObject)
        {
            _baselines[indexName] = baseline;
        }

        _logger.LogInformation("성능 기준선 설정 완료: {IndexName} - 평균 쿼리 시간: {Time:F2}ms, Recall: {Recall:P2}",
            indexName, baseline.BaselineQueryTimeMs, baseline.BaselineRecall);

        return baseline;
    }

    /// <summary>
    /// 성능 회귀 감지
    /// </summary>
    public async Task<PerformanceRegressionReport> DetectPerformanceRegressionAsync(
        string indexName,
        CancellationToken cancellationToken = default)
    {
        if (!_baselines.TryGetValue(indexName, out var baseline))
        {
            throw new InvalidOperationException($"인덱스 {indexName}에 대한 기준선이 설정되지 않았습니다.");
        }

        _logger.LogInformation("성능 회귀 감지 시작: {IndexName}", indexName);

        var currentResult = await _benchmark.BenchmarkHnswIndexAsync(
            baseline.BenchmarkOptions, cancellationToken);

        var report = new PerformanceRegressionReport
        {
            IndexName = indexName,
            TestTimestamp = DateTime.UtcNow,
            Baseline = baseline,
            CurrentPerformance = currentResult,
            Regressions = new List<PerformanceRegression>()
        };

        // 쿼리 시간 회귀 검사
        var queryTimeRegression = CalculateQueryTimeRegression(baseline, currentResult);
        if (queryTimeRegression.HasRegression)
        {
            report.Regressions.Add(queryTimeRegression);
        }

        // Recall 회귀 검사
        var recallRegression = CalculateRecallRegression(baseline, currentResult);
        if (recallRegression.HasRegression)
        {
            report.Regressions.Add(recallRegression);
        }

        // 메모리 사용량 회귀 검사
        var memoryRegression = CalculateMemoryRegression(baseline, currentResult);
        if (memoryRegression.HasRegression)
        {
            report.Regressions.Add(memoryRegression);
        }

        // QPS 회귀 검사
        var qpsRegression = CalculateQPSRegression(baseline, currentResult);
        if (qpsRegression.HasRegression)
        {
            report.Regressions.Add(qpsRegression);
        }

        report.OverallRegressionSeverity = CalculateOverallSeverity(report.Regressions);

        _logger.LogInformation("성능 회귀 감지 완료: {IndexName} - {Count}개 회귀 발견, 심각도: {Severity}",
            indexName, report.Regressions.Count, report.OverallRegressionSeverity);

        return report;
    }

    /// <summary>
    /// 연속 성능 모니터링
    /// </summary>
    public async Task<ContinuousMonitoringReport> RunContinuousMonitoringAsync(
        string indexName,
        TimeSpan monitoringDuration,
        TimeSpan sampleInterval,
        CancellationToken cancellationToken = default)
    {
        if (!_baselines.TryGetValue(indexName, out var baseline))
        {
            throw new InvalidOperationException($"인덱스 {indexName}에 대한 기준선이 설정되지 않았습니다.");
        }

        _logger.LogInformation("연속 성능 모니터링 시작: {IndexName} - 기간: {Duration}, 간격: {Interval}",
            indexName, monitoringDuration, sampleInterval);

        var report = new ContinuousMonitoringReport
        {
            IndexName = indexName,
            MonitoringStartTime = DateTime.UtcNow,
            MonitoringDuration = monitoringDuration,
            SampleInterval = sampleInterval,
            PerformanceSamples = new List<PerformanceSample>(),
            DetectedAnomalies = new List<PerformanceAnomaly>()
        };

        var stopwatch = Stopwatch.StartNew();
        var sampleCount = 0;

        while (stopwatch.Elapsed < monitoringDuration && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var sampleStart = DateTime.UtcNow;
                var currentMetrics = await _benchmark.CollectPerformanceMetricsAsync(indexName, cancellationToken);

                var sample = new PerformanceSample
                {
                    SampleNumber = ++sampleCount,
                    Timestamp = sampleStart,
                    Metrics = currentMetrics
                };

                report.PerformanceSamples.Add(sample);

                // 이상 징후 감지
                var anomalies = DetectAnomalies(sample, baseline, report.PerformanceSamples);
                report.DetectedAnomalies.AddRange(anomalies);

                if (anomalies.Count > 0)
                {
                    _logger.LogWarning("성능 이상 감지: {IndexName} - {Count}개 이상 징후",
                        indexName, anomalies.Count);
                }

                await Task.Delay(sampleInterval, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "연속 모니터링 중 오류 발생: {IndexName}", indexName);
            }
        }

        report.MonitoringEndTime = DateTime.UtcNow;
        report.ActualMonitoringDuration = stopwatch.Elapsed;

        // 모니터링 요약 계산
        CalculateMonitoringSummary(report);

        _logger.LogInformation("연속 성능 모니터링 완료: {IndexName} - {Samples}개 샘플, {Anomalies}개 이상 징후",
            indexName, report.PerformanceSamples.Count, report.DetectedAnomalies.Count);

        return report;
    }

    /// <summary>
    /// 성능 트렌드 분석
    /// </summary>
    public PerformanceTrendAnalysis AnalyzePerformanceTrends(
        IReadOnlyList<PerformanceSample> samples,
        TimeSpan analysisWindow)
    {
        if (samples.Count < 2)
        {
            throw new ArgumentException("트렌드 분석을 위해서는 최소 2개의 샘플이 필요합니다.");
        }

        var analysis = new PerformanceTrendAnalysis
        {
            AnalysisTimestamp = DateTime.UtcNow,
            AnalysisWindow = analysisWindow,
            SampleCount = samples.Count,
            Trends = new Dictionary<string, TrendInfo>()
        };

        // 효율성 점수 트렌드 분석
        var efficiencyScores = new List<double>();
        foreach (var sample in samples)
        {
            efficiencyScores.Add(sample.Metrics.GetEfficiencyScore());
        }

        analysis.Trends["EfficiencyScore"] = CalculateTrend(efficiencyScores, "효율성 점수");

        // 인덱스 스캔 트렌드 분석
        var indexScans = new List<double>();
        foreach (var sample in samples)
        {
            indexScans.Add(sample.Metrics.IndexScans);
        }

        analysis.Trends["IndexScans"] = CalculateTrend(indexScans, "인덱스 스캔");

        // 전체 트렌드 요약
        analysis.OverallTrend = DetermineOverallTrend(analysis.Trends.Values);

        return analysis;
    }

    #region Private Methods

    private PerformanceRegression CalculateQueryTimeRegression(
        PerformanceBaseline baseline,
        HnswBenchmarkResult current)
    {
        var thresholdPercent = 20.0; // 20% 이상 느려지면 회귀로 판단
        var degradationPercent = ((current.AverageQueryTimeMs - baseline.BaselineQueryTimeMs) / baseline.BaselineQueryTimeMs) * 100;

        return new PerformanceRegression
        {
            MetricName = "QueryTime",
            HasRegression = degradationPercent > thresholdPercent,
            BaselineValue = baseline.BaselineQueryTimeMs,
            CurrentValue = current.AverageQueryTimeMs,
            DegradationPercent = degradationPercent,
            Severity = degradationPercent > 50 ? RegressionSeverity.Critical :
                      degradationPercent > 30 ? RegressionSeverity.Major :
                      degradationPercent > 20 ? RegressionSeverity.Minor : RegressionSeverity.None,
            Description = $"평균 쿼리 시간이 {degradationPercent:F1}% 증가했습니다."
        };
    }

    private PerformanceRegression CalculateRecallRegression(
        PerformanceBaseline baseline,
        HnswBenchmarkResult current)
    {
        var thresholdPercent = 5.0; // 5% 이상 감소하면 회귀로 판단
        var degradationPercent = ((baseline.BaselineRecall - current.RecallAtK) / baseline.BaselineRecall) * 100;

        return new PerformanceRegression
        {
            MetricName = "Recall",
            HasRegression = degradationPercent > thresholdPercent,
            BaselineValue = baseline.BaselineRecall,
            CurrentValue = current.RecallAtK,
            DegradationPercent = degradationPercent,
            Severity = degradationPercent > 15 ? RegressionSeverity.Critical :
                      degradationPercent > 10 ? RegressionSeverity.Major :
                      degradationPercent > 5 ? RegressionSeverity.Minor : RegressionSeverity.None,
            Description = $"Recall이 {degradationPercent:F1}% 감소했습니다."
        };
    }

    private PerformanceRegression CalculateMemoryRegression(
        PerformanceBaseline baseline,
        HnswBenchmarkResult current)
    {
        var thresholdPercent = 30.0; // 30% 이상 증가하면 회귀로 판단
        var degradationPercent = ((current.MemoryUsageBytes - baseline.BaselineMemoryUsageBytes) / (double)baseline.BaselineMemoryUsageBytes) * 100;

        return new PerformanceRegression
        {
            MetricName = "MemoryUsage",
            HasRegression = degradationPercent > thresholdPercent,
            BaselineValue = baseline.BaselineMemoryUsageBytes,
            CurrentValue = current.MemoryUsageBytes,
            DegradationPercent = degradationPercent,
            Severity = degradationPercent > 100 ? RegressionSeverity.Critical :
                      degradationPercent > 60 ? RegressionSeverity.Major :
                      degradationPercent > 30 ? RegressionSeverity.Minor : RegressionSeverity.None,
            Description = $"메모리 사용량이 {degradationPercent:F1}% 증가했습니다."
        };
    }

    private PerformanceRegression CalculateQPSRegression(
        PerformanceBaseline baseline,
        HnswBenchmarkResult current)
    {
        var thresholdPercent = 15.0; // 15% 이상 감소하면 회귀로 판단
        var degradationPercent = ((baseline.BaselineQPS - current.QueriesPerSecond) / baseline.BaselineQPS) * 100;

        return new PerformanceRegression
        {
            MetricName = "QPS",
            HasRegression = degradationPercent > thresholdPercent,
            BaselineValue = baseline.BaselineQPS,
            CurrentValue = current.QueriesPerSecond,
            DegradationPercent = degradationPercent,
            Severity = degradationPercent > 40 ? RegressionSeverity.Critical :
                      degradationPercent > 25 ? RegressionSeverity.Major :
                      degradationPercent > 15 ? RegressionSeverity.Minor : RegressionSeverity.None,
            Description = $"QPS가 {degradationPercent:F1}% 감소했습니다."
        };
    }

    private RegressionSeverity CalculateOverallSeverity(IReadOnlyList<PerformanceRegression> regressions)
    {
        var maxSeverity = RegressionSeverity.None;

        foreach (var regression in regressions)
        {
            if (regression.Severity > maxSeverity)
            {
                maxSeverity = regression.Severity;
            }
        }

        return maxSeverity;
    }

    private IReadOnlyList<PerformanceAnomaly> DetectAnomalies(
        PerformanceSample currentSample,
        PerformanceBaseline baseline,
        IReadOnlyList<PerformanceSample> historicalSamples)
    {
        var anomalies = new List<PerformanceAnomaly>();

        // 효율성 점수 이상 감지
        var currentEfficiency = currentSample.Metrics.GetEfficiencyScore();
        if (currentEfficiency < 0.5) // 임계값 설정
        {
            anomalies.Add(new PerformanceAnomaly
            {
                DetectedAt = currentSample.Timestamp,
                AnomalyType = "LowEfficiency",
                Description = $"효율성 점수가 {currentEfficiency:F2}로 낮습니다.",
                Severity = currentEfficiency < 0.3 ? AnomalySeverity.High :
                          currentEfficiency < 0.4 ? AnomalySeverity.Medium : AnomalySeverity.Low,
                Value = currentEfficiency
            });
        }

        // 급격한 스캔 증가 감지
        if (historicalSamples.Count > 1)
        {
            var previousSample = historicalSamples[historicalSamples.Count - 2];
            var scanIncrease = currentSample.Metrics.IndexScans - previousSample.Metrics.IndexScans;

            if (scanIncrease > 1000) // 급격한 증가 임계값
            {
                anomalies.Add(new PerformanceAnomaly
                {
                    DetectedAt = currentSample.Timestamp,
                    AnomalyType = "SuddenScanIncrease",
                    Description = $"인덱스 스캔이 {scanIncrease:N0}만큼 급격히 증가했습니다.",
                    Severity = scanIncrease > 10000 ? AnomalySeverity.High :
                              scanIncrease > 5000 ? AnomalySeverity.Medium : AnomalySeverity.Low,
                    Value = scanIncrease
                });
            }
        }

        return anomalies.AsReadOnly();
    }

    private void CalculateMonitoringSummary(ContinuousMonitoringReport report)
    {
        if (report.PerformanceSamples.Count == 0) return;

        var efficiencyScores = new List<double>();
        var indexScans = new List<long>();

        foreach (var sample in report.PerformanceSamples)
        {
            efficiencyScores.Add(sample.Metrics.GetEfficiencyScore());
            indexScans.Add(sample.Metrics.IndexScans);
        }

        report.Summary = new MonitoringSummary
        {
            AverageEfficiencyScore = efficiencyScores.Count > 0 ? efficiencyScores.Average() : 0,
            MinEfficiencyScore = efficiencyScores.Count > 0 ? efficiencyScores.Min() : 0,
            MaxEfficiencyScore = efficiencyScores.Count > 0 ? efficiencyScores.Max() : 0,
            TotalIndexScans = indexScans.Sum(),
            AverageIndexScansPerSample = indexScans.Count > 0 ? indexScans.Average() : 0,
            CriticalAnomalies = report.DetectedAnomalies.Count(a => a.Severity == AnomalySeverity.High),
            MediumAnomalies = report.DetectedAnomalies.Count(a => a.Severity == AnomalySeverity.Medium),
            LowAnomalies = report.DetectedAnomalies.Count(a => a.Severity == AnomalySeverity.Low)
        };
    }

    private TrendInfo CalculateTrend(IReadOnlyList<double> values, string metricName)
    {
        if (values.Count < 2)
        {
            return new TrendInfo
            {
                MetricName = metricName,
                Direction = TrendDirection.Stable,
                ChangePercent = 0,
                Confidence = 0
            };
        }

        // 간단한 선형 회귀를 통한 트렌드 계산
        var n = values.Count;
        var sumX = 0.0;
        var sumY = 0.0;
        var sumXY = 0.0;
        var sumX2 = 0.0;

        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += values[i];
            sumXY += i * values[i];
            sumX2 += i * i;
        }

        var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        var firstValue = values[0];
        var lastValue = values[n - 1];

        var direction = slope > 0.01 ? TrendDirection.Increasing :
                       slope < -0.01 ? TrendDirection.Decreasing : TrendDirection.Stable;

        var changePercent = firstValue != 0 ? ((lastValue - firstValue) / firstValue) * 100 : 0;

        return new TrendInfo
        {
            MetricName = metricName,
            Direction = direction,
            ChangePercent = changePercent,
            Confidence = Math.Min(1.0, Math.Abs(slope) * 10) // 간단한 신뢰도 계산
        };
    }

    private TrendDirection DetermineOverallTrend(IEnumerable<TrendInfo> trends)
    {
        var increasingCount = 0;
        var decreasingCount = 0;
        var stableCount = 0;

        foreach (var trend in trends)
        {
            switch (trend.Direction)
            {
                case TrendDirection.Increasing:
                    increasingCount++;
                    break;
                case TrendDirection.Decreasing:
                    decreasingCount++;
                    break;
                case TrendDirection.Stable:
                    stableCount++;
                    break;
            }
        }

        if (increasingCount > decreasingCount && increasingCount > stableCount)
            return TrendDirection.Increasing;
        if (decreasingCount > increasingCount && decreasingCount > stableCount)
            return TrendDirection.Decreasing;

        return TrendDirection.Stable;
    }

    #endregion
}

#region Data Models

/// <summary>
/// 성능 기준선
/// </summary>
public class PerformanceBaseline
{
    public string IndexName { get; set; } = string.Empty;
    public DateTime EstablishedAt { get; set; }
    public double BaselineQueryTimeMs { get; set; }
    public double BaselineRecall { get; set; }
    public long BaselineMemoryUsageBytes { get; set; }
    public double BaselineQPS { get; set; }
    public HnswBenchmarkOptions BenchmarkOptions { get; set; } = new();
}

/// <summary>
/// 성능 회귀 보고서
/// </summary>
public class PerformanceRegressionReport
{
    public string IndexName { get; set; } = string.Empty;
    public DateTime TestTimestamp { get; set; }
    public PerformanceBaseline Baseline { get; set; } = new();
    public HnswBenchmarkResult CurrentPerformance { get; set; } = new();
    public List<PerformanceRegression> Regressions { get; set; } = new();
    public RegressionSeverity OverallRegressionSeverity { get; set; }
}

/// <summary>
/// 성능 회귀
/// </summary>
public class PerformanceRegression
{
    public string MetricName { get; set; } = string.Empty;
    public bool HasRegression { get; set; }
    public double BaselineValue { get; set; }
    public double CurrentValue { get; set; }
    public double DegradationPercent { get; set; }
    public RegressionSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 연속 모니터링 보고서
/// </summary>
public class ContinuousMonitoringReport
{
    public string IndexName { get; set; } = string.Empty;
    public DateTime MonitoringStartTime { get; set; }
    public DateTime MonitoringEndTime { get; set; }
    public TimeSpan MonitoringDuration { get; set; }
    public TimeSpan ActualMonitoringDuration { get; set; }
    public TimeSpan SampleInterval { get; set; }
    public List<PerformanceSample> PerformanceSamples { get; set; } = new();
    public List<PerformanceAnomaly> DetectedAnomalies { get; set; } = new();
    public MonitoringSummary Summary { get; set; } = new();
}

/// <summary>
/// 성능 샘플
/// </summary>
public class PerformanceSample
{
    public int SampleNumber { get; set; }
    public DateTime Timestamp { get; set; }
    public IndexPerformanceMetrics Metrics { get; set; } = new();
}

/// <summary>
/// 성능 이상
/// </summary>
public class PerformanceAnomaly
{
    public DateTime DetectedAt { get; set; }
    public string AnomalyType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AnomalySeverity Severity { get; set; }
    public double Value { get; set; }
}

/// <summary>
/// 모니터링 요약
/// </summary>
public class MonitoringSummary
{
    public double AverageEfficiencyScore { get; set; }
    public double MinEfficiencyScore { get; set; }
    public double MaxEfficiencyScore { get; set; }
    public long TotalIndexScans { get; set; }
    public double AverageIndexScansPerSample { get; set; }
    public int CriticalAnomalies { get; set; }
    public int MediumAnomalies { get; set; }
    public int LowAnomalies { get; set; }
}

/// <summary>
/// 성능 트렌드 분석
/// </summary>
public class PerformanceTrendAnalysis
{
    public DateTime AnalysisTimestamp { get; set; }
    public TimeSpan AnalysisWindow { get; set; }
    public int SampleCount { get; set; }
    public Dictionary<string, TrendInfo> Trends { get; set; } = new();
    public TrendDirection OverallTrend { get; set; }
}

/// <summary>
/// 트렌드 정보
/// </summary>
public class TrendInfo
{
    public string MetricName { get; set; } = string.Empty;
    public TrendDirection Direction { get; set; }
    public double ChangePercent { get; set; }
    public double Confidence { get; set; }
}

/// <summary>
/// 회귀 심각도
/// </summary>
public enum RegressionSeverity
{
    None,
    Minor,
    Major,
    Critical
}

/// <summary>
/// 이상 심각도
/// </summary>
public enum AnomalySeverity
{
    Low,
    Medium,
    High
}

/// <summary>
/// 트렌드 방향
/// </summary>
public enum TrendDirection
{
    Decreasing,
    Stable,
    Increasing
}

#endregion