using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using FluxIndex.SDK.Configuration;
using FluxIndex.SDK.Interfaces;
using FluxIndex.SDK.Services;
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

    /// <summary>
    /// PostgreSQL 벡터 저장소 추가
    /// </summary>
    public static IServiceCollection AddPostgreSQLVectorStore(this IServiceCollection services, string connectionString)
    {
        // NOTE: PostgreSQL implementation available in FluxIndex.Storage.PostgreSQL package
        // For now, using in-memory store as fallback
        services.AddSingleton<IVectorStore, InMemoryVectorStore>();
        return services;
    }

    /// <summary>
    /// SQLite 벡터 저장소 추가
    /// </summary>
    public static IServiceCollection AddSQLiteVectorStore(this IServiceCollection services, Action<SQLiteOptions> configure)
    {
        var options = new SQLiteOptions();
        configure(options);
        // NOTE: SQLite implementation available in FluxIndex.Storage.SQLite package
        // For now, using in-memory store as fallback
        services.AddSingleton<IVectorStore, InMemoryVectorStore>();
        return services;
    }

    /// <summary>
    /// OpenAI 임베딩 서비스 추가 (소비자가 IEmbeddingService 구현 필요)
    /// </summary>
    public static IServiceCollection AddOpenAIEmbedding(this IServiceCollection services, Action<OpenAIOptions> configure)
    {
        var options = new OpenAIOptions();
        configure(options);
        // Consumer must implement IEmbeddingService for OpenAI
        // See UseEmbeddingService<T>() in FluxIndexContextBuilder for custom implementations
        return services;
    }

    /// <summary>
    /// Azure OpenAI 임베딩 서비스 추가 (소비자가 IEmbeddingService 구현 필요)
    /// </summary>
    public static IServiceCollection AddAzureOpenAIEmbedding(this IServiceCollection services, Action<AzureOpenAIOptions> configure)
    {
        var options = new AzureOpenAIOptions();
        configure(options);
        // Consumer must implement IEmbeddingService for Azure OpenAI
        // See UseEmbeddingService<T>() in FluxIndexContextBuilder for custom implementations
        return services;
    }

    /// <summary>
    /// Redis 캐시 서비스 추가
    /// </summary>
    public static IServiceCollection AddRedisCache(this IServiceCollection services, Action<RedisCacheOptions> configure)
    {
        var options = new RedisCacheOptions();
        configure(options);
        // NOTE: Redis implementation available in FluxIndex.Cache.Redis package
        // For now, using in-memory cache as fallback
        services.AddSingleton<ICacheService, InMemoryCacheService>();
        return services;
    }
}

public class SQLiteOptions
{
    public string ConnectionString { get; set; } = "Data Source=fluxindex.db";
    public bool UseInMemory { get; set; }
    public bool AutoMigrate { get; set; }
}

public class OpenAIOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = "text-embedding-3-small";
    public bool IsAzure { get; set; }
}

public class AzureOpenAIOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
}

public class RedisCacheOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}

/// <summary>
/// Document processing pipeline service collection extensions
/// </summary>
public static class DocumentProcessingExtensions
{
    /// <summary>
    /// Adds document processing pipeline with default (mock) services.
    /// Use this when no LLM provider is configured.
    /// </summary>
    public static IServiceCollection AddDocumentProcessingPipeline(this IServiceCollection services)
    {
        // Register mock services for contextual enrichment and QA generation
        // These return empty results when no LLM is available
        services.AddSingleton<IContextualEnrichmentService, MockContextualEnrichmentService>();
        services.AddSingleton<IQAGenerationService, MockQAGenerationService>();
        services.AddSingleton<ITextCompletionService, MockTextCompletionService>();

        // Register the pipeline
        services.AddScoped<FluxIndex.SDK.Processing.DocumentProcessingPipeline>();

        return services;
    }

    /// <summary>
    /// Adds document processing pipeline with custom service implementations.
    /// </summary>
    public static IServiceCollection AddDocumentProcessingPipeline<TContextual, TQA, TCompletion>(
        this IServiceCollection services)
        where TContextual : class, IContextualEnrichmentService
        where TQA : class, IQAGenerationService
        where TCompletion : class, ITextCompletionService
    {
        services.AddSingleton<IContextualEnrichmentService, TContextual>();
        services.AddSingleton<IQAGenerationService, TQA>();
        services.AddSingleton<ITextCompletionService, TCompletion>();
        services.AddScoped<FluxIndex.SDK.Processing.DocumentProcessingPipeline>();

        return services;
    }

    /// <summary>
    /// Adds document processing pipeline with externally registered services.
    /// Assumes IContextualEnrichmentService, IQAGenerationService, and ITextCompletionService
    /// are already registered. Falls back to mock implementations if services are not found.
    /// </summary>
    public static IServiceCollection AddDocumentProcessingPipelineWithFallback(this IServiceCollection services)
    {
        // Register mock services only if not already registered
        services.TryAddSingleton<IContextualEnrichmentService, MockContextualEnrichmentService>();
        services.TryAddSingleton<IQAGenerationService, MockQAGenerationService>();
        services.TryAddSingleton<ITextCompletionService, MockTextCompletionService>();

        // Register the pipeline
        services.AddScoped<FluxIndex.SDK.Processing.DocumentProcessingPipeline>();

        return services;
    }
}