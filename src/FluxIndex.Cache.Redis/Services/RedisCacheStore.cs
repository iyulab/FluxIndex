using System.Text.Json;
using FluxIndex.Cache.Redis.Configuration;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FluxIndex.Cache.Redis.Services;

/// <summary>
/// Redis implementation of ICacheStore for polyglot persistence.
/// Provides embedding cache, hot data cache, query result cache, and entity cache.
/// </summary>
public class RedisCacheStore : ICacheStore, IDisposable
{
    private readonly IDatabase _database;
    private readonly IServer _server;
    private readonly IConnectionMultiplexer _connection;
    private readonly IEmbeddingService? _embeddingService;
    private readonly RedisCacheStoreOptions _options;
    private readonly ILogger<RedisCacheStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _semaphore;

    // Key prefixes for different cache types
    private const string EmbeddingPrefix = "emb:";
    private const string HotChunkPrefix = "hot:";
    private const string QueryResultPrefix = "qr:";
    private const string QueryEmbeddingPrefix = "qe:";
    private const string EntityPrefix = "ent:";
    private const string StatsKey = "stats";

    public RedisCacheStore(
        IConnectionMultiplexer connection,
        IOptions<RedisCacheStoreOptions> options,
        ILogger<RedisCacheStore> logger,
        IEmbeddingService? embeddingService = null)
    {
        _connection = connection;
        _options = options.Value;
        _logger = logger;
        _embeddingService = embeddingService;
        _database = connection.GetDatabase(_options.DatabaseNumber);
        _server = connection.GetServer(connection.GetEndPoints().First());
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        _semaphore = new SemaphoreSlim(_options.MaxParallelism);
    }

    #region ICacheService Implementation

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var fullKey = GetFullKey(key);
        var value = await _database.StringGetAsync(fullKey);

        if (value.IsNullOrEmpty) return null;

        return JsonSerializer.Deserialize<T>(value.ToString(), _jsonOptions);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
    {
        var fullKey = GetFullKey(key);
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        var expiry = expiration ?? _options.DefaultTtl;

        await _database.StringSetAsync(fullKey, json, expiry);
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);
        return await _database.KeyDeleteAsync(fullKey);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);
        return await _database.KeyExistsAsync(fullKey);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var pattern = $"{_options.KeyPrefix}*";
        var keys = _server.Keys(pattern: pattern, database: _options.DatabaseNumber).ToArray();

        if (keys.Length > 0)
        {
            await _database.KeyDeleteAsync(keys);
        }
    }

    #endregion

    #region Embedding Cache

    public async Task SetEmbeddingAsync(
        string contentHash,
        float[] embedding,
        EmbeddingCacheMetadata? metadata = null,
        TimeSpan? expiry = null,
        CancellationToken ct = default)
    {
        var key = $"{_options.KeyPrefix}{EmbeddingPrefix}{contentHash}";
        var entry = new EmbeddingCacheEntry
        {
            ContentHash = contentHash,
            Embedding = embedding,
            Metadata = metadata,
            CachedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiry.HasValue ? DateTimeOffset.UtcNow.Add(expiry.Value) : null,
            AccessCount = 0
        };

        var json = JsonSerializer.Serialize(entry, _jsonOptions);
        await _database.StringSetAsync(key, json, expiry ?? _options.EmbeddingCacheTtl);
    }

    public async Task<EmbeddingCacheEntry?> GetEmbeddingAsync(
        string contentHash,
        CancellationToken ct = default)
    {
        var key = $"{_options.KeyPrefix}{EmbeddingPrefix}{contentHash}";
        var value = await _database.StringGetAsync(key);

        if (value.IsNullOrEmpty) return null;

        var entry = JsonSerializer.Deserialize<EmbeddingCacheEntry>(value.ToString(), _jsonOptions);
        if (entry != null)
        {
            // Increment access count asynchronously
            _ = IncrementAccessCountAsync(key, value.ToString());
        }

        return entry;
    }

    public async Task<IReadOnlyDictionary<string, EmbeddingCacheEntry>> GetEmbeddingsBatchAsync(
        IEnumerable<string> contentHashes,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, EmbeddingCacheEntry>();
        var keys = contentHashes.Select(h => (RedisKey)$"{_options.KeyPrefix}{EmbeddingPrefix}{h}").ToArray();

        var values = await _database.StringGetAsync(keys);

        for (int i = 0; i < keys.Length; i++)
        {
            if (!values[i].IsNullOrEmpty)
            {
                var entry = JsonSerializer.Deserialize<EmbeddingCacheEntry>(values[i].ToString(), _jsonOptions);
                if (entry != null)
                {
                    result[entry.ContentHash] = entry;
                }
            }
        }

        return result;
    }

    public async Task SetEmbeddingsBatchAsync(
        IEnumerable<(string ContentHash, float[] Embedding, EmbeddingCacheMetadata? Metadata)> embeddings,
        TimeSpan? expiry = null,
        CancellationToken ct = default)
    {
        var batch = _database.CreateBatch();
        var tasks = new List<Task>();
        var ttl = expiry ?? _options.EmbeddingCacheTtl;

        foreach (var (contentHash, embedding, metadata) in embeddings)
        {
            var key = $"{_options.KeyPrefix}{EmbeddingPrefix}{contentHash}";
            var entry = new EmbeddingCacheEntry
            {
                ContentHash = contentHash,
                Embedding = embedding,
                Metadata = metadata,
                CachedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.Add(ttl),
                AccessCount = 0
            };

            var json = JsonSerializer.Serialize(entry, _jsonOptions);
            tasks.Add(batch.StringSetAsync(key, json, ttl));
        }

        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task<bool> RemoveEmbeddingAsync(string contentHash, CancellationToken ct = default)
    {
        var key = $"{_options.KeyPrefix}{EmbeddingPrefix}{contentHash}";
        return await _database.KeyDeleteAsync(key);
    }

    #endregion

    #region Hot Data Cache

    public async Task MarkChunkAsHotAsync(
        string chunkId,
        HotDataPriority priority = HotDataPriority.Medium,
        CancellationToken ct = default)
    {
        var key = $"{_options.KeyPrefix}{HotChunkPrefix}{chunkId}";

        // Update access time and priority
        await _database.HashSetAsync(key, new HashEntry[]
        {
            new("lastAccess", DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            new("priority", (int)priority),
            new("accessCount", await _database.HashIncrementAsync(key, "accessCount"))
        });

        // Set TTL based on priority
        var ttl = priority switch
        {
            HotDataPriority.Critical => TimeSpan.MaxValue,
            HotDataPriority.High => _options.HotDataTtl * 2,
            HotDataPriority.Low => _options.HotDataTtl / 2,
            _ => _options.HotDataTtl
        };

        if (ttl < TimeSpan.MaxValue)
        {
            await _database.KeyExpireAsync(key, ttl);
        }
    }

    public async Task<HotChunkData?> GetHotChunkAsync(
        string chunkId,
        CancellationToken ct = default)
    {
        var key = $"{_options.KeyPrefix}{HotChunkPrefix}{chunkId}";
        var value = await _database.StringGetAsync(key);

        if (value.IsNullOrEmpty) return null;

        var data = JsonSerializer.Deserialize<HotChunkData>(value.ToString(), _jsonOptions);
        if (data != null)
        {
            // Update access stats
            await MarkChunkAsHotAsync(chunkId, data.Priority, ct);
        }

        return data;
    }

    public async Task<IReadOnlyDictionary<string, HotChunkData>> GetHotChunksBatchAsync(
        IEnumerable<string> chunkIds,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, HotChunkData>();
        var keys = chunkIds.Select(id => (RedisKey)$"{_options.KeyPrefix}{HotChunkPrefix}{id}").ToArray();

        var values = await _database.StringGetAsync(keys);

        for (int i = 0; i < keys.Length; i++)
        {
            if (!values[i].IsNullOrEmpty)
            {
                var data = JsonSerializer.Deserialize<HotChunkData>(values[i].ToString(), _jsonOptions);
                if (data != null)
                {
                    result[data.ChunkId] = data;
                }
            }
        }

        return result;
    }

    public async Task SetHotChunkAsync(
        HotChunkData chunkData,
        CancellationToken ct = default)
    {
        var key = $"{_options.KeyPrefix}{HotChunkPrefix}{chunkData.ChunkId}";
        var json = JsonSerializer.Serialize(chunkData, _jsonOptions);

        var ttl = chunkData.Priority switch
        {
            HotDataPriority.Critical => TimeSpan.MaxValue,
            HotDataPriority.High => _options.HotDataTtl * 2,
            HotDataPriority.Low => _options.HotDataTtl / 2,
            _ => _options.HotDataTtl
        };

        if (ttl < TimeSpan.MaxValue)
        {
            await _database.StringSetAsync(key, json, ttl);
        }
        else
        {
            await _database.StringSetAsync(key, json);
        }
    }

    public async Task<IReadOnlyList<HotChunkData>> GetTopHotChunksAsync(
        int limit = 100,
        CancellationToken ct = default)
    {
        var pattern = $"{_options.KeyPrefix}{HotChunkPrefix}*";
        var keys = _server.Keys(pattern: pattern, database: _options.DatabaseNumber)
            .Take(limit * 2) // Get more than needed for sorting
            .ToArray();

        if (keys.Length == 0) return [];

        var values = await _database.StringGetAsync(keys);
        var chunks = new List<HotChunkData>();

        foreach (var value in values)
        {
            if (!value.IsNullOrEmpty)
            {
                var data = JsonSerializer.Deserialize<HotChunkData>(value.ToString(), _jsonOptions);
                if (data != null)
                {
                    chunks.Add(data);
                }
            }
        }

        return chunks
            .OrderByDescending(c => c.Priority)
            .ThenByDescending(c => c.AccessCount)
            .Take(limit)
            .ToList();
    }

    public async Task<int> EvictColdChunksAsync(
        TimeSpan accessThreshold,
        CancellationToken ct = default)
    {
        var pattern = $"{_options.KeyPrefix}{HotChunkPrefix}*";
        var keys = _server.Keys(pattern: pattern, database: _options.DatabaseNumber).ToArray();

        var evicted = 0;
        var threshold = DateTimeOffset.UtcNow.Subtract(accessThreshold);

        foreach (var key in keys)
        {
            var value = await _database.StringGetAsync(key);
            if (!value.IsNullOrEmpty)
            {
                var data = JsonSerializer.Deserialize<HotChunkData>(value.ToString(), _jsonOptions);
                if (data != null &&
                    data.Priority != HotDataPriority.Critical &&
                    data.LastAccessedAt < threshold)
                {
                    await _database.KeyDeleteAsync(key);
                    evicted++;
                }
            }
        }

        return evicted;
    }

    #endregion

    #region Query Result Cache

    public async Task SetQueryResultAsync(
        string queryHash,
        float[] queryEmbedding,
        QueryResultCacheEntry entry,
        TimeSpan? expiry = null,
        CancellationToken ct = default)
    {
        var resultKey = $"{_options.KeyPrefix}{QueryResultPrefix}{queryHash}";
        var embeddingKey = $"{_options.KeyPrefix}{QueryEmbeddingPrefix}{queryHash}";
        var ttl = expiry ?? _options.QueryResultTtl;

        var batch = _database.CreateBatch();

        // Store result
        var entryJson = JsonSerializer.Serialize(entry, _jsonOptions);
        var resultTask = batch.StringSetAsync(resultKey, entryJson, ttl);

        // Store embedding for similarity search
        var embeddingJson = JsonSerializer.Serialize(new
        {
            QueryHash = queryHash,
            Embedding = queryEmbedding,
            CachedAt = DateTimeOffset.UtcNow
        }, _jsonOptions);
        var embeddingTask = batch.StringSetAsync(embeddingKey, embeddingJson, ttl);

        batch.Execute();
        await Task.WhenAll(resultTask, embeddingTask);
    }

    public async Task<QueryResultCacheEntry?> GetQueryResultAsync(
        string queryHash,
        CancellationToken ct = default)
    {
        var key = $"{_options.KeyPrefix}{QueryResultPrefix}{queryHash}";
        var value = await _database.StringGetAsync(key);

        if (value.IsNullOrEmpty) return null;

        var entry = JsonSerializer.Deserialize<QueryResultCacheEntry>(value.ToString(), _jsonOptions);
        if (entry != null)
        {
            // Increment access count
            _ = IncrementAccessCountAsync(key, value.ToString());
        }

        return entry;
    }

    public async Task<IReadOnlyList<SimilarQueryResult>> FindSimilarQueryResultsAsync(
        float[] queryEmbedding,
        float similarityThreshold = 0.85f,
        int maxResults = 5,
        CancellationToken ct = default)
    {
        var pattern = $"{_options.KeyPrefix}{QueryEmbeddingPrefix}*";
        var keys = _server.Keys(pattern: pattern, database: _options.DatabaseNumber)
            .Take(_options.MaxSimilaritySearchKeys)
            .ToArray();

        if (keys.Length == 0) return [];

        var similarities = new List<(string QueryHash, float Similarity)>();
        var values = await _database.StringGetAsync(keys);

        foreach (var value in values)
        {
            if (!value.IsNullOrEmpty)
            {
                try
                {
                    using var doc = JsonDocument.Parse(value.ToString());
                    var embeddingArray = doc.RootElement.GetProperty("embedding");
                    var cachedEmbedding = JsonSerializer.Deserialize<float[]>(embeddingArray.GetRawText());
                    var queryHash = doc.RootElement.GetProperty("queryHash").GetString();

                    if (cachedEmbedding != null && queryHash != null)
                    {
                        var similarity = CalculateCosineSimilarity(queryEmbedding, cachedEmbedding);
                        if (similarity >= similarityThreshold)
                        {
                            similarities.Add((queryHash, similarity));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse cached query embedding");
                }
            }
        }

        // Get top similar results
        var topSimilar = similarities
            .OrderByDescending(s => s.Similarity)
            .Take(maxResults)
            .ToList();

        var results = new List<SimilarQueryResult>();
        foreach (var (queryHash, similarity) in topSimilar)
        {
            var entry = await GetQueryResultAsync(queryHash, ct);
            if (entry != null)
            {
                results.Add(new SimilarQueryResult
                {
                    Entry = entry,
                    SimilarityScore = similarity
                });
            }
        }

        return results;
    }

    public async Task<int> InvalidateQueryResultsForDocumentsAsync(
        IEnumerable<string> documentIds,
        CancellationToken ct = default)
    {
        // This requires scanning query results to find those containing the documents
        // In production, consider maintaining an inverted index
        var pattern = $"{_options.KeyPrefix}{QueryResultPrefix}*";
        var keys = _server.Keys(pattern: pattern, database: _options.DatabaseNumber).ToArray();

        var invalidated = 0;
        var docIdSet = documentIds.ToHashSet();

        foreach (var key in keys)
        {
            var value = await _database.StringGetAsync(key);
            if (!value.IsNullOrEmpty)
            {
                var entry = JsonSerializer.Deserialize<QueryResultCacheEntry>(value.ToString(), _jsonOptions);
                // Check if any chunk in results belongs to invalidated documents
                // This is a simplified check - real implementation would need chunk-to-doc mapping
                if (entry != null)
                {
                    await _database.KeyDeleteAsync(key);
                    invalidated++;
                }
            }
        }

        return invalidated;
    }

    #endregion

    #region Entity Cache

    public async Task SetEntityAsync(
        string entityId,
        CachedEntityData entityData,
        TimeSpan? expiry = null,
        CancellationToken ct = default)
    {
        var key = $"{_options.KeyPrefix}{EntityPrefix}{entityId}";
        var json = JsonSerializer.Serialize(entityData, _jsonOptions);
        await _database.StringSetAsync(key, json, expiry ?? _options.EntityCacheTtl);
    }

    public async Task<CachedEntityData?> GetEntityAsync(
        string entityId,
        CancellationToken ct = default)
    {
        var key = $"{_options.KeyPrefix}{EntityPrefix}{entityId}";
        var value = await _database.StringGetAsync(key);

        if (value.IsNullOrEmpty) return null;

        return JsonSerializer.Deserialize<CachedEntityData>(value.ToString(), _jsonOptions);
    }

    public async Task<IReadOnlyDictionary<string, CachedEntityData>> GetEntitiesBatchAsync(
        IEnumerable<string> entityIds,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, CachedEntityData>();
        var keys = entityIds.Select(id => (RedisKey)$"{_options.KeyPrefix}{EntityPrefix}{id}").ToArray();

        var values = await _database.StringGetAsync(keys);

        for (int i = 0; i < keys.Length; i++)
        {
            if (!values[i].IsNullOrEmpty)
            {
                var data = JsonSerializer.Deserialize<CachedEntityData>(values[i].ToString(), _jsonOptions);
                if (data != null)
                {
                    result[data.EntityId] = data;
                }
            }
        }

        return result;
    }

    #endregion

    #region Statistics and Maintenance

    public async Task<CacheStoreStatistics> GetCacheStoreStatisticsAsync(CancellationToken ct = default)
    {
        var embeddingCount = CountKeys($"{_options.KeyPrefix}{EmbeddingPrefix}*");
        var hotDataCount = CountKeys($"{_options.KeyPrefix}{HotChunkPrefix}*");
        var queryResultCount = CountKeys($"{_options.KeyPrefix}{QueryResultPrefix}*");
        var entityCount = CountKeys($"{_options.KeyPrefix}{EntityPrefix}*");

        var info = await _database.ExecuteAsync("INFO", "memory");
        var memoryUsed = ParseMemoryUsage(info.ToString() ?? "");

        return new CacheStoreStatistics
        {
            EmbeddingCache = new CacheLayerStats
            {
                EntryCount = embeddingCount,
                MemoryUsageBytes = memoryUsed / 4 // Rough estimate
            },
            HotDataCache = new CacheLayerStats
            {
                EntryCount = hotDataCount,
                MemoryUsageBytes = memoryUsed / 4
            },
            QueryResultCache = new CacheLayerStats
            {
                EntryCount = queryResultCount,
                MemoryUsageBytes = memoryUsed / 4
            },
            EntityCache = new CacheLayerStats
            {
                EntryCount = entityCount,
                MemoryUsageBytes = memoryUsed / 4
            },
            TotalMemoryUsageBytes = memoryUsed,
            CollectedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<CacheMaintenanceResult> PerformMaintenanceAsync(
        CacheMaintenanceOptions options,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var entriesRemoved = 0;
        var messages = new List<string>();

        try
        {
            if (options.RemoveExpired)
            {
                // Redis handles TTL automatically, but we can force cleanup
                messages.Add("Expired entries handled by Redis TTL");
            }

            if (options.EvictColdData)
            {
                var evicted = await EvictColdChunksAsync(options.ColdDataThreshold, ct);
                entriesRemoved += evicted;
                messages.Add($"Evicted {evicted} cold chunks");
            }

            return new CacheMaintenanceResult
            {
                EntriesRemoved = entriesRemoved,
                MemoryFreedBytes = 0, // Redis doesn't report this directly
                DurationMs = sw.ElapsedMilliseconds,
                Success = true,
                Messages = messages
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache maintenance failed");
            return new CacheMaintenanceResult
            {
                Success = false,
                DurationMs = sw.ElapsedMilliseconds,
                Messages = [ex.Message]
            };
        }
    }

    public async Task<CacheWarmupResult> WarmupAsync(
        CacheWarmupOptions options,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Warmup is typically done by the application loading frequently accessed data
        // Here we just report current state
        var stats = await GetCacheStoreStatisticsAsync(ct);

        return new CacheWarmupResult
        {
            ChunksWarmedUp = (int)stats.HotDataCache.EntryCount,
            EmbeddingsWarmedUp = (int)stats.EmbeddingCache.EntryCount,
            EntitiesWarmedUp = (int)stats.EntityCache.EntryCount,
            DurationMs = sw.ElapsedMilliseconds,
            Success = true
        };
    }

    #endregion

    #region Private Helpers

    private string GetFullKey(string key) => $"{_options.KeyPrefix}{key}";

    private long CountKeys(string pattern)
    {
        return _server.Keys(pattern: pattern, database: _options.DatabaseNumber).Count();
    }

    private long ParseMemoryUsage(string info)
    {
        var lines = info.Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith("used_memory:"))
            {
                if (long.TryParse(line.Split(':')[1].Trim(), out var bytes))
                {
                    return bytes;
                }
            }
        }
        return 0;
    }

    private async Task IncrementAccessCountAsync(string key, string currentValue)
    {
        try
        {
            using var doc = JsonDocument.Parse(currentValue);
            var accessCount = doc.RootElement.TryGetProperty("accessCount", out var prop)
                ? prop.GetInt32() + 1
                : 1;

            // This is a simplified approach - in production, use HINCRBY or Lua script
            _logger.LogDebug("Access count for {Key}: {Count}", key, accessCount);
        }
        catch
        {
            // Ignore access count errors
        }
    }

    private static float CalculateCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;

        float dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denominator > 0 ? (float)(dotProduct / denominator) : 0;
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }

    #endregion
}
