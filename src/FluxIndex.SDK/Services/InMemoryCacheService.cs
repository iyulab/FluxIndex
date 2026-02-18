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
internal sealed partial class InMemoryCacheService : ICacheService
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
            LogCacheHit(_logger, key);
            return Task.FromResult<T?>(value);
        }

        LogCacheMiss(_logger, key);
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
        LogCachedValue(_logger, key);

        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        LogRemovedCachedValue(_logger, key);
        return Task.FromResult(true);
    }

    public Task<long> RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        // In-memory cache doesn't support pattern-based removal
        LogPatternRemovalNotSupported(_logger);
        return Task.FromResult(0L);
    }

    public async Task CacheSearchResultsAsync(
        string query,
        IEnumerable<FluxIndex.Core.Domain.Entities.SearchResult> results,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"search:{ComputeHash(query)}";
        var resultList = results.ToList();
        await SetAsync(cacheKey, resultList, expiry ?? TimeSpan.FromMinutes(5), cancellationToken);
    }

    public async Task<IEnumerable<FluxIndex.Core.Domain.Entities.SearchResult>?> GetCachedSearchResultsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"search:{ComputeHash(query)}";
        var results = await GetAsync<List<FluxIndex.Core.Domain.Entities.SearchResult>>(cacheKey, cancellationToken);
        return results;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var exists = _cache.TryGetValue(key, out _);
        LogCacheKeyExistsCheck(_logger, key, exists);
        return Task.FromResult(exists);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is MemoryCache memoryCache)
        {
            memoryCache.Compact(1.0);
        }
        LogCacheCleared(_logger);
        return Task.CompletedTask;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache hit for key: {Key}")]
    private static partial void LogCacheHit(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache miss for key: {Key}")]
    private static partial void LogCacheMiss(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cached value for key: {Key}")]
    private static partial void LogCachedValue(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Removed cached value for key: {Key}")]
    private static partial void LogRemovedCachedValue(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pattern-based removal is not supported in memory cache")]
    private static partial void LogPatternRemovalNotSupported(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache key exists check for: {Key} = {Exists}")]
    private static partial void LogCacheKeyExistsCheck(ILogger logger, string key, bool exists);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache cleared")]
    private static partial void LogCacheCleared(ILogger logger);

    #endregion

    private static string ComputeHash(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(bytes).Replace("/", "_").Replace("+", "-");
    }
}