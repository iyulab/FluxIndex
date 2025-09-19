using FluxIndex.Cache.Redis.Configuration;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Cache.Redis.Services;

/// <summary>
/// Redis 기반 시맨틱 캐시 서비스
/// 쿼리 임베딩 벡터의 유사도를 계산하여 캐시 히트 판정
/// </summary>
public class RedisSemanticCacheService : ISemanticCacheService
{
    private readonly IDatabase _database;
    private readonly IServer _server;
    private readonly IEmbeddingService _embeddingService;
    private readonly RedisSemanticCacheOptions _options;
    private readonly ILogger<RedisSemanticCacheService> _logger;
    private readonly SemaphoreSlim _semaphore;

    private const string CACHE_KEY_PREFIX = "semantic_cache:";
    private const string EMBEDDING_KEY_PREFIX = "embedding:";
    private const string STATS_KEY = "cache_stats";
    private const string QUERY_INDEX_KEY = "query_index";

    public RedisSemanticCacheService(
        IConnectionMultiplexer redis,
        IEmbeddingService embeddingService,
        IOptions<RedisSemanticCacheOptions> options,
        ILogger<RedisSemanticCacheService> logger)
    {
        _database = redis.GetDatabase(options.Value.DatabaseNumber);
        _server = redis.GetServers().FirstOrDefault()
            ?? throw new InvalidOperationException("No Redis servers available");
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _semaphore = new SemaphoreSlim(_options.MaxParallelism, _options.MaxParallelism);

        ValidateOptions();
    }

    /// <summary>
    /// 캐시에서 유사한 쿼리의 결과 검색
    /// </summary>
    public async Task<CachedSearchResult?> GetCachedResultAsync(
        string query,
        float similarityThreshold = 0.95f,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            _logger.LogDebug("Searching cache for query: {Query}", query);

            // 1. 쿼리 임베딩 생성
            var queryEmbedding = await _embeddingService.CreateEmbeddingAsync(query, cancellationToken);

            // 2. 캐시된 쿼리들의 임베딩과 유사도 계산
            var bestMatch = await FindBestMatchAsync(queryEmbedding, similarityThreshold, cancellationToken);

            if (bestMatch == null)
            {
                await RecordCacheMissAsync();
                _logger.LogDebug("Cache miss for query: {Query}", query);
                return null;
            }

            // 3. 캐시된 결과 로드
            var cachedResult = await LoadCachedResultAsync(bestMatch.Value.CachedQuery, cancellationToken);
            if (cachedResult == null)
            {
                await RecordCacheMissAsync();
                return null;
            }

            // 4. 통계 업데이트
            await RecordCacheHitAsync(bestMatch.Value.CachedQuery, stopwatch.ElapsedMilliseconds);

            cachedResult.OriginalQuery = query;
            cachedResult.SimilarityScore = bestMatch.Value.Similarity;
            cachedResult.HitCount++;
            cachedResult.LastAccessedAt = DateTime.UtcNow;

            _logger.LogInformation("Cache hit for query '{Query}' -> '{CachedQuery}' (similarity: {Similarity:F3})",
                query, bestMatch.Value.CachedQuery, bestMatch.Value.Similarity);

            return cachedResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cached result for query: {Query}", query);
            await RecordCacheMissAsync();
            return null;
        }
        finally
        {
            _semaphore.Release();
            stopwatch.Stop();
        }
    }

    /// <summary>
    /// 검색 결과를 캐시에 저장
    /// </summary>
    public async Task SetCachedResultAsync(
        string query,
        IReadOnlyList<DocumentChunk> results,
        SearchMetadata? metadata = null,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        if (results == null)
            throw new ArgumentNullException(nameof(results));

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            _logger.LogDebug("Caching results for query: {Query}", query);

            // 1. 쿼리 임베딩 생성
            var queryEmbedding = await _embeddingService.CreateEmbeddingAsync(query, cancellationToken);

            // 2. 캐시 결과 객체 생성
            var cachedResult = new CachedSearchResult
            {
                OriginalQuery = query,
                CachedQuery = query,
                SimilarityScore = 1.0f,
                Results = results,
                Metadata = metadata,
                CachedAt = DateTime.UtcNow,
                HitCount = 0,
                LastAccessedAt = DateTime.UtcNow
            };

            // 3. Redis에 저장
            var tasks = new List<Task>();
            var expiry = ttl ?? _options.DefaultTtl;

            // 캐시 결과 저장
            var cacheKey = CACHE_KEY_PREFIX + query;
            var resultJson = JsonSerializer.Serialize(cachedResult, GetJsonOptions());
            tasks.Add(_database.StringSetAsync(cacheKey, resultJson, expiry));

            // 임베딩 벡터 저장
            var embeddingKey = EMBEDDING_KEY_PREFIX + query;
            var embeddingBytes = SerializeEmbedding(queryEmbedding);
            tasks.Add(_database.StringSetAsync(embeddingKey, embeddingBytes, expiry));

            // 쿼리 인덱스에 추가
            tasks.Add(_database.SetAddAsync(QUERY_INDEX_KEY, query));

            await Task.WhenAll(tasks);

            // 4. 캐시 크기 제한 확인 및 정리
            if (_options.MaxCacheEntries > 0)
            {
                _ = Task.Run(() => EnforceCacheSizeLimitAsync(cancellationToken), cancellationToken);
            }

            _logger.LogInformation("Cached results for query: {Query} (results: {Count}, TTL: {TTL})",
                query, results.Count, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching results for query: {Query}", query);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 특정 쿼리 패턴의 캐시 무효화
    /// </summary>
    public async Task InvalidateCacheAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return;

        try
        {
            var keys = _server.Keys(_database.Database, CACHE_KEY_PREFIX + pattern, pageSize: 1000);
            var keyArray = keys.ToArray();

            if (keyArray.Length == 0)
                return;

            var tasks = new List<Task>();
            foreach (var key in keyArray)
            {
                var query = key.ToString().Substring(CACHE_KEY_PREFIX.Length);
                tasks.Add(_database.KeyDeleteAsync(key));
                tasks.Add(_database.KeyDeleteAsync(EMBEDDING_KEY_PREFIX + query));
                tasks.Add(_database.SetRemoveAsync(QUERY_INDEX_KEY, query));
            }

            await Task.WhenAll(tasks);

            _logger.LogInformation("Invalidated {Count} cache entries matching pattern: {Pattern}",
                keyArray.Length, pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating cache pattern: {Pattern}", pattern);
            throw;
        }
    }

    /// <summary>
    /// 캐시 통계 조회
    /// </summary>
    public async Task<CacheStatistics> GetCacheStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = new CacheStatistics { CollectedAt = DateTime.UtcNow };

            // Redis에서 통계 정보 수집
            var statsData = await _database.HashGetAllAsync(STATS_KEY);
            var statsDict = statsData.ToDictionary(x => x.Name!, x => x.Value!);

            if (statsDict.TryGetValue("cache_hits", out var hits))
                stats.CacheHits = (long)hits;

            if (statsDict.TryGetValue("cache_misses", out var misses))
                stats.CacheMisses = (long)misses;

            if (statsDict.TryGetValue("avg_response_time", out var avgTime))
                stats.AverageResponseTimeMs = (float)avgTime;

            if (statsDict.TryGetValue("avg_similarity", out var avgSim))
                stats.AverageSimilarityScore = (float)avgSim;

            // 캐시 엔트리 수 계산
            stats.TotalEntries = (long)await _database.SetLengthAsync(QUERY_INDEX_KEY);

            // 캐시 크기 추정 (샘플링 기반)
            var sampleKeys = _server.Keys(_database.Database, CACHE_KEY_PREFIX + "*", pageSize: 100).Take(50);
            long totalSize = 0;
            int sampleCount = 0;

            foreach (var key in sampleKeys)
            {
                var size = await _database.StringLengthAsync(key);
                totalSize += size;
                sampleCount++;
            }

            if (sampleCount > 0)
            {
                var avgSize = totalSize / sampleCount;
                stats.CacheSizeBytes = avgSize * stats.TotalEntries;
            }

            // 최고 성능 쿼리들 (향후 구현 가능)
            stats.TopPerformingQueries = Array.Empty<QueryPerformance>();

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting cache statistics");
            return new CacheStatistics { CollectedAt = DateTime.UtcNow };
        }
    }

    /// <summary>
    /// 캐시 워밍업
    /// </summary>
    public async Task WarmupCacheAsync(IReadOnlyList<string> popularQueries, CancellationToken cancellationToken = default)
    {
        if (popularQueries == null || popularQueries.Count == 0)
            return;

        _logger.LogInformation("Starting cache warmup with {Count} popular queries", popularQueries.Count);

        var tasks = popularQueries.Select(async query =>
        {
            try
            {
                // 이미 캐시된 쿼리는 스킵
                var exists = await _database.KeyExistsAsync(CACHE_KEY_PREFIX + query);
                if (exists)
                    return;

                // 임베딩만 미리 계산해서 저장 (실제 검색 결과는 없음)
                var embedding = await _embeddingService.CreateEmbeddingAsync(query, cancellationToken);
                var embeddingKey = EMBEDDING_KEY_PREFIX + query;
                var embeddingBytes = SerializeEmbedding(embedding);

                await _database.StringSetAsync(embeddingKey, embeddingBytes, _options.DefaultTtl);
                await _database.SetAddAsync(QUERY_INDEX_KEY, query);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to warmup query: {Query}", query);
            }
        });

        await Task.WhenAll(tasks);
        _logger.LogInformation("Cache warmup completed");
    }

    /// <summary>
    /// 캐시 압축 및 정리
    /// </summary>
    public async Task CompactCacheAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting cache compaction");

        try
        {
            // 1. 만료된 키들 정리
            var queryKeys = await _database.SetMembersAsync(QUERY_INDEX_KEY);
            var expiredQueries = new List<RedisValue>();

            foreach (var query in queryKeys)
            {
                var cacheKey = CACHE_KEY_PREFIX + query;
                var exists = await _database.KeyExistsAsync(cacheKey);
                if (!exists)
                {
                    expiredQueries.Add(query);
                }
            }

            if (expiredQueries.Count > 0)
            {
                await _database.SetRemoveAsync(QUERY_INDEX_KEY, expiredQueries.ToArray());

                var cleanupTasks = expiredQueries.Select(async query =>
                {
                    await _database.KeyDeleteAsync(EMBEDDING_KEY_PREFIX + query);
                });
                await Task.WhenAll(cleanupTasks);

                _logger.LogInformation("Cleaned up {Count} expired cache entries", expiredQueries.Count);
            }

            // 2. 캐시 크기 제한 적용
            if (_options.MaxCacheEntries > 0)
            {
                await EnforceCacheSizeLimitAsync(cancellationToken);
            }

            _logger.LogInformation("Cache compaction completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache compaction");
            throw;
        }
    }

    /// <summary>
    /// 최적 매치 검색
    /// </summary>
    private async Task<(string CachedQuery, float Similarity)?> FindBestMatchAsync(
        float[] queryEmbedding,
        float similarityThreshold,
        CancellationToken cancellationToken)
    {
        var cachedQueries = await _database.SetMembersAsync(QUERY_INDEX_KEY);
        if (cachedQueries.Length == 0)
            return null;

        var bestMatch = (CachedQuery: string.Empty, Similarity: 0f);

        // 병렬로 유사도 계산
        var semaphore = new SemaphoreSlim(_options.MaxParallelism);
        var tasks = cachedQueries.Select(async cachedQuery =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var embeddingKey = EMBEDDING_KEY_PREFIX + (string)cachedQuery;
                var embeddingBytes = await _database.StringGetAsync(embeddingKey);

                if (!embeddingBytes.HasValue)
                    return (CachedQuery: string.Empty, Similarity: 0f);

                var cachedEmbedding = DeserializeEmbedding(embeddingBytes!);
                var similarity = CalculateCosineSimilarity(queryEmbedding, cachedEmbedding);

                return (CachedQuery: (string)cachedQuery!, Similarity: similarity);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        semaphore.Dispose();

        foreach (var result in results)
        {
            if (result.Similarity > bestMatch.Similarity && result.Similarity >= similarityThreshold)
            {
                bestMatch = result;
            }
        }

        return bestMatch.Similarity >= similarityThreshold ? bestMatch : null;
    }

    /// <summary>
    /// 캐시된 결과 로드
    /// </summary>
    private async Task<CachedSearchResult?> LoadCachedResultAsync(string query, CancellationToken cancellationToken)
    {
        var cacheKey = CACHE_KEY_PREFIX + query;
        var resultJson = await _database.StringGetAsync(cacheKey);

        if (!resultJson.HasValue)
            return null;

        try
        {
            return JsonSerializer.Deserialize<CachedSearchResult>(resultJson!, GetJsonOptions());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize cached result for query: {Query}", query);
            return null;
        }
    }

    /// <summary>
    /// 코사인 유사도 계산
    /// </summary>
    private static float CalculateCosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length)
            return 0f;

        var dotProduct = 0f;
        var magnitude1 = 0f;
        var magnitude2 = 0f;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            magnitude1 += vector1[i] * vector1[i];
            magnitude2 += vector2[i] * vector2[i];
        }

        var magnitudeProduct = (float)(Math.Sqrt(magnitude1) * Math.Sqrt(magnitude2));
        return magnitudeProduct == 0f ? 0f : dotProduct / magnitudeProduct;
    }

    /// <summary>
    /// 임베딩 벡터 직렬화
    /// </summary>
    private static byte[] SerializeEmbedding(float[] embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>
    /// 임베딩 벡터 역직렬화
    /// </summary>
    private static float[] DeserializeEmbedding(byte[] bytes)
    {
        var embedding = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, embedding, 0, bytes.Length);
        return embedding;
    }

    /// <summary>
    /// 캐시 히트 기록
    /// </summary>
    private async Task RecordCacheHitAsync(string query, long responseTimeMs)
    {
        try
        {
            await _database.HashIncrementAsync(STATS_KEY, "cache_hits", 1);

            // 평균 응답 시간 업데이트 (이동 평균)
            var currentAvg = await _database.HashGetAsync(STATS_KEY, "avg_response_time");
            var currentHits = await _database.HashGetAsync(STATS_KEY, "cache_hits");

            if (currentAvg.HasValue && currentHits.HasValue)
            {
                var newAvg = ((float)currentAvg * ((long)currentHits - 1) + responseTimeMs) / (long)currentHits;
                await _database.HashSetAsync(STATS_KEY, "avg_response_time", newAvg);
            }
            else
            {
                await _database.HashSetAsync(STATS_KEY, "avg_response_time", responseTimeMs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record cache hit statistics");
        }
    }

    /// <summary>
    /// 캐시 미스 기록
    /// </summary>
    private async Task RecordCacheMissAsync()
    {
        try
        {
            await _database.HashIncrementAsync(STATS_KEY, "cache_misses", 1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record cache miss statistics");
        }
    }

    /// <summary>
    /// 캐시 크기 제한 적용
    /// </summary>
    private async Task EnforceCacheSizeLimitAsync(CancellationToken cancellationToken)
    {
        try
        {
            var queryCount = await _database.SetLengthAsync(QUERY_INDEX_KEY);
            if (queryCount <= _options.MaxCacheEntries)
                return;

            var excessCount = queryCount - _options.MaxCacheEntries;
            _logger.LogInformation("Cache size ({Current}) exceeds limit ({Limit}), removing {Excess} oldest entries",
                queryCount, _options.MaxCacheEntries, excessCount);

            // LRU 방식으로 오래된 엔트리 제거 (간단한 구현)
            var queries = await _database.SetMembersAsync(QUERY_INDEX_KEY);
            var toRemove = queries.Take((int)excessCount);

            var removeTasks = toRemove.Select(async query =>
            {
                await _database.KeyDeleteAsync(CACHE_KEY_PREFIX + query);
                await _database.KeyDeleteAsync(EMBEDDING_KEY_PREFIX + query);
                await _database.SetRemoveAsync(QUERY_INDEX_KEY, query);
            });

            await Task.WhenAll(removeTasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enforce cache size limit");
        }
    }

    /// <summary>
    /// JSON 직렬화 옵션
    /// </summary>
    private static JsonSerializerOptions GetJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// 설정 유효성 검증
    /// </summary>
    private void ValidateOptions()
    {
        if (_options.DefaultTtl <= TimeSpan.Zero)
            throw new ArgumentException("DefaultTtl must be positive");

        if (_options.MaxParallelism <= 0)
            throw new ArgumentException("MaxParallelism must be positive");
    }

    /// <summary>
    /// 리소스 정리
    /// </summary>
    public void Dispose()
    {
        _semaphore?.Dispose();
    }
}

