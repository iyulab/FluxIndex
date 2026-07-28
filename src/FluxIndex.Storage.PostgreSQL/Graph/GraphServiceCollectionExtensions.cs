using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FluxIndex.Storage.PostgreSQL.Graph;

/// <summary>
/// PostgreSQL Graph 저장소 서비스 등록 확장 메서드
/// </summary>
public static class GraphServiceCollectionExtensions
{
    /// <summary>
    /// PostgreSQL 그래프 저장소 등록
    /// </summary>
    public static IServiceCollection AddPostgreSQLGraphStore(
        this IServiceCollection services,
        Action<PostgresGraphOptions> configureOptions)
    {
        services.Configure(configureOptions);

        // DbContext 등록 with dynamic JSON support
        services.AddDbContext<PostgresGraphDbContext>((serviceProvider, dbOptions) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<PostgresGraphOptions>>().Value;

            // NpgsqlDataSource for dynamic JSONB support
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(options.ConnectionString);
            dataSourceBuilder.EnableDynamicJson();
            var dataSource = dataSourceBuilder.Build();

            dbOptions.UseNpgsql(dataSource, npgsqlOptions =>
            {
                npgsqlOptions.CommandTimeout(options.CommandTimeout);
            });
            dbOptions.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }, ServiceLifetime.Scoped);

        // Repository 등록
        services.AddScoped<IChunkHierarchyRepository, PostgresGraphStore>();
        services.AddScoped<PostgresGraphStore>();

        // 마이그레이션 — 두 경로(SDK 빌더 Build(), 앱 호스트 시작)가 같은 루틴을 공유한다.
        services.AddSingleton<PostgresGraphSchemaInitializer>();
        services.AddHostedService<PostgresGraphMigrationService>();

        return services;
    }

    /// <summary>
    /// PostgreSQL 그래프 저장소 등록 (연결 문자열)
    /// </summary>
    public static IServiceCollection AddPostgreSQLGraphStore(
        this IServiceCollection services,
        string connectionString)
    {
        return services.AddPostgreSQLGraphStore(options =>
        {
            options.ConnectionString = connectionString;
            options.AutoMigrate = true;
        });
    }
}

/// <summary>
/// Provisions the PostgreSQL graph schema. Implemented as an <see cref="IStorageInitializer"/> so the
/// SDK builder runs it during Build(), and hosted by <see cref="PostgresGraphMigrationService"/> for
/// consumers that register the graph store directly into an application's service collection.
/// </summary>
/// <remarks>
/// The migration used to live only in the hosted service, which the SDK builder never starts — it
/// builds its own service provider and runs storage initializers, so a builder-configured graph store
/// was never provisioned at all and the first graph write failed with 42P01.
/// </remarks>
internal sealed partial class PostgresGraphSchemaInitializer : IStorageInitializer
{
    private readonly ILogger<PostgresGraphSchemaInitializer> _logger;

    public PostgresGraphSchemaInitializer(ILogger<PostgresGraphSchemaInitializer> logger)
    {
        _logger = logger;
    }

    public void InitializeSync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<PostgresGraphOptions>>().Value;

        if (!options.AutoMigrate)
        {
            LogGraphAutoMigrationDisabled(_logger);
            return;
        }

        LogStartingGraphMigration(_logger);

        var context = scope.ServiceProvider.GetRequiredService<PostgresGraphDbContext>();

        try
        {
            RelationalSchemaProvisioner.Provision(context);

            if (options.UseJsonbIndex)
            {
                CreateGinIndexes(context);
            }

            LogGraphMigrationCompleted(_logger);
        }
        catch (Exception ex)
        {
            LogGraphMigrationFailed(_logger, ex);
            throw;
        }
    }

    private void CreateGinIndexes(PostgresGraphDbContext context)
    {
        try
        {
            context.Database.ExecuteSqlRaw(@"
                CREATE INDEX IF NOT EXISTS idx_chunk_hierarchies_child_ids_gin
                ON chunk_hierarchies USING gin (""ChildChunkIds"");

                CREATE INDEX IF NOT EXISTS idx_chunk_relationships_metadata_gin
                ON chunk_relationships USING gin (""Metadata"");
            ");

            LogGinIndexesCreated(_logger);
        }
        catch (Exception ex)
        {
            LogGinIndexesFailed(_logger, ex);
        }
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgreSQL graph auto-migration is disabled")]
    private static partial void LogGraphAutoMigrationDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting PostgreSQL graph database migration")]
    private static partial void LogStartingGraphMigration(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgreSQL graph database migration completed")]
    private static partial void LogGraphMigrationCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "PostgreSQL graph database migration failed")]
    private static partial void LogGraphMigrationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "GIN indexes created for JSONB columns")]
    private static partial void LogGinIndexesCreated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create GIN indexes, continuing without them")]
    private static partial void LogGinIndexesFailed(ILogger logger, Exception exception);

    #endregion
}

/// <summary>
/// Runs <see cref="PostgresGraphSchemaInitializer"/> at host start for consumers that register the
/// graph store directly (the SDK builder path runs the initializer itself during Build()).
/// </summary>
internal sealed class PostgresGraphMigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PostgresGraphSchemaInitializer _initializer;

    public PostgresGraphMigrationService(
        IServiceProvider serviceProvider,
        PostgresGraphSchemaInitializer initializer)
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
