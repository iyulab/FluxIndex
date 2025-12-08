using FileFlux;
using FluxIndex.AI.LocalReranker;
using FluxIndex.Cache.Redis.Extensions;
using FluxIndex.Cache.Redis.Configuration;
using FluxIndex.Extensions.FluxImprover;
using FluxIndex.Extensions.FluxImprover.Services;
using FluxIndex.SDK;
using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Application.Services;
using FluxIndex.Stack.Infrastructure.Data;
using FluxIndex.Stack.Infrastructure.Repositories;
using FluxIndex.Stack.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// Type aliases to resolve collisions with Core interfaces
using CoreTextCompletionService = FluxIndex.Core.Application.Interfaces.ITextCompletionService;
using CoreEmbeddingService = FluxIndex.Core.Application.Interfaces.IEmbeddingService;
using ISemanticCacheService = FluxIndex.Core.Application.Interfaces.ISemanticCacheService;

namespace FluxIndex.Stack.Infrastructure.Extensions;

/// <summary>
/// Extension methods for registering infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all infrastructure services to the service collection.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDatabase(configuration);
        services.AddRepositories();
        services.AddApplicationServices();
        services.AddFluxIndexSDK(configuration);
        services.AddFileFluxChunking(configuration);
        services.AddLocalRerankerService(configuration);
        services.AddTextCompletionService(configuration);
        services.AddFluxImproverServices(configuration);
        services.AddRedisCache(configuration);

        return services;
    }

    /// <summary>
    /// Adds FileFlux intelligent chunking services.
    /// </summary>
    public static IServiceCollection AddFileFluxChunking(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register FileFlux core services
        services.AddFileFlux();

        // Configure FileFlux chunking options
        services.Configure<FileFluxChunkingConfiguration>(options =>
        {
            var section = configuration.GetSection("FileFlux");
            if (section.Exists())
            {
                options.DefaultStrategy = section.GetValue<string>("Strategy") ?? "Auto";
                options.DefaultMaxChunkSize = section.GetValue<int>("MaxChunkSize", 1024);
                options.DefaultOverlapSize = section.GetValue<int>("OverlapSize", 128);
                options.EnableLanguageDetection = section.GetValue<bool>("EnableLanguageDetection", true);
            }
        });

        // Register FileFlux-based chunking service
        services.AddScoped<IChunkingService, FileFluxChunkingService>();

        return services;
    }

    /// <summary>
    /// Adds database context with PostgreSQL.
    /// </summary>
    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string not configured.");

        // Build NpgsqlDataSource with required features
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        dataSourceBuilder.EnableDynamicJson(); // Required for Dictionary<string, object> to JSONB
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<ServiceDbContext>(options =>
        {
            options.UseNpgsql(dataSource, npgsqlOptions =>
            {
                npgsqlOptions.UseVector(); // EF Core level pgvector support
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);
            });
        });

        return services;
    }

    /// <summary>
    /// Adds repository implementations.
    /// </summary>
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IDocumentChunkRepository, DocumentChunkRepository>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IIndexingJobRepository, IndexingJobRepository>();
        services.AddScoped<IIndexingJobLogRepository, IndexingJobLogRepository>();
        services.AddScoped<ISearchHistoryRepository, SearchHistoryRepository>();
        services.AddScoped<IAiProviderSettingsRepository, AiProviderSettingsRepository>();

        return services;
    }

    /// <summary>
    /// Adds application service implementations.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IIndexingService, IndexingService>();
        services.AddScoped<IChunkService, ChunkService>();
        services.AddScoped<IAiProviderSettingsService, AiProviderSettingsService>();

        // Register embedding service factory for dynamic provider creation
        services.AddSingleton<IEmbeddingServiceFactory, EmbeddingServiceFactory>();

        // Register dynamic embedding provider that reads from AiProviderSettings
        // Falls back to LocalEmbedder when no API is configured
        services.AddSingleton<DynamicEmbeddingProvider>();
        services.AddSingleton<IEmbeddingProvider>(sp =>
        {
            // Use DynamicEmbeddingProvider which reads from Settings DB
            return sp.GetRequiredService<DynamicEmbeddingProvider>();
        });
        services.AddSingleton<IEmbeddingProviderCache>(sp =>
        {
            // IEmbeddingProviderCache is implemented by DynamicEmbeddingProvider
            return sp.GetRequiredService<DynamicEmbeddingProvider>();
        });

        // Also keep SDK-based provider for backward compatibility
        services.AddScoped<FluxIndexEmbeddingProvider>(sp =>
        {
            var embeddingService = sp.GetService<FluxIndex.SDK.Interfaces.IEmbeddingService>();
            if (embeddingService != null)
            {
                return new FluxIndexEmbeddingProvider(embeddingService);
            }
            return null!;
        });

        // Register document content provider
        services.AddSingleton<IDocumentContentProvider, FileSystemContentProvider>();

        return services;
    }

    /// <summary>
    /// Adds LocalReranker cross-encoder based semantic reranking.
    /// </summary>
    public static IServiceCollection AddLocalRerankerService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("LocalReranker");

        // Register ResilientLocalReranker with warmup and fallback
        services.AddResilientLocalRerankerWithWarmup(options =>
        {
            if (section.Exists())
            {
                options.ModelId = section.GetValue<string>("ModelId") ?? "default";
                options.UseGpu = section.GetValue<bool>("UseGpu", false);
                options.BatchSize = section.GetValue<int>("BatchSize", 32);
                options.WarmupOnStartup = section.GetValue<bool>("WarmupOnStartup", true);

                var cacheDir = section.GetValue<string>("CacheDirectory");
                if (!string.IsNullOrEmpty(cacheDir))
                {
                    options.CacheDirectory = cacheDir;
                }
            }
        });

        return services;
    }

    /// <summary>
    /// Adds dynamic text completion service with database-driven configuration.
    /// </summary>
    public static IServiceCollection AddTextCompletionService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register text completion service factory
        services.AddSingleton<ITextCompletionServiceFactory, TextCompletionServiceFactory>();

        // Register dynamic text completion provider that reads from AiProviderSettings
        services.AddSingleton<DynamicTextCompletionProvider>();
        services.AddSingleton<CoreTextCompletionService>(sp =>
        {
            return sp.GetRequiredService<DynamicTextCompletionProvider>();
        });
        services.AddSingleton<ITextCompletionProviderCache>(sp =>
        {
            return sp.GetRequiredService<DynamicTextCompletionProvider>();
        });

        return services;
    }

    /// <summary>
    /// Adds FluxImprover services for chunk enrichment, QA generation, and RAG evaluation.
    /// Requires ITextCompletionService to be registered.
    /// </summary>
    public static IServiceCollection AddFluxImproverServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("FluxImprover");
        var enableFluxImprover = section.GetValue<bool>("Enabled", true);

        if (!enableFluxImprover)
        {
            return services;
        }

        // Register FluxImprover integration using the one-stop method
        // This registers all FluxImprover services and wrappers
        services.AddFluxIndexFluxImprover();

        // Register FluxImproverPipeline for orchestration
        services.AddFluxImproverPipeline();

        return services;
    }

    /// <summary>
    /// Adds Redis caching support with semantic cache.
    /// </summary>
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis");
        if (string.IsNullOrEmpty(redisConnection))
        {
            return services;
        }

        // Register standard Redis distributed cache
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnection;
            options.InstanceName = "FluxIndex:";
        });

        // Register EmbeddingProvider to EmbeddingService adapter for semantic cache
        services.AddSingleton<CoreEmbeddingService>(sp =>
        {
            var embeddingProvider = sp.GetRequiredService<IEmbeddingProvider>();
            return new EmbeddingProviderToEmbeddingServiceAdapter(embeddingProvider);
        });

        // Register Redis semantic cache service
        services.AddRedisSemanticCache(options =>
        {
            var section = configuration.GetSection("SemanticCache");
            options.ConnectionString = redisConnection;
            options.KeyPrefix = section.GetValue<string>("KeyPrefix") ?? "fluxindex:semantic:";
            options.DefaultSimilarityThreshold = section.GetValue<float>("SimilarityThreshold", 0.95f);
            options.DefaultTtl = TimeSpan.FromHours(section.GetValue<int>("TtlHours", 1));
            options.MaxCacheEntries = section.GetValue<long>("MaxEntries", 10000);
            options.MaxParallelism = section.GetValue<int>("MaxParallelism", Environment.ProcessorCount);
            options.DatabaseNumber = section.GetValue<int>("DatabaseNumber", 1);
            options.EnableMetrics = section.GetValue<bool>("EnableMetrics", true);
        });

        return services;
    }
}
