using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Interfaces;

/// <summary>
/// 쿼리 복잡도 분석 및 전략 결정 인터페이스
/// </summary>
public interface IQueryComplexityAnalyzer
{
    /// <summary>
    /// 쿼리 복잡도 분석
    /// </summary>
    Task<QueryAnalysis> AnalyzeAsync(string query, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 분석 결과 기반 검색 전략 추천
    /// </summary>
    SearchStrategy RecommendStrategy(QueryAnalysis analysis);
    
    /// <summary>
    /// 쿼리 유형별 성능 통계 업데이트
    /// </summary>
    Task UpdatePerformanceAsync(string query, QueryAnalysis analysis, SearchResult result, CancellationToken cancellationToken = default);
}

/// <summary>
/// 쿼리 분석 결과
/// </summary>
public class QueryAnalysis
{
    public QueryType Type { get; set; }
    public ComplexityLevel Complexity { get; set; }
    public double Specificity { get; set; } // 0.0 (일반적) ~ 1.0 (매우 구체적)
    public List<string> Entities { get; set; } = new();
    public List<string> Concepts { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public QueryIntent Intent { get; set; }
    public Language Language { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    // 추론형 쿼리 특성
    public bool RequiresReasoning { get; set; }
    public bool HasTemporalContext { get; set; }
    public bool HasComparativeContext { get; set; }
    public bool IsMultiHop { get; set; }
    
    // 성능 예측
    public TimeSpan EstimatedProcessingTime { get; set; }
    public double ConfidenceScore { get; set; } // 분석 신뢰도
}

/// <summary>
/// 쿼리 유형
/// </summary>
public enum QueryType
{
    /// <summary>단순 키워드 검색</summary>
    SimpleKeyword,
    
    /// <summary>자연어 질문</summary>
    NaturalQuestion,
    
    /// <summary>복합 조건 검색</summary>
    ComplexSearch,
    
    /// <summary>추론 필요 질문</summary>
    ReasoningQuery,
    
    /// <summary>비교/대조 질문</summary>
    ComparisonQuery,
    
    /// <summary>시간적 컨텍스트 질문</summary>
    TemporalQuery,
    
    /// <summary>멀티홉 추론</summary>
    MultiHopQuery
}

/// <summary>
/// 복잡도 수준
/// </summary>
public enum ComplexityLevel
{
    /// <summary>단순 (키워드 매칭)</summary>
    Simple = 1,
    
    /// <summary>보통 (의미 검색)</summary>
    Moderate = 2,
    
    /// <summary>복잡 (다단계 처리)</summary>
    Complex = 3,
    
    /// <summary>매우 복잡 (추론 필요)</summary>
    VeryComplex = 4
}

/// <summary>
/// 쿼리 의도
/// </summary>
public enum QueryIntent
{
    /// <summary>정보 검색</summary>
    Informational,
    
    /// <summary>내비게이셀</summary>
    Navigational,
    
    /// <summary>트랜잭셔널</summary>
    Transactional,
    
    /// <summary>분석적</summary>
    Analytical,
    
    /// <summary>탐색적</summary>
    Exploratory
}

/// <summary>
/// 언어 정보
/// </summary>
public enum Language
{
    Korean,
    English,
    Mixed,
    Other
}

/// <summary>
/// 검색 전략
/// </summary>
public enum SearchStrategy
{
    /// <summary>직접 벡터 검색</summary>
    DirectVector,
    
    /// <summary>키워드 검색</summary>
    KeywordOnly,
    
    /// <summary>하이브리드 검색</summary>
    Hybrid,
    
    /// <summary>다중 쿼리 확장</summary>
    MultiQuery,
    
    /// <summary>HyDE 가설 생성</summary>
    HyDE,
    
    /// <summary>Step-Back 추상화</summary>
    StepBack,
    
    /// <summary>2단계 재순위화</summary>
    TwoStage,
    
    /// <summary>적응형 검색</summary>
    Adaptive,
    
    /// <summary>Self-RAG</summary>
    SelfRAG
}

/// <summary>
/// 검색 결과 (성능 피드백용)
/// </summary>
public class QueryAnalysisResult
{
    public int ResultCount { get; set; }
    public double RelevanceScore { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public bool UserSatisfied { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
}