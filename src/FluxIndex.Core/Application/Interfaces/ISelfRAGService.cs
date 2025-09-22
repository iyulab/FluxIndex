using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Self-RAG (Self-Reflective Retrieval Augmented Generation) 서비스 인터페이스
/// </summary>
public interface ISelfRAGService
{
    /// <summary>
    /// Self-RAG를 사용한 적응형 검색
    /// </summary>
    Task<SelfRAGResult> SearchAsync(
        string query,
        SelfRAGOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 검색 결과 품질 평가
    /// </summary>
    Task<QualityAssessment> AssessResultQualityAsync(
        string query,
        IEnumerable<Document> results,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 쿼리 개선 제안
    /// </summary>
    Task<QueryRefinementSuggestions> SuggestQueryRefinementsAsync(
        string originalQuery,
        QualityAssessment assessment,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Self-RAG 옵션
/// </summary>
public class SelfRAGOptions
{
    /// <summary>최대 반복 횟수</summary>
    public int MaxIterations { get; set; } = 3;

    /// <summary>품질 임계값 (0.0-1.0)</summary>
    public double QualityThreshold { get; set; } = 0.7;

    /// <summary>최대 결과 개수</summary>
    public int MaxResults { get; set; } = 20;

    /// <summary>최소 결과 개수</summary>
    public int MinResults { get; set; } = 5;

    /// <summary>검색 타임아웃</summary>
    public TimeSpan SearchTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>자동 쿼리 개선 사용</summary>
    public bool EnableAutoRefinement { get; set; } = true;

    /// <summary>컨텍스트 확장 사용</summary>
    public bool EnableContextExpansion { get; set; } = true;

    /// <summary>다중 관점 검색 사용</summary>
    public bool EnableMultiPerspectiveSearch { get; set; } = true;

    /// <summary>상세 로깅</summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>사용자 컨텍스트</summary>
    public Dictionary<string, object> UserContext { get; set; } = new();
}

/// <summary>
/// Self-RAG 검색 결과
/// </summary>
public class SelfRAGResult
{
    /// <summary>최종 검색 결과</summary>
    public IEnumerable<Document> FinalResults { get; set; } = Enumerable.Empty<Document>();

    /// <summary>검색 반복 기록</summary>
    public List<SearchIteration> Iterations { get; set; } = new();

    /// <summary>최종 품질 점수</summary>
    public double FinalQualityScore { get; set; }

    /// <summary>총 처리 시간</summary>
    public TimeSpan TotalProcessingTime { get; set; }

    /// <summary>수행된 개선 작업</summary>
    public List<RefinementAction> RefinementActions { get; set; } = new();

    /// <summary>성공 여부</summary>
    public bool IsSuccessful { get; set; }

    /// <summary>중단 이유</summary>
    public string? TerminationReason { get; set; }

    /// <summary>메타데이터</summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 검색 반복 정보
/// </summary>
public class SearchIteration
{
    /// <summary>반복 번호</summary>
    public int IterationNumber { get; set; }

    /// <summary>사용된 쿼리</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>사용된 검색 전략</summary>
    public SearchStrategy Strategy { get; set; }

    /// <summary>검색 결과</summary>
    public IEnumerable<Document> Results { get; set; } = Enumerable.Empty<Document>();

    /// <summary>품질 평가</summary>
    public QualityAssessment QualityAssessment { get; set; } = new();

    /// <summary>처리 시간</summary>
    public TimeSpan ProcessingTime { get; set; }

    /// <summary>개선 사항</summary>
    public List<string> ImprovementNotes { get; set; } = new();

    /// <summary>다음 반복 계획</summary>
    public string? NextIterationPlan { get; set; }
}

/// <summary>
/// 품질 평가 결과
/// </summary>
public class QualityAssessment
{
    /// <summary>전체 품질 점수 (0.0-1.0)</summary>
    public double OverallScore { get; set; }

    /// <summary>관련성 점수 (0.0-1.0)</summary>
    public double RelevanceScore { get; set; }

    /// <summary>완전성 점수 (0.0-1.0)</summary>
    public double CompletenessScore { get; set; }

    /// <summary>다양성 점수 (0.0-1.0)</summary>
    public double DiversityScore { get; set; }

    /// <summary>신뢰성 점수 (0.0-1.0)</summary>
    public double CredibilityScore { get; set; }

    /// <summary>최신성 점수 (0.0-1.0)</summary>
    public double FreshnessScore { get; set; }

    /// <summary>결과 개수</summary>
    public int ResultCount { get; set; }

    /// <summary>품질 문제점</summary>
    public List<QualityIssue> Issues { get; set; } = new();

    /// <summary>개선 제안</summary>
    public List<ImprovementSuggestion> Suggestions { get; set; } = new();

    /// <summary>평가 근거</summary>
    public Dictionary<string, string> Rationale { get; set; } = new();
}

/// <summary>
/// 품질 문제점
/// </summary>
public class QualityIssue
{
    /// <summary>문제 유형</summary>
    public QualityIssueType Type { get; set; }

    /// <summary>심각도 (1-5)</summary>
    public int Severity { get; set; }

    /// <summary>설명</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>영향받는 결과 인덱스</summary>
    public List<int> AffectedResultIndices { get; set; } = new();

    /// <summary>권장 해결 방법</summary>
    public string? RecommendedAction { get; set; }
}

/// <summary>
/// 품질 문제 유형
/// </summary>
public enum QualityIssueType
{
    /// <summary>관련성 부족</summary>
    InsufficientRelevance,
    
    /// <summary>결과 부족</summary>
    InsufficientResults,
    
    /// <summary>중복된 결과</summary>
    DuplicateResults,
    
    /// <summary>다양성 부족</summary>
    LackOfDiversity,
    
    /// <summary>오래된 정보</summary>
    OutdatedInformation,
    
    /// <summary>신뢰성 부족</summary>
    LowCredibility,
    
    /// <summary>불완전한 답변</summary>
    IncompleteAnswer,
    
    /// <summary>편향된 결과</summary>
    BiasedResults
}

/// <summary>
/// 개선 제안
/// </summary>
public class ImprovementSuggestion
{
    /// <summary>제안 유형</summary>
    public ImprovementType Type { get; set; }

    /// <summary>우선순위 (1-5)</summary>
    public int Priority { get; set; }

    /// <summary>제안 내용</summary>
    public string Suggestion { get; set; } = string.Empty;

    /// <summary>예상 효과</summary>
    public string ExpectedImpact { get; set; } = string.Empty;

    /// <summary>구현 복잡도</summary>
    public ImplementationComplexity Complexity { get; set; }
}

/// <summary>
/// 개선 유형
/// </summary>
public enum ImprovementType
{
    /// <summary>쿼리 수정</summary>
    QueryModification,
    
    /// <summary>검색 전략 변경</summary>
    StrategyChange,
    
    /// <summary>필터 추가</summary>
    AddFilters,
    
    /// <summary>확장 검색</summary>
    ExpandSearch,
    
    /// <summary>재순위화</summary>
    Reranking,
    
    /// <summary>중복 제거</summary>
    Deduplication,
    
    /// <summary>컨텍스트 확장</summary>
    ContextExpansion
}

/// <summary>
/// 구현 복잡도
/// </summary>
public enum ImplementationComplexity
{
    Low = 1,
    Medium = 2,
    High = 3
}

/// <summary>
/// 쿼리 개선 제안
/// </summary>
public class QueryRefinementSuggestions
{
    /// <summary>원본 쿼리</summary>
    public string OriginalQuery { get; set; } = string.Empty;

    /// <summary>개선된 쿼리 후보들</summary>
    public List<RefinedQuery> RefinedQueries { get; set; } = new();

    /// <summary>추가 키워드 제안</summary>
    public List<string> SuggestedKeywords { get; set; } = new();

    /// <summary>제외할 키워드 제안</summary>
    public List<string> KeywordsToExclude { get; set; } = new();

    /// <summary>대안 검색 전략</summary>
    public List<SearchStrategy> AlternativeStrategies { get; set; } = new();

    /// <summary>컨텍스트 확장 제안</summary>
    public List<string> ContextExpansions { get; set; } = new();
}

/// <summary>
/// 개선된 쿼리
/// </summary>
public class RefinedQuery
{
    /// <summary>개선된 쿼리 텍스트</summary>
    public string QueryText { get; set; } = string.Empty;

    /// <summary>개선 유형</summary>
    public RefinementType RefinementType { get; set; }

    /// <summary>개선 근거</summary>
    public string Rationale { get; set; } = string.Empty;

    /// <summary>예상 효과 점수 (0.0-1.0)</summary>
    public double ExpectedImprovementScore { get; set; }

    /// <summary>권장 검색 전략</summary>
    public SearchStrategy RecommendedStrategy { get; set; }
}

/// <summary>
/// 개선 유형
/// </summary>
public enum RefinementType
{
    /// <summary>키워드 추가</summary>
    KeywordAddition,
    
    /// <summary>키워드 제거</summary>
    KeywordRemoval,
    
    /// <summary>동의어 대체</summary>
    SynonymReplacement,
    
    /// <summary>구체화</summary>
    Specification,
    
    /// <summary>일반화</summary>
    Generalization,
    
    /// <summary>재구성</summary>
    Restructuring,
    
    /// <summary>컨텍스트 추가</summary>
    ContextAddition
}

/// <summary>
/// 개선 작업 기록
/// </summary>
public class RefinementAction
{
    /// <summary>작업 유형</summary>
    public RefinementActionType ActionType { get; set; }

    /// <summary>시작 시간</summary>
    public DateTime StartTime { get; set; }

    /// <summary>완료 시간</summary>
    public DateTime EndTime { get; set; }

    /// <summary>작업 설명</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>입력 데이터</summary>
    public Dictionary<string, object> Input { get; set; } = new();

    /// <summary>출력 결과</summary>
    public Dictionary<string, object> Output { get; set; } = new();

    /// <summary>성공 여부</summary>
    public bool IsSuccessful { get; set; }

    /// <summary>에러 메시지</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 개선 작업 유형
/// </summary>
public enum RefinementActionType
{
    /// <summary>품질 평가</summary>
    QualityAssessment,
    
    /// <summary>쿼리 개선</summary>
    QueryRefinement,
    
    /// <summary>재검색</summary>
    ReSearch,
    
    /// <summary>결과 필터링</summary>
    ResultFiltering,
    
    /// <summary>재순위화</summary>
    Reranking,
    
    /// <summary>중복 제거</summary>
    Deduplication,
    
    /// <summary>컨텍스트 확장</summary>
    ContextExpansion
}