using System;

namespace FluxIndex.Core.Options;

/// <summary>
/// 쿼리 변환 일반 옵션
/// </summary>
public class QueryTransformationOptions
{
    /// <summary>
    /// 병렬 처리 활성화
    /// </summary>
    public bool EnableParallelProcessing { get; set; } = true;

    /// <summary>
    /// 최대 동시 요청 수
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 5;

    /// <summary>
    /// 기본 타임아웃
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 캐싱 활성화
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// 캐시 만료 시간
    /// </summary>
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 자동 품질 필터링 활성화
    /// </summary>
    public bool EnableQualityFiltering { get; set; } = true;

    /// <summary>
    /// 최소 품질 임계값
    /// </summary>
    public float MinQualityThreshold { get; set; } = 0.4f;

    /// <summary>
    /// 설정 유효성 검증
    /// </summary>
    public bool IsValid =>
        MaxConcurrentRequests > 0 &&
        DefaultTimeout > TimeSpan.Zero &&
        CacheExpiration > TimeSpan.Zero &&
        MinQualityThreshold >= 0.0f && MinQualityThreshold <= 1.0f;

    /// <summary>
    /// 테스트용 설정
    /// </summary>
    public static QueryTransformationOptions CreateForTesting() => new()
    {
        EnableParallelProcessing = false,
        MaxConcurrentRequests = 1,
        DefaultTimeout = TimeSpan.FromSeconds(10),
        EnableCaching = false,
        CacheExpiration = TimeSpan.FromMinutes(5),
        EnableQualityFiltering = true,
        MinQualityThreshold = 0.3f
    };

    /// <summary>
    /// 운영용 설정
    /// </summary>
    public static QueryTransformationOptions CreateForProduction() => new()
    {
        EnableParallelProcessing = true,
        MaxConcurrentRequests = 10,
        DefaultTimeout = TimeSpan.FromSeconds(45),
        EnableCaching = true,
        CacheExpiration = TimeSpan.FromHours(1),
        EnableQualityFiltering = true,
        MinQualityThreshold = 0.5f
    };
}

/// <summary>
/// OpenAI 옵션
/// </summary>
public class OpenAIOptions
{
    /// <summary>
    /// API 키
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 기본 URL
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.openai.com";

    /// <summary>
    /// Azure OpenAI 사용 여부
    /// </summary>
    public bool IsAzure { get; set; } = false;

    /// <summary>
    /// 배포 이름 (Azure용)
    /// </summary>
    public string DeploymentName { get; set; } = string.Empty;

    /// <summary>
    /// 기본 모델명
    /// </summary>
    public string DefaultModel { get; set; } = "gpt-3.5-turbo";

    /// <summary>
    /// 최대 토큰 수
    /// </summary>
    public int MaxTokens { get; set; } = 2000;

    /// <summary>
    /// 온도 설정
    /// </summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// 타임아웃
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 테스트 모드 여부
    /// </summary>
    public bool IsTestMode { get; set; } = false;

    /// <summary>
    /// 설정 유효성 검증
    /// </summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        MaxTokens > 0 &&
        Temperature >= 0.0f && Temperature <= 2.0f &&
        Timeout > TimeSpan.Zero;

    /// <summary>
    /// 테스트용 설정
    /// </summary>
    public static OpenAIOptions CreateForTesting() => new()
    {
        ApiKey = "test-api-key",
        BaseUrl = "https://api.openai.com",
        DefaultModel = "gpt-3.5-turbo",
        MaxTokens = 1000,
        Temperature = 0.5f,
        Timeout = TimeSpan.FromSeconds(10),
        IsTestMode = true
    };
}