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
    /// FluxIndex 서비스 등록 — <c>FluxIndex</c> 설정 섹션을 옵션으로 바인딩합니다.
    /// </summary>
    /// <remarks>
    /// 세 오버로드는 같은 기본값을 등록합니다(<see cref="AddFluxIndexCore"/>). 기본값은 <c>TryAdd</c> 로 등록하므로
    /// 이 호출 <b>전에</b> 등록한 소비자 구현이 남습니다. 완성된 인덱싱·검색 파이프라인은 <c>FluxIndexContext.CreateBuilder()</c> 가 조립합니다.
    /// </remarks>
    public static IServiceCollection AddFluxIndex(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("FluxIndex");
        var options = new FluxIndexOptions();
        section.Bind(options);
        services.Configure<FluxIndexOptions>(section);

        return AddFluxIndexCore(services, options);
    }

    /// <summary>
    /// FluxIndex 서비스 등록 (액션 설정)
    /// </summary>
    /// <remarks>기본값 등록 규칙은 <see cref="AddFluxIndex(IServiceCollection, IConfiguration)"/> 와 같습니다.</remarks>
    public static IServiceCollection AddFluxIndex(
        this IServiceCollection services,
        Action<FluxIndexOptions> configureOptions)
    {
        var options = new FluxIndexOptions();
        configureOptions(options);
        services.Configure(configureOptions);

        return AddFluxIndexCore(services, options);
    }

    /// <summary>
    /// FluxIndex 서비스 등록 (기본 설정)
    /// </summary>
    /// <remarks>기본값 등록 규칙은 <see cref="AddFluxIndex(IServiceCollection, IConfiguration)"/> 와 같습니다.</remarks>
    public static IServiceCollection AddFluxIndex(this IServiceCollection services)
        => services.AddFluxIndex(_ => { });

    private static IServiceCollection AddFluxIndexCore(
        IServiceCollection services,
        FluxIndexOptions options)
    {
        services.TryAddSingleton(options);

        // Singleton, not scoped: the default keyword index lives in process memory, so a scoped
        // lifetime would hand each scope its own empty index.
        services.TryAddSingleton<IKeywordSearchService, BM25SparseRetriever>();
        services.TryAddScoped<IHybridSearchService, HybridSearchService>();

        if (options.Cache.EnableEmbeddingCache || options.Cache.EnableSearchCache)
        {
            // CacheProvider "Redis" is not wired here: the Redis store lives in FluxIndex.Cache.Redis
            // (AddRedisCacheStore()). Only the in-memory provider is registered by this method.
            if (!options.Cache.CacheProvider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
                services.AddMemoryCache();
        }

        services.AddHttpClient();
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
