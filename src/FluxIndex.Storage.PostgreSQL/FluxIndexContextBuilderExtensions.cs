using FluxIndex.SDK;
using FluxIndex.SDK.Configuration;
using FluxIndex.Storage.PostgreSQL.Cache;
using FluxIndex.Storage.PostgreSQL.Graph;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// Extension methods for FluxIndexContextBuilder to add PostgreSQL storage support.
/// Consumers must reference FluxIndex.Storage.PostgreSQL and call these methods
/// after setting options (e.g., builder.UsePostgreSQL(conn).AddPostgreSQLStorage()).
/// </summary>
public static class FluxIndexContextBuilderExtensions
{
    /// <summary>
    /// Register PostgreSQL storage services for all configured components (Vector, Graph, SemanticCache).
    /// Call this after UsePostgreSQL() or UseBestInClass().
    /// This method reads the options already set on the builder.
    /// </summary>
    public static FluxIndexContextBuilder AddPostgreSQLStorage(this FluxIndexContextBuilder builder)
    {
        builder.RegisterStorageServices(services =>
        {
            var options = builder.Options;
            var vectorProvider = options.VectorStore.Provider?.ToLowerInvariant();

            if (vectorProvider == "postgresql")
            {
                services.AddPostgreSQLVectorStore(options.VectorStore.ConnectionString);
            }

            var graphProvider = options.GraphStore.Provider?.ToLowerInvariant();
            if (graphProvider == "postgresql")
            {
                var connectionString = options.GraphStore.UseVectorStoreConnection
                    ? options.VectorStore.ConnectionString
                    : options.GraphStore.ConnectionString;

                services.AddPostgreSQLGraphStore(graphOptions =>
                {
                    graphOptions.ConnectionString = connectionString;
                    graphOptions.AutoMigrate = options.GraphStore.AutoMigrate;
                    graphOptions.MaxRecursionDepth = options.GraphStore.MaxRecursionDepth;
                });
            }

            var cacheProvider = options.SemanticCache.Provider?.ToLowerInvariant();
            if (cacheProvider == "postgresql")
            {
                var connectionString = options.SemanticCache.UseVectorStoreConnection
                    ? options.VectorStore.ConnectionString
                    : options.SemanticCache.ConnectionString;

                services.AddPostgreSQLSemanticCache(cacheOptions =>
                {
                    cacheOptions.ConnectionString = connectionString;
                    cacheOptions.AutoMigrate = options.SemanticCache.AutoMigrate;
                    cacheOptions.DefaultExpiry = options.SemanticCache.DefaultExpiry;
                    cacheOptions.MaxEntries = options.SemanticCache.MaxEntries;
                    cacheOptions.EmbeddingDimensions = options.SemanticCache.EmbeddingDimensions;
                    cacheOptions.EnableAutoCleanup = options.SemanticCache.EnableAutoCleanup;
                    cacheOptions.CleanupInterval = options.SemanticCache.CleanupInterval;
                    cacheOptions.UseUnloggedTable = options.SemanticCache.UseUnloggedTable;
                });
            }
        });

        return builder;
    }
}
