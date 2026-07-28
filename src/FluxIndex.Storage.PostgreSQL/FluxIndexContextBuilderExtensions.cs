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
    /// <remarks>
    /// Unless <see cref="FluxIndex.SDK.Configuration.VectorStoreOptions.EnableAutoMigration"/> is set to
    /// false, Build() provisions the pgvector extension and the relations this store owns. Only the
    /// owned relations are inspected and created, so the target database may be shared with the
    /// consumer's own application tables. If some — but not all — FluxIndex relations already exist,
    /// Build() throws rather than half-repairing the schema.
    /// </remarks>
    public static FluxIndexContextBuilder AddPostgreSQLStorage(this FluxIndexContextBuilder builder)
    {
        builder.RegisterStorageServices(services => RegisterPostgreSQLServices(services, builder.Options));
        return builder;
    }

    internal static void RegisterPostgreSQLServices(IServiceCollection services, FluxIndexOptions options)
    {
        var vectorProvider = options.VectorStore.Provider?.ToLowerInvariant();

        if (vectorProvider == "postgresql")
        {
            services.AddPostgreSQLVectorStore(options.VectorStore.ConnectionString);

            // Symmetric with SQLite (which always auto-initializes on Build): register a schema
            // initializer so Build() creates the pgvector extension + tables. Gated by
            // EnableAutoMigration (default true) so callers that manage schema externally — or run
            // on managed PostgreSQL without CREATE EXTENSION privilege — can opt out.
            if (options.VectorStore.EnableAutoMigration)
            {
                services.AddSingleton<IStorageInitializer, PostgreSQLStorageInitializer>();
            }
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

            // AddPostgreSQLGraphStore migrates from a hosted service, which Build() never starts —
            // it builds its own provider and runs IStorageInitializer only. Register the same
            // routine as an initializer so the builder path provisions the graph schema too.
            services.AddSingleton<IStorageInitializer>(sp =>
                sp.GetRequiredService<Graph.PostgresGraphSchemaInitializer>());
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

            // Same reason as the graph store above: the cache migrates from a hosted service that
            // the builder path never starts.
            services.AddSingleton<IStorageInitializer>(sp =>
                sp.GetRequiredService<Cache.PostgresCacheSchemaInitializer>());
        }
    }
}
