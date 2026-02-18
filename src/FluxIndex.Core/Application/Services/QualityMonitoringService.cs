using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// 실시간 품질 모니터링 서비스 구현
/// 메모리 기반 고성능 품질 모니터링 및 알림 시스템
/// </summary>
public partial class QualityMonitoringService : IQualityMonitoringService, IDisposable
{
    private readonly ILogger<QualityMonitoringService> _logger;
    private readonly ConcurrentQueue<QualityMetrics> _metricsBuffer;
    private readonly ConcurrentDictionary<string, QualityAlert> _activeAlerts;
    private readonly SemaphoreSlim _processingLock;
    private readonly System.Timers.Timer _processingTimer;

    private QualityThresholds _thresholds;
    private bool _isMonitoring;
    private volatile bool _disposed;

    // 고성능 메트릭 저장소 (시간 기반 슬라이딩 윈도우)
    private readonly ConcurrentDictionary<DateTime, QualityMetrics> _timeSeriesData;
    private readonly TimeSpan _dataRetentionPeriod = TimeSpan.FromHours(24);

    public QualityMonitoringService(ILogger<QualityMonitoringService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsBuffer = new ConcurrentQueue<QualityMetrics>();
        _activeAlerts = new ConcurrentDictionary<string, QualityAlert>();
        _timeSeriesData = new ConcurrentDictionary<DateTime, QualityMetrics>();
        _processingLock = new SemaphoreSlim(1, 1);

        // 기본 임계값 설정
        _thresholds = new QualityThresholds
        {
            MaxResponseTimeMs = 250,  // Phase 7.4 목표 기준
            MinResultCount = 6,       // Phase 7.3 목표 기준
            MinQualityScore = 85,     // 높은 품질 기준
            MinSuccessRate = 0.98,    // 매우 높은 성공률 기준
            MinCacheHitRate = 0.8,    // 높은 캐시 효율 기준
            MinDiversityScore = 0.7   // 다양성 보장 기준
        };

        // 5초마다 메트릭 처리 및 정리
        _processingTimer = new System.Timers.Timer(5000);
        _processingTimer.Elapsed += ProcessMetricsAsync;
        _processingTimer.AutoReset = true;

        LogQualityMonitoring12(_logger);
    }

    public async Task<QualityMetrics> EvaluateSearchQualityAsync(
        string query,
        IReadOnlyList<SearchResult> results,
        TimeSpan responseTime,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var metrics = new QualityMetrics
        {
            Query = query,
            ResponseTimeMs = responseTime.TotalMilliseconds,
            ResultCount = results.Count,
            IsSuccessful = results.Count != 0,
            Metadata = metadata ?? new Dictionary<string, object>()
        };

        if (results.Count != 0)
        {
            // 품질 메트릭 계산
            metrics.AverageSimilarity = results.Average(r => r.Score);
            metrics.DiversityScore = CalculateDiversityScore(results);
            metrics.CacheHit = metadata?.ContainsKey("cache_hit") == true && (bool)metadata["cache_hit"];
            metrics.SearchStrategy = metadata?.GetValueOrDefault("search_strategy")?.ToString() ?? "unknown";

            // 전체 품질 점수 계산 (0-100)
            metrics.QualityScore = CalculateOverallQualityScore(metrics);
        }

        // 메트릭을 버퍼에 추가 (비동기 처리용)
        _metricsBuffer.Enqueue(metrics);

        // 실시간 경고 검사
        await CheckQualityAlertsAsync(metrics, cancellationToken);

        if (_logger.IsEnabled(LogLevel.Information))
            LogQualityMonitoring11(_logger, query, metrics.QualityScore);

        return metrics;
    }

    public async Task<QualityDashboard> GetRealTimeMetricsAsync(
        TimeSpan timeWindow,
        CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTime.UtcNow - timeWindow;
        var relevantMetrics = _timeSeriesData.Values
            .Where(m => m.Timestamp >= cutoffTime)
            .ToList();

        if (relevantMetrics.Count == 0)
        {
            return new QualityDashboard { TimeWindow = timeWindow };
        }

        var successfulMetrics = relevantMetrics.Where(m => m.IsSuccessful).ToList();
        var responseTimes = relevantMetrics.Select(m => m.ResponseTimeMs).ToList();

        var dashboard = new QualityDashboard
        {
            TimeWindow = timeWindow,
            TotalQueries = relevantMetrics.Count,
            SuccessfulQueries = successfulMetrics.Count,
            AverageResponseTimeMs = responseTimes.Average(),
            P95ResponseTimeMs = CalculatePercentile(responseTimes, 0.95),
            P99ResponseTimeMs = CalculatePercentile(responseTimes, 0.99),
            AverageResultCount = successfulMetrics.Count != 0 ? successfulMetrics.Average(m => m.ResultCount) : 0,
            AverageQualityScore = successfulMetrics.Count != 0 ? successfulMetrics.Average(m => m.QualityScore) : 0,
            CacheHitRate = relevantMetrics.Count(m => m.CacheHit) / (double)relevantMetrics.Count,
            AverageDiversityScore = successfulMetrics.Count != 0 ? successfulMetrics.Average(m => m.DiversityScore) : 0,
            ActiveAlerts = _activeAlerts.Count(kvp => !kvp.Value.IsResolved),
            TopQueries = GetTopQueries(relevantMetrics, 10),
            RecentMetrics = relevantMetrics.OrderByDescending(m => m.Timestamp).Take(50).ToList()
        };

        if (_logger.IsEnabled(LogLevel.Information))
            LogQualityMonitoring10(_logger, dashboard.TotalQueries, dashboard.SuccessRate);

        return dashboard;
    }

    public Task SetQualityThresholdsAsync(QualityThresholds thresholds, CancellationToken cancellationToken = default)
    {
        _thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));

        if (_logger.IsEnabled(LogLevel.Information))
            LogQualityMonitoring9(_logger, _thresholds.MaxResponseTimeMs, _thresholds.MinResultCount, _thresholds.MinQualityScore);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QualityAlert>> GetQualityAlertsAsync(
        AlertSeverity? severity = null,
        TimeSpan? timeWindow = null,
        CancellationToken cancellationToken = default)
    {
        var alerts = _activeAlerts.Values.AsEnumerable();

        if (severity.HasValue)
        {
            alerts = alerts.Where(a => a.Severity == severity.Value);
        }

        if (timeWindow.HasValue)
        {
            var cutoffTime = DateTime.UtcNow - timeWindow.Value;
            alerts = alerts.Where(a => a.CreatedAt >= cutoffTime);
        }

        var result = alerts.OrderByDescending(a => a.CreatedAt).ToList();

        if (_logger.IsEnabled(LogLevel.Information))
            LogQualityMonitoring8(_logger, result.Count, severity);

        return Task.FromResult<IReadOnlyList<QualityAlert>>(result);
    }

    public async Task<QualityTrends> AnalyzeQualityTrendsAsync(
        TimeSpan period,
        TimeSpan granularity,
        CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTime.UtcNow - period;
        var relevantMetrics = _timeSeriesData.Values
            .Where(m => m.Timestamp >= cutoffTime)
            .OrderBy(m => m.Timestamp)
            .ToList();

        if (relevantMetrics.Count == 0)
        {
            return new QualityTrends { Period = period };
        }

        // 시간대별 데이터 포인트 생성
        var dataPoints = new List<QualityDataPoint>();
        var startTime = cutoffTime;

        while (startTime < DateTime.UtcNow)
        {
            var endTime = startTime + granularity;
            var periodMetrics = relevantMetrics
                .Where(m => m.Timestamp >= startTime && m.Timestamp < endTime)
                .ToList();

            if (periodMetrics.Count != 0)
            {
                var successfulMetrics = periodMetrics.Where(m => m.IsSuccessful).ToList();
                dataPoints.Add(new QualityDataPoint
                {
                    Timestamp = startTime,
                    AvgResponseTime = periodMetrics.Average(m => m.ResponseTimeMs),
                    AvgQualityScore = successfulMetrics.Count != 0 ? successfulMetrics.Average(m => m.QualityScore) : 0,
                    AvgResultCount = successfulMetrics.Count != 0 ? successfulMetrics.Average(m => m.ResultCount) : 0,
                    SuccessRate = periodMetrics.Count > 0 ? (double)successfulMetrics.Count / periodMetrics.Count : 0,
                    CacheHitRate = periodMetrics.Count > 0 ? (double)periodMetrics.Count(m => m.CacheHit) / periodMetrics.Count : 0
                });
            }

            startTime = endTime;
        }

        // 트렌드 방향 분석
        var trendDirection = AnalyzeTrendDirection(dataPoints);
        var insights = GenerateInsights(dataPoints, relevantMetrics);

        return new QualityTrends
        {
            Period = period,
            DataPoints = dataPoints,
            OverallTrend = trendDirection,
            KeyInsights = insights
        };
    }

    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_isMonitoring)
        {
            LogQualityMonitoring7(_logger);
            return;
        }

        _isMonitoring = true;
        _processingTimer.Start();

        LogQualityMonitoring6(_logger);
    }

    public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (!_isMonitoring)
        {
            return;
        }

        _isMonitoring = false;
        _processingTimer.Stop();

        // 남은 메트릭 처리
        await ProcessPendingMetricsAsync();

        LogQualityMonitoring5(_logger);
    }

    private async void ProcessMetricsAsync(object? sender, ElapsedEventArgs e)
    {
        if (_disposed || !_isMonitoring)
            return;

        try
        {
            await ProcessPendingMetricsAsync();
            CleanOldData();
        }
        catch (Exception ex)
        {
            LogQualityMonitoring4(_logger, ex);
        }
    }

    private async Task ProcessPendingMetricsAsync()
    {
        if (!await _processingLock.WaitAsync(100))
            return;

        try
        {
            var processedCount = 0;
            while (_metricsBuffer.TryDequeue(out var metrics))
            {
                _timeSeriesData[metrics.Timestamp] = metrics;
                processedCount++;

                if (processedCount >= 1000) // 배치 처리 제한
                    break;
            }

            if (processedCount > 0)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                    LogQualityMonitoring3(_logger, processedCount);
            }
        }
        finally
        {
            _processingLock.Release();
        }
    }

    private void CleanOldData()
    {
        var cutoffTime = DateTime.UtcNow - _dataRetentionPeriod;
        var keysToRemove = _timeSeriesData.Keys
            .Where(k => k < cutoffTime)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _timeSeriesData.TryRemove(key, out _);
        }

        // 해결된 오래된 경고 정리
        var alertsToRemove = _activeAlerts.Values
            .Where(a => a.IsResolved && a.ResolvedAt < cutoffTime)
            .Select(a => a.Id)
            .ToList();

        foreach (var alertId in alertsToRemove)
        {
            _activeAlerts.TryRemove(alertId, out _);
        }
    }

    private async Task CheckQualityAlertsAsync(QualityMetrics metrics, CancellationToken cancellationToken)
    {
        var alerts = new List<QualityAlert>();

        // 응답 시간 검사
        if (metrics.ResponseTimeMs > _thresholds.MaxResponseTimeMs)
        {
            alerts.Add(CreateAlert(AlertType.Performance, AlertSeverity.Warning,
                "높은 응답 시간 감지",
                $"응답 시간이 임계값을 초과했습니다: {metrics.ResponseTimeMs:F1}ms > {_thresholds.MaxResponseTimeMs}ms",
                "ResponseTime", metrics.ResponseTimeMs, _thresholds.MaxResponseTimeMs));
        }

        // 결과 수 검사
        if (metrics.IsSuccessful && metrics.ResultCount < _thresholds.MinResultCount)
        {
            alerts.Add(CreateAlert(AlertType.Quality, AlertSeverity.Warning,
                "낮은 결과 수 감지",
                $"검색 결과 수가 임계값보다 적습니다: {metrics.ResultCount}개 < {_thresholds.MinResultCount}개",
                "ResultCount", metrics.ResultCount, _thresholds.MinResultCount));
        }

        // 품질 점수 검사
        if (metrics.IsSuccessful && metrics.QualityScore < _thresholds.MinQualityScore)
        {
            alerts.Add(CreateAlert(AlertType.Quality, AlertSeverity.Critical,
                "낮은 품질 점수 감지",
                $"품질 점수가 임계값보다 낮습니다: {metrics.QualityScore:F1} < {_thresholds.MinQualityScore}",
                "QualityScore", metrics.QualityScore, _thresholds.MinQualityScore));
        }

        // 검색 실패 검사
        if (!metrics.IsSuccessful)
        {
            alerts.Add(CreateAlert(AlertType.Availability, AlertSeverity.Critical,
                "검색 실패 감지",
                $"검색이 실패했습니다: {metrics.ErrorMessage ?? "알 수 없는 오류"}",
                "Success", 0, 1));
        }

        // 경고 저장
        foreach (var alert in alerts)
        {
            _activeAlerts[alert.Id] = alert;
            LogQualityMonitoring2(_logger, alert.Title, alert.Message);
        }
    }

    private static QualityAlert CreateAlert(AlertType type, AlertSeverity severity, string title, string message,
        string metricName, double currentValue, double thresholdValue)
    {
        return new QualityAlert
        {
            Type = type,
            Severity = severity,
            Title = title,
            Message = message,
            MetricName = metricName,
            CurrentValue = currentValue,
            ThresholdValue = thresholdValue
        };
    }

    private static double CalculateDiversityScore(IReadOnlyList<SearchResult> results)
    {
        if (results.Count <= 1) return 1.0;

        var uniqueSources = results.Select(r => r.DocumentId).Distinct().Count();
        return (double)uniqueSources / results.Count;
    }

    private double CalculateOverallQualityScore(QualityMetrics metrics)
    {
        var score = 0.0;

        // 응답 시간 점수 (40점)
        if (metrics.ResponseTimeMs <= _thresholds.MaxResponseTimeMs)
            score += 40 * (1.0 - Math.Min(metrics.ResponseTimeMs / _thresholds.MaxResponseTimeMs, 1.0));

        // 결과 수 점수 (25점)
        if (metrics.ResultCount >= _thresholds.MinResultCount)
            score += 25;
        else
            score += 25 * ((double)metrics.ResultCount / _thresholds.MinResultCount);

        // 유사도 점수 (20점)
        score += 20 * metrics.AverageSimilarity;

        // 다양성 점수 (15점)
        score += 15 * metrics.DiversityScore;

        return Math.Min(100, Math.Max(0, score));
    }

    private static double CalculatePercentile(List<double> values, double percentile)
    {
        if (values.Count == 0) return 0;

        var sorted = values.OrderBy(x => x).ToList();
        var index = (int)(percentile * (sorted.Count - 1));
        return sorted[index];
    }

    private static List<QueryFrequency> GetTopQueries(List<QualityMetrics> metrics, int count)
    {
        return metrics
            .Where(m => m.IsSuccessful)
            .GroupBy(m => m.Query)
            .Select(g => new QueryFrequency
            {
                Query = g.Key,
                Frequency = g.Count(),
                AverageQualityScore = g.Average(m => m.QualityScore)
            })
            .OrderByDescending(q => q.Frequency)
            .Take(count)
            .ToList();
    }

    private static TrendDirection AnalyzeTrendDirection(List<QualityDataPoint> dataPoints)
    {
        if (dataPoints.Count < 2) return TrendDirection.Stable;

        var recentPoints = dataPoints.TakeLast(Math.Min(10, dataPoints.Count / 2)).ToList();
        var olderPoints = dataPoints.Take(Math.Min(10, dataPoints.Count / 2)).ToList();

        if (recentPoints.Count == 0 || olderPoints.Count == 0) return TrendDirection.Stable;

        var recentAvgQuality = recentPoints.Average(p => p.AvgQualityScore);
        var olderAvgQuality = olderPoints.Average(p => p.AvgQualityScore);

        var qualityChange = (recentAvgQuality - olderAvgQuality) / olderAvgQuality;

        if (qualityChange > 0.05) return TrendDirection.Improving;
        if (qualityChange < -0.05) return TrendDirection.Degrading;
        return TrendDirection.Stable;
    }

    private List<string> GenerateInsights(List<QualityDataPoint> dataPoints, List<QualityMetrics> allMetrics)
    {
        var insights = new List<string>();

        if (dataPoints.Count == 0) return insights;

        // 성능 인사이트
        var avgResponseTime = dataPoints.Average(p => p.AvgResponseTime);
        if (avgResponseTime < _thresholds.MaxResponseTimeMs)
            insights.Add($"🚀 응답 시간 우수: 평균 {avgResponseTime:F1}ms (목표: {_thresholds.MaxResponseTimeMs}ms)");
        else
            insights.Add($"⚠️ 응답 시간 개선 필요: 평균 {avgResponseTime:F1}ms");

        // 품질 인사이트
        var avgQuality = dataPoints.Where(p => p.AvgQualityScore > 0).Average(p => p.AvgQualityScore);
        if (avgQuality > _thresholds.MinQualityScore)
            insights.Add($"✨ 품질 점수 우수: 평균 {avgQuality:F1}점");
        else
            insights.Add($"📉 품질 점수 개선 필요: 평균 {avgQuality:F1}점");

        // 캐시 인사이트
        var avgCacheHit = dataPoints.Average(p => p.CacheHitRate);
        if (avgCacheHit > _thresholds.MinCacheHitRate)
            insights.Add($"⚡ 캐시 효율 우수: {avgCacheHit:P1} 히트율");
        else
            insights.Add($"💾 캐시 효율 개선 필요: {avgCacheHit:P1} 히트율");

        return insights;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _processingTimer?.Stop();
        _processingTimer?.Dispose();
        _processingLock?.Dispose();

        GC.SuppressFinalize(this);

        LogQualityMonitoring1(_logger);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "품질 모니터링 서비스 초기화 완료")]
    private static partial void LogQualityMonitoring12(ILogger logger);
    [LoggerMessage(Level = LogLevel.Debug, Message = "품질 메트릭 평가 완료: Query={Query}, Quality={QualityScore:F1}")]
    private static partial void LogQualityMonitoring11(ILogger logger, string query, double qualityScore);
    [LoggerMessage(Level = LogLevel.Debug, Message = "실시간 대시보드 생성 완료: {TotalQueries}개 쿼리, {SuccessRate:P1} 성공률")]
    private static partial void LogQualityMonitoring10(ILogger logger, long totalQueries, double successRate);
    [LoggerMessage(Level = LogLevel.Information, Message = "품질 임계값 업데이트: 응답시간={MaxResponseTime}ms, 최소결과={MinResults}개, 품질점수={MinQuality}")]
    private static partial void LogQualityMonitoring9(ILogger logger, double maxResponseTime, double minResults, double minQuality);
    [LoggerMessage(Level = LogLevel.Debug, Message = "품질 경고 조회 완료: {Count}개 ({Severity})")]
    private static partial void LogQualityMonitoring8(ILogger logger, int count, AlertSeverity? severity);
    [LoggerMessage(Level = LogLevel.Warning, Message = "품질 모니터링이 이미 실행 중입니다")]
    private static partial void LogQualityMonitoring7(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "품질 모니터링 시작")]
    private static partial void LogQualityMonitoring6(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "품질 모니터링 중지")]
    private static partial void LogQualityMonitoring5(ILogger logger);
    [LoggerMessage(Level = LogLevel.Error, Message = "메트릭 처리 중 오류 발생")]
    private static partial void LogQualityMonitoring4(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Debug, Message = "메트릭 {Count}개 처리 완료")]
    private static partial void LogQualityMonitoring3(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Warning, Message = "품질 경고 생성: {Title} - {Message}")]
    private static partial void LogQualityMonitoring2(ILogger logger, string title, string message);
    [LoggerMessage(Level = LogLevel.Information, Message = "품질 모니터링 서비스 리소스 정리 완료")]
    private static partial void LogQualityMonitoring1(ILogger logger);

    #endregion
}
