using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FluxIndex.Cache.Redis;

/// <summary>
/// Redis 캐시 서비스 등록 확장 메서드
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Redis 캐시 서비스 등록
    /// </summary>
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new RedisOptions();
        configuration.GetSection("Redis").Bind(options);
        
        return services.AddRedisCache(options);
    }
    
    /// <summary>
    /// Redis 캐시 서비스 등록 (옵션 직접 지정)
    /// </summary>
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        RedisOptions options)
    {
        services.AddSingleton(options);
        
        // Redis ConnectionMultiplexer 설정
        var configurationOptions = ConfigurationOptions.Parse(options.ConnectionString);
        configurationOptions.ConnectTimeout = options.ConnectTimeout;
        configurationOptions.SyncTimeout = options.SyncTimeout;
        configurationOptions.AsyncTimeout = options.AsyncTimeout;
        configurationOptions.KeepAlive = options.KeepAlive;
        configurationOptions.ConnectRetry = options.ConnectRetry;
        configurationOptions.Ssl = options.UseSsl;
        configurationOptions.AbortOnConnectFail = options.AbortOnConnectFail;
        configurationOptions.DefaultDatabase = options.Database;
        
        // ConnectionMultiplexer를 싱글톤으로 등록
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<RedisCacheService>>();
            try
            {
                var connection = ConnectionMultiplexer.Connect(configurationOptions);
                logger.LogInformation("Successfully connected to Redis at {Endpoints}", 
                    string.Join(", ", connection.GetEndPoints()));
                return connection;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to connect to Redis");
                throw;
            }
        });
        
        // 캐시 서비스 등록
        services.AddSingleton<ICacheService, RedisCacheService>();
        
        return services;
    }
    
    /// <summary>
    /// Redis 캐시 서비스 등록 (연결 문자열만 지정)
    /// </summary>
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        string connectionString)
    {
        var options = new RedisOptions
        {
            ConnectionString = connectionString
        };
        
        return services.AddRedisCache(options);
    }
    
    /// <summary>
    /// Redis 캐시 서비스 등록 (Action 설정)
    /// </summary>
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        Action<RedisOptions> configureOptions)
    {
        var options = new RedisOptions();
        configureOptions(options);
        
        return services.AddRedisCache(options);
    }
}