using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using FluxIndex.SDK.Configuration;
using FluxIndex.SDK.Interfaces;
using FluxIndex.SDK.Services;
using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services;

namespace FluxIndex.SDK.Extensions;

/// <summary>
/// FluxIndex 서비스 등록 확장 메서드
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// FluxIndex 서비스 등록
    /// </summary>
    public static IServiceCollection AddFluxIndex(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 설정 바인딩
        var options = new FluxIndexOptions();
        configuration.GetSection("FluxIndex").Bind(options);
        services.Configure<FluxIndexOptions>(configuration.GetSection("FluxIndex"));
        
        // 핵심 서비스 등록
        services.AddSingleton(options);
        
        // 인터페이스 구현체는 아직 구현되지 않았으므로 주석 처리
        // 추후 구현체 완성 시 주석 해제
        
        // 핵심 검색 서비스 등록
        services.AddScoped<ISparseRetriever, BM25SparseRetriever>();
        services.AddScoped<IHybridSearchService, HybridSearchService>();

        // services.AddScoped<IVectorStore, PostgresVectorStore>();
        // services.AddScoped<IEmbeddingService, OpenAIEmbeddingService>();
        // services.AddScoped<IIndexingService, DefaultIndexingService>();
        
        // 캐싱 서비스
        if (options.Cache.EnableEmbeddingCache || options.Cache.EnableSearchCache)
        {
            if (options.Cache.CacheProvider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
            {
                // Redis 캐시 설정
                // services.AddStackExchangeRedisCache(opt =>
                // {
                //     opt.Configuration = options.Cache.RedisConnectionString;
                // });
            }
            else
            {
                // 메모리 캐시 설정
                services.AddMemoryCache();
            }
        }
        
        // HTTP 클라이언트 (OpenAI 등 외부 API용)
        services.AddHttpClient();
        
        // 로깅
        services.AddLogging();
        
        return services;
    }
    
    /// <summary>
    /// FluxIndex 서비스 등록 (액션 설정)
    /// </summary>
    public static IServiceCollection AddFluxIndex(
        this IServiceCollection services,
        Action<FluxIndexOptions> configureOptions)
    {
        var options = new FluxIndexOptions();
        configureOptions(options);
        
        services.AddSingleton(options);
        services.Configure<FluxIndexOptions>(opt =>
        {
            configureOptions(opt);
        });
        
        // 나머지 서비스 등록 로직
        return AddFluxIndexCore(services, options);
    }
    
    /// <summary>
    /// FluxIndex 서비스 등록 (기본 설정)
    /// </summary>
    public static IServiceCollection AddFluxIndex(this IServiceCollection services)
    {
        return services.AddFluxIndex(options =>
        {
            // 기본 설정 사용
        });
    }
    
    private static IServiceCollection AddFluxIndexCore(
        IServiceCollection services,
        FluxIndexOptions options)
    {
        // 캐싱 서비스
        if (options.Cache.EnableEmbeddingCache || options.Cache.EnableSearchCache)
        {
            if (options.Cache.CacheProvider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
            {
                // Redis 캐시 설정
            }
            else
            {
                services.AddMemoryCache();
            }
        }
        
        // HTTP 클라이언트
        services.AddHttpClient();
        
        // 로깅
        services.AddLogging();
        
        return services;
    }

    // NOTE: Storage-specific extension methods (AddPostgreSQLVectorStore, AddRedisCache, etc.)
    // have been moved to their respective storage packages:
    // - FluxIndex.Storage.PostgreSQL: AddPostgreSQLVectorStore()
    // - FluxIndex.Cache.Redis: AddRedisCacheStore()
    // - FluxIndex.Storage.SQLite: AddSQLiteVectorStore()
    // - FluxIndex.Storage.Qdrant: AddQdrantVectorStore()
}
