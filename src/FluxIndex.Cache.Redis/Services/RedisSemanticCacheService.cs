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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Cache.Redis.Services;

/// <summary>
/// Redis 기반 시맨틱 캐시 서비스
/// 쿼리 임베딩 벡터의 유사도를 계산하여 캐시 히트 판정
/// </summary>
public partial class RedisSemanticCacheService : ISemanticCacheService, IDisposable
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

            LogSearchingCache(_logger, query);

            // 1. 쿼리 임베딩 생성
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

            // 2. 캐시된 쿼리들의 임베딩과 유사도 계산
            var bestMatch = await FindBestMatchAsync(queryEmbedding, similarityThreshold, cancellationToken);

            if (bestMatch == null)
            {
                await RecordCacheMissAsync();
                LogCacheMiss(_logger, query);
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

            LogCacheHit(_logger, query, bestMatch.Value.CachedQuery, bestMatch.Value.Similarity);

            return cachedResult;
        }
        catch (Exception ex)
        {
            LogRetrieveError(_logger, ex, query);
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
        IReadOnlyList<CacheDocumentChunk> results,
        SearchMetadata? metadata = null,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        ArgumentNullException.ThrowIfNull(results);

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            LogCachingResults(_logger, query);

            // 1. 쿼리 임베딩 생성
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

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

            LogCachedResults(_logger, query, results.Count, expiry);
        }
        catch (Exception ex)
        {
            LogCacheError(_logger, ex, query);
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

            LogInvalidated(_logger, keyArray.Length, pattern);
        }
        catch (Exception ex)
        {
            LogInvalidateError(_logger, ex, pattern);
            throw;
        }
    }

    /// <summary>
    /// 캐시 통계 조회
    /// </summary>
    public async Task<SemanticCacheStatistics> GetCacheStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = new SemanticCacheStatistics { CollectedAt = DateTime.UtcNow };

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
            LogStatisticsError(_logger, ex);
            return new SemanticCacheStatistics { CollectedAt = DateTime.UtcNow };
        }
    }

    /// <summary>
    /// 캐시 워밍업
    /// </summary>
    public async Task WarmupCacheAsync(IReadOnlyList<string> popularQueries, CancellationToken cancellationToken = default)
    {
        if (popularQueries == null || popularQueries.Count == 0)
            return;

        LogWarmupStarting(_logger, popularQueries.Count);

        var tasks = popularQueries.Select(async query =>
        {
            try
            {
                // 이미 캐시된 쿼리는 스킵
                var exists = await _database.KeyExistsAsync(CACHE_KEY_PREFIX + query);
                if (exists)
                    return;

                // 임베딩만 미리 계산해서 저장 (실제 검색 결과는 없음)
                var embedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
                var embeddingKey = EMBEDDING_KEY_PREFIX + query;
                var embeddingBytes = SerializeEmbedding(embedding);

                await _database.StringSetAsync(embeddingKey, embeddingBytes, _options.DefaultTtl);
                await _database.SetAddAsync(QUERY_INDEX_KEY, query);
            }
            catch (Exception ex)
            {
                LogWarmupQueryFailed(_logger, ex, query);
            }
        });

        await Task.WhenAll(tasks);
        LogWarmupCompleted(_logger);
    }

    /// <summary>
    /// 캐시 압축 및 정리
    /// </summary>
    public async Task CompactCacheAsync(CancellationToken cancellationToken = default)
    {
        LogCompactionStarting(_logger);

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

                LogExpiredCleaned(_logger, expiredQueries.Count);
            }

            // 2. 캐시 크기 제한 적용
            if (_options.MaxCacheEntries > 0)
            {
                await EnforceCacheSizeLimitAsync(cancellationToken);
            }

            LogCompactionCompleted(_logger);
        }
        catch (Exception ex)
        {
            LogCompactionError(_logger, ex);
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
                var embeddingKey = EMBEDDING_KEY_PREFIX + ((string?)cachedQuery ?? string.Empty);
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
            return JsonSerializer.Deserialize<CachedSearchResult>((string)resultJson!, GetJsonOptions());
        }
        catch (Exception ex)
        {
            LogDeserializeFailed(_logger, ex, query);
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
            LogRecordHitFailed(_logger, ex);
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
            LogRecordMissFailed(_logger, ex);
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
            LogCacheSizeExceeded(_logger, queryCount, _options.MaxCacheEntries, excessCount);

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
            LogEnforceSizeLimitFailed(_logger, ex);
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
        GC.SuppressFinalize(this);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Searching cache for query: {Query}")]
    private static partial void LogSearchingCache(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache miss for query: {Query}")]
    private static partial void LogCacheMiss(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache hit for query '{Query}' -> '{CachedQuery}' (similarity: {Similarity:F3})")]
    private static partial void LogCacheHit(ILogger logger, string query, string cachedQuery, float similarity);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error retrieving cached result for query: {Query}")]
    private static partial void LogRetrieveError(ILogger logger, Exception exception, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Caching results for query: {Query}")]
    private static partial void LogCachingResults(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cached results for query: {Query} (results: {Count}, TTL: {TTL})")]
    private static partial void LogCachedResults(ILogger logger, string query, int count, TimeSpan ttl);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error caching results for query: {Query}")]
    private static partial void LogCacheError(ILogger logger, Exception exception, string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "Invalidated {Count} cache entries matching pattern: {Pattern}")]
    private static partial void LogInvalidated(ILogger logger, int count, string pattern);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error invalidating cache pattern: {Pattern}")]
    private static partial void LogInvalidateError(ILogger logger, Exception exception, string pattern);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error collecting cache statistics")]
    private static partial void LogStatisticsError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting cache warmup with {Count} popular queries")]
    private static partial void LogWarmupStarting(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to warmup query: {Query}")]
    private static partial void LogWarmupQueryFailed(ILogger logger, Exception exception, string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache warmup completed")]
    private static partial void LogWarmupCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting cache compaction")]
    private static partial void LogCompactionStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaned up {Count} expired cache entries")]
    private static partial void LogExpiredCleaned(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache compaction completed")]
    private static partial void LogCompactionCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during cache compaction")]
    private static partial void LogCompactionError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize cached result for query: {Query}")]
    private static partial void LogDeserializeFailed(ILogger logger, Exception exception, string query);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to record cache hit statistics")]
    private static partial void LogRecordHitFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to record cache miss statistics")]
    private static partial void LogRecordMissFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache size ({Current}) exceeds limit ({Limit}), removing {Excess} oldest entries")]
    private static partial void LogCacheSizeExceeded(ILogger logger, long current, long limit, long excess);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to enforce cache size limit")]
    private static partial void LogEnforceSizeLimitFailed(ILogger logger, Exception exception);

    #endregion
}

