using FluxIndex.Cache.Redis.Configuration;
using FluxIndex.Cache.Redis.Services;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System;

namespace FluxIndex.Cache.Redis.Extensions;

/// <summary>
/// Redis 시맨틱 캐시 서비스를 위한 DI 확장 메서드
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Redis 시맨틱 캐시 서비스를 등록합니다.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="connectionString">Redis 연결 문자열</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddRedisSemanticCache(
        this IServiceCollection services,
        string connectionString)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (connectionString == null)
            throw new ArgumentNullException(nameof(connectionString));

        return services.AddRedisSemanticCache(options =>
        {
            options.ConnectionString = connectionString;
        });
    }

    /// <summary>
    /// Redis 시맨틱 캐시 서비스를 등록합니다.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">Redis 캐시 옵션 구성</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddRedisSemanticCache(
        this IServiceCollection services,
        Action<RedisSemanticCacheOptions> configureOptions)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (configureOptions == null)
            throw new ArgumentNullException(nameof(configureOptions));

        var options = new RedisSemanticCacheOptions();
        configureOptions(options);

        // Redis 연결 등록
        services.TryAddSingleton<IConnectionMultiplexer>(provider =>
        {
            try
            {
                return ConnectionMultiplexer.Connect(options.ConnectionString);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to connect to Redis with connection string: {options.ConnectionString}", ex);
            }
        });

        // Redis 캐시 옵션 등록
        services.Configure(configureOptions);

        // 시맨틱 캐시 서비스 등록
        services.TryAddSingleton<ISemanticCacheService, RedisSemanticCacheService>();

        return services;
    }

    /// <summary>
    /// 기존 Redis 연결을 사용하여 시맨틱 캐시 서비스를 등록합니다.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">Redis 캐시 옵션 구성</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddRedisSemanticCacheWithExistingConnection(
        this IServiceCollection services,
        Action<RedisSemanticCacheOptions> configureOptions)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (configureOptions == null)
            throw new ArgumentNullException(nameof(configureOptions));

        // Redis 캐시 옵션만 등록 (연결은 이미 등록된 것을 사용)
        services.Configure(configureOptions);

        // 시맨틱 캐시 서비스 등록
        services.TryAddSingleton<ISemanticCacheService, RedisSemanticCacheService>();

        return services;
    }

    /// <summary>
    /// Redis를 사용한 분산 캐싱과 함께 시맨틱 캐시 서비스를 등록합니다.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="connectionString">Redis 연결 문자열</param>
    /// <param name="configureCache">Redis 분산 캐시 옵션 구성</param>
    /// <param name="configureSemanticCache">시맨틱 캐시 옵션 구성</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddRedisDistributedCacheWithSemanticCache(
        this IServiceCollection services,
        string connectionString,
        Action<Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions>? configureCache = null,
        Action<RedisSemanticCacheOptions>? configureSemanticCache = null)
    {
        // Redis 분산 캐시 등록
        if (configureCache != null)
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = connectionString;
                configureCache(options);
            });
        }
        else
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = connectionString;
            });
        }

        // 시맨틱 캐시 등록
        if (configureSemanticCache != null)
        {
            services.AddRedisSemanticCache(configureSemanticCache);
        }
        else
        {
            services.AddRedisSemanticCache(connectionString);
        }

        return services;
    }

    #region ICacheStore Implementation

    /// <summary>
    /// Adds RedisCacheStore (ICacheStore implementation) for polyglot persistence.
    /// Provides embedding cache, hot data cache, query result cache, and entity cache.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="connectionString">Redis connection string</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddRedisCacheStore(
        this IServiceCollection services,
        string connectionString,
        Action<RedisCacheStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionString);

        var options = new RedisCacheStoreOptions
        {
            ConnectionString = connectionString
        };
        configure?.Invoke(options);

        // Register options
        services.Configure<RedisCacheStoreOptions>(opt =>
        {
            opt.ConnectionString = options.ConnectionString;
            opt.KeyPrefix = options.KeyPrefix;
            opt.DatabaseNumber = options.DatabaseNumber;
            opt.DefaultTtl = options.DefaultTtl;
            opt.EmbeddingCacheTtl = options.EmbeddingCacheTtl;
            opt.HotDataTtl = options.HotDataTtl;
            opt.QueryResultTtl = options.QueryResultTtl;
            opt.EntityCacheTtl = options.EntityCacheTtl;
            opt.MaxSimilaritySearchKeys = options.MaxSimilaritySearchKeys;
            opt.MaxParallelism = options.MaxParallelism;
            opt.ConnectionTimeoutSeconds = options.ConnectionTimeoutSeconds;
            opt.CommandTimeoutSeconds = options.CommandTimeoutSeconds;
            opt.EnableDetailedLogging = options.EnableDetailedLogging;
        });

        // Register Redis connection
        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
        {
            var configOptions = ConfigurationOptions.Parse(options.ConnectionString);
            configOptions.ConnectTimeout = options.ConnectionTimeoutSeconds * 1000;
            configOptions.SyncTimeout = options.CommandTimeoutSeconds * 1000;
            return ConnectionMultiplexer.Connect(configOptions);
        });

        // Register cache store
        services.TryAddSingleton<ICacheStore, RedisCacheStore>();

        // Also register as ICacheService for compatibility
        services.TryAddSingleton<ICacheService>(sp => sp.GetRequiredService<ICacheStore>());

        return services;
    }

    /// <summary>
    /// Adds RedisCacheStore with existing Redis connection.
    /// </summary>
    public static IServiceCollection AddRedisCacheStoreWithExistingConnection(
        this IServiceCollection services,
        Action<RedisCacheStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register options
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.TryAddSingleton(Options.Create(new RedisCacheStoreOptions()));
        }

        // Register cache store (assumes IConnectionMultiplexer is already registered)
        services.TryAddSingleton<ICacheStore, RedisCacheStore>();
        services.TryAddSingleton<ICacheService>(sp => sp.GetRequiredService<ICacheStore>());

        return services;
    }

    /// <summary>
    /// Adds full Redis cache stack: ICacheStore + ISemanticCacheService.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="connectionString">Redis connection string</param>
    /// <param name="configureCacheStore">Optional ICacheStore configuration</param>
    /// <param name="configureSemanticCache">Optional ISemanticCacheService configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddRedisFullCacheStack(
        this IServiceCollection services,
        string connectionString,
        Action<RedisCacheStoreOptions>? configureCacheStore = null,
        Action<RedisSemanticCacheOptions>? configureSemanticCache = null)
    {
        // Add cache store
        services.AddRedisCacheStore(connectionString, configureCacheStore);

        // Add semantic cache with existing connection
        services.AddRedisSemanticCacheWithExistingConnection(opts =>
        {
            opts.ConnectionString = connectionString;
            configureSemanticCache?.Invoke(opts);
        });

        return services;
    }

    #endregion
}