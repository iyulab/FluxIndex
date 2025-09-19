# Phase 6.1: 시맨틱 캐싱 구현 태스크

## 📋 전체 개요
**목표**: 쿼리 유사도 기반 캐싱으로 응답 속도 50% 향상 및 API 비용 40-60% 절감
**기간**: 2주 (10 영업일)
**성과 지표**: 캐시 히트율 60%, 응답시간 473ms → 250ms

---

## 🏗️ Task 1: Core 인터페이스 및 모델 설계 (2일)

### 1.1 캐시 인터페이스 정의 (1일)
```csharp
// FluxIndex.Core/Application/Interfaces/ISemanticCacheService.cs
public interface ISemanticCacheService
{
    Task<CacheResult?> GetCachedResponseAsync(
        string query,
        float similarityThreshold = 0.95f,
        CancellationToken cancellationToken = default);

    Task CacheResponseAsync(
        string query,
        string response,
        List<SearchResult>? searchResults = null,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    Task InvalidateCacheAsync(string pattern, CancellationToken cancellationToken = default);

    Task<bool> WarmupCacheAsync(
        IEnumerable<string> commonQueries,
        CancellationToken cancellationToken = default);
}
```

### 1.2 캐시 모델 정의 (1일)
```csharp
// FluxIndex.Core/Domain/ValueObjects/CacheModels.cs
public class CacheResult
{
    public string CachedResponse { get; init; } = string.Empty;
    public List<SearchResult> SearchResults { get; init; } = new();
    public float SimilarityScore { get; init; }
    public DateTime CachedAt { get; init; }
    public string OriginalQuery { get; init; } = string.Empty;
    public TimeSpan? Expiry { get; init; }
    public CacheMetadata Metadata { get; init; } = new();
}

public class CacheStatistics
{
    public long TotalQueries { get; init; }
    public long CacheHits { get; init; }
    public long CacheMisses { get; init; }
    public float HitRate => TotalQueries > 0 ? (float)CacheHits / TotalQueries : 0;
    public TimeSpan AverageResponseTime { get; init; }
    public TimeSpan CacheResponseTime { get; init; }
    public Dictionary<string, object> AdditionalMetrics { get; init; } = new();
}

public class CacheOptions
{
    public float DefaultSimilarityThreshold { get; set; } = 0.95f;
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromHours(24);
    public int MaxCacheSize { get; set; } = 10000;
    public string RedisConnectionString { get; set; } = string.Empty;
    public string CacheKeyPrefix { get; set; } = "fluxindex:cache:";
    public bool EnableStatistics { get; set; } = true;
}
```

---

## 🔧 Task 2: Redis 기반 캐시 구현 (4일)

### 2.1 Redis 벡터 캐시 서비스 (2일)
```csharp
// FluxIndex.Cache.Redis/Services/RedisSemanticCacheService.cs
public class RedisSemanticCacheService : ISemanticCacheService
{
    private readonly IDatabase _database;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<RedisSemanticCacheService> _logger;
    private readonly CacheOptions _options;

    public async Task<CacheResult?> GetCachedResponseAsync(
        string query,
        float similarityThreshold = 0.95f,
        CancellationToken cancellationToken = default)
    {
        // 1. 쿼리 임베딩 생성
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        // 2. Redis에서 벡터 검색 (RediSearch + Vector Similarity)
        var searchCommand = $"FT.SEARCH idx:cache_embeddings \"*=>[KNN 10 @embedding $query_vector]\"";

        // 3. 유사도 계산 및 임계값 비교
        // 4. 가장 유사한 캐시 항목 반환
    }

    public async Task CacheResponseAsync(
        string query,
        string response,
        List<SearchResult>? searchResults = null,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        // 1. 쿼리 임베딩 생성
        // 2. 캐시 엔트리 생성 (JSON 직렬화)
        // 3. Redis Hash + Vector Index 저장
        // 4. TTL 설정
        // 5. 통계 업데이트
    }
}
```

### 2.2 Redis 설정 및 인덱스 초기화 (1일)
```csharp
// FluxIndex.Cache.Redis/Configuration/RedisConfiguration.cs
public class RedisCacheConfiguration
{
    public static async Task InitializeAsync(IDatabase database)
    {
        // RediSearch 벡터 인덱스 생성
        var createIndexCommand = @"
FT.CREATE idx:cache_embeddings
ON HASH PREFIX 1 fluxindex:cache:
SCHEMA
  query TEXT SORTABLE
  response TEXT
  embedding VECTOR FLAT 6 TYPE FLOAT32 DIM 1536 DISTANCE_METRIC COSINE
  cached_at NUMERIC SORTABLE
  expiry NUMERIC";

        try
        {
            await database.ExecuteAsync("FT.CREATE", createIndexCommand.Split(' '));
        }
        catch (RedisServerException ex) when (ex.Message.Contains("Index already exists"))
        {
            // 인덱스가 이미 존재하는 경우 무시
        }
    }
}
```

### 2.3 캐시 통계 및 모니터링 (1일)
```csharp
// FluxIndex.Cache.Redis/Services/CacheStatisticsService.cs
public class CacheStatisticsService
{
    public async Task<CacheStatistics> GetStatisticsAsync()
    {
        // Redis에서 통계 데이터 수집
        // 히트율, 평균 응답시간, 캐시 크기 등
        // Sliding window 기반 통계 (최근 1시간, 24시간, 7일)
    }

    public async Task UpdateStatisticsAsync(bool isHit, TimeSpan responseTime)
    {
        // 통계 데이터 실시간 업데이트
        // Redis Stream 또는 Sorted Set 활용
    }
}
```

---

## 🧪 Task 3: 캐시 전략 및 최적화 (3일)

### 3.1 지능형 캐시 워밍업 (1일)
```csharp
// FluxIndex.Cache.Redis/Services/CacheWarmupService.cs
public class CacheWarmupService
{
    public async Task<bool> WarmupFromAnalyticsAsync(CancellationToken cancellationToken = default)
    {
        // 1. 가장 빈번한 쿼리 패턴 분석
        // 2. 시맨틱적으로 유사한 쿼리 그룹 식별
        // 3. 대표 쿼리들에 대한 미리 계산된 응답 생성
        // 4. 캐시에 사전 로드
    }

    public async Task<bool> WarmupFromQueryLogAsync(
        IEnumerable<string> queryLog,
        CancellationToken cancellationToken = default)
    {
        // 쿼리 로그 기반 캐시 워밍업
        var popularQueries = AnalyzeQueryFrequency(queryLog);

        foreach (var query in popularQueries.Take(100))
        {
            if (!await _cacheService.GetCachedResponseAsync(query))
            {
                // 캐시에 없는 인기 쿼리들을 미리 처리
                var result = await _searchService.SearchAsync(query, cancellationToken);
                await _cacheService.CacheResponseAsync(query, result.ToString());
            }
        }
    }
}
```

### 3.2 캐시 무효화 전략 (1일)
```csharp
// FluxIndex.Cache.Redis/Services/CacheInvalidationService.cs
public class CacheInvalidationService
{
    public async Task InvalidateByContentUpdateAsync(string documentId)
    {
        // 문서 업데이트 시 관련된 캐시 엔트리 무효화
        // 문서-쿼리 매핑 기반 선택적 무효화
    }

    public async Task InvalidateBySemanticSimilarityAsync(string[] keywords)
    {
        // 키워드와 시맨틱적으로 유사한 캐시 엔트리 무효화
        // 벡터 검색을 통한 관련 캐시 식별
    }

    public async Task SmartCleanupAsync()
    {
        // LRU + 시맨틱 유사도 기반 정리
        // 1. 오래된 엔트리 식별
        // 2. 시맨틱적으로 중복되는 엔트리 정리
        // 3. 캐시 크기 최적화
    }
}
```

### 3.3 성능 최적화 및 메모리 관리 (1일)
- Redis 메모리 사용량 최적화
- 압축 알고리즘 적용 (gzip, lz4)
- 캐시 키 관리 및 TTL 최적화
- 동시성 제어 및 락 메커니즘

---

## ⚙️ Task 4: SDK 통합 및 설정 (1일)

### 4.1 FluxIndexClient 캐시 통합
```csharp
// FluxIndex.SDK/FluxIndexClient.cs 확장
public class FluxIndexClient
{
    private readonly ISemanticCacheService? _cacheService;

    public async Task<List<SearchResult>> SearchAsync(
        string query,
        SearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // 1. 캐시 확인
        if (_cacheService != null)
        {
            var cachedResult = await _cacheService.GetCachedResponseAsync(query, 0.95f, cancellationToken);
            if (cachedResult != null)
            {
                _logger.LogInformation("Cache hit for query: {Query} (similarity: {Similarity})",
                    query, cachedResult.SimilarityScore);
                return cachedResult.SearchResults;
            }
        }

        // 2. 실제 검색 수행
        var searchResults = await _retriever.SearchAsync(query, options, cancellationToken);

        // 3. 결과 캐싱
        if (_cacheService != null && searchResults.Any())
        {
            await _cacheService.CacheResponseAsync(query, string.Empty, searchResults, cancellationToken: cancellationToken);
        }

        return searchResults;
    }
}
```

### 4.2 빌더 패턴 확장
```csharp
// FluxIndex.SDK/FluxIndexClientBuilder.cs
public FluxIndexClientBuilder WithSemanticCaching(
    Action<CacheOptions>? configure = null)
{
    var options = new CacheOptions();
    configure?.Invoke(options);

    _services.Configure<CacheOptions>(options);
    _services.AddSingleton<ISemanticCacheService, RedisSemanticCacheService>();
    _services.AddSingleton<CacheStatisticsService>();

    return this;
}
```

---

## 📊 성공 기준 및 검증

### 정량적 지표
- ✅ **캐시 히트율**: 60% 이상 (운영 1개월 후)
- ✅ **응답 시간**: 473ms → 250ms (-47%)
- ✅ **API 비용 절감**: 40-60%
- ✅ **메모리 효율성**: 10GB 이하 (100만 쿼리 캐시)

### 정성적 지표
- ✅ **유사도 정확성**: 캐시 히트의 95% 이상이 의미적으로 적절
- ✅ **시스템 안정성**: 캐시 장애 시에도 검색 기능 정상 동작
- ✅ **확장성**: 동시 사용자 1000명 지원

---

## 🚀 다음 단계 연결점

**즉시 혜택**:
- 반복적인 질문에 대한 즉각적인 응답
- LLM API 호출 비용 대폭 절감
- 사용자 경험 개선 (응답 속도)

**Phase 7 평가 프레임워크 연결**:
- 캐시 히트율 및 품질 지표 수집
- A/B 테스트를 통한 캐시 효과 측정
- 사용자 쿼리 패턴 분석 데이터 축적

**장기적 가치**:
- 사용자 주도형 지식 베이스 구축
- 쿼리-답변 쌍 데이터셋으로 모델 파인튜닝 소스
- 비즈니스 인텔리전스 (인기 주제, 사용자 의도 분석)