using FluxIndex.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 실시간 품질 모니터링 서비스 인터페이스
/// RAG 시스템의 검색 품질을 지속적으로 모니터링하고 성능 지표를 추적
/// </summary>
public interface IQualityMonitoringService
{
    /// <summary>
    /// 검색 결과의 품질을 실시간으로 평가
    /// </summary>
    /// <param name="query">검색 쿼리</param>
    /// <param name="results">검색 결과</param>
    /// <param name="responseTime">응답 시간</param>
    /// <param name="metadata">추가 메타데이터</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task<QualityMetrics> EvaluateSearchQualityAsync(
        string query,
        IReadOnlyList<SearchResult> results,
        TimeSpan responseTime,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 실시간 품질 메트릭 조회
    /// </summary>
    /// <param name="timeWindow">조회할 시간 창 (예: 최근 1시간)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task<QualityDashboard> GetRealTimeMetricsAsync(
        TimeSpan timeWindow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 품질 임계값 설정
    /// </summary>
    /// <param name="thresholds">품질 임계값 설정</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task SetQualityThresholdsAsync(
        QualityThresholds thresholds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 품질 경고 조회
    /// </summary>
    /// <param name="severity">경고 심각도</param>
    /// <param name="timeWindow">조회할 시간 창</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task<IReadOnlyList<QualityAlert>> GetQualityAlertsAsync(
        AlertSeverity? severity = null,
        TimeSpan? timeWindow = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 품질 트렌드 분석
    /// </summary>
    /// <param name="period">분석 기간</param>
    /// <param name="granularity">데이터 세분화 수준</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task<QualityTrends> AnalyzeQualityTrendsAsync(
        TimeSpan period,
        TimeSpan granularity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 품질 모니터링 시작
    /// </summary>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 품질 모니터링 중지
    /// </summary>
    Task StopMonitoringAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 검색 품질 메트릭
/// </summary>
public class QualityMetrics
{
    /// <summary>
    /// 측정 시간
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 검색 쿼리
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// 응답 시간 (밀리초)
    /// </summary>
    public double ResponseTimeMs { get; set; }

    /// <summary>
    /// 검색 결과 수
    /// </summary>
    public int ResultCount { get; set; }

    /// <summary>
    /// 평균 유사도 점수
    /// </summary>
    public double AverageSimilarity { get; set; }

    /// <summary>
    /// 결과 다양성 점수 (0-1)
    /// </summary>
    public double DiversityScore { get; set; }

    /// <summary>
    /// 캐시 히트 여부
    /// </summary>
    public bool CacheHit { get; set; }

    /// <summary>
    /// 검색 전략
    /// </summary>
    public string SearchStrategy { get; set; } = string.Empty;

    /// <summary>
    /// 품질 점수 (0-100)
    /// </summary>
    public double QualityScore { get; set; }

    /// <summary>
    /// 성공 여부
    /// </summary>
    public bool IsSuccessful { get; set; }

    /// <summary>
    /// 오류 메시지 (있을 경우)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 추가 메타데이터
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 실시간 품질 대시보드 데이터
/// </summary>
public class QualityDashboard
{
    /// <summary>
    /// 대시보드 생성 시간
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 조회 시간 범위
    /// </summary>
    public TimeSpan TimeWindow { get; set; }

    /// <summary>
    /// 총 쿼리 수
    /// </summary>
    public long TotalQueries { get; set; }

    /// <summary>
    /// 성공한 쿼리 수
    /// </summary>
    public long SuccessfulQueries { get; set; }

    /// <summary>
    /// 성공률 (0-1)
    /// </summary>
    public double SuccessRate => TotalQueries > 0 ? (double)SuccessfulQueries / TotalQueries : 0;

    /// <summary>
    /// 평균 응답 시간 (밀리초)
    /// </summary>
    public double AverageResponseTimeMs { get; set; }

    /// <summary>
    /// P95 응답 시간 (밀리초)
    /// </summary>
    public double P95ResponseTimeMs { get; set; }

    /// <summary>
    /// P99 응답 시간 (밀리초)
    /// </summary>
    public double P99ResponseTimeMs { get; set; }

    /// <summary>
    /// 평균 결과 수
    /// </summary>
    public double AverageResultCount { get; set; }

    /// <summary>
    /// 평균 품질 점수
    /// </summary>
    public double AverageQualityScore { get; set; }

    /// <summary>
    /// 캐시 히트율
    /// </summary>
    public double CacheHitRate { get; set; }

    /// <summary>
    /// 평균 다양성 점수
    /// </summary>
    public double AverageDiversityScore { get; set; }

    /// <summary>
    /// 활성 경고 수
    /// </summary>
    public int ActiveAlerts { get; set; }

    /// <summary>
    /// 상위 검색 쿼리
    /// </summary>
    public IReadOnlyList<QueryFrequency> TopQueries { get; set; } = Array.Empty<QueryFrequency>();

    /// <summary>
    /// 최근 품질 메트릭
    /// </summary>
    public IReadOnlyList<QualityMetrics> RecentMetrics { get; set; } = Array.Empty<QualityMetrics>();
}

/// <summary>
/// 품질 임계값 설정
/// </summary>
public class QualityThresholds
{
    /// <summary>
    /// 최소 응답 시간 임계값 (밀리초) - 초과 시 경고
    /// </summary>
    public double MaxResponseTimeMs { get; set; } = 1000;

    /// <summary>
    /// 최소 결과 수 임계값 - 미만 시 경고
    /// </summary>
    public int MinResultCount { get; set; } = 3;

    /// <summary>
    /// 최소 품질 점수 임계값 - 미만 시 경고
    /// </summary>
    public double MinQualityScore { get; set; } = 70;

    /// <summary>
    /// 최소 성공률 임계값 - 미만 시 경고
    /// </summary>
    public double MinSuccessRate { get; set; } = 0.95;

    /// <summary>
    /// 최소 캐시 히트율 임계값 - 미만 시 경고
    /// </summary>
    public double MinCacheHitRate { get; set; } = 0.6;

    /// <summary>
    /// 최소 다양성 점수 임계값 - 미만 시 경고
    /// </summary>
    public double MinDiversityScore { get; set; } = 0.5;
}

/// <summary>
/// 품질 경고
/// </summary>
public class QualityAlert
{
    /// <summary>
    /// 경고 ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 경고 생성 시간
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 경고 유형
    /// </summary>
    public AlertType Type { get; set; }

    /// <summary>
    /// 경고 심각도
    /// </summary>
    public AlertSeverity Severity { get; set; }

    /// <summary>
    /// 경고 제목
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 경고 메시지
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 관련 메트릭
    /// </summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>
    /// 현재 값
    /// </summary>
    public double CurrentValue { get; set; }

    /// <summary>
    /// 임계값
    /// </summary>
    public double ThresholdValue { get; set; }

    /// <summary>
    /// 해결 여부
    /// </summary>
    public bool IsResolved { get; set; }

    /// <summary>
    /// 해결 시간
    /// </summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// 추가 데이터
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new();
}

/// <summary>
/// 품질 트렌드 분석 결과
/// </summary>
public class QualityTrends
{
    /// <summary>
    /// 분석 기간
    /// </summary>
    public TimeSpan Period { get; set; }

    /// <summary>
    /// 시간대별 품질 데이터
    /// </summary>
    public IReadOnlyList<QualityDataPoint> DataPoints { get; set; } = Array.Empty<QualityDataPoint>();

    /// <summary>
    /// 전반적 트렌드 (개선/악화/안정)
    /// </summary>
    public TrendDirection OverallTrend { get; set; }

    /// <summary>
    /// 주요 인사이트
    /// </summary>
    public IReadOnlyList<string> KeyInsights { get; set; } = Array.Empty<string>();
}

/// <summary>
/// 시간대별 품질 데이터 포인트
/// </summary>
public class QualityDataPoint
{
    /// <summary>
    /// 시간
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 평균 응답 시간
    /// </summary>
    public double AvgResponseTime { get; set; }

    /// <summary>
    /// 평균 품질 점수
    /// </summary>
    public double AvgQualityScore { get; set; }

    /// <summary>
    /// 평균 결과 수
    /// </summary>
    public double AvgResultCount { get; set; }

    /// <summary>
    /// 성공률
    /// </summary>
    public double SuccessRate { get; set; }

    /// <summary>
    /// 캐시 히트율
    /// </summary>
    public double CacheHitRate { get; set; }
}

/// <summary>
/// 쿼리 빈도
/// </summary>
public class QueryFrequency
{
    /// <summary>
    /// 쿼리
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// 빈도
    /// </summary>
    public int Frequency { get; set; }

    /// <summary>
    /// 평균 품질 점수
    /// </summary>
    public double AverageQualityScore { get; set; }
}

/// <summary>
/// 경고 유형
/// </summary>
public enum AlertType
{
    Performance,
    Quality,
    Availability,
    Cache,
    Diversity
}

/// <summary>
/// 경고 심각도
/// </summary>
public enum AlertSeverity
{
    Info,
    Warning,
    Critical,
    Emergency
}

/// <summary>
/// 트렌드 방향
/// </summary>
public enum TrendDirection
{
    Improving,
    Stable,
    Degrading
}