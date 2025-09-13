using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FluxIndex.Cache.Redis;

/// <summary>
/// Redis 기반 분산 캐시 서비스 구현
/// </summary>
public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly RedisOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisCacheService(
        IConnectionMultiplexer redis,
        ILogger<RedisCacheService> logger,
        RedisOptions options)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        
        _database = _redis.GetDatabase(_options.Database);
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) 
        where T : class
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be empty", nameof(key));

        try
        {
            var prefixedKey = GetPrefixedKey(key);
            var value = await _database.StringGetAsync(prefixedKey);
            
            if (value.IsNullOrEmpty)
            {
                _logger.LogDebug("Cache miss for key: {Key}", key);
                return null;
            }

            _logger.LogDebug("Cache hit for key: {Key}", key);
            
            var result = JsonSerializer.Deserialize<T>(value!, _jsonOptions);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cached value for key: {Key}", key);
            return null;
        }
    }

    public async Task SetAsync<T>(
        string key, 
        T value, 
        TimeSpan? expiration = null, 
        CancellationToken cancellationToken = default) 
        where T : class
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be empty", nameof(key));
        
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        try
        {
            var prefixedKey = GetPrefixedKey(key);
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            var ttl = expiration ?? TimeSpan.FromSeconds(_options.DefaultTtlSeconds);
            
            var result = await _database.StringSetAsync(prefixedKey, json, ttl);
            
            if (result)
            {
                _logger.LogDebug("Successfully cached value for key: {Key} with TTL: {TTL}", key, ttl);
            }
            else
            {
                _logger.LogWarning("Failed to cache value for key: {Key}", key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching value for key: {Key}", key);
        }
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be empty", nameof(key));

        try
        {
            var prefixedKey = GetPrefixedKey(key);
            var result = await _database.KeyDeleteAsync(prefixedKey);
            
            if (result)
            {
                _logger.LogDebug("Successfully removed cached value for key: {Key}", key);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cached value for key: {Key}", key);
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be empty", nameof(key));

        try
        {
            var prefixedKey = GetPrefixedKey(key);
            return await _database.KeyExistsAsync(prefixedKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking existence for key: {Key}", key);
            return false;
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var searchPattern = $"{_options.KeyPrefix}:*";
            
            var keys = server.Keys(_options.Database, searchPattern).ToArray();
            
            if (keys.Any())
            {
                await _database.KeyDeleteAsync(keys);
                _logger.LogInformation("Cleared {Count} cached items", keys.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
        }
    }

    public async Task<IEnumerable<SearchResult>> CacheSearchResultsAsync(
        string queryKey,
        IEnumerable<SearchResult> results,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        var resultList = results.ToList();
        
        if (!resultList.Any())
            return resultList;

        var cacheKey = $"search:{queryKey}";
        await SetAsync(cacheKey, resultList, expiration, cancellationToken);
        
        return resultList;
    }

    public async Task<IEnumerable<SearchResult>?> GetCachedSearchResultsAsync(
        string queryKey,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"search:{queryKey}";
        return await GetAsync<List<SearchResult>>(cacheKey, cancellationToken);
    }

    public async Task CacheEmbeddingAsync(
        string text,
        float[] embedding,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be empty", nameof(text));
        
        if (embedding == null || embedding.Length == 0)
            throw new ArgumentException("Embedding cannot be null or empty", nameof(embedding));

        var cacheKey = $"embedding:{ComputeHash(text)}";
        await SetAsync(cacheKey, embedding, expiration, cancellationToken);
    }

    public async Task<float[]?> GetCachedEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be empty", nameof(text));

        var cacheKey = $"embedding:{ComputeHash(text)}";
        return await GetAsync<float[]>(cacheKey, cancellationToken);
    }

    public async Task InvalidateDocumentCacheAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        // 문서 관련 모든 캐시 무효화
        var patterns = new[]
        {
            $"doc:{documentId}:*",
            $"search:*{documentId}*",
            $"chunk:{documentId}:*"
        };

        foreach (var pattern in patterns)
        {
            await ClearByPatternAsync(pattern, cancellationToken);
        }
        
        _logger.LogInformation("Invalidated cache for document: {DocumentId}", documentId);
    }

    public async Task<long> GetCacheSizeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var searchPattern = $"{_options.KeyPrefix}:*";
            var keys = server.Keys(_options.Database, searchPattern);
            return keys.LongCount();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache size");
            return 0;
        }
    }

    public async Task<IDictionary<string, object>> GetCacheStatsAsync(
        CancellationToken cancellationToken = default)
    {
        var stats = new Dictionary<string, object>();
        
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var info = await server.InfoAsync("stats");
            
            if (info != null && info.Any())
            {
                var statsSection = info.FirstOrDefault(s => s.Key == "Stats");
                if (statsSection.Any())
                {
                    foreach (var stat in statsSection)
                    {
                        stats[stat.Key] = stat.Value;
                    }
                }
            }
            
            // 추가 커스텀 통계
            stats["cache_size"] = await GetCacheSizeAsync(cancellationToken);
            stats["key_prefix"] = _options.KeyPrefix;
            stats["database"] = _options.Database;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache statistics");
        }
        
        return stats;
    }

    private string GetPrefixedKey(string key)
    {
        return $"{_options.KeyPrefix}:{key}";
    }

    private string ComputeHash(string text)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(bytes).Replace("/", "_").Replace("+", "-");
    }

    private async Task ClearByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var prefixedPattern = GetPrefixedKey(pattern);
            var keys = server.Keys(_options.Database, prefixedPattern);

            var database = _redis.GetDatabase(_options.Database);
            foreach (var key in keys)
            {
                await database.KeyDeleteAsync(key);
            }

            _logger.LogDebug("Cleared {Count} keys matching pattern: {Pattern}", keys.LongCount(), pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache by pattern: {Pattern}", pattern);
        }
    }
}