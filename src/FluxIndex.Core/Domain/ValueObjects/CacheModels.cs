using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Domain.ValueObjects;

/// <summary>
/// 시맨틱 캐시 검색 결과
/// </summary>
public class CacheResult
{
    /// <summary>
    /// 캐시된 응답 텍스트
    /// </summary>
    public string CachedResponse { get; init; } = string.Empty;

    /// <summary>
    /// 캐시된 검색 결과 목록
    /// </summary>
    public List<SearchResult> SearchResults { get; init; } = new();

    /// <summary>
    /// 입력 쿼리와의 유사도 점수 (0.0-1.0)
    /// </summary>
    public float SimilarityScore { get; init; }

    /// <summary>
    /// 캐시된 시간
    /// </summary>
    public DateTime CachedAt { get; init; }

    /// <summary>
    /// 원본 캐시된 쿼리
    /// </summary>
    public string OriginalQuery { get; init; } = string.Empty;

    /// <summary>
    /// 캐시 만료 시간 (null이면 만료 없음)
    /// </summary>
    public TimeSpan? Expiry { get; init; }

    /// <summary>
    /// 캐시 메타데이터 정보
    /// </summary>
    public CacheMetadata Metadata { get; init; } = new();

    /// <summary>
    /// 캐시 히트 타입
    /// </summary>
    public CacheHitType HitType { get; init; } = CacheHitType.Exact;

    /// <summary>
    /// 캐시 품질 점수 (0.0-1.0)
    /// </summary>
    public float QualityScore { get; init; }

    /// <summary>
    /// 만료 여부 확인
    /// </summary>
    public bool IsExpired => Expiry.HasValue && DateTime.UtcNow - CachedAt > Expiry.Value;

    /// <summary>
    /// 캐시 결과 생성
    /// </summary>
    public static CacheResult Create(
        string cachedResponse,
        List<SearchResult> searchResults,
        float similarityScore,
        string originalQuery,
        CacheMetadata? metadata = null,
        TimeSpan? expiry = null,
        CacheHitType hitType = CacheHitType.Semantic)
    {
        return new CacheResult
        {
            CachedResponse = cachedResponse,
            SearchResults = searchResults,
            SimilarityScore = similarityScore,
            CachedAt = DateTime.UtcNow,
            OriginalQuery = originalQuery,
            Expiry = expiry,
            Metadata = metadata ?? new CacheMetadata(),
            HitType = hitType,
            QualityScore = CalculateQualityScore(similarityScore, searchResults.Count)
        };
    }

    private static float CalculateQualityScore(float similarity, int resultCount)
    {
        // 유사도와 결과 수를 기반으로 품질 점수 계산
        var resultQuality = Math.Min(resultCount / 10.0f, 1.0f); // 최대 10개 결과 기준
        return (similarity * 0.7f) + (resultQuality * 0.3f);
    }
}

/// <summary>
/// 캐시 메타데이터
/// </summary>
public class CacheMetadata
{
    /// <summary>
    /// 캐시 키
    /// </summary>
    public string CacheKey { get; init; } = string.Empty;

    /// <summary>
    /// 사용 횟수
    /// </summary>
    public int UsageCount { get; set; }

    /// <summary>
    /// 마지막 사용 시간
    /// </summary>
    public DateTime LastUsedAt { get; set; }

    /// <summary>
    /// 캐시 생성 소스
    /// </summary>
    public string Source { get; init; } = "FluxIndex";

    /// <summary>
    /// 사용자 정의 태그
    /// </summary>
    public List<string> Tags { get; init; } = new();

    /// <summary>
    /// 추가 메타데이터
    /// </summary>
    public Dictionary<string, object> AdditionalData { get; init; } = new();

    /// <summary>
    /// 임베딩 벡터 차원
    /// </summary>
    public int EmbeddingDimension { get; init; }

    /// <summary>
    /// 사용 통계 업데이트
    /// </summary>
    public void UpdateUsageStats()
    {
        UsageCount++;
        LastUsedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// 캐시 통계 정보
/// </summary>
public class CacheStatistics
{
    /// <summary>
    /// 전체 쿼리 수
    /// </summary>
    public long TotalQueries { get; init; }

    /// <summary>
    /// 캐시 히트 수
    /// </summary>
    public long CacheHits { get; init; }

    /// <summary>
    /// 캐시 미스 수
    /// </summary>
    public long CacheMisses { get; init; }

    /// <summary>
    /// 캐시 히트율 (0.0-1.0)
    /// </summary>
    public float HitRate => TotalQueries > 0 ? (float)CacheHits / TotalQueries : 0f;

    /// <summary>
    /// 평균 응답 시간 (캐시 미스)
    /// </summary>
    public TimeSpan AverageResponseTime { get; init; }

    /// <summary>
    /// 캐시 응답 시간
    /// </summary>
    public TimeSpan CacheResponseTime { get; init; }

    /// <summary>
    /// 성능 향상률
    /// </summary>
    public float PerformanceGain => AverageResponseTime > TimeSpan.Zero
        ? (float)(1 - CacheResponseTime.TotalMilliseconds / AverageResponseTime.TotalMilliseconds)
        : 0f;

    /// <summary>
    /// 캐시된 항목 수
    /// </summary>
    public long CachedItemsCount { get; init; }

    /// <summary>
    /// 캐시 메모리 사용량 (바이트)
    /// </summary>
    public long MemoryUsageBytes { get; init; }

    /// <summary>
    /// 평균 유사도 점수
    /// </summary>
    public float AverageSimilarityScore { get; init; }

    /// <summary>
    /// 추가 메트릭
    /// </summary>
    public Dictionary<string, object> AdditionalMetrics { get; init; } = new();

    /// <summary>
    /// 통계 수집 기간
    /// </summary>
    public TimeSpan CollectionPeriod { get; init; }

    /// <summary>
    /// 통계 생성 시간
    /// </summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 히트 타입별 통계
    /// </summary>
    public Dictionary<CacheHitType, long> HitsByType { get; init; } = new();

    /// <summary>
    /// 성능 리포트 생성
    /// </summary>
    public string GeneratePerformanceReport()
    {
        return $@"
=== 시맨틱 캐시 성능 리포트 ===
📊 기본 통계:
- 전체 쿼리: {TotalQueries:N0}
- 캐시 히트: {CacheHits:N0} ({HitRate:P1})
- 캐시 미스: {CacheMisses:N0}

⚡ 성능 개선:
- 평균 응답시간: {AverageResponseTime.TotalMilliseconds:F1}ms
- 캐시 응답시간: {CacheResponseTime.TotalMilliseconds:F1}ms
- 성능 향상: {PerformanceGain:P1}

💾 리소스:
- 캐시 항목 수: {CachedItemsCount:N0}
- 메모리 사용량: {MemoryUsageBytes / 1024 / 1024:F1}MB
- 평균 유사도: {AverageSimilarityScore:F3}

📅 수집 기간: {CollectionPeriod.TotalHours:F1}시간
🕒 생성 시간: {GeneratedAt:yyyy-MM-dd HH:mm:ss}
";
    }
}

/// <summary>
/// 캐시 히트 타입
/// </summary>
public enum CacheHitType
{
    /// <summary>
    /// 정확히 일치하는 쿼리
    /// </summary>
    Exact,

    /// <summary>
    /// 시맨틱 유사도 기반 매칭
    /// </summary>
    Semantic,

    /// <summary>
    /// 키워드 기반 매칭
    /// </summary>
    Keyword,

    /// <summary>
    /// 패턴 기반 매칭
    /// </summary>
    Pattern
}

/// <summary>
/// 캐시 옵션 설정
/// </summary>
public class CacheOptions
{
    /// <summary>
    /// 기본 유사도 임계값
    /// </summary>
    public float DefaultSimilarityThreshold { get; set; } = 0.95f;

    /// <summary>
    /// 기본 캐시 만료 시간
    /// </summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// 최대 캐시 크기 (항목 수)
    /// </summary>
    public int MaxCacheSize { get; set; } = 10000;

    /// <summary>
    /// Redis 연결 문자열
    /// </summary>
    public string RedisConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 캐시 키 접두사
    /// </summary>
    public string CacheKeyPrefix { get; set; } = "fluxindex:cache:";

    /// <summary>
    /// 통계 수집 활성화
    /// </summary>
    public bool EnableStatistics { get; set; } = true;

    /// <summary>
    /// 압축 활성화
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// 배치 크기
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// 워밍업 활성화
    /// </summary>
    public bool EnableWarmup { get; set; } = true;

    /// <summary>
    /// 자동 최적화 활성화
    /// </summary>
    public bool EnableAutoOptimization { get; set; } = true;

    /// <summary>
    /// 최적화 실행 간격
    /// </summary>
    public TimeSpan OptimizationInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// 최대 메모리 사용량 (바이트)
    /// </summary>
    public long MaxMemoryUsageBytes { get; set; } = 1024 * 1024 * 1024; // 1GB

    /// <summary>
    /// 기본 설정으로 CacheOptions 생성
    /// </summary>
    public static CacheOptions Default => new();

    /// <summary>
    /// 개발용 설정
    /// </summary>
    public static CacheOptions Development => new()
    {
        DefaultExpiry = TimeSpan.FromMinutes(30),
        MaxCacheSize = 1000,
        EnableStatistics = true,
        EnableWarmup = false
    };

    /// <summary>
    /// 운영용 설정
    /// </summary>
    public static CacheOptions Production => new()
    {
        DefaultExpiry = TimeSpan.FromHours(24),
        MaxCacheSize = 50000,
        EnableStatistics = true,
        EnableCompression = true,
        EnableAutoOptimization = true
    };
}