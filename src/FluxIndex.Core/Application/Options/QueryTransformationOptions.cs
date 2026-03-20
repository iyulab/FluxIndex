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