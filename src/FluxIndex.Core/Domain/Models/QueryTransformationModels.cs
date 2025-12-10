using System;
using System.Collections.Generic;

namespace FluxIndex.Core.Domain.Models;

/// <summary>
/// HyDE (Hypothetical Document Embeddings) result.
/// Supports multi-hypothetical mode for improved retrieval accuracy.
/// </summary>
public class HyDEResult
{
    /// <summary>
    /// Original query
    /// </summary>
    public string OriginalQuery { get; set; } = string.Empty;

    /// <summary>
    /// Primary hypothetical document (first generated document)
    /// </summary>
    public string HypotheticalDocument { get; set; } = string.Empty;

    /// <summary>
    /// All generated hypothetical documents (for multi-hypothetical mode)
    /// </summary>
    public IReadOnlyList<string> HypotheticalDocuments { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Quality scores for each hypothetical document
    /// </summary>
    public IReadOnlyList<float> QualityScores { get; set; } = Array.Empty<float>();

    /// <summary>
    /// Average quality score (0-1)
    /// </summary>
    public float QualityScore { get; set; }

    /// <summary>
    /// Total tokens used across all documents
    /// </summary>
    public int TokensUsed { get; set; }

    /// <summary>
    /// Generation time (milliseconds)
    /// </summary>
    public long GenerationTimeMs { get; set; }

    /// <summary>
    /// Number of documents generated
    /// </summary>
    public int DocumentCount => HypotheticalDocuments.Count > 0
        ? HypotheticalDocuments.Count
        : string.IsNullOrEmpty(HypotheticalDocument) ? 0 : 1;

    /// <summary>
    /// Success indicator
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
/// HyDE (Hypothetical Document Embeddings) options.
/// Supports multi-hypothetical mode for generating diverse perspectives.
/// </summary>
public class HyDEOptions
{
    /// <summary>
    /// Maximum length per hypothetical document (in tokens)
    /// </summary>
    public int MaxLength { get; set; } = 300;

    /// <summary>
    /// Document style (informative, technical, conversational, academic)
    /// </summary>
    public string DocumentStyle { get; set; } = "informative";

    /// <summary>
    /// Domain context for more relevant document generation
    /// </summary>
    public string DomainContext { get; set; } = string.Empty;

    /// <summary>
    /// Number of hypothetical documents to generate (1-10).
    /// Multiple documents provide diverse perspectives and improve retrieval.
    /// Default: 1 (standard HyDE), Recommended: 3-5 for multi-hypothetical mode.
    /// </summary>
    public int DocumentCount { get; set; } = 1;

    /// <summary>
    /// Temperature variation for multi-document generation.
    /// Higher values create more diverse documents.
    /// Applied as: base_temperature + (index * TemperatureStep)
    /// </summary>
    public float TemperatureStep { get; set; } = 0.1f;

    /// <summary>
    /// Document perspectives for multi-hypothetical mode.
    /// If empty, generic perspectives are used.
    /// Examples: ["expert", "beginner", "critical", "supportive", "historical"]
    /// </summary>
    public IList<string> Perspectives { get; set; } = new List<string>();

    /// <summary>
    /// Enable parallel document generation (default: true)
    /// </summary>
    public bool EnableParallelGeneration { get; set; } = true;

    /// <summary>
    /// Creates default options (single document)
    /// </summary>
    public static HyDEOptions CreateDefault() => new();

    /// <summary>
    /// Creates multi-hypothetical options with specified document count
    /// </summary>
    /// <param name="documentCount">Number of hypothetical documents (3-5 recommended)</param>
    public static HyDEOptions CreateMultiHypothetical(int documentCount = 5) => new()
    {
        DocumentCount = Math.Clamp(documentCount, 1, 10),
        TemperatureStep = 0.1f,
        EnableParallelGeneration = true
    };

    /// <summary>
    /// Creates multi-hypothetical options with custom perspectives
    /// </summary>
    /// <param name="perspectives">Custom perspectives for document generation</param>
    public static HyDEOptions CreateWithPerspectives(params string[] perspectives) => new()
    {
        DocumentCount = perspectives.Length,
        Perspectives = perspectives.ToList(),
        EnableParallelGeneration = true
    };
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
