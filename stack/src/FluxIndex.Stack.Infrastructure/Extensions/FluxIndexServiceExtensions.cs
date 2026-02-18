using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.SDK;
using FluxIndex.SDK.Configuration;
using FluxIndex.SDK.Extensions;
using FluxIndex.Storage.PostgreSQL;
using FluxIndex.Cache.Redis.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace FluxIndex.Stack.Infrastructure.Extensions;

/// <summary>
/// Extension methods for registering FluxIndex SDK services.
/// Provides comprehensive integration with multiple AI providers, vector stores, and caching strategies.
/// </summary>
public static class FluxIndexServiceExtensions
{
    /// <summary>
    /// Adds FluxIndex SDK services with full configuration support.
    /// Supports multiple AI providers (OpenAI, Anthropic, Google, Azure, Local),
    /// multiple vector stores (PostgreSQL pgvector, Qdrant), Redis caching,
    /// and FileFlux integration for document processing.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration containing FluxIndex settings</param>
    /// <param name="configureOptions">Optional additional configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFluxIndexSDK(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<FluxIndexOptions>? configureOptions = null)
    {
        // Load configuration from IConfiguration
        var fluxIndexConfig = configuration.GetSection("FluxIndex");
        var options = new FluxIndexOptions();
        fluxIndexConfig.Bind(options);

        // Apply additional configuration if provided
        configureOptions?.Invoke(options);

        // Create FluxIndexContextBuilder
        var builder = new FluxIndexContextBuilder();

        // Configure vector store
        ConfigureVectorStore(builder, options, configuration);

        // Configure embedding service (AI provider)
        ConfigureEmbeddingService(builder, options);

        // Configure cache service
        ConfigureCacheService(builder, options, configuration);

        // Configure chunking strategy
        ConfigureChunking(builder, options);

        // Configure search options
        ConfigureSearchOptions(builder, options);

        // Configure parallel processing
        ConfigureParallelProcessing(builder, options);

        // Configure quality monitoring
        ConfigureQualityMonitoring(builder, options);

        // Configure RAG Enhancement features
        ConfigureRAGEnhancement(services, options);

        // Configure FileFlux integration if enabled
        ConfigureFileFluxIntegration(services, options);

        // Configure local reranker if enabled
        ConfigureLocalReranker(services, options);

        // Build context and register as singleton
        var context = builder.Build();
        services.AddSingleton<IFluxIndexContext>(context);
        services.AddSingleton(context);

        // Register Retriever and Indexer separately for direct access
        services.AddSingleton(context.Retriever);
        services.AddSingleton(context.Indexer);

        // Register Core services from FluxIndexContext's ServiceProvider
        // These are needed by HybridSearchService and other advanced search services
        services.AddScoped<IVectorStore>(sp =>
            context.ServiceProvider.GetRequiredService<IVectorStore>());
        services.AddScoped<ISparseRetriever>(sp =>
            context.ServiceProvider.GetRequiredService<ISparseRetriever>());
        services.AddScoped<IEmbeddingService>(sp =>
            context.ServiceProvider.GetRequiredService<IEmbeddingService>());
        services.AddScoped<IHybridSearchService>(sp =>
            context.ServiceProvider.GetRequiredService<IHybridSearchService>());
        services.AddScoped<ISmallToBigRetriever>(sp =>
            context.ServiceProvider.GetRequiredService<ISmallToBigRetriever>());
        services.AddScoped<IRankFusionService>(sp =>
            context.ServiceProvider.GetRequiredService<IRankFusionService>());
        services.AddScoped<IAdaptiveSearchService>(sp =>
            context.ServiceProvider.GetRequiredService<IAdaptiveSearchService>());

        // Register options for configuration access
        services.AddSingleton(options);

        return services;
    }

    /// <summary>
    /// Configure vector store based on provider setting.
    /// Supports PostgreSQL with pgvector and Qdrant.
    /// </summary>
    private static void ConfigureVectorStore(
        FluxIndexContextBuilder builder,
        FluxIndexOptions options,
        IConfiguration configuration)
    {
        var provider = options.VectorStore.Provider?.ToLowerInvariant();
        var connectionString = options.VectorStore.ConnectionString;

        // Fallback to connection strings section if not specified in FluxIndex config
        if (string.IsNullOrEmpty(connectionString))
        {
            connectionString = configuration.GetConnectionString("PostgreSQL");
        }

        switch (provider)
        {
            case "postgresql":
            case "postgres":
            case "pgvector":
                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new InvalidOperationException(
                        "PostgreSQL connection string is required. " +
                        "Configure it in FluxIndex:VectorStore:ConnectionString or ConnectionStrings:PostgreSQL");
                }
                builder.UsePostgreSQL(connectionString);
                break;

            case "sqlite":
                var dbPath = string.IsNullOrEmpty(connectionString)
                    ? "fluxindex.db"
                    : connectionString;
                builder.UseLocalStorage(dbPath);
                break;

            case "inmemory":
                builder.UseLocalStorage(":memory:");
                break;

            default:
                // Default to PostgreSQL if provider not specified
                if (!string.IsNullOrEmpty(connectionString))
                {
                    builder.UsePostgreSQL(connectionString);
                }
                else
                {
                    throw new InvalidOperationException(
                        "Vector store provider and connection string must be configured. " +
                        "Set FluxIndex:VectorStore:Provider and FluxIndex:VectorStore:ConnectionString");
                }
                break;
        }
    }

    /// <summary>
    /// Configure embedding service based on AI provider setting.
    /// Stack uses DynamicEmbeddingProvider from database settings for actual embeddings.
    /// This method configures InMemory embedding as fallback for SDK internal operations.
    /// Real embedding is handled by IEmbeddingServiceFactory and DynamicEmbeddingProvider.
    /// </summary>
    private static void ConfigureEmbeddingService(
        FluxIndexContextBuilder builder,
        FluxIndexOptions options)
    {
        // Stack uses DynamicEmbeddingProvider for actual embedding operations
        // SDK embedding is used as fallback for internal operations only
        // All real embedding is delegated to IEmbeddingServiceFactory implementations
        builder.UseInMemoryEmbedding();

        var provider = options.Embedding.Provider?.ToLowerInvariant();
        if (!string.IsNullOrEmpty(provider) && provider != "inmemory")
        {
            Console.WriteLine(
                $"[FluxIndex] Provider '{provider}' configured. " +
                "Embedding is handled by DynamicEmbeddingProvider. " +
                "Configure AI providers in Settings UI for external API access.");
        }
    }

    /// <summary>
    /// Configure cache service (Redis or in-memory).
    /// </summary>
    private static void ConfigureCacheService(
        FluxIndexContextBuilder builder,
        FluxIndexOptions options,
        IConfiguration configuration)
    {
        var cacheProvider = options.Cache.CacheProvider?.ToLowerInvariant();
        var redisConnectionString = options.Cache.RedisConnectionString;

        // Fallback to connection strings section
        if (string.IsNullOrEmpty(redisConnectionString))
        {
            redisConnectionString = configuration.GetConnectionString("Redis");
        }

        switch (cacheProvider)
        {
            case "redis":
                if (string.IsNullOrEmpty(redisConnectionString))
                {
                    throw new InvalidOperationException(
                        "Redis connection string is required. " +
                        "Configure FluxIndex:Cache:RedisConnectionString or ConnectionStrings:Redis");
                }
                builder.UseRedisCache(redisConnectionString);
                break;

            case "memory":
                builder.UseMemoryCache(options.Cache.MaxCacheSize);
                break;

            case "none":
            case "disabled":
                // No caching
                break;

            default:
                // Default to memory cache if not specified
                if (options.Cache.EnableSearchCache || options.Cache.EnableEmbeddingCache)
                {
                    builder.UseMemoryCache(options.Cache.MaxCacheSize);
                }
                break;
        }

        // Configure cache duration if specified
        if (options.Cache.CacheTTL > TimeSpan.Zero)
        {
            builder.WithCacheDuration(options.Cache.CacheTTL);
        }
    }

    /// <summary>
    /// Configure chunking strategy and parameters.
    /// </summary>
    private static void ConfigureChunking(
        FluxIndexContextBuilder builder,
        FluxIndexOptions options)
    {
        var strategy = options.Indexing.ChunkingDefaults.Strategy;
        var maxChunkSize = options.Indexing.ChunkingDefaults.MaxChunkSize;
        var overlapSize = options.Indexing.ChunkingDefaults.OverlapSize;

        if (maxChunkSize <= 0) maxChunkSize = 512;
        if (overlapSize < 0) overlapSize = 64;

        builder.WithChunking(strategy, maxChunkSize, overlapSize);
    }

    /// <summary>
    /// Configure search options and defaults.
    /// </summary>
    private static void ConfigureSearchOptions(
        FluxIndexContextBuilder builder,
        FluxIndexOptions options)
    {
        var maxResults = options.Search.DefaultMaxResults;
        var minScore = options.Search.DefaultMinScore;

        if (maxResults <= 0) maxResults = 10;
        if (minScore < 0) minScore = 0.0f;

        builder.WithSearchOptions(maxResults, minScore);
    }

    /// <summary>
    /// Configure parallel processing options.
    /// </summary>
    private static void ConfigureParallelProcessing(
        FluxIndexContextBuilder builder,
        FluxIndexOptions options)
    {
        var maxParallel = options.Indexing.MaxParallelDocuments;
        if (maxParallel <= 0) maxParallel = 4;

        builder.WithParallelProcessing(true, maxParallel);
    }

    /// <summary>
    /// Configure quality monitoring system.
    /// </summary>
    private static void ConfigureQualityMonitoring(
        FluxIndexContextBuilder builder,
        FluxIndexOptions options)
    {
        if (options.QualityMonitoring.EnableMonitoring)
        {
            builder.WithQualityMonitoring(options.QualityMonitoring.EnableRealTimeAlerts);
        }
    }

    /// <summary>
    /// Configure FileFlux integration for document processing.
    /// </summary>
    private static void ConfigureFileFluxIntegration(
        IServiceCollection services,
        FluxIndexOptions options)
    {
        // FileFlux integration is optional and can be configured separately
        // This method is here for future extensibility
        // Users can call services.AddFileFluxIntegration() separately if needed

        // Example configuration (commented out - users should configure explicitly):
        // services.AddFileFluxIntegration(fileFluxOptions =>
        // {
        //     fileFluxOptions.DefaultChunkingStrategy = ChunkingStrategies.Intelligent;
        //     fileFluxOptions.DefaultMaxChunkSize = options.Indexing.ChunkingDefaults.MaxChunkSize;
        //     fileFluxOptions.DefaultOverlapSize = options.Indexing.ChunkingDefaults.OverlapSize;
        //     fileFluxOptions.EnableMetadataEnrichment = true;
        // });
    }

    /// <summary>
    /// Configure local reranker for improved search quality.
    /// </summary>
    private static void ConfigureLocalReranker(
        IServiceCollection services,
        FluxIndexOptions options)
    {
        // Local reranker can be optionally enabled
        // This uses neural reranking with local models (no API required)

        // Example configuration (commented out - users should configure explicitly):
        // services.AddLocalReranker(rerankerOptions =>
        // {
        //     rerankerOptions.ModelId = "cross-encoder/ms-marco-MiniLM-L-6-v2";
        //     rerankerOptions.EnableCaching = true;
        // });
    }

    /// <summary>
    /// Configure advanced RAG Enhancement features.
    /// Supports three modes:
    /// - Disabled: No enhancements (legacy behavior)
    /// - Auto: Intelligent selection based on content/query characteristics (default)
    /// - Custom: Manual configuration via sub-options
    /// </summary>
    private static void ConfigureRAGEnhancement(
        IServiceCollection services,
        FluxIndexOptions options)
    {
        var ragOptions = options.RAGEnhancement;

        // Skip if disabled
        if (!ragOptions.IsEnabled)
            return;

        // Auto mode: Register all services with intelligent defaults
        // Services will self-select based on content characteristics at runtime
        if (ragOptions.IsAutoMode)
        {
            // Register Late Chunking with auto-activation thresholds
            services.AddLateChunking(lateChunkingOptions =>
            {
                lateChunkingOptions.MaxDocumentLength = 8000;
                lateChunkingOptions.ContextIntegrationMode = ContextIntegrationMode.SurroundingContext;
                lateChunkingOptions.DocumentContextWeight = 0.3;
                lateChunkingOptions.SurroundingContextSize = 500;
            });

            // Register Contextual Embedding with balanced threshold
            services.AddContextualEmbedding(contextualOptions =>
            {
                contextualOptions.LlmThreshold = 0.6; // Balanced: rule-based for simple, LLM for complex
                contextualOptions.GenerateDualEmbeddings = false; // Single embedding for efficiency
            });

            return;
        }

        // Custom mode: Use explicit configuration
        if (ragOptions.LateChunking.Enabled)
        {
            var contextMode = Enum.TryParse<ContextIntegrationMode>(
                ragOptions.LateChunking.ContextIntegrationMode,
                out var mode) ? mode : ContextIntegrationMode.SurroundingContext;

            services.AddLateChunking(lateChunkingOptions =>
            {
                lateChunkingOptions.MaxDocumentLength = ragOptions.LateChunking.MaxDocumentLength;
                lateChunkingOptions.ContextIntegrationMode = contextMode;
                lateChunkingOptions.DocumentContextWeight = ragOptions.LateChunking.DocumentContextWeight;
                lateChunkingOptions.SurroundingContextSize = ragOptions.LateChunking.SurroundingContextSize;
            });
        }

        if (ragOptions.ContextualRetrieval.Enabled)
        {
            services.AddContextualEmbedding(contextualOptions =>
            {
                contextualOptions.LlmThreshold = ragOptions.ContextualRetrieval.LlmThreshold;
                contextualOptions.GenerateDualEmbeddings = ragOptions.ContextualRetrieval.GenerateDualEmbeddings;
            });
        }

        // Multi-Hypothetical HyDE is applied at query time via IQueryTransformationService
        // No additional registration needed - the options are passed to the service when invoked
    }

    /// <summary>
    /// Adds FluxIndex SDK services with custom builder configuration.
    /// Provides maximum flexibility for advanced scenarios.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureBuilder">Builder configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFluxIndexSDK(
        this IServiceCollection services,
        Action<FluxIndexContextBuilder> configureBuilder)
    {
        ArgumentNullException.ThrowIfNull(configureBuilder);

        // Create builder and apply configuration
        var builder = new FluxIndexContextBuilder();
        configureBuilder(builder);

        // Build and register context
        builder.ConfigureServices(innerServices =>
        {
            foreach (var service in innerServices)
            {
                services.Add(service);
            }
        });

        var context = builder.Build();
        services.AddSingleton<IFluxIndexContext>(context);
        services.AddSingleton(context);
        services.AddSingleton(context.Retriever);
        services.AddSingleton(context.Indexer);

        return services;
    }

    /// <summary>
    /// Adds FluxIndex SDK services with minimal configuration.
    /// Uses LocalEmbedder by default (no API keys required).
    /// Ideal for development and testing.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="connectionString">PostgreSQL connection string</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFluxIndexSDKDevelopment(
        this IServiceCollection services,
        string? connectionString = null)
    {
        return services.AddFluxIndexSDK(builder =>
        {
            // Use local storage (SQLite) for development - Vector + Graph + Cache all included
            if (string.IsNullOrEmpty(connectionString))
            {
                builder.UseLocalStorage("fluxindex-dev.db");
            }
            else
            {
                builder.UsePostgreSQL(connectionString);
            }

            // Use InMemory embedding as fallback (actual embedding via DynamicEmbeddingProvider)
            builder.UseInMemoryEmbedding();

            // Memory cache for development
            builder.UseMemoryCache(1000);

            // Standard chunking
            builder.WithChunking("Auto", 512, 64);

            // Development-friendly search settings
            builder.WithSearchOptions(10, 0.2f);

            // Limited parallelism for development
            builder.WithParallelProcessing(true, 2);
        });
    }

    /// <summary>
    /// Adds FluxIndex SDK services with production-ready configuration.
    /// Requires configuration section with all connection strings and API keys.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration containing FluxIndex settings</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFluxIndexSDKProduction(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new FluxIndexOptions();
        configuration.GetSection("FluxIndex").Bind(options);

        // Validate required configuration
        ValidateProductionConfiguration(options, configuration);

        return services.AddFluxIndexSDK(configuration, opt =>
        {
            // Enable quality monitoring for production
            opt.QualityMonitoring.EnableMonitoring = true;
            opt.QualityMonitoring.EnableRealTimeAlerts = true;

            // Optimize for production performance
            opt.Indexing.MaxParallelDocuments = Environment.ProcessorCount;
            opt.Search.SearchTimeout = TimeSpan.FromSeconds(5);
        });
    }

    /// <summary>
    /// Validate production configuration to ensure all required settings are present.
    /// </summary>
    private static void ValidateProductionConfiguration(
        FluxIndexOptions options,
        IConfiguration configuration)
    {
        var errors = new List<string>();

        // Validate vector store
        var vectorStoreConnectionString = options.VectorStore.ConnectionString
            ?? configuration.GetConnectionString("PostgreSQL");

        if (string.IsNullOrEmpty(vectorStoreConnectionString))
        {
            errors.Add("Vector store connection string is required for production. " +
                      "Configure FluxIndex:VectorStore:ConnectionString or ConnectionStrings:PostgreSQL");
        }

        // Validate embedding provider (warn if using LocalEmbedder in production)
        var provider = options.Embedding.Provider?.ToLowerInvariant();
        if (provider == "localembedder" || provider == "local" || provider == "inmemory")
        {
            // Warning: LocalEmbedder is not recommended for production
            // But we don't fail validation - let users decide
        }

        // Validate API keys for cloud providers
        if ((provider == "openai" || provider == "azureopenai" || provider == "azure")
            && string.IsNullOrEmpty(options.Embedding.ApiKey))
        {
            errors.Add($"API key is required for {provider} embedding provider. " +
                      "Configure FluxIndex:Embedding:ApiKey");
        }

        // Validate cache configuration for production
        if (options.Cache.CacheProvider?.ToLowerInvariant() == "redis")
        {
            var redisConnectionString = options.Cache.RedisConnectionString
                ?? configuration.GetConnectionString("Redis");

            if (string.IsNullOrEmpty(redisConnectionString))
            {
                errors.Add("Redis connection string is required when using Redis cache. " +
                          "Configure FluxIndex:Cache:RedisConnectionString or ConnectionStrings:Redis");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Production configuration validation failed:\n" +
                string.Join("\n", errors));
        }
    }
}
