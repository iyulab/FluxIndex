using System;
using System.ComponentModel.DataAnnotations;

namespace FluxIndex.Core.Application.Options;

/// <summary>
/// 메타데이터 추출 서비스 설정 옵션
/// 테스트와 운영 환경에서 유연하게 조정 가능
/// </summary>
public class MetadataExtractionOptions
{
    /// <summary>
    /// 최대 키워드 개수 (기본값: 10)
    /// </summary>
    [Range(1, 50)]
    public int MaxKeywords { get; set; } = 10;

    /// <summary>
    /// 최대 엔터티 개수 (기본값: 15)
    /// </summary>
    [Range(1, 30)]
    public int MaxEntities { get; set; } = 15;

    /// <summary>
    /// 최대 생성 질문 개수 (기본값: 5)
    /// </summary>
    [Range(1, 20)]
    public int MaxQuestions { get; set; } = 5;

    /// <summary>
    /// 배치 처리 크기 (기본값: 5)
    /// </summary>
    [Range(1, 10)]
    public int BatchSize { get; set; } = 5;

    /// <summary>
    /// API 호출 타임아웃 (기본값: 30초)
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 품질 점수 계산 활성화 (기본값: true)
    /// </summary>
    public bool EnableQualityScoring { get; set; } = true;

    /// <summary>
    /// 프롬프트 템플릿 (기본값: 내장 템플릿)
    /// </summary>
    public string PromptTemplate { get; set; } = DefaultPrompts.MetadataExtraction;

    /// <summary>
    /// 최소 품질 점수 임계값 (기본값: 0.3)
    /// </summary>
    [Range(0.0, 1.0)]
    public float MinQualityThreshold { get; set; } = 0.3f;

    /// <summary>
    /// 재시도 횟수 (기본값: 2)
    /// </summary>
    [Range(0, 5)]
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// 동시 처리 제한 (기본값: 3)
    /// </summary>
    [Range(1, 10)]
    public int MaxConcurrency { get; set; } = 3;

    /// <summary>
    /// 디버그 모드 (상세 로깅)
    /// </summary>
    public bool EnableDebugLogging { get; set; } = false;

    /// <summary>
    /// 비용 추적 활성화
    /// </summary>
    public bool EnableCostTracking { get; set; } = true;

    /// <summary>
    /// 테스트용 설정 생성
    /// </summary>
    public static MetadataExtractionOptions CreateForTesting() => new()
    {
        MaxKeywords = 5,
        MaxEntities = 5,
        MaxQuestions = 3,
        BatchSize = 2,
        Timeout = TimeSpan.FromSeconds(10),
        MaxRetries = 1,
        MaxConcurrency = 1,
        EnableDebugLogging = true,
        EnableCostTracking = false
    };

    /// <summary>
    /// 운영용 설정 생성
    /// </summary>
    public static MetadataExtractionOptions CreateForProduction() => new()
    {
        MaxKeywords = 15,
        MaxEntities = 20,
        MaxQuestions = 8,
        BatchSize = 10,
        Timeout = TimeSpan.FromSeconds(45),
        MaxRetries = 3,
        MaxConcurrency = 5,
        EnableDebugLogging = false,
        EnableCostTracking = true
    };

    /// <summary>
    /// 설정 유효성 검증
    /// </summary>
    public bool IsValid =>
        MaxKeywords > 0 && MaxKeywords <= 50 &&
        MaxEntities > 0 && MaxEntities <= 30 &&
        MaxQuestions > 0 && MaxQuestions <= 20 &&
        BatchSize > 0 && BatchSize <= 10 &&
        Timeout > TimeSpan.Zero &&
        MinQualityThreshold >= 0.0f && MinQualityThreshold <= 1.0f &&
        MaxRetries >= 0 && MaxRetries <= 5 &&
        MaxConcurrency > 0 && MaxConcurrency <= 10;
}

/// <summary>
/// 배치 처리 옵션
/// </summary>
public class BatchProcessingOptions
{
    /// <summary>
    /// 배치 크기
    /// </summary>
    public int Size { get; init; } = 5;

    /// <summary>
    /// 배치 간 지연 시간
    /// </summary>
    public TimeSpan DelayBetweenBatches { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// 실패 시 계속 진행 여부
    /// </summary>
    public bool ContinueOnFailure { get; init; } = true;

    /// <summary>
    /// 진행 상황 콜백
    /// </summary>
    public Action<int, int>? ProgressCallback { get; init; }

    /// <summary>
    /// 테스트용 배치 옵션
    /// </summary>
    public static BatchProcessingOptions CreateForTesting() => new()
    {
        Size = 2,
        DelayBetweenBatches = TimeSpan.FromMilliseconds(10),
        ContinueOnFailure = false
    };
}

/// <summary>
/// 기본 프롬프트 템플릿
/// </summary>
public static class DefaultPrompts
{
    public const string MetadataExtraction = @"
Extract structured metadata from the following text chunk.
Return a valid JSON object with this exact schema:

{
  ""title"": ""Clear, descriptive title (max 100 chars)"",
  ""summary"": ""Concise summary (max 200 chars)"",
  ""keywords"": [""keyword1"", ""keyword2"", ""keyword3""],
  ""entities"": [""Entity1"", ""Entity2"", ""Entity3""],
  ""generated_questions"": [""Question 1?"", ""Question 2?""],
  ""quality_score"": 0.85
}

Rules:
- Title must be under 100 characters
- Summary must be under 200 characters
- Keywords should be relevant single words or short phrases
- Entities should include people, organizations, locations, concepts
- Questions should be answerable from the content
- Quality score: 0.0 (poor) to 1.0 (excellent)

Text to analyze:
{content}

Context (if available):
{context}

Return only the JSON object, no other text.";
}