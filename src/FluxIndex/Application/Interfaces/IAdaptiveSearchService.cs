using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Domain.Entities;

namespace FluxIndex.Application.Interfaces;

/// <summary>
/// 적응형 검색 서비스 인터페이스
/// </summary>
public interface IAdaptiveSearchService
{
    /// <summary>
    /// 쿼리 분석 기반 적응형 검색
    /// </summary>
    Task<AdaptiveSearchResult> SearchAsync(
        string query, 
        AdaptiveSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 검색 전략 강제 지정
    /// </summary>
    Task<AdaptiveSearchResult> SearchWithStrategyAsync(
        string query,
        SearchStrategy strategy,
        AdaptiveSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 성능 피드백 업데이트
    /// </summary>
    Task UpdateFeedbackAsync(
        string query,
        AdaptiveSearchResult result,
        UserFeedback feedback,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 검색 전략 성능 통계 조회
    /// </summary>
    Task<StrategyPerformanceReport> GetPerformanceReportAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 적응형 검색 옵션
/// </summary>
public class AdaptiveSearchOptions
{
    /// <summary>최대 결과 개수</summary>
    public int MaxResults { get; set; } = 20;

    /// <summary>최소 유사도 점수</summary>
    public double MinScore { get; set; } = 0.0;

    /// <summary>검색 타임아웃</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>강제 전략 지정</summary>
    public SearchStrategy? ForceStrategy { get; set; }

    /// <summary>A/B 테스트 모드</summary>
    public bool EnableABTest { get; set; } = false;

    /// <summary>상세 분석 로깅</summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>캐싱 사용</summary>
    public bool UseCache { get; set; } = true;

    /// <summary>사용자 컨텍스트</summary>
    public Dictionary<string, object> UserContext { get; set; } = new();
}

/// <summary>
/// 적응형 검색 결과
/// </summary>
public class AdaptiveSearchResult
{
    /// <summary>검색 결과 문서들</summary>
    public IEnumerable<Document> Documents { get; set; } = Enumerable.Empty<Document>();

    /// <summary>사용된 검색 전략</summary>
    public SearchStrategy UsedStrategy { get; set; }

    /// <summary>쿼리 분석 결과</summary>
    public QueryAnalysis QueryAnalysis { get; set; } = new();

    /// <summary>검색 성능 지표</summary>
    public SearchPerformanceMetrics Performance { get; set; } = new();

    /// <summary>전략 변경 이유</summary>
    public List<string> StrategyReasons { get; set; } = new();

    /// <summary>A/B 테스트 정보</summary>
    public ABTestInfo? ABTestInfo { get; set; }

    /// <summary>검색 신뢰도 점수</summary>
    public double ConfidenceScore { get; set; }

    /// <summary>추가 메타데이터</summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 검색 성능 지표
/// </summary>
public class SearchPerformanceMetrics
{
    /// <summary>총 처리 시간</summary>
    public TimeSpan TotalTime { get; set; }

    /// <summary>쿼리 분석 시간</summary>
    public TimeSpan AnalysisTime { get; set; }

    /// <summary>검색 실행 시간</summary>
    public TimeSpan SearchTime { get; set; }

    /// <summary>후처리 시간</summary>
    public TimeSpan PostProcessingTime { get; set; }

    /// <summary>결과 개수</summary>
    public int ResultCount { get; set; }

    /// <summary>평균 관련성 점수</summary>
    public double AverageRelevanceScore { get; set; }

    /// <summary>캐시 히트 여부</summary>
    public bool CacheHit { get; set; }

    /// <summary>사용된 리소스</summary>
    public Dictionary<string, object> ResourceUsage { get; set; } = new();
}

/// <summary>
/// A/B 테스트 정보
/// </summary>
public class ABTestInfo
{
    /// <summary>테스트 ID</summary>
    public string TestId { get; set; } = string.Empty;

    /// <summary>그룹 (A 또는 B)</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>대안 전략</summary>
    public SearchStrategy AlternativeStrategy { get; set; }

    /// <summary>대안 결과</summary>
    public IEnumerable<Document>? AlternativeResults { get; set; }

    /// <summary>성능 비교</summary>
    public Dictionary<string, double> PerformanceComparison { get; set; } = new();
}

/// <summary>
/// 사용자 피드백
/// </summary>
public class UserFeedback
{
    /// <summary>만족도 (1-5)</summary>
    public int Satisfaction { get; set; }

    /// <summary>관련성 (1-5)</summary>
    public int Relevance { get; set; }

    /// <summary>완전성 (1-5)</summary>
    public int Completeness { get; set; }

    /// <summary>응답 시간 만족도 (1-5)</summary>
    public int ResponseTime { get; set; }

    /// <summary>클릭한 결과 인덱스들</summary>
    public List<int> ClickedResults { get; set; } = new();

    /// <summary>읽은 결과 인덱스들</summary>
    public List<int> ReadResults { get; set; } = new();

    /// <summary>자유 텍스트 피드백</summary>
    public string? Comments { get; set; }

    /// <summary>타임스탬프</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 전략 성능 보고서
/// </summary>
public class StrategyPerformanceReport
{
    /// <summary>전략별 성능 통계</summary>
    public Dictionary<SearchStrategy, StrategyMetrics> StrategyMetrics { get; set; } = new();

    /// <summary>쿼리 타입별 최적 전략</summary>
    public Dictionary<QueryType, SearchStrategy> OptimalStrategies { get; set; } = new();

    /// <summary>전체 통계</summary>
    public OverallStatistics Overall { get; set; } = new();

    /// <summary>최근 트렌드</summary>
    public List<TrendData> Trends { get; set; } = new();

    /// <summary>보고서 생성 시간</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 전략별 성능 지표
/// </summary>
public class StrategyMetrics
{
    /// <summary>총 사용 횟수</summary>
    public int TotalUses { get; set; }

    /// <summary>성공률</summary>
    public double SuccessRate { get; set; }

    /// <summary>평균 처리 시간</summary>
    public TimeSpan AverageProcessingTime { get; set; }

    /// <summary>평균 만족도</summary>
    public double AverageSatisfaction { get; set; }

    /// <summary>평균 관련성 점수</summary>
    public double AverageRelevance { get; set; }

    /// <summary>사용 빈도 순위</summary>
    public int UsageRank { get; set; }

    /// <summary>성능 순위</summary>
    public int PerformanceRank { get; set; }
}

/// <summary>
/// 전체 통계
/// </summary>
public class OverallStatistics
{
    /// <summary>총 검색 횟수</summary>
    public long TotalSearches { get; set; }

    /// <summary>평균 처리 시간</summary>
    public TimeSpan AverageProcessingTime { get; set; }

    /// <summary>캐시 히트율</summary>
    public double CacheHitRate { get; set; }

    /// <summary>전체 만족도</summary>
    public double OverallSatisfaction { get; set; }

    /// <summary>최다 사용 전략</summary>
    public SearchStrategy MostUsedStrategy { get; set; }

    /// <summary>최고 성능 전략</summary>
    public SearchStrategy BestPerformingStrategy { get; set; }
}

/// <summary>
/// 트렌드 데이터
/// </summary>
public class TrendData
{
    /// <summary>날짜</summary>
    public DateTime Date { get; set; }

    /// <summary>검색 횟수</summary>
    public int SearchCount { get; set; }

    /// <summary>평균 만족도</summary>
    public double AverageSatisfaction { get; set; }

    /// <summary>주요 전략</summary>
    public SearchStrategy PrimaryStrategy { get; set; }
}