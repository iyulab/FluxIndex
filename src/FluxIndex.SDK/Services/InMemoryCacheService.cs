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
/// 메모리 기반 캐시 서비스 구현 (Core 인터페이스)
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
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var options = expiry.HasValue
            ? new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiry }
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

    public Task<long> RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        // In-memory cache doesn't support pattern-based removal
        _logger.LogWarning("Pattern-based removal is not supported in memory cache");
        return Task.FromResult(0L);
    }

    public async Task CacheSearchResultsAsync(
        string query,
        IEnumerable<Core.Domain.Entities.SearchResult> results,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"search:{ComputeHash(query)}";
        var resultList = results.ToList();
        await SetAsync(cacheKey, resultList, expiry ?? TimeSpan.FromMinutes(5), cancellationToken);
    }

    public async Task<IEnumerable<Core.Domain.Entities.SearchResult>?> GetCachedSearchResultsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"search:{ComputeHash(query)}";
        var results = await GetAsync<List<Core.Domain.Entities.SearchResult>>(cacheKey, cancellationToken);
        return results;
    }

    private string ComputeHash(string text)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(bytes).Replace("/", "_").Replace("+", "-");
    }
}