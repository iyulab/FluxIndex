using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace FluxIndex.Cache.Redis.Services;

/// <summary>
/// Redis 기반 시맨틱 캐시 서비스
/// 벡터 유사도 검색을 통해 의미적으로 유사한 쿼리의 캐시 결과를 반환
/// </summary>
public class RedisSemanticCacheService : ISemanticCacheService, IDisposable
{
    private readonly IDatabase _database;
    private readonly IConnectionMultiplexer _redis;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<RedisSemanticCacheService> _logger;
    private readonly CacheOptions _options;
    private readonly SemaphoreSlim _semaphore;
    private readonly Timer _optimizationTimer;

    private bool _disposed = false;

    public RedisSemanticCacheService(
        IConnectionMultiplexer redis,
        IEmbeddingService embeddingService,
        IOptions<CacheOptions> options,
        ILogger<RedisSemanticCacheService> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _database = _redis.GetDatabase();
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 동시성 제어용 세마포어
        _semaphore = new SemaphoreSlim(_options.BatchSize, _options.BatchSize);

        // 자동 최적화 타이머 설정
        if (_options.EnableAutoOptimization)
        {
            _optimizationTimer = new Timer(
                async _ => await OptimizeCacheAsync(),
                null,
                _options.OptimizationInterval,
                _options.OptimizationInterval);
        }
        else
        {
            _optimizationTimer = null!;
        }

        _logger.LogInformation("RedisSemanticCacheService initialized with options: {Options}",
            JsonSerializer.Serialize(_options, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// 쿼리와 유사한 캐시된 응답을 검색
    /// </summary>
    public async Task<CacheResult?> GetCachedResponseAsync(
        string query,
        float similarityThreshold = 0.95f,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be null or empty", nameof(query));

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var startTime = DateTime.UtcNow;

            // 1. 정확히 일치하는 쿼리 먼저 확인 (빠른 경로)
            var exactMatch = await CheckExactMatchAsync(query, cancellationToken);
            if (exactMatch != null)
            {
                _logger.LogDebug("Exact cache hit for query: {Query}", query);
                await UpdateCacheStatisticsAsync(true, DateTime.UtcNow - startTime);
                return exactMatch;
            }

            // 2. 시맨틱 유사도 검색
            var semanticMatch = await FindSemanticMatchAsync(query, similarityThreshold, cancellationToken);
            if (semanticMatch != null)
            {
                _logger.LogDebug("Semantic cache hit for query: {Query} (similarity: {Similarity})",
                    query, semanticMatch.SimilarityScore);
                await UpdateCacheStatisticsAsync(true, DateTime.UtcNow - startTime);
                return semanticMatch;
            }

            _logger.LogDebug("Cache miss for query: {Query}", query);
            await UpdateCacheStatisticsAsync(false, DateTime.UtcNow - startTime);
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 쿼리와 응답을 캐시에 저장
    /// </summary>
    public async Task CacheResponseAsync(
        string query,
        string response,
        List<SearchResult>? searchResults = null,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be null or empty", nameof(query));

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // 1. 쿼리 임베딩 생성
            var embedding = await _embeddingService.CreateEmbeddingAsync(query, cancellationToken);

            // 2. 캐시 키 생성
            var cacheKey = GenerateCacheKey(query);
            var exactKey = GenerateExactKey(query);

            // 3. 캐시 데이터 구성
            var cacheData = new
            {
                Query = query,
                Response = response,
                SearchResults = searchResults ?? new List<SearchResult>(),
                CachedAt = DateTime.UtcNow,
                Expiry = expiry ?? _options.DefaultExpiry,
                Embedding = embedding.ToArray(),
                EmbeddingDimension = embedding.Length,
                Metadata = new CacheMetadata
                {
                    CacheKey = cacheKey,
                    Source = "RedisSemanticCache",
                    EmbeddingDimension = embedding.Length,
                    UsageCount = 0,
                    LastUsedAt = DateTime.UtcNow
                }
            };

            var serializedData = JsonSerializer.Serialize(cacheData);

            // 4. Redis에 저장 (Hash + Vector Index)
            var tasks = new List<Task>
            {
                // Hash로 데이터 저장
                _database.HashSetAsync(cacheKey, new HashEntry[]
                {
                    new("query", query),
                    new("response", response),
                    new("data", serializedData),
                    new("cached_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                    new("embedding", JsonSerializer.Serialize(embedding.ToArray()))
                }),

                // 정확 매치용 키 저장
                _database.StringSetAsync(exactKey, cacheKey, expiry ?? _options.DefaultExpiry)
            };

            // TTL 설정
            if (expiry.HasValue || _options.DefaultExpiry > TimeSpan.Zero)
            {
                tasks.Add(_database.KeyExpireAsync(cacheKey, expiry ?? _options.DefaultExpiry));
            }

            await Task.WhenAll(tasks);

            _logger.LogDebug("Cached response for query: {Query} with key: {CacheKey}", query, cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cache response for query: {Query}", query);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 배치로 여러 쿼리-응답 쌍을 캐시에 저장
    /// </summary>
    public async Task CacheBatchAsync(
        IEnumerable<CacheEntry> cacheEntries,
        CancellationToken cancellationToken = default)
    {
        var entries = cacheEntries.ToList();
        if (!entries.Any())
            return;

        _logger.LogInformation("Batch caching {Count} entries", entries.Count);

        // 배치 크기만큼 나누어 처리
        var batches = entries.Chunk(_options.BatchSize);

        foreach (var batch in batches)
        {
            var tasks = batch.Select(entry => CacheResponseAsync(
                entry.Query,
                entry.Response,
                entry.SearchResults,
                entry.Expiry,
                cancellationToken));

            await Task.WhenAll(tasks);
        }

        _logger.LogInformation("Completed batch caching of {Count} entries", entries.Count);
    }

    /// <summary>
    /// 캐시 통계 정보 조회
    /// </summary>
    public async Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var statsKey = $"{_options.CacheKeyPrefix}stats";
            var stats = await _database.HashGetAllAsync(statsKey);
            var statsDict = stats.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());

            // 캐시된 항목 수 계산
            var pattern = $"{_options.CacheKeyPrefix}*";
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys = server.Keys(pattern: pattern).ToList();

            var totalQueries = long.Parse(statsDict.GetValueOrDefault("total_queries", "0"));
            var cacheHits = long.Parse(statsDict.GetValueOrDefault("cache_hits", "0"));
            var cacheMisses = totalQueries - cacheHits;

            return new CacheStatistics
            {
                TotalQueries = totalQueries,
                CacheHits = cacheHits,
                CacheMisses = cacheMisses,
                AverageResponseTime = TimeSpan.FromMilliseconds(
                    double.Parse(statsDict.GetValueOrDefault("avg_response_time_ms", "0"))),
                CacheResponseTime = TimeSpan.FromMilliseconds(
                    double.Parse(statsDict.GetValueOrDefault("cache_response_time_ms", "0"))),
                CachedItemsCount = keys.Count,
                MemoryUsageBytes = await CalculateMemoryUsageAsync(),
                AverageSimilarityScore = float.Parse(statsDict.GetValueOrDefault("avg_similarity", "0")),
                CollectionPeriod = TimeSpan.FromHours(24),
                AdditionalMetrics = new Dictionary<string, object>
                {
                    ["redis_memory_usage"] = await GetRedisMemoryInfoAsync(),
                    ["active_connections"] = _redis.GetDatabase().Multiplexer.GetCounters().Interactive.CompletedSynchronously
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cache statistics");
            return new CacheStatistics();
        }
    }

    /// <summary>
    /// 패턴에 매칭되는 캐시 항목들을 무효화
    /// </summary>
    public async Task InvalidateCacheAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPattern = $"{_options.CacheKeyPrefix}{pattern}";
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys = server.Keys(pattern: fullPattern).ToArray();

            if (keys.Any())
            {
                await _database.KeyDeleteAsync(keys);
                _logger.LogInformation("Invalidated {Count} cache entries matching pattern: {Pattern}",
                    keys.Length, pattern);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate cache with pattern: {Pattern}", pattern);
            throw;
        }
    }

    /// <summary>
    /// 자주 사용되는 쿼리들로 캐시를 미리 워밍업
    /// </summary>
    public async Task<bool> WarmupCacheAsync(
        IEnumerable<string> commonQueries,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableWarmup)
        {
            _logger.LogInformation("Cache warmup is disabled");
            return false;
        }

        var queries = commonQueries.ToList();
        _logger.LogInformation("Starting cache warmup with {Count} queries", queries.Count);

        try
        {
            var successCount = 0;
            foreach (var query in queries)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                // 이미 캐시된 쿼리는 건너뛰기
                var existingCache = await GetCachedResponseAsync(query, 0.99f, cancellationToken);
                if (existingCache != null)
                    continue;

                // 워밍업을 위한 더미 응답 생성 (실제로는 검색 서비스 호출 필요)
                var warmupResponse = $"Warmup response for: {query}";
                await CacheResponseAsync(query, warmupResponse, null, TimeSpan.FromHours(1), cancellationToken);
                successCount++;
            }

            _logger.LogInformation("Cache warmup completed. {Success}/{Total} queries warmed up",
                successCount, queries.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache warmup failed");
            return false;
        }
    }

    /// <summary>
    /// 캐시 크기 및 메모리 사용량 최적화
    /// </summary>
    public async Task OptimizeCacheAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting cache optimization");

        try
        {
            // 1. 만료된 키 정리
            await CleanupExpiredKeysAsync();

            // 2. LRU 기반 정리 (캐시 크기 초과시)
            await EnforceCacheSizeLimitAsync();

            // 3. 메모리 사용량 확인 및 정리
            await OptimizeMemoryUsageAsync();

            _logger.LogInformation("Cache optimization completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache optimization failed");
        }
    }

    #region Private Methods

    private async Task<CacheResult?> CheckExactMatchAsync(string query, CancellationToken cancellationToken)
    {
        var exactKey = GenerateExactKey(query);
        var cacheKey = await _database.StringGetAsync(exactKey);

        if (!cacheKey.HasValue)
            return null;

        return await LoadCacheResultAsync(cacheKey!, query, 1.0f, CacheHitType.Exact);
    }

    private async Task<CacheResult?> FindSemanticMatchAsync(
        string query,
        float threshold,
        CancellationToken cancellationToken)
    {
        try
        {
            // 쿼리 임베딩 생성
            var queryEmbedding = await _embeddingService.CreateEmbeddingAsync(query, cancellationToken);

            // Redis Vector Search (RediSearch 필요)
            // 현재는 간단한 구현으로 대체
            var pattern = $"{_options.CacheKeyPrefix}cache:*";
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys = server.Keys(pattern: pattern).Take(100); // 최대 100개 검사

            CacheResult? bestMatch = null;
            float bestSimilarity = 0f;

            foreach (var key in keys)
            {
                var embeddingJson = await _database.HashGetAsync(key, "embedding");
                if (!embeddingJson.HasValue)
                    continue;

                var cachedEmbedding = JsonSerializer.Deserialize<float[]>(embeddingJson!)!;
                var similarity = CalculateCosineSimilarity(queryEmbedding.ToArray(), cachedEmbedding);

                if (similarity >= threshold && similarity > bestSimilarity)
                {
                    var cacheResult = await LoadCacheResultAsync(key!, query, similarity, CacheHitType.Semantic);
                    if (cacheResult != null)
                    {
                        bestMatch = cacheResult;
                        bestSimilarity = similarity;
                    }
                }
            }

            return bestMatch;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in semantic search for query: {Query}", query);
            return null;
        }
    }

    private async Task<CacheResult?> LoadCacheResultAsync(
        string cacheKey,
        string currentQuery,
        float similarity,
        CacheHitType hitType)
    {
        try
        {
            var dataJson = await _database.HashGetAsync(cacheKey, "data");
            if (!dataJson.HasValue)
                return null;

            var cacheData = JsonSerializer.Deserialize<JsonElement>(dataJson!);

            var searchResults = JsonSerializer.Deserialize<List<SearchResult>>(
                cacheData.GetProperty("SearchResults").GetRawText()) ?? new List<SearchResult>();

            var metadata = JsonSerializer.Deserialize<CacheMetadata>(
                cacheData.GetProperty("Metadata").GetRawText()) ?? new CacheMetadata();

            // 사용 통계 업데이트
            metadata.UpdateUsageStats();

            return CacheResult.Create(
                cacheData.GetProperty("Response").GetString() ?? "",
                searchResults,
                similarity,
                cacheData.GetProperty("Query").GetString() ?? "",
                metadata,
                cacheData.TryGetProperty("Expiry", out var expiryElement)
                    ? TimeSpan.Parse(expiryElement.GetString() ?? "24:00:00")
                    : null,
                hitType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load cache result from key: {CacheKey}", cacheKey);
            return null;
        }
    }

    private static float CalculateCosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length)
            return 0f;

        float dotProduct = 0f;
        float norm1 = 0f;
        float norm2 = 0f;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            norm1 += vector1[i] * vector1[i];
            norm2 += vector2[i] * vector2[i];
        }

        if (norm1 == 0f || norm2 == 0f)
            return 0f;

        return dotProduct / (float)(Math.Sqrt(norm1) * Math.Sqrt(norm2));
    }

    private string GenerateCacheKey(string query)
    {
        var hash = query.GetHashCode();
        return $"{_options.CacheKeyPrefix}cache:{hash:X8}";
    }

    private string GenerateExactKey(string query)
    {
        var hash = query.GetHashCode();
        return $"{_options.CacheKeyPrefix}exact:{hash:X8}";
    }

    private async Task UpdateCacheStatisticsAsync(bool isHit, TimeSpan responseTime)
    {
        if (!_options.EnableStatistics)
            return;

        try
        {
            var statsKey = $"{_options.CacheKeyPrefix}stats";
            var tasks = new List<Task>
            {
                _database.HashIncrementAsync(statsKey, "total_queries", 1)
            };

            if (isHit)
            {
                tasks.Add(_database.HashIncrementAsync(statsKey, "cache_hits", 1));
                tasks.Add(_database.HashSetAsync(statsKey, "cache_response_time_ms", responseTime.TotalMilliseconds));
            }
            else
            {
                tasks.Add(_database.HashSetAsync(statsKey, "avg_response_time_ms", responseTime.TotalMilliseconds));
            }

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update cache statistics");
        }
    }

    private async Task<long> CalculateMemoryUsageAsync()
    {
        try
        {
            var info = await _database.ExecuteAsync("MEMORY", "USAGE", $"{_options.CacheKeyPrefix}*");
            return (long)info;
        }
        catch
        {
            return 0; // Redis가 MEMORY 명령을 지원하지 않는 경우
        }
    }

    private async Task<string> GetRedisMemoryInfoAsync()
    {
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var info = await server.InfoAsync("memory");
            return info.FirstOrDefault()?.FirstOrDefault(x => x.Key == "used_memory_human").Value ?? "N/A";
        }
        catch
        {
            return "N/A";
        }
    }

    private async Task CleanupExpiredKeysAsync()
    {
        // Redis의 자동 만료 기능을 사용하므로 별도 구현 불필요
        // 필요시 SCAN을 통해 만료된 키 수동 정리 가능
        await Task.CompletedTask;
    }

    private async Task EnforceCacheSizeLimitAsync()
    {
        var pattern = $"{_options.CacheKeyPrefix}cache:*";
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var keys = server.Keys(pattern: pattern).ToList();

        if (keys.Count <= _options.MaxCacheSize)
            return;

        // LRU 방식으로 오래된 키들 제거
        var keysToRemove = keys.Take(keys.Count - _options.MaxCacheSize).ToArray();
        await _database.KeyDeleteAsync(keysToRemove);

        _logger.LogInformation("Removed {Count} cache entries to enforce size limit", keysToRemove.Length);
    }

    private async Task OptimizeMemoryUsageAsync()
    {
        var currentUsage = await CalculateMemoryUsageAsync();
        if (currentUsage > _options.MaxMemoryUsageBytes)
        {
            _logger.LogWarning("Memory usage {Current} exceeds limit {Limit}, starting aggressive cleanup",
                currentUsage, _options.MaxMemoryUsageBytes);

            // 공격적인 정리 실행
            await EnforceCacheSizeLimitAsync();
        }
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _optimizationTimer?.Dispose();
            _semaphore?.Dispose();
            _disposed = true;
        }
    }
}