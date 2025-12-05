using FluxIndex.Service.Application.Interfaces.Repositories;
using FluxIndex.Service.Application.Interfaces.Services;
using FluxIndex.Service.Application.Services;
using FluxIndex.Service.Infrastructure.Data;
using FluxIndex.Service.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Service.Infrastructure.Extensions;

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

        services.AddDbContext<ServiceDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.UseVector();
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
        services.AddScoped<ISearchHistoryRepository, SearchHistoryRepository>();

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
        // IDocumentService, ISearchService, IIndexingService will be added with FluxIndex integration

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
