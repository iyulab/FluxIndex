using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FluxIndex.SDK.Services;

/// <summary>
/// 메모리 기반 캐시 서비스 구현 (기본값)
/// </summary>
internal class InMemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<InMemoryCacheService> _logger;
    private readonly MemoryCacheEntryOptions _defaultOptions;

    public InMemoryCacheService(
        IMemoryCache cache,
        ILogger<InMemoryCacheService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _defaultOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(15),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        };
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) 
        where T : class
    {
        if (_cache.TryGetValue<T>(key, out var value))
        {
            _logger.LogDebug("Cache hit for key: {Key}", key);
            return Task.FromResult<T?>(value);
        }
        
        _logger.LogDebug("Cache miss for key: {Key}", key);
        return Task.FromResult<T?>(null);
    }

    public Task SetAsync<T>(
        string key, 
        T value, 
        TimeSpan? expiration = null, 
        CancellationToken cancellationToken = default) 
        where T : class
    {
        var options = expiration.HasValue
            ? new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiration }
            : _defaultOptions;
        
        _cache.Set(key, value, options);
        _logger.LogDebug("Cached value for key: {Key}", key);
        
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        _logger.LogDebug("Removed cached value for key: {Key}", key);
        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_cache.TryGetValue(key, out _));
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        // In-memory cache doesn't support full clearing without disposing
        // This would require maintaining a list of all keys
        _logger.LogWarning("Full clearing is not supported in default memory cache");
        return Task.CompletedTask;
    }

    public async Task<IEnumerable<SearchResult>> CacheSearchResultsAsync(
        string queryKey,
        IEnumerable<SearchResult> results,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        var resultList = results.ToList();
        var cacheKey = $"search:{queryKey}";
        await SetAsync(cacheKey, resultList, expiration ?? TimeSpan.FromMinutes(5), cancellationToken);
        return resultList;
    }

    public async Task<IEnumerable<SearchResult>?> GetCachedSearchResultsAsync(
        string queryKey,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"search:{queryKey}";
        var results = await GetAsync<List<SearchResult>>(cacheKey, cancellationToken);
        return results;
    }

    public async Task CacheEmbeddingAsync(
        string text,
        float[] embedding,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"embedding:{ComputeHash(text)}";
        await SetAsync(cacheKey, embedding, expiration ?? TimeSpan.FromHours(24), cancellationToken);
    }

    public Task<float[]?> GetCachedEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"embedding:{ComputeHash(text)}";
        return GetAsync<float[]>(cacheKey, cancellationToken);
    }

    public Task InvalidateDocumentCacheAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        // In-memory cache doesn't support pattern-based invalidation
        _logger.LogWarning("Document cache invalidation is not fully supported in memory cache");
        return Task.CompletedTask;
    }

    public Task<long> GetCacheSizeAsync(CancellationToken cancellationToken = default)
    {
        // Memory cache doesn't expose size information
        return Task.FromResult(0L);
    }

    public Task<IDictionary<string, object>> GetCacheStatsAsync(
        CancellationToken cancellationToken = default)
    {
        var stats = new Dictionary<string, object>
        {
            ["type"] = "InMemory",
            ["sliding_expiration"] = _defaultOptions.SlidingExpiration?.ToString() ?? "N/A",
            ["absolute_expiration"] = _defaultOptions.AbsoluteExpirationRelativeToNow?.ToString() ?? "N/A"
        };
        
        return Task.FromResult<IDictionary<string, object>>(stats);
    }

    private string ComputeHash(string text)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(bytes).Replace("/", "_").Replace("+", "-");
    }
}