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
    /// Adds Redis caching support.
    /// </summary>
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redisConnection))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = "FluxIndex:";
            });
        }

        return services;
    }
}
