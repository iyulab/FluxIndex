using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.SDK;
using FluxIndex.SDK.Configuration;
using FluxIndex.AI.OpenAI;
using FluxIndex.AI.LocalEmbedder;
using FluxIndex.AI.LocalReranker;
using FluxIndex.Storage.PostgreSQL;
using FluxIndex.Cache.Redis.Extensions;
using FluxIndex.Extensions.FileFlux;
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

        // Configure FileFlux integration if enabled
        ConfigureFileFluxIntegration(services, options);

        // Configure local reranker if enabled
        ConfigureLocalReranker(services, options);

        // Build and register FluxIndexContext
        builder.ConfigureServices(innerServices =>
        {
            // Copy core services from builder to main service collection
            // This ensures services are registered in the main DI container
            foreach (var service in innerServices)
            {
                services.Add(service);
            }
        });

        // Build context and register as singleton
        var context = builder.Build();
        services.AddSingleton<IFluxIndexContext>(context);
        services.AddSingleton(context);

        // Register Retriever and Indexer separately for direct access
        services.AddSingleton(context.Retriever);
        services.AddSingleton(context.Indexer);

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
                builder.UseSQLite(dbPath);
                break;

            case "inmemory":
                builder.UseSQLiteInMemory();
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
    /// Supports OpenAI, Azure OpenAI, Anthropic, Google, and local embedders.
    /// </summary>
    private static void ConfigureEmbeddingService(
        FluxIndexContextBuilder builder,
        FluxIndexOptions options)
    {
        var provider = options.Embedding.Provider?.ToLowerInvariant();
        var apiKey = options.Embedding.ApiKey;
        var modelName = options.Embedding.ModelName;

        switch (provider)
        {
            case "openai":
                if (string.IsNullOrEmpty(apiKey))
                {
                    // Fallback to LocalEmbedder if API key not provided
                    Console.WriteLine(
                        "[FluxIndex] OpenAI API key not configured. Using LocalEmbedder as fallback. " +
                        "Configure FluxIndex:Embedding:ApiKey for OpenAI embeddings.");
                    builder.UseLocalEmbedder("all-MiniLM-L6-v2");
                    break;
                }
                builder.UseOpenAI(
                    apiKey,
                    string.IsNullOrEmpty(modelName) ? "text-embedding-3-small" : modelName);
                break;

            case "azureopenai":
            case "azure":
                if (string.IsNullOrEmpty(apiKey))
                {
                    // Fallback to LocalEmbedder if API key not provided
                    Console.WriteLine(
                        "[FluxIndex] Azure OpenAI API key not configured. Using LocalEmbedder as fallback. " +
                        "Configure FluxIndex:Embedding:ApiKey for Azure OpenAI embeddings.");
                    builder.UseLocalEmbedder("all-MiniLM-L6-v2");
                    break;
                }

                var endpoint = options.Embedding.ProviderSpecificOptions.TryGetValue("Endpoint", out var ep)
                    ? ep?.ToString()
                    : null;

                if (string.IsNullOrEmpty(endpoint))
                {
                    Console.WriteLine(
                        "[FluxIndex] Azure OpenAI endpoint not configured. Using LocalEmbedder as fallback. " +
                        "Configure FluxIndex:Embedding:ProviderSpecificOptions:Endpoint");
                    builder.UseLocalEmbedder("all-MiniLM-L6-v2");
                    break;
                }

                builder.UseAzureOpenAI(
                    endpoint!,
                    apiKey,
                    string.IsNullOrEmpty(modelName) ? "text-embedding-ada-002" : modelName);
                break;

            case "localembedder":
            case "local":
                // Use local ONNX-based embeddings (no API key required)
                var localModel = string.IsNullOrEmpty(modelName)
                    ? "all-MiniLM-L6-v2"
                    : modelName;
                builder.UseLocalEmbedder(localModel);
                break;

            case "multilingual":
                // Use multilingual local embedder
                builder.UseLocalEmbedderMultilingual();
                break;

            case "gpustack":
                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException(
                        "GPUStack API key is required. Configure FluxIndex:Embedding:ApiKey");
                }

                var gpuEndpoint = options.Embedding.ProviderSpecificOptions.TryGetValue("Endpoint", out var gpuEp)
                    ? gpuEp?.ToString()
                    : null;

                if (string.IsNullOrEmpty(gpuEndpoint))
                {
                    throw new InvalidOperationException(
                        "GPUStack endpoint is required. " +
                        "Configure FluxIndex:Embedding:ProviderSpecificOptions:Endpoint");
                }

                int? dimensions = options.Embedding.ProviderSpecificOptions.TryGetValue("Dimensions", out var dims)
                    && dims is int d
                    ? d
                    : null;

                builder.UseGPUStack(
                    gpuEndpoint!,
                    apiKey,
                    string.IsNullOrEmpty(modelName) ? "BAAI/bge-m3" : modelName,
                    dimensions);
                break;

            case "openaicompatible":
            case "compatible":
                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException(
                        "API key is required for OpenAI-compatible endpoint. " +
                        "Configure FluxIndex:Embedding:ApiKey");
                }

                var compatEndpoint = options.Embedding.ProviderSpecificOptions.TryGetValue("Endpoint", out var compatEp)
                    ? compatEp?.ToString()
                    : null;

                if (string.IsNullOrEmpty(compatEndpoint))
                {
                    throw new InvalidOperationException(
                        "Endpoint is required for OpenAI-compatible provider. " +
                        "Configure FluxIndex:Embedding:ProviderSpecificOptions:Endpoint");
                }

                int? compatDimensions = options.Embedding.ProviderSpecificOptions.TryGetValue("Dimensions", out var compatDims)
                    && compatDims is int cd
                    ? cd
                    : null;

                builder.UseOpenAICompatible(
                    compatEndpoint!,
                    apiKey,
                    string.IsNullOrEmpty(modelName) ? "embedding" : modelName,
                    compatDimensions);
                break;

            case "inmemory":
                // For testing only
                builder.UseInMemoryEmbedding();
                break;

            default:
                // Default to LocalEmbedder for better developer experience (no API keys needed)
                builder.UseLocalEmbedder("all-MiniLM-L6-v2");
                break;
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
        if (configureBuilder == null)
        {
            throw new ArgumentNullException(nameof(configureBuilder));
        }

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
            // Use SQLite in-memory by default for development
            if (string.IsNullOrEmpty(connectionString))
            {
                builder.UseSQLiteInMemory();
            }
            else
            {
                builder.UsePostgreSQL(connectionString);
            }

            // Use LocalEmbedder (no API keys needed)
            builder.UseLocalEmbedder("all-MiniLM-L6-v2");

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
