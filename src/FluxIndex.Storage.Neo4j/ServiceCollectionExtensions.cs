using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Storage.Neo4j;

/// <summary>
/// Extension methods for registering Neo4j graph store services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Neo4j graph store services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure Neo4j options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNeo4jGraphStore(
        this IServiceCollection services,
        Action<Neo4jOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddSingleton<IGraphStore, Neo4jGraphStore>();

        return services;
    }

    /// <summary>
    /// Adds Neo4j graph store services with connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="uri">Neo4j Bolt URI.</param>
    /// <param name="username">Neo4j username.</param>
    /// <param name="password">Neo4j password.</param>
    /// <param name="database">Optional database name.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNeo4jGraphStore(
        this IServiceCollection services,
        string uri,
        string username,
        string password,
        string? database = null)
    {
        return services.AddNeo4jGraphStore(options =>
        {
            options.Uri = uri;
            options.Username = username;
            options.Password = password;
            options.Database = database;
        });
    }

    /// <summary>
    /// Adds Neo4j as a specialized graph provider.
    /// Registers IStorageProvider for auto-detection by StorageOrchestrator.
    /// Neo4j takes priority over general-purpose providers for graph operations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNeo4jProvider(this IServiceCollection services)
    {
        services.AddSingleton<IStorageProvider>(sp =>
        {
            var graphStore = sp.GetRequiredService<Neo4jGraphStore>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<Neo4jProvider>>();
            return new Neo4jProvider(graphStore, logger);
        });

        return services;
    }
}
