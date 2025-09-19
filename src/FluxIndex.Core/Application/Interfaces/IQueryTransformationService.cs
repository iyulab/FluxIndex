using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 쿼리 변환 및 확장 서비스 인터페이스
/// HyDE, QuOTE 등 고급 검색 기법을 통한 쿼리 최적화
/// </summary>
public interface IQueryTransformationService
{
    /// <summary>
    /// HyDE: 가상의 답변 문서를 생성하여 검색 품질 향상
    /// </summary>
    /// <param name="query">원본 쿼리</param>
    /// <param name="options">HyDE 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>가상 문서 변환 결과</returns>
    Task<HyDEResult> GenerateHypotheticalDocumentAsync(
        string query,
        HyDEOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// QuOTE: 질문 지향적 텍스트 임베딩을 위한 쿼리 확장
    /// </summary>
    /// <param name="query">원본 쿼리</param>
    /// <param name="options">QuOTE 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>질문 지향 변환 결과</returns>
    Task<QuOTEResult> GenerateQuestionOrientedEmbeddingAsync(
        string query,
        QuOTEOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 다중 쿼리 생성 (쿼리의 다양한 표현 생성)
    /// </summary>
    /// <param name="query">원본 쿼리</param>
    /// <param name="count">생성할 쿼리 개수</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>생성된 쿼리 목록</returns>
    Task<IReadOnlyList<string>> GenerateMultipleQueriesAsync(
        string query,
        int count = 3,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 쿼리 분해 (복합 쿼리를 단순 쿼리들로 분해)
    /// </summary>
    /// <param name="query">복합 쿼리</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>분해된 하위 쿼리들</returns>
    Task<QueryDecompositionResult> DecomposeQueryAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 쿼리 의도 분석
    /// </summary>
    /// <param name="query">분석할 쿼리</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>쿼리 의도 분석 결과</returns>
    Task<QueryIntentResult> AnalyzeQueryIntentAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 서비스 상태 확인
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>서비스 상태</returns>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// HyDE 결과
/// </summary>
public record HyDEResult
{
    /// <summary>
    /// 원본 쿼리
    /// </summary>
    public string OriginalQuery { get; init; } = string.Empty;

    /// <summary>
    /// 생성된 가상 문서
    /// </summary>
    public string HypotheticalDocument { get; init; } = string.Empty;

    /// <summary>
    /// 가상 문서의 품질 점수 (0.0 ~ 1.0)
    /// </summary>
    public float QualityScore { get; init; }

    /// <summary>
    /// 생성에 사용된 토큰 수
    /// </summary>
    public int TokensUsed { get; init; }

    /// <summary>
    /// 생성 시간 (밀리초)
    /// </summary>
    public double GenerationTimeMs { get; init; }

    /// <summary>
    /// 생성 성공 여부
    /// </summary>
    public bool IsSuccessful => !string.IsNullOrWhiteSpace(HypotheticalDocument) && QualityScore > 0.3f;

    /// <summary>
    /// 테스트용 성공 결과 생성
    /// </summary>
    public static HyDEResult CreateSuccess(string originalQuery, string hypotheticalDocument) => new()
    {
        OriginalQuery = originalQuery,
        HypotheticalDocument = hypotheticalDocument,
        QualityScore = 0.85f,
        TokensUsed = 150,
        GenerationTimeMs = 850
    };

    /// <summary>
    /// 테스트용 실패 결과 생성
    /// </summary>
    public static HyDEResult CreateFailure(string originalQuery) => new()
    {
        OriginalQuery = originalQuery,
        HypotheticalDocument = string.Empty,
        QualityScore = 0.0f,
        TokensUsed = 0,
        GenerationTimeMs = 100
    };
}

/// <summary>
/// QuOTE 결과
/// </summary>
public record QuOTEResult
{
    /// <summary>
    /// 원본 쿼리
    /// </summary>
    public string OriginalQuery { get; init; } = string.Empty;

    /// <summary>
    /// 질문 지향 확장 쿼리들
    /// </summary>
    public IReadOnlyList<string> ExpandedQueries { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 생성된 관련 질문들
    /// </summary>
    public IReadOnlyList<string> RelatedQuestions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 쿼리별 가중치 (검색 시 사용)
    /// </summary>
    public IReadOnlyDictionary<string, float> QueryWeights { get; init; } =
        new Dictionary<string, float>();

    /// <summary>
    /// 전체 품질 점수
    /// </summary>
    public float QualityScore { get; init; }

    /// <summary>
    /// 확장 성공 여부
    /// </summary>
    public bool IsSuccessful => ExpandedQueries.Count > 0 && QualityScore > 0.4f;

    /// <summary>
    /// 테스트용 성공 결과 생성
    /// </summary>
    public static QuOTEResult CreateSuccess(string originalQuery, IReadOnlyList<string> expandedQueries) => new()
    {
        OriginalQuery = originalQuery,
        ExpandedQueries = expandedQueries,
        RelatedQuestions = new[] { "관련 질문 1?", "관련 질문 2?" },
        QueryWeights = expandedQueries.ToDictionary(q => q, _ => 1.0f / expandedQueries.Count),
        QualityScore = 0.8f
    };
}

/// <summary>
/// 쿼리 분해 결과
/// </summary>
public record QueryDecompositionResult
{
    /// <summary>
    /// 원본 복합 쿼리
    /// </summary>
    public string OriginalQuery { get; init; } = string.Empty;

    /// <summary>
    /// 분해된 하위 쿼리들
    /// </summary>
    public IReadOnlyList<SubQuery> SubQueries { get; init; } = Array.Empty<SubQuery>();

    /// <summary>
    /// 쿼리 간 관계 정보
    /// </summary>
    public QueryRelationshipType Relationship { get; init; } = QueryRelationshipType.Independent;

    /// <summary>
    /// 분해 성공 여부
    /// </summary>
    public bool IsSuccessful => SubQueries.Count > 1;
}

/// <summary>
/// 하위 쿼리
/// </summary>
public record SubQuery
{
    /// <summary>
    /// 하위 쿼리 텍스트
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// 중요도 (0.0 ~ 1.0)
    /// </summary>
    public float Importance { get; init; }

    /// <summary>
    /// 쿼리 타입
    /// </summary>
    public QueryType Type { get; init; } = QueryType.Factual;
}

/// <summary>
/// 쿼리 의도 분석 결과
/// </summary>
public record QueryIntentResult
{
    /// <summary>
    /// 원본 쿼리
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// 주요 의도
    /// </summary>
    public QueryIntent PrimaryIntent { get; init; } = QueryIntent.Informational;

    /// <summary>
    /// 보조 의도들
    /// </summary>
    public IReadOnlyList<QueryIntent> SecondaryIntents { get; init; } = Array.Empty<QueryIntent>();

    /// <summary>
    /// 의도별 신뢰도
    /// </summary>
    public IReadOnlyDictionary<QueryIntent, float> Confidence { get; init; } =
        new Dictionary<QueryIntent, float>();

    /// <summary>
    /// 도메인 카테고리
    /// </summary>
    public string Domain { get; init; } = "General";

    /// <summary>
    /// 복잡도 수준
    /// </summary>
    public QueryComplexity Complexity { get; init; } = QueryComplexity.Simple;
}

/// <summary>
/// HyDE 옵션
/// </summary>
public class HyDEOptions
{
    /// <summary>
    /// 생성할 가상 문서의 최대 길이
    /// </summary>
    public int MaxLength { get; set; } = 300;

    /// <summary>
    /// 생성 온도 (창의성 수준)
    /// </summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// 도메인 특화 컨텍스트
    /// </summary>
    public string? DomainContext { get; set; }

    /// <summary>
    /// 문서 스타일 (예: academic, technical, conversational)
    /// </summary>
    public string DocumentStyle { get; set; } = "informative";

    /// <summary>
    /// 테스트용 기본 옵션
    /// </summary>
    public static HyDEOptions CreateDefault() => new()
    {
        MaxLength = 200,
        Temperature = 0.5f,
        DocumentStyle = "technical"
    };
}

/// <summary>
/// QuOTE 옵션
/// </summary>
public class QuOTEOptions
{
    /// <summary>
    /// 생성할 확장 쿼리 개수
    /// </summary>
    public int MaxExpansions { get; set; } = 3;

    /// <summary>
    /// 관련 질문 생성 개수
    /// </summary>
    public int MaxRelatedQuestions { get; set; } = 5;

    /// <summary>
    /// 다양성 수준 (0.0 ~ 1.0)
    /// </summary>
    public float DiversityLevel { get; set; } = 0.7f;

    /// <summary>
    /// 도메인 특화 가중치
    /// </summary>
    public Dictionary<string, float> DomainWeights { get; set; } = new();

    /// <summary>
    /// 테스트용 기본 옵션
    /// </summary>
    public static QuOTEOptions CreateDefault() => new()
    {
        MaxExpansions = 2,
        MaxRelatedQuestions = 3,
        DiversityLevel = 0.6f
    };
}

/// <summary>
/// 쿼리 관계 타입
/// </summary>
public enum QueryRelationshipType
{
    /// <summary>
    /// 독립적인 쿼리들
    /// </summary>
    Independent,

    /// <summary>
    /// 순차적 의존성 (A → B → C)
    /// </summary>
    Sequential,

    /// <summary>
    /// AND 관계 (모든 조건 만족)
    /// </summary>
    Conjunction,

    /// <summary>
    /// OR 관계 (어느 하나라도 만족)
    /// </summary>
    Disjunction,

    /// <summary>
    /// 계층적 관계 (상위-하위)
    /// </summary>
    Hierarchical
}

/// <summary>
/// 쿼리 타입
/// </summary>
public enum QueryType
{
    /// <summary>
    /// 사실 기반 질문
    /// </summary>
    Factual,

    /// <summary>
    /// 절차/방법 질문
    /// </summary>
    Procedural,

    /// <summary>
    /// 개념 설명 질문
    /// </summary>
    Conceptual,

    /// <summary>
    /// 비교/분석 질문
    /// </summary>
    Comparative,

    /// <summary>
    /// 문제 해결 질문
    /// </summary>
    ProblemSolving
}

/// <summary>
/// 쿼리 의도
/// </summary>
public enum QueryIntent
{
    /// <summary>
    /// 정보 탐색
    /// </summary>
    Informational,

    /// <summary>
    /// 특정 항목 찾기
    /// </summary>
    Navigational,

    /// <summary>
    /// 거래/행동 수행
    /// </summary>
    Transactional,

    /// <summary>
    /// 문제 해결
    /// </summary>
    Troubleshooting,

    /// <summary>
    /// 학습/교육
    /// </summary>
    Educational,

    /// <summary>
    /// 비교/평가
    /// </summary>
    Comparative
}

/// <summary>
/// 쿼리 복잡도
/// </summary>
public enum QueryComplexity
{
    /// <summary>
    /// 단순 (단일 개념)
    /// </summary>
    Simple,

    /// <summary>
    /// 중간 (2-3개 개념)
    /// </summary>
    Moderate,

    /// <summary>
    /// 복잡 (다중 조건/관계)
    /// </summary>
    Complex,

    /// <summary>
    /// 매우 복잡 (다단계 추론 필요)
    /// </summary>
    VeryComplex
}

/// <summary>
/// 쿼리 변환 예외
/// </summary>
public class QueryTransformationException : Exception
{
    /// <summary>
    /// 변환 오류 타입
    /// </summary>
    public QueryTransformationErrorType ErrorType { get; }

    public QueryTransformationException(
        string message,
        QueryTransformationErrorType errorType = QueryTransformationErrorType.Unknown,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorType = errorType;
    }

    /// <summary>
    /// 테스트용 예외 생성
    /// </summary>
    public static QueryTransformationException CreateForTesting(string message = "Test transformation error") =>
        new(message, QueryTransformationErrorType.GenerationFailed);
}

/// <summary>
/// 쿼리 변환 오류 타입
/// </summary>
public enum QueryTransformationErrorType
{
    Unknown = 0,
    InvalidInput = 1,
    GenerationFailed = 2,
    ServiceUnavailable = 3,
    TimeoutError = 4,
    QualityThresholdNotMet = 5
}