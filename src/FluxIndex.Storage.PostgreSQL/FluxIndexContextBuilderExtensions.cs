using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK;
using FluxIndex.SDK.Configuration;
using FluxIndex.Storage.PostgreSQL.Cache;
using FluxIndex.Storage.PostgreSQL.Graph;
using FluxIndex.Storage.PostgreSQL.KeywordSearch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // The keyword leg is resolved independently of the vector provider. Gating it on
        // "vectors live in PostgreSQL" made the recommended split deployment — Qdrant vectors,
        // PostgreSQL metadata — unable to register a keyword leg at all, so consumers copied this
        // registration into their own code. Provider unset means "follow the vector store", which
        // is exactly what the old gate did.
        if (options.KeywordSearch.ResolveProviderName(options.VectorStore) == "postgresql")
        {
            RegisterPostgresKeywordSearch(
                services,
                options.KeywordSearch.UseVectorStoreConnection
                    ? options.VectorStore.ConnectionString
                    : options.KeywordSearch.ConnectionString,
                // Unset falls back to the vector store's flag, which is what gated this before.
                options.KeywordSearch.EnableAutoMigration ?? options.VectorStore.EnableAutoMigration);
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

    /// <summary>
    /// Registers the PostgreSQL-backed keyword search service on the same database as the vector
    /// store. Replaces the in-memory BM25 default that the SDK registers, so the keyword leg of a
    /// hybrid search survives the process instead of silently degrading to vector-only.
    /// </summary>
    /// <remarks>
    /// Delegates to the public <see cref="ServiceCollectionExtensions.AddPostgreSQLKeywordSearch"/>
    /// so the builder path and the direct-DI path cannot drift into two different registrations of
    /// the same service (LAYERING section 4). The SDK registers its in-memory BM25 fallback with
    /// TryAdd precisely so this concrete registration wins, and the singleton lifetime is what lets
    /// the indexer and the retriever share one instance.
    ///
    /// NOTE: the service still creates its tables lazily on first use, so opting out of migration
    /// delays the DDL rather than preventing it. Same behavior as the SQLite backend; making the
    /// opt-out total is a contract change for both, tracked as a proposal.
    /// </remarks>
    private static void RegisterPostgresKeywordSearch(
        IServiceCollection services,
        string connectionString,
        bool enableAutoMigration)
        => services.AddPostgreSQLKeywordSearch(connectionString, enableAutoMigration);
}

/// <summary>
/// Creates the keyword index schema during Build() so the contract matches every other component:
/// once Build() returns, the tables exist.
/// </summary>
internal sealed class PostgresKeywordSearchInitializer(PostgresKeywordSearchService keywordSearchService)
    : IStorageInitializer
{
    public void InitializeSync(IServiceProvider serviceProvider)
    {
        keywordSearchService.EnsureSchemaAsync().GetAwaiter().GetResult();
    }
}
