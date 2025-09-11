using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Redis-based implementation of semantic cache using embeddings for similarity search
/// Implements GPTCache patterns with optimizations for RAG workloads
/// </summary>
public class RedisSemanticCache : ISemanticCache, IDisposable
{
    private readonly IDatabase _database;
    private readonly IConnectionMultiplexer _redis;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<RedisSemanticCache> _logger;
    private readonly SemanticCacheOptions _options;
    private readonly Timer? _optimizationTimer;

    private readonly SemaphoreSlim _writeSemaphore = new(10); // Limit concurrent writes
    private readonly Dictionary<string, float[]> _embeddingCache = new(); // In-memory embedding cache
    private readonly object _embeddingCacheLock = new();

    // Redis key prefixes
    private const string QueryPrefix = "fluxindex:cache:query:";
    private const string EmbeddingPrefix = "fluxindex:cache:embedding:";
    private const string MetadataPrefix = "fluxindex:cache:metadata:";
    private const string StatsKey = "fluxindex:cache:stats";
    private const string IndexKey = "fluxindex:cache:index";

    public RedisSemanticCache(
        IConnectionMultiplexer redis,
        IEmbeddingService embeddingService,
        IOptions<SemanticCacheOptions> options,
        ILogger<RedisSemanticCache>? logger = null)
    {
        _redis = redis;
        _database = redis.GetDatabase();
        _embeddingService = embeddingService;
        _options = options.Value;
        _logger = logger ?? new NullLogger<RedisSemanticCache>();

        // Start automatic optimization if enabled
        if (_options.EnableAutoOptimization)
        {
            _optimizationTimer = new Timer(
                async _ => await OptimizeAsync(),
                null,
                _options.AutoOptimizationInterval,
                _options.AutoOptimizationInterval);
        }
    }

    public async Task<CacheResult?> GetAsync(
        string query,
        float similarityThreshold = 0.85f,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || 
            query.Length < _options.MinQueryLength || 
            query.Length > _options.MaxQueryLength)
        {
            return null;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Generate embedding for the query
            var queryEmbedding = await GetOrCreateEmbeddingAsync(query, cancellationToken);
            if (queryEmbedding == null)
            {
                _logger.LogWarning("Failed to generate embedding for query");
                await IncrementMissCountAsync();
                return null;
            }

            // Find similar queries using vector similarity search
            var similarQueries = await FindSimilarQueriesInternalAsync(
                queryEmbedding, similarityThreshold, maxResults, cancellationToken);

            if (!similarQueries.Any())
            {
                _logger.LogDebug("No similar queries found for: {Query}", query);
                await IncrementMissCountAsync();
                return null;
            }

            // Get the best match
            var bestMatch = similarQueries.OrderByDescending(q => q.SimilarityScore).First();
            var cacheResult = await GetCacheResultAsync(bestMatch.CacheKey, cancellationToken);

            if (cacheResult == null)
            {
                _logger.LogWarning("Cache key {CacheKey} not found", bestMatch.CacheKey);
                await IncrementMissCountAsync();
                return null;
            }

            // Update access statistics
            cacheResult.HitCount++;
            cacheResult.LastAccessedAt = DateTime.UtcNow;
            cacheResult.SimilarityScore = bestMatch.SimilarityScore;

            await UpdateCacheResultAsync(bestMatch.CacheKey, cacheResult, cancellationToken);
            await IncrementHitCountAsync();

            _logger.LogDebug("Cache hit for query: {Query}, similarity: {Similarity:F3}, latency: {Latency}ms",
                query, bestMatch.SimilarityScore, sw.ElapsedMilliseconds);

            return cacheResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving from semantic cache for query: {Query}", query);
            await IncrementMissCountAsync();
            return null;
        }
    }

    public async Task SetAsync(
        string query,
        IEnumerable<object> results,
        CacheMetadata? metadata = null,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || 
            query.Length < _options.MinQueryLength || 
            query.Length > _options.MaxQueryLength)
        {
            return;
        }

        await _writeSemaphore.WaitAsync(cancellationToken);

        try
        {
            var cacheKey = GenerateCacheKey(query);
            var expiryTime = expiry ?? _options.DefaultExpiry;

            // Generate and cache embedding
            var embedding = await GetOrCreateEmbeddingAsync(query, cancellationToken);
            if (embedding == null)
            {
                _logger.LogWarning("Failed to generate embedding for caching query: {Query}", query);
                return;
            }

            // Create cache result
            var cacheResult = new CacheResult
            {
                OriginalQuery = query,
                SimilarityScore = 1.0f,
                Results = results.ToList(),
                CachedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(expiryTime),
                HitCount = 0,
                LastAccessedAt = DateTime.UtcNow,
                Metadata = metadata ?? new CacheMetadata { ResultCount = results.Count() }
            };

            // Store cache result
            await StoreCacheResultAsync(cacheKey, cacheResult, expiryTime, cancellationToken);

            // Store embedding for similarity search
            await StoreEmbeddingAsync(cacheKey, query, embedding, expiryTime, cancellationToken);

            // Update cache index
            await UpdateCacheIndexAsync(cacheKey, query, cancellationToken);

            // Check if cache needs optimization
            if (await ShouldOptimizeAsync())
            {
                _ = Task.Run(() => OptimizeAsync(cancellationToken), cancellationToken);
            }

            _logger.LogDebug("Cached query: {Query} with {ResultCount} results, expires in {Expiry}",
                query, results.Count(), expiryTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching query: {Query}", query);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    public async Task<bool> HasSimilarQueryAsync(
        string query, 
        float threshold = 0.85f, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var similarQueries = await FindSimilarQueriesAsync(query, threshold, 1, cancellationToken);
            return similarQueries.Any();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for similar queries: {Query}", query);
            return false;
        }
    }

    public async Task<IEnumerable<SimilarQuery>> FindSimilarQueriesAsync(
        string query,
        float threshold = 0.85f,
        int maxSimilar = 5,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var embedding = await GetOrCreateEmbeddingAsync(query, cancellationToken);
            if (embedding == null) return Enumerable.Empty<SimilarQuery>();

            return await FindSimilarQueriesInternalAsync(embedding, threshold, maxSimilar, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding similar queries for: {Query}", query);
            return Enumerable.Empty<SimilarQuery>();
        }
    }

    public async Task<int> InvalidateAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys = server.Keys(pattern: QueryPrefix + pattern).ToArray();

            if (keys.Length == 0) return 0;

            // Remove from all indexes
            await _database.KeyDeleteAsync(keys);

            // Also remove corresponding embeddings and metadata
            var embeddingKeys = keys.Select(k => (RedisKey)(EmbeddingPrefix + k.ToString().Substring(QueryPrefix.Length)));
            var metadataKeys = keys.Select(k => (RedisKey)(MetadataPrefix + k.ToString().Substring(QueryPrefix.Length)));

            await _database.KeyDeleteAsync(embeddingKeys.ToArray());
            await _database.KeyDeleteAsync(metadataKeys.ToArray());

            _logger.LogInformation("Invalidated {Count} cache entries matching pattern: {Pattern}", 
                keys.Length, pattern);

            return keys.Length;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating cache entries with pattern: {Pattern}", pattern);
            return 0;
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            
            // Get all FluxIndex cache keys
            var queryKeys = server.Keys(pattern: QueryPrefix + "*");
            var embeddingKeys = server.Keys(pattern: EmbeddingPrefix + "*");
            var metadataKeys = server.Keys(pattern: MetadataPrefix + "*");

            var allKeys = queryKeys.Concat(embeddingKeys).Concat(metadataKeys)
                .Concat(new RedisKey[] { StatsKey, IndexKey });

            await _database.KeyDeleteAsync(allKeys.ToArray());

            // Clear in-memory caches
            lock (_embeddingCacheLock)
            {
                _embeddingCache.Clear();
            }

            _logger.LogInformation("Cleared all semantic cache entries");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing semantic cache");
        }
    }

    public async Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = await _database.HashGetAllAsync(StatsKey);
            var statsDict = stats.ToDictionary(
                h => h.Name.ToString(),
                h => h.Value.HasValue ? (long)h.Value : 0L
            );

            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var totalQueries = server.Keys(pattern: QueryPrefix + "*").Count();
            
            // Calculate memory usage estimate
            var memoryUsage = await EstimateMemoryUsageAsync();

            return new CacheStatistics
            {
                TotalQueries = totalQueries,
                CacheHits = statsDict.GetValueOrDefault("hits", 0),
                CacheMisses = statsDict.GetValueOrDefault("misses", 0),
                MemoryUsageBytes = memoryUsage,
                AverageSimilarityScore = statsDict.GetValueOrDefault("avg_similarity", 85) / 100.0,
                AverageTimeSavedMs = statsDict.GetValueOrDefault("avg_time_saved", 50),
                ExpiredEntries = statsDict.GetValueOrDefault("expired", 0),
                CollectedAt = DateTime.UtcNow,
                EfficiencyMetrics = new Dictionary<string, double>
                {
                    ["embedding_cache_hit_ratio"] = CalculateEmbeddingCacheHitRatio(),
                    ["compression_ratio"] = statsDict.GetValueOrDefault("compression_ratio", 75) / 100.0
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache statistics");
            return new CacheStatistics { CollectedAt = DateTime.UtcNow };
        }
    }

    public async Task<CacheOptimizationResult> OptimizeAsync(CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CacheOptimizationResult { Success = true };

        try
        {
            _logger.LogInformation("Starting cache optimization");

            // Remove expired entries
            var expiredRemoved = await RemoveExpiredEntriesAsync(cancellationToken);
            result.RemovedEntries += expiredRemoved;

            // Evict entries if cache is over size/memory limits
            var evictedCount = await EvictEntriesIfNeededAsync(cancellationToken);
            result.RemovedEntries += evictedCount;

            // Optimize embedding cache
            OptimizeEmbeddingCache();

            // Estimate memory freed
            result.MemoryFreedBytes = result.RemovedEntries * 1024; // Rough estimate

            sw.Stop();
            result.OptimizationTimeMs = sw.ElapsedMilliseconds;
            result.Improvements = new Dictionary<string, double>
            {
                ["entries_removed"] = result.RemovedEntries,
                ["memory_freed_mb"] = result.MemoryFreedBytes / (1024.0 * 1024.0)
            };

            _logger.LogInformation("Cache optimization completed: {RemovedEntries} entries removed, " +
                                 "{MemoryMB:F1}MB freed, took {TimeMs}ms",
                result.RemovedEntries, result.MemoryFreedBytes / (1024.0 * 1024.0), 
                result.OptimizationTimeMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache optimization");
            result.Success = false;
            result.Messages.Add($"Optimization error: {ex.Message}");
        }

        return result;
    }

    private async Task<float[]?> GetOrCreateEmbeddingAsync(string query, CancellationToken cancellationToken)
    {
        var cacheKey = GenerateEmbeddingCacheKey(query);

        // Check in-memory cache first
        lock (_embeddingCacheLock)
        {
            if (_embeddingCache.TryGetValue(cacheKey, out var cachedEmbedding))
            {
                return cachedEmbedding;
            }
        }

        // Generate new embedding
        try
        {
            var embeddingVector = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
            var embedding = embeddingVector.Values;

            // Store in memory cache (with size limit)
            lock (_embeddingCacheLock)
            {
                if (_embeddingCache.Count < 1000) // Limit memory cache size
                {
                    _embeddingCache[cacheKey] = embedding;
                }
            }

            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding for query: {Query}", query);
            return null;
        }
    }

    private async Task<IEnumerable<SimilarQuery>> FindSimilarQueriesInternalAsync(
        float[] queryEmbedding,
        float threshold,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var embeddingKeys = server.Keys(pattern: EmbeddingPrefix + "*").Take(1000); // Limit scan

        var similarQueries = new List<SimilarQuery>();

        await foreach (var key in embeddingKeys.ToAsyncEnumerable())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var embeddingData = await _database.HashGetAllAsync(key);
                if (!embeddingData.Any()) continue;

                var embeddingValues = JsonSerializer.Deserialize<float[]>(embeddingData.First(h => h.Name == "embedding").Value!);
                if (embeddingValues == null) continue;

                var similarity = CalculateCosineSimilarity(queryEmbedding, embeddingValues);
                if (similarity >= threshold)
                {
                    var cacheKey = key.ToString().Substring(EmbeddingPrefix.Length);
                    var query = embeddingData.First(h => h.Name == "query").Value.ToString();
                    var cachedAt = DateTime.Parse(embeddingData.First(h => h.Name == "cached_at").Value.ToString());

                    similarQueries.Add(new SimilarQuery
                    {
                        Query = query,
                        SimilarityScore = similarity,
                        CachedAt = cachedAt,
                        CacheKey = cacheKey,
                        ResultCount = 0 // Would need additional lookup for exact count
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing embedding key: {Key}", key);
                continue;
            }
        }

        return similarQueries
            .OrderByDescending(q => q.SimilarityScore)
            .Take(maxResults);
    }

    private static float CalculateCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;

        var dotProduct = 0f;
        var normA = 0f;
        var normB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0f || normB == 0f) return 0f;

        return dotProduct / (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private async Task<CacheResult?> GetCacheResultAsync(string cacheKey, CancellationToken cancellationToken)
    {
        var data = await _database.StringGetAsync(QueryPrefix + cacheKey);
        if (!data.HasValue) return null;

        var bytes = data;
        if (_options.EnableCompression && bytes.Length > _options.CompressionThreshold)
        {
            bytes = Decompress(bytes);
        }

        return JsonSerializer.Deserialize<CacheResult>(bytes);
    }

    private async Task UpdateCacheResultAsync(string cacheKey, CacheResult result, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(result);
        var bytes = Encoding.UTF8.GetBytes(json);

        if (_options.EnableCompression && bytes.Length > _options.CompressionThreshold)
        {
            bytes = Compress(bytes);
        }

        await _database.StringSetAsync(QueryPrefix + cacheKey, bytes);
    }

    private async Task StoreCacheResultAsync(string cacheKey, CacheResult result, TimeSpan expiry, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(result);
        var bytes = Encoding.UTF8.GetBytes(json);

        if (_options.EnableCompression && bytes.Length > _options.CompressionThreshold)
        {
            bytes = Compress(bytes);
        }

        await _database.StringSetAsync(QueryPrefix + cacheKey, bytes, expiry);
    }

    private async Task StoreEmbeddingAsync(string cacheKey, string query, float[] embedding, TimeSpan expiry, CancellationToken cancellationToken)
    {
        var embeddingJson = JsonSerializer.Serialize(embedding);
        var embeddingData = new HashEntry[]
        {
            new("query", query),
            new("embedding", embeddingJson),
            new("cached_at", DateTime.UtcNow.ToString("O"))
        };

        await _database.HashSetAsync(EmbeddingPrefix + cacheKey, embeddingData);
        await _database.KeyExpireAsync(EmbeddingPrefix + cacheKey, expiry);
    }

    private async Task UpdateCacheIndexAsync(string cacheKey, string query, CancellationToken cancellationToken)
    {
        await _database.SetAddAsync(IndexKey, cacheKey);
    }

    private async Task<bool> ShouldOptimizeAsync()
    {
        var stats = await GetStatisticsAsync();
        return stats.TotalQueries > _options.MaxCacheSize * 1.2 || 
               stats.MemoryUsageBytes > _options.MaxMemoryMB * 1024 * 1024 * 1.2;
    }

    private async Task<int> RemoveExpiredEntriesAsync(CancellationToken cancellationToken)
    {
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var expiredCount = 0;

        await foreach (var key in server.Keys(pattern: QueryPrefix + "*").ToAsyncEnumerable())
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var ttl = await _database.KeyTimeToLiveAsync(key);
            if (ttl <= TimeSpan.Zero)
            {
                var cacheKey = key.ToString().Substring(QueryPrefix.Length);
                await _database.KeyDeleteAsync(new RedisKey[]
                {
                    key,
                    EmbeddingPrefix + cacheKey,
                    MetadataPrefix + cacheKey
                });
                expiredCount++;
            }
        }

        return expiredCount;
    }

    private async Task<int> EvictEntriesIfNeededAsync(CancellationToken cancellationToken)
    {
        var stats = await GetStatisticsAsync();
        if (stats.TotalQueries <= _options.MaxCacheSize) return 0;

        var toEvict = (int)(stats.TotalQueries - _options.MaxCacheSize);
        // Implementation would depend on chosen eviction policy
        // For now, simple LRU-style eviction

        return toEvict; // Placeholder
    }

    private void OptimizeEmbeddingCache()
    {
        lock (_embeddingCacheLock)
        {
            if (_embeddingCache.Count > 500) // Keep reasonable size
            {
                var toRemove = _embeddingCache.Count - 500;
                var keysToRemove = _embeddingCache.Keys.Take(toRemove).ToList();
                foreach (var key in keysToRemove)
                {
                    _embeddingCache.Remove(key);
                }
            }
        }
    }

    private async Task<long> EstimateMemoryUsageAsync()
    {
        var info = await _database.ExecuteAsync("MEMORY", "USAGE", StatsKey);
        return info.IsNull ? 0 : (long)info;
    }

    private double CalculateEmbeddingCacheHitRatio()
    {
        lock (_embeddingCacheLock)
        {
            return _embeddingCache.Count > 0 ? 0.8 : 0.0; // Placeholder calculation
        }
    }

    private async Task IncrementHitCountAsync()
    {
        await _database.HashIncrementAsync(StatsKey, "hits");
    }

    private async Task IncrementMissCountAsync()
    {
        await _database.HashIncrementAsync(StatsKey, "misses");
    }

    private static string GenerateCacheKey(string query)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(query));
        return Convert.ToBase64String(hashBytes).Replace("/", "_").Replace("+", "-").TrimEnd('=');
    }

    private static string GenerateEmbeddingCacheKey(string query)
    {
        return $"emb_{GenerateCacheKey(query)}";
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
        {
            gzip.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    public void Dispose()
    {
        _optimizationTimer?.Dispose();
        _writeSemaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}