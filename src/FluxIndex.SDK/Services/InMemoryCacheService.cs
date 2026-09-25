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
        
        // Size on every entry: the injected IMemoryCache is the host's shared instance, and a host
        // that sets SizeLimit on it makes Set throw for an entry without one. One value, one unit.
        _defaultOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(15),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
            Size = 1
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
            ? new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiry, Size = 1 }
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache key exists check for: {Key} = {Exists}")]
    private static partial void LogCacheKeyExistsCheck(ILogger logger, string key, bool exists);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache cleared")]
    private static partial void LogCacheCleared(ILogger logger);

    #endregion
}
