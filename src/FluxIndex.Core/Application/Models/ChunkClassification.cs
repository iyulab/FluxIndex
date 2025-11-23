namespace FluxIndex.Core.Application.Models;

/// <summary>
/// LLM 기반 청크 분류 결과
/// </summary>
public class ChunkClassification
{
    /// <summary>
    /// 주제 태그 (예: ["RAG", "Vector Search", "Embedding"])
    /// </summary>
    public List<string> Topics { get; set; } = new();

    /// <summary>
    /// 카테고리 (예: ["Technical Documentation", "Tutorial"])
    /// </summary>
    public List<string> Categories { get; set; } = new();

    /// <summary>
    /// 태그 (예: ["beginner", "python", "api"])
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// 정제된 키워드 (원본 키워드 개선)
    /// </summary>
    public List<string> RefinedKeywords { get; set; } = new();

    /// <summary>
    /// 청크 요약 (1-2문장)
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// 예상 질문 (이 청크로 답변 가능한 질문들)
    /// </summary>
    public List<string> PotentialQuestions { get; set; } = new();

    /// <summary>
    /// 분류 신뢰도 (0-1)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 분류 소스 (LLM, Cache, Inherited)
    /// </summary>
    public ClassificationSource Source { get; set; }

    /// <summary>
    /// 분류 생성 시간
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 분류 결과 소스
/// </summary>
public enum ClassificationSource
{
    /// <summary>
    /// LLM에서 새로 생성
    /// </summary>
    Llm,

    /// <summary>
    /// 캐시에서 조회
    /// </summary>
    Cache,

    /// <summary>
    /// 유사 청크에서 상속
    /// </summary>
    Inherited,

    /// <summary>
    /// 문서 메타데이터에서 추출
    /// </summary>
    Metadata,

    /// <summary>
    /// 검증 실패로 스킵
    /// </summary>
    Skipped
}

/// <summary>
/// 분류 검증 결과
/// </summary>
public class ClassificationValidationResult
{
    /// <summary>
    /// LLM 분류 필요 여부
    /// </summary>
    public bool RequiresLlmClassification { get; set; }

    /// <summary>
    /// 스킵 사유 (LLM 불필요 시)
    /// </summary>
    public string? SkipReason { get; set; }

    /// <summary>
    /// 기존 분류 (있는 경우)
    /// </summary>
    public ChunkClassification? ExistingClassification { get; set; }

    /// <summary>
    /// 검증 점수 (0-1, 높을수록 LLM 필요)
    /// </summary>
    public double ValidationScore { get; set; }

    /// <summary>
    /// 권장 분류 항목 (비용 최적화)
    /// </summary>
    public ClassificationScope RecommendedScope { get; set; }
}

/// <summary>
/// 분류 범위 (비용 최적화용)
/// </summary>
[Flags]
public enum ClassificationScope
{
    None = 0,
    Topics = 1,
    Categories = 2,
    Tags = 4,
    Summary = 8,
    Questions = 16,
    Keywords = 32,
    All = Topics | Categories | Tags | Summary | Questions | Keywords
}
