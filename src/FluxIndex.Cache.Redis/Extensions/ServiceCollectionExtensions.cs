using FluxIndex.Cache.Redis.Configuration;
using FluxIndex.Cache.Redis.Services;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
}