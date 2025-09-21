using System;
using System.Collections.Generic;

namespace FluxIndex.Core.Domain.Models;

/// <summary>
/// HyDE 결과
/// </summary>
public class HyDEResult
{
    /// <summary>
    /// 원본 쿼리
    /// </summary>
    public string OriginalQuery { get; set; } = string.Empty;

    /// <summary>
    /// 생성된 가상 문서
    /// </summary>
    public string HypotheticalDocument { get; set; } = string.Empty;

    /// <summary>
    /// 품질 점수 (0-1)
    /// </summary>
    public float QualityScore { get; set; }

    /// <summary>
    /// 사용된 토큰 수
    /// </summary>
    public int TokensUsed { get; set; }

    /// <summary>
    /// 생성 시간 (밀리초)
    /// </summary>
    public long GenerationTimeMs { get; set; }

    /// <summary>
    /// 성공 여부
    /// </summary>
    public bool IsSuccessful => QualityScore > 0.3f && !string.IsNullOrEmpty(HypotheticalDocument);
}

/// <summary>
/// QuOTE 결과
/// </summary>
public class QuOTEResult
{
    /// <summary>
    /// 원본 쿼리
    /// </summary>
    public string OriginalQuery { get; set; } = string.Empty;

    /// <summary>
    /// 확장된 쿼리들
    /// </summary>
    public IReadOnlyList<string> ExpandedQueries { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 관련 질문들
    /// </summary>
    public IReadOnlyList<string> RelatedQuestions { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 쿼리별 가중치
    /// </summary>
    public Dictionary<string, float> QueryWeights { get; set; } = new();

    /// <summary>
    /// 품질 점수 (0-1)
    /// </summary>
    public float QualityScore { get; set; }
}

/// <summary>
/// 쿼리 분해 결과
/// </summary>
public class QueryDecompositionResult
{
    /// <summary>
    /// 원본 쿼리
    /// </summary>
    public string OriginalQuery { get; set; } = string.Empty;

    /// <summary>
    /// 분해된 하위 쿼리들
    /// </summary>
    public IReadOnlyList<string> SubQueries { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 신뢰도 (0-1)
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// 쿼리 관계 타입
    /// </summary>
    public QueryRelationshipType RelationshipType { get; set; } = QueryRelationshipType.Sequential;
}

/// <summary>
/// 쿼리 의도 분석 결과
/// </summary>
public class QueryIntentResult
{
    /// <summary>
    /// 원본 쿼리
    /// </summary>
    public string OriginalQuery { get; set; } = string.Empty;

    /// <summary>
    /// 주요 의도
    /// </summary>
    public string PrimaryIntent { get; set; } = string.Empty;

    /// <summary>
    /// 도메인
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// 쿼리 타입
    /// </summary>
    public QueryType QueryType { get; set; } = QueryType.Informational;

    /// <summary>
    /// 복잡도
    /// </summary>
    public QueryComplexity Complexity { get; set; } = QueryComplexity.Simple;

    /// <summary>
    /// 키워드들
    /// </summary>
    public IReadOnlyList<string> Keywords { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 신뢰도 (0-1)
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// 추천 처리 전략
    /// </summary>
    public string RecommendedStrategy { get; set; } = string.Empty;
}

/// <summary>
/// HyDE 옵션
/// </summary>
public class HyDEOptions
{
    /// <summary>
    /// 최대 문서 길이
    /// </summary>
    public int MaxLength { get; set; } = 300;

    /// <summary>
    /// 문서 스타일
    /// </summary>
    public string DocumentStyle { get; set; } = "informative";

    /// <summary>
    /// 도메인 컨텍스트
    /// </summary>
    public string DomainContext { get; set; } = string.Empty;

    /// <summary>
    /// 기본 옵션 생성
    /// </summary>
    public static HyDEOptions CreateDefault() => new();
}

/// <summary>
/// QuOTE 옵션
/// </summary>
public class QuOTEOptions
{
    /// <summary>
    /// 최대 확장 쿼리 수
    /// </summary>
    public int MaxExpansions { get; set; } = 3;

    /// <summary>
    /// 최대 관련 질문 수
    /// </summary>
    public int MaxRelatedQuestions { get; set; } = 5;

    /// <summary>
    /// 다양성 수준 (0-1)
    /// </summary>
    public float DiversityLevel { get; set; } = 0.7f;

    /// <summary>
    /// 도메인별 가중치
    /// </summary>
    public Dictionary<string, float> DomainWeights { get; set; } = new();

    /// <summary>
    /// 기본 옵션 생성
    /// </summary>
    public static QuOTEOptions CreateDefault() => new();
}

/// <summary>
/// 쿼리 관계 타입
/// </summary>
public enum QueryRelationshipType
{
    /// <summary>
    /// 순차적 관계
    /// </summary>
    Sequential,

    /// <summary>
    /// 병렬 관계
    /// </summary>
    Parallel,

    /// <summary>
    /// 계층적 관계
    /// </summary>
    Hierarchical,

    /// <summary>
    /// 조건부 관계
    /// </summary>
    Conditional
}

/// <summary>
/// 쿼리 타입
/// </summary>
public enum QueryType
{
    /// <summary>
    /// 정보 검색
    /// </summary>
    Informational,

    /// <summary>
    /// 방법 설명
    /// </summary>
    Procedural,

    /// <summary>
    /// 문제 해결
    /// </summary>
    Troubleshooting,

    /// <summary>
    /// 비교 분석
    /// </summary>
    Comparative,

    /// <summary>
    /// 의견/평가
    /// </summary>
    Evaluative,

    /// <summary>
    /// 구문 검색
    /// </summary>
    Phrase,

    /// <summary>
    /// 불리언 검색
    /// </summary>
    Boolean,

    /// <summary>
    /// 키워드 검색
    /// </summary>
    Keyword,

    /// <summary>
    /// 자연어 검색
    /// </summary>
    Natural
}

/// <summary>
/// 쿼리 복잡도
/// </summary>
public enum QueryComplexity
{
    /// <summary>
    /// 단순
    /// </summary>
    Simple,

    /// <summary>
    /// 중간
    /// </summary>
    Medium,

    /// <summary>
    /// 복잡
    /// </summary>
    Complex,

    /// <summary>
    /// 매우 복잡
    /// </summary>
    VeryComplex
}

/// <summary>
/// 쿼리 의도
/// </summary>
public enum QueryIntent
{
    /// <summary>
    /// 학습/교육
    /// </summary>
    Learning,

    /// <summary>
    /// 문제 해결
    /// </summary>
    ProblemSolving,

    /// <summary>
    /// 연구/탐색
    /// </summary>
    Research,

    /// <summary>
    /// 결정 지원
    /// </summary>
    DecisionSupport,

    /// <summary>
    /// 참조/확인
    /// </summary>
    Reference
}