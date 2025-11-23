namespace FluxIndex.Core.Application.Models;

/// <summary>
/// 청크 분류 서비스 설정
/// </summary>
public class ClassificationOptions
{
    /// <summary>
    /// LLM 분류 활성화 여부
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 분류 범위 (기본: 전체)
    /// </summary>
    public ClassificationScope Scope { get; set; } = ClassificationScope.All;

    /// <summary>
    /// 최대 토픽 수
    /// </summary>
    public int MaxTopics { get; set; } = 5;

    /// <summary>
    /// 최대 카테고리 수
    /// </summary>
    public int MaxCategories { get; set; } = 3;

    /// <summary>
    /// 최대 태그 수
    /// </summary>
    public int MaxTags { get; set; } = 10;

    /// <summary>
    /// 최대 예상 질문 수
    /// </summary>
    public int MaxQuestions { get; set; } = 5;

    /// <summary>
    /// 요약 최대 길이 (문자)
    /// </summary>
    public int MaxSummaryLength { get; set; } = 200;

    /// <summary>
    /// 배치 크기 (한 번에 처리할 청크 수)
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// 캐시 활성화
    /// </summary>
    public bool EnableCache { get; set; } = true;

    /// <summary>
    /// 캐시 만료 시간 (시간)
    /// </summary>
    public int CacheExpirationHours { get; set; } = 24;

    /// <summary>
    /// 유사 청크 상속 임계값 (코사인 유사도)
    /// </summary>
    public double SimilarityInheritanceThreshold { get; set; } = 0.95;

    /// <summary>
    /// LLM 온도 (창의성)
    /// </summary>
    public float Temperature { get; set; } = 0.3f;

    /// <summary>
    /// 최대 토큰 수
    /// </summary>
    public int MaxTokens { get; set; } = 500;

    /// <summary>
    /// 재시도 횟수
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// 검증 설정
    /// </summary>
    public ClassificationValidationOptions Validation { get; set; } = new();
}

/// <summary>
/// 분류 검증 설정
/// </summary>
public class ClassificationValidationOptions
{
    /// <summary>
    /// 최소 품질 임계값 (이하 스킵)
    /// </summary>
    public double MinQualityThreshold { get; set; } = 0.3;

    /// <summary>
    /// 최소 콘텐츠 길이 (문자, 이하 스킵)
    /// </summary>
    public int MinContentLength { get; set; } = 50;

    /// <summary>
    /// 기존 메타데이터 충분 임계값
    /// </summary>
    public int MinExistingKeywords { get; set; } = 3;

    /// <summary>
    /// 중복 검사 활성화
    /// </summary>
    public bool EnableDuplicateCheck { get; set; } = true;

    /// <summary>
    /// 중복 임계값 (해시 유사도)
    /// </summary>
    public double DuplicateThreshold { get; set; } = 0.98;

    /// <summary>
    /// 출력 검증 활성화
    /// </summary>
    public bool EnableOutputValidation { get; set; } = true;

    /// <summary>
    /// 최소 신뢰도 (이하 재시도)
    /// </summary>
    public double MinConfidenceThreshold { get; set; } = 0.6;
}
