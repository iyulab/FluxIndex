using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK;
using FluxIndex.SDK.Configuration;
using FluxIndex.Storage.SQLite.Cache;
using FluxIndex.Storage.SQLite.Graph;
using Microsoft.EntityFrameworkCore;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// Extension methods for FluxIndexContextBuilder to add SQLite storage support.
/// Consumers must reference FluxIndex.Storage.SQLite and call these methods
/// after setting options (e.g., builder.UseSQLite("data.db").AddSQLiteStorage()).
/// </summary>
public static class FluxIndexContextBuilderExtensions
{
    /// <summary>
    /// Register SQLite storage services for all configured components (Vector, Graph, SemanticCache).
    /// Call this after UseSQLite(), UseSQLiteInMemory(), or UseLocalStorage().
    /// This method reads the options already set on the builder.
    /// </summary>
    public static FluxIndexContextBuilder AddSQLiteStorage(this FluxIndexContextBuilder builder)
    {
        builder.RegisterStorageServices(services =>
        {
            var options = builder.Options;
            var vectorProvider = options.VectorStore.Provider?.ToLowerInvariant();

            if (vectorProvider == "sqlite")
            {
                RegisterSQLiteVectorStore(services, options);
                RegisterSQLiteInitializer(services);

                // The keyword (sparse) leg. Without a persistent backend the leg only ever holds what
                // this process indexed, so hybrid search degrades to vector-only after a restart.
                RegisterSQLiteKeywordSearch(services, options);
            }

            var graphProvider = options.GraphStore.Provider?.ToLowerInvariant();
            if (graphProvider == "sqlite")
            {
                RegisterSQLiteGraphStore(services, options);

                // The graph stores migrate from hosted services, which Build() never starts — it
                // builds its own provider and runs IStorageInitializer only. Register the same
                // routines as initializers so the builder path provisions them too.
                services.AddSingleton<IStorageInitializer>(sp =>
                    sp.GetRequiredService<SQLiteGraphSchemaInitializer>());
                services.AddSingleton<IStorageInitializer>(sp =>
                    sp.GetRequiredService<SQLiteEntityGraphSchemaInitializer>());
            }

            var cacheProvider = options.SemanticCache.Provider?.ToLowerInvariant();
            if (cacheProvider == "sqlite")
            {
                RegisterSQLiteSemanticCache(services, options);

                // Same reason as the graph stores above.
                services.AddSingleton<IStorageInitializer>(sp =>
                    sp.GetRequiredService<SQLiteCacheSchemaInitializer>());
            }
        });

        return builder;
    }

    private static void RegisterSQLiteVectorStore(IServiceCollection services, FluxIndexOptions options)
    {
        services.AddSQLiteVectorStore(sqliteOptions =>
        {
            var connStr = options.VectorStore.ConnectionString;
            var isInMemory = connStr.Contains(":memory:");

            if (isInMemory)
            {
                sqliteOptions.UseInMemory = true;
                sqliteOptions.DatabasePath = connStr;
            }
            else
            {
                var path = ParseDatabasePath(connStr);
                sqliteOptions.DatabasePath = path;
                sqliteOptions.UseInMemory = false;
            }

            sqliteOptions.AutoMigrate = true;
        });
    }

    private static void RegisterSQLiteGraphStore(IServiceCollection services, FluxIndexOptions options)
    {
        var connectionString = options.GraphStore.UseVectorStoreConnection
            ? options.VectorStore.ConnectionString
            : options.GraphStore.ConnectionString;

        var connStr = connectionString;
        var isInMemory = connStr.Contains(":memory:");
        string dbPath;

        if (isInMemory)
        {
            dbPath = connStr;
        }
        else
        {
            dbPath = ParseDatabasePath(connStr);
        }

        // IChunkHierarchyRepository for Small-to-Big retrieval
        services.AddSQLiteGraphStore(graphOptions =>
        {
            if (isInMemory)
            {
                graphOptions.UseInMemory = true;
            }
            else
            {
                graphOptions.GraphDatabasePath = dbPath;
                graphOptions.UseInMemory = false;
            }
            graphOptions.AutoMigrate = options.GraphStore.AutoMigrate;
        });

        // IGraphStore for GraphRAG entity graph
        services.AddSQLiteEntityGraphStore(entityOptions =>
        {
            if (isInMemory)
            {
                entityOptions.UseInMemory = true;
            }
            else
            {
                var entityGraphDbPath = System.IO.Path.ChangeExtension(dbPath, null) + "-entitygraph.db";
                entityOptions.DatabasePath = entityGraphDbPath;
                entityOptions.UseInMemory = false;
            }
            entityOptions.AutoMigrate = options.GraphStore.AutoMigrate;
        });
    }

    private static void RegisterSQLiteSemanticCache(IServiceCollection services, FluxIndexOptions options)
    {
        var connectionString = options.SemanticCache.UseVectorStoreConnection
            ? options.VectorStore.ConnectionString
            : options.SemanticCache.ConnectionString;

        services.AddSQLiteSemanticCache(cacheOptions =>
        {
            var connStr = connectionString;
            var isInMemory = connStr.Contains(":memory:");

            if (isInMemory)
            {
                cacheOptions.UseInMemory = true;
                cacheOptions.DatabasePath = connStr;
            }
            else
            {
                var path = ParseDatabasePath(connStr);
                cacheOptions.DatabasePath = path;
                cacheOptions.UseInMemory = false;
            }

            cacheOptions.AutoMigrate = options.SemanticCache.AutoMigrate;
            cacheOptions.DefaultExpiry = options.SemanticCache.DefaultExpiry;
            cacheOptions.MaxEntries = options.SemanticCache.MaxEntries;
            cacheOptions.EnableAutoCleanup = options.SemanticCache.EnableAutoCleanup;
            cacheOptions.CleanupInterval = options.SemanticCache.CleanupInterval;
        });
    }

    /// <summary>
    /// Registers the SQLite-backed keyword search service on the same database as the vector store.
    /// Replaces the in-memory BM25 default that the SDK registers, so the index survives the process.
    /// </summary>
    private static void RegisterSQLiteKeywordSearch(IServiceCollection services, FluxIndexOptions options)
    {
        var connectionString = options.VectorStore.ConnectionString;

        // The SDK registers its in-memory BM25 fallback with TryAdd precisely so this concrete
        // registration wins. Singleton so the indexer and the retriever share one instance.
        services.AddSingleton<SQLiteKeywordSearchService>(sp => new SQLiteKeywordSearchService(
            connectionString,
            sp.GetRequiredService<ILogger<SQLiteKeywordSearchService>>()));
        services.AddSingleton<IKeywordSearchService>(sp =>
            sp.GetRequiredService<SQLiteKeywordSearchService>());

        // Build() runs IStorageInitializer only; the service also creates its tables lazily, but
        // provisioning here keeps the contract uniform with every other component: after Build(),
        // the schema exists.
        services.AddSingleton<IStorageInitializer>(sp =>
            new SQLiteKeywordSearchInitializer(sp.GetRequiredService<SQLiteKeywordSearchService>()));
    }

    private static void RegisterSQLiteInitializer(IServiceCollection services)
    {
        services.AddSingleton<IStorageInitializer, SQLiteStorageInitializer>();
    }

    private static string ParseDatabasePath(string connStr)
    {
        var dataSourcePrefix = "Data Source=";
        var startIndex = connStr.IndexOf(dataSourcePrefix, StringComparison.OrdinalIgnoreCase);
        if (startIndex >= 0)
        {
            var path = connStr.Substring(startIndex + dataSourcePrefix.Length).Trim();
            var semicolonIndex = path.IndexOf(';');
            if (semicolonIndex >= 0)
            {
                path = path.Substring(0, semicolonIndex).Trim();
            }
            return path;
        }
        return connStr;
    }
}

/// <summary>
/// SQLite storage initializer. Ensures database is created and configured on Build().
/// </summary>
internal class SQLiteStorageInitializer : IStorageInitializer
{
    public void InitializeSync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<SQLiteDbContext>();

        // Per owned table, not EnsureCreated: the vector store, graph store and semantic cache share
        // one database file, and consumers may point us at a database that already holds their own
        // tables — EnsureCreated stops as soon as any relation exists and would create nothing.
        // The counterpart hosted service (SQLiteMigrationService) never runs on the builder path,
        // so this is the only thing provisioning the vector schema here.
        SQLiteSchemaProvisioner.Provision(context);

        // 추가 초기화 (필요시)
        var options = scope.ServiceProvider.GetService<Microsoft.Extensions.Options.IOptions<SQLiteOptions>>();

        if (options?.Value != null && !options.Value.UseInMemory)
        {
            // WAL 모드 활성화 (성능 향상)
            RelationalDatabaseFacadeExtensions.ExecuteSqlRaw(context.Database, "PRAGMA journal_mode=WAL");

            // 동기화 모드 설정 (성능과 안정성 균형)
            RelationalDatabaseFacadeExtensions.ExecuteSqlRaw(context.Database, "PRAGMA synchronous=NORMAL");
        }
    }
}

/// <summary>
/// Creates the keyword index schema during Build() so the contract matches every other component:
/// once Build() returns, the tables exist.
/// </summary>
internal sealed class SQLiteKeywordSearchInitializer(SQLiteKeywordSearchService keywordSearchService)
    : IStorageInitializer
{
    public void InitializeSync(IServiceProvider serviceProvider)
    {
        keywordSearchService.EnsureSchemaAsync().GetAwaiter().GetResult();
    }
}
