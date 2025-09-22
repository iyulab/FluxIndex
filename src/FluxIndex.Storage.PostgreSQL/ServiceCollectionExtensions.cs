using FluxIndex.Core.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// Extension methods for registering PostgreSQL vector store with dependency injection
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds PostgreSQL vector store to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddPostgreSQLVectorStore(
        this IServiceCollection services,
        Action<PostgreSQLOptions> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);

        // Register DbContext
        services.AddDbContext<FluxIndexDbContext>((serviceProvider, options) =>
        {
            var postgresOptions = serviceProvider.GetRequiredService<IOptions<PostgreSQLOptions>>().Value;
            options.UseNpgsql(postgresOptions.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.UseVector();
                npgsqlOptions.CommandTimeout(postgresOptions.CommandTimeout);
            });
        });

        // Register vector store
        services.AddScoped<IVectorStore, PostgreSQLVectorStore>();

        return services;
    }

    /// <summary>
    /// Adds PostgreSQL vector store with connection string
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="connectionString">PostgreSQL connection string</param>
    /// <param name="embeddingDimensions">Embedding vector dimensions (default: 1536)</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddPostgreSQLVectorStore(
        this IServiceCollection services,
        string connectionString,
        int embeddingDimensions = 1536)
    {
        return services.AddPostgreSQLVectorStore(options =>
        {
            options.ConnectionString = connectionString;
            options.EmbeddingDimensions = embeddingDimensions;
        });
    }
}