using FluxIndex.Cache.Redis.Extensions;
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Cache.Redis;

/// <summary>
/// Extension methods for FluxIndexContextBuilder to add Redis cache support.
/// Consumers must reference FluxIndex.Cache.Redis and call these methods.
/// </summary>
public static class FluxIndexContextBuilderExtensions
{
    /// <summary>
    /// Register Redis cache services using the options already set on the builder
    /// (via UseRedisCache()).
    /// </summary>
    public static FluxIndexContextBuilder AddRedisStorage(this FluxIndexContextBuilder builder)
    {
        builder.RegisterStorageServices(services =>
        {
            var options = builder.Options;
            var cacheProvider = options.Cache.CacheProvider?.ToLowerInvariant();

            if (cacheProvider != "redis")
                return;

            services.AddRedisCacheStore(options.Cache.RedisConnectionString);
        });

        return builder;
    }
}
