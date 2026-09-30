using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK;
using FluxIndex.Core.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FluxIndex.Storage.PostgreSQL.Cache;

/// <summary>
/// PostgreSQL Cache 저장소 서비스 등록 확장 메서드
/// </summary>
public static class CacheServiceCollectionExtensions
{
    /// <summary>
    /// PostgreSQL 시맨틱 캐시 등록
    /// </summary>
    public static IServiceCollection AddPostgreSQLSemanticCache(
        this IServiceCollection services,
        Action<PostgresCacheOptions> configureOptions)
    {
        services.Configure(configureOptions);

        // DbContext factory with dynamic JSON and pgvector support. The cache opens a context per operation, so one store instance is safe for concurrent callers. The context type itself
        // stays resolvable (scoped). The options
        // are built once, so the data source is shared rather than rebuilt per scope.
        services.AddDbContextFactory<PostgresCacheDbContext>((serviceProvider, dbOptions) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<PostgresCacheOptions>>().Value;

            // NpgsqlDataSource for dynamic JSONB support
            // UseVector() must be on the data source too: the EF-level npgsqlOptions.UseVector() below only adds the
            // type mappings, and writing the cached query embedding then fails with InvalidCastException.
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(options.ConnectionString);
            dataSourceBuilder.EnableDynamicJson();
            dataSourceBuilder.UseVector();
            var dataSource = dataSourceBuilder.Build();

            dbOptions.UseNpgsql(dataSource, npgsqlOptions =>
            {
                npgsqlOptions.UseVector();
                npgsqlOptions.CommandTimeout(options.CommandTimeout);
            });
            dbOptions.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        // Cache 서비스 등록
        services.AddScoped<ISemanticCache, PostgresSemanticCache>();
        services.AddScoped<PostgresSemanticCache>();

        // 마이그레이션 — 두 경로(SDK 빌더 Build(), 앱 호스트 시작)가 같은 루틴을 공유한다.
        services.AddSingleton<PostgresCacheSchemaInitializer>();
        services.AddHostedService<PostgresCacheMigrationService>();

        return services;
    }

    /// <summary>
    /// PostgreSQL 시맨틱 캐시 등록 (연결 문자열)
    /// </summary>
    public static IServiceCollection AddPostgreSQLSemanticCache(
        this IServiceCollection services,
        string connectionString,
        int embeddingDimensions = EmbeddingDefaults.DefaultVectorDimension)
    {
        return services.AddPostgreSQLSemanticCache(options =>
        {
            options.ConnectionString = connectionString;
            options.EmbeddingDimensions = embeddingDimensions;
            options.AutoMigrate = true;
        });
    }
}

/// <summary>
/// Provisions the PostgreSQL semantic cache schema. Implemented as an <see cref="IStorageInitializer"/>
/// so the SDK builder runs it during Build(), and hosted by <see cref="PostgresCacheMigrationService"/>
/// for consumers that register the cache directly into an application's service collection.
/// </summary>
/// <remarks>
/// The migration used to live only in the hosted service, which the SDK builder never starts — it
/// builds its own service provider and runs storage initializers, so a builder-configured semantic
/// cache was never provisioned at all and the first cache write failed with 42P01.
/// </remarks>
internal sealed partial class PostgresCacheSchemaInitializer : IStorageInitializer
{
    private readonly ILogger<PostgresCacheSchemaInitializer> _logger;

    public PostgresCacheSchemaInitializer(ILogger<PostgresCacheSchemaInitializer> logger)
    {
        _logger = logger;
    }

    public void InitializeSync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<PostgresCacheOptions>>().Value;

        if (!options.AutoMigrate)
        {
            LogAutoMigrationDisabled(_logger);
            return;
        }

        LogStartingMigration(_logger);

        var context = scope.ServiceProvider.GetRequiredService<PostgresCacheDbContext>();

        try
        {
            RelationalSchemaProvisioner.EnsureDatabase(context);

            // pgvector 확장 활성화
            if (options.UsePgVector)
            {
                EnablePgVectorExtension(context);
            }

            // 테이블 생성 (UNLOGGED 옵션 적용)
            if (options.UseUnloggedTable)
            {
                CreateUnloggedTables(context, options);
            }
            else
            {
                RelationalSchemaProvisioner.ProvisionTables(context);
            }

            // 인덱스 생성
            CreateIndexes(context, options);

            LogMigrationCompleted(_logger);
        }
        catch (Exception ex)
        {
            LogMigrationFailed(_logger, ex);
            throw;
        }
    }

    private void EnablePgVectorExtension(PostgresCacheDbContext context)
    {
        try
        {
            RelationalSchemaProvisioner.EnsureVectorExtension(context);
            LogPgVectorEnabled(_logger);
        }
        catch (Exception ex)
        {
            LogPgVectorExtensionFailed(_logger, ex);
        }
    }

    private void CreateUnloggedTables(
        PostgresCacheDbContext context,
        PostgresCacheOptions options)
    {
        // UNLOGGED 테이블은 WAL 로깅을 하지 않아 쓰기 성능이 좋지만 크래시 시 데이터 손실 가능
        // 캐시에 적합한 트레이드오프
        var createTableSql = $@"
            CREATE UNLOGGED TABLE IF NOT EXISTS semantic_cache (
                ""Id"" VARCHAR(100) PRIMARY KEY,
                ""QueryHash"" VARCHAR(64) NOT NULL,
                ""Query"" TEXT NOT NULL,
                ""Embedding"" vector({options.EmbeddingDimensions}),
                ""Results"" JSONB,
                ""Metadata"" JSONB,
                ""CreatedAt"" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                ""ExpiresAt"" TIMESTAMPTZ NOT NULL,
                ""HitCount"" INTEGER NOT NULL DEFAULT 0,
                ""LastAccessedAt"" TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS cache_stats (
                ""Id"" INTEGER PRIMARY KEY,
                ""TotalHits"" BIGINT NOT NULL DEFAULT 0,
                ""TotalMisses"" BIGINT NOT NULL DEFAULT 0,
                ""TotalEvictions"" BIGINT NOT NULL DEFAULT 0,
                ""TotalEntries"" BIGINT NOT NULL DEFAULT 0,
                ""LastUpdated"" TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            INSERT INTO cache_stats (""Id"") VALUES (1) ON CONFLICT DO NOTHING;
        ";

        context.Database.ExecuteSqlRaw(createTableSql);

        // Tables created before these columns were TIMESTAMPTZ hold TIMESTAMP (without time zone), which reads back as
        // DateTime.Kind Unspecified — and Npgsql refuses to write that to the timestamptz the model maps, so every cache
        // hit failed when it saved its hit count. The stored values were written as UTC, so convert them as UTC.
        context.Database.ExecuteSqlRaw(@"
            DO $$
            DECLARE col record;
            BEGIN
                FOR col IN
                    SELECT table_name, column_name FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name IN ('semantic_cache', 'cache_stats')
                      AND column_name IN ('CreatedAt', 'ExpiresAt', 'LastAccessedAt', 'LastUpdated')
                      AND data_type = 'timestamp without time zone'
                LOOP
                    EXECUTE format('ALTER TABLE %I ALTER COLUMN %I TYPE timestamptz USING %I AT TIME ZONE ''UTC''',
                                   col.table_name, col.column_name, col.column_name);
                END LOOP;
            END $$;");
        LogUnloggedTablesCreated(_logger);
    }

    private void CreateIndexes(
        PostgresCacheDbContext context,
        PostgresCacheOptions options)
    {
        try
        {
            var indexSql = $@"
                CREATE INDEX IF NOT EXISTS idx_semantic_cache_query_hash
                ON semantic_cache (""QueryHash"");

                CREATE INDEX IF NOT EXISTS idx_semantic_cache_expires
                ON semantic_cache (""ExpiresAt"");

                CREATE INDEX IF NOT EXISTS idx_semantic_cache_last_accessed
                ON semantic_cache (""LastAccessedAt"");

                CREATE INDEX IF NOT EXISTS idx_semantic_cache_hit_count
                ON semantic_cache (""HitCount"" DESC);
            ";

            context.Database.ExecuteSqlRaw(indexSql);

            // pgvector HNSW 인덱스 (근사 최근접 이웃 검색)
            if (options.UsePgVector)
            {
                try
                {
                    context.Database.ExecuteSqlRaw(@"
                        CREATE INDEX IF NOT EXISTS idx_semantic_cache_embedding_hnsw
                        ON semantic_cache USING hnsw (""Embedding"" vector_cosine_ops)
                        WITH (m = 16, ef_construction = 64);
                    ");
                    LogHnswIndexCreated(_logger);
                }
                catch (Exception ex)
                {
                    LogHnswIndexFailed(_logger, ex);
                }
            }

            LogCacheIndexesCreated(_logger);
        }
        catch (Exception ex)
        {
            LogIndexCreationFailed(_logger, ex);
        }
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgreSQL cache auto-migration is disabled")]
    private static partial void LogAutoMigrationDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting PostgreSQL cache database migration")]
    private static partial void LogStartingMigration(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgreSQL cache database migration completed")]
    private static partial void LogMigrationCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "PostgreSQL cache database migration failed")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "pgvector extension enabled")]
    private static partial void LogPgVectorEnabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create vector extension, it may already exist or require superuser")]
    private static partial void LogPgVectorExtensionFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "UNLOGGED tables created for semantic cache")]
    private static partial void LogUnloggedTablesCreated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "HNSW index created for vector similarity search")]
    private static partial void LogHnswIndexCreated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create HNSW index, falling back to sequential scan")]
    private static partial void LogHnswIndexFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache indexes created")]
    private static partial void LogCacheIndexesCreated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create some indexes, continuing")]
    private static partial void LogIndexCreationFailed(ILogger logger, Exception exception);

    #endregion
}

/// <summary>
/// Runs <see cref="PostgresCacheSchemaInitializer"/> at host start for consumers that register the
/// semantic cache directly (the SDK builder path runs the initializer itself during Build()).
/// </summary>
internal sealed class PostgresCacheMigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PostgresCacheSchemaInitializer _initializer;

    public PostgresCacheMigrationService(
        IServiceProvider serviceProvider,
        PostgresCacheSchemaInitializer initializer)
    {
        _serviceProvider = serviceProvider;
        _initializer = initializer;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _initializer.InitializeSync(_serviceProvider);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
