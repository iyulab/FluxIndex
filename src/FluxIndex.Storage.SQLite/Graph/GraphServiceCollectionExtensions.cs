using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Storage.SQLite.Graph;

/// <summary>
/// SQLite Graph 저장소 서비스 등록 확장 메서드
/// </summary>
public static class GraphServiceCollectionExtensions
{
    /// <summary>
    /// SQLite 그래프 저장소 등록
    /// </summary>
    public static IServiceCollection AddSQLiteGraphStore(
        this IServiceCollection services,
        Action<SQLiteGraphOptions> configureOptions)
    {
        services.Configure(configureOptions);

        // DbContext 등록
        services.AddDbContext<SQLiteGraphDbContext>((serviceProvider, dbOptions) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<SQLiteGraphOptions>>().Value;
            dbOptions.UseSqlite(options.GetGraphConnectionString(), sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(options.CommandTimeout);
            });
            dbOptions.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }, ServiceLifetime.Scoped);

        // Repository 등록
        services.AddScoped<IChunkHierarchyRepository, SQLiteGraphStore>();
        services.AddScoped<SQLiteGraphStore>();

        // 마이그레이션 — 두 경로(SDK 빌더 Build(), 앱 호스트 시작)가 같은 루틴을 공유한다.
        services.AddSingleton<SQLiteGraphSchemaInitializer>();
        services.AddHostedService<SQLiteGraphMigrationService>();

        return services;
    }

    /// <summary>
    /// SQLite 그래프 저장소 등록 (간단한 설정)
    /// </summary>
    public static IServiceCollection AddSQLiteGraphStore(
        this IServiceCollection services,
        string databasePath = "fluxindex-graph.db")
    {
        return services.AddSQLiteGraphStore(options =>
        {
            options.GraphDatabasePath = databasePath;
            options.AutoMigrate = true;
        });
    }

    /// <summary>
    /// SQLite 인메모리 그래프 저장소 등록 (테스트용)
    /// </summary>
    public static IServiceCollection AddSQLiteInMemoryGraphStore(
        this IServiceCollection services)
    {
        return services.AddSQLiteGraphStore(options =>
        {
            options.UseInMemory = true;
            options.AutoMigrate = true;
        });
    }

    /// <summary>
    /// SQLite Entity Graph Store 등록 (IGraphStore 구현)
    /// Local 모드에서 GraphRAG 기능 지원
    /// </summary>
    public static IServiceCollection AddSQLiteEntityGraphStore(
        this IServiceCollection services,
        Action<SQLiteEntityGraphOptions> configureOptions)
    {
        services.Configure(configureOptions);

        // DbContext 등록
        services.AddDbContext<SQLiteEntityGraphDbContext>((serviceProvider, dbOptions) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<SQLiteEntityGraphOptions>>().Value;
            dbOptions.UseSqlite(options.GetConnectionString(), sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(options.CommandTimeout);
            });
            dbOptions.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }, ServiceLifetime.Scoped);

        // IGraphStore 구현 등록
        services.AddScoped<IGraphStore, SQLiteEntityGraphStore>();
        services.AddScoped<SQLiteEntityGraphStore>();

        // 마이그레이션 — 두 경로(SDK 빌더 Build(), 앱 호스트 시작)가 같은 루틴을 공유한다.
        services.AddSingleton<SQLiteEntityGraphSchemaInitializer>();
        services.AddHostedService<SQLiteEntityGraphMigrationService>();

        return services;
    }

    /// <summary>
    /// SQLite Entity Graph Store 등록 (간단한 설정)
    /// </summary>
    public static IServiceCollection AddSQLiteEntityGraphStore(
        this IServiceCollection services,
        string databasePath = "fluxindex-entitygraph.db")
    {
        return services.AddSQLiteEntityGraphStore(options =>
        {
            options.DatabasePath = databasePath;
            options.AutoMigrate = true;
        });
    }

    /// <summary>
    /// SQLite 인메모리 Entity Graph Store 등록 (테스트용)
    /// </summary>
    public static IServiceCollection AddSQLiteInMemoryEntityGraphStore(
        this IServiceCollection services)
    {
        return services.AddSQLiteEntityGraphStore(options =>
        {
            options.UseInMemory = true;
            options.AutoMigrate = true;
        });
    }
}

/// <summary>
/// Provisions the SQLite entity graph schema. An <see cref="IStorageInitializer"/> so the SDK builder
/// runs it during Build(), hosted by <see cref="SQLiteEntityGraphMigrationService"/> for consumers
/// that register the store directly into an application's service collection.
/// </summary>
internal sealed partial class SQLiteEntityGraphSchemaInitializer : IStorageInitializer
{
    private readonly ILogger<SQLiteEntityGraphSchemaInitializer> _logger;

    public SQLiteEntityGraphSchemaInitializer(ILogger<SQLiteEntityGraphSchemaInitializer> logger)
    {
        _logger = logger;
    }

    public void InitializeSync(IServiceProvider serviceProvider)
    {
        LogMigrationStarting(_logger);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SQLiteEntityGraphDbContext>();

        try
        {
            SQLiteSchemaProvisioner.Provision(context);

            var options = scope.ServiceProvider.GetRequiredService<IOptions<SQLiteEntityGraphOptions>>().Value;
            if (!options.UseInMemory)
            {
                context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL");
            }

            LogMigrationCompleted(_logger);
        }
        catch (Exception ex)
        {
            LogMigrationFailed(_logger, ex);
            throw;
        }
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting SQLite entity graph database migration")]
    private static partial void LogMigrationStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SQLite entity graph database migration completed")]
    private static partial void LogMigrationCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "SQLite entity graph database migration failed")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception);

    #endregion
}

/// <summary>
/// Runs <see cref="SQLiteEntityGraphSchemaInitializer"/> at host start for consumers that register
/// the entity graph directly (the SDK builder path runs the initializer itself during Build()).
/// </summary>
internal sealed class SQLiteEntityGraphMigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SQLiteEntityGraphSchemaInitializer _initializer;

    public SQLiteEntityGraphMigrationService(
        IServiceProvider serviceProvider,
        SQLiteEntityGraphSchemaInitializer initializer)
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

/// <summary>
/// Provisions the SQLite graph schema. An <see cref="IStorageInitializer"/> so the SDK builder runs it
/// during Build(), hosted by <see cref="SQLiteGraphMigrationService"/> for consumers that register the
/// store directly into an application's service collection.
/// </summary>
internal sealed partial class SQLiteGraphSchemaInitializer : IStorageInitializer
{
    private readonly ILogger<SQLiteGraphSchemaInitializer> _logger;

    public SQLiteGraphSchemaInitializer(ILogger<SQLiteGraphSchemaInitializer> logger)
    {
        _logger = logger;
    }

    public void InitializeSync(IServiceProvider serviceProvider)
    {
        LogMigrationStarting(_logger);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SQLiteGraphDbContext>();

        try
        {
            SQLiteSchemaProvisioner.Provision(context);

            var options = scope.ServiceProvider.GetRequiredService<IOptions<SQLiteGraphOptions>>().Value;
            if (!options.UseInMemory)
            {
                ApplyGraphPragmas(context, options);
            }

            LogMigrationCompleted(_logger);
        }
        catch (Exception ex)
        {
            LogMigrationFailed(_logger, ex);
            throw;
        }
    }

    private void ApplyGraphPragmas(
        SQLiteGraphDbContext context,
        SQLiteGraphOptions options)
    {
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL");

#pragma warning disable EF1002
        context.Database.ExecuteSqlRaw(
            $"PRAGMA synchronous={options.Synchronous.ToString().ToUpperInvariant()}");
        context.Database.ExecuteSqlRaw(
            $"PRAGMA cache_size={options.CacheSize}");
#pragma warning restore EF1002

        LogPragmaApplied(_logger);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting SQLite graph database migration")]
    private static partial void LogMigrationStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SQLite graph database migration completed")]
    private static partial void LogMigrationCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "SQLite graph database migration failed")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Graph database PRAGMA optimizations applied")]
    private static partial void LogPragmaApplied(ILogger logger);

    #endregion
}

/// <summary>
/// Runs <see cref="SQLiteGraphSchemaInitializer"/> at host start for consumers that register the
/// graph store directly (the SDK builder path runs the initializer itself during Build()).
/// </summary>
internal sealed class SQLiteGraphMigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SQLiteGraphSchemaInitializer _initializer;

    public SQLiteGraphMigrationService(
        IServiceProvider serviceProvider,
        SQLiteGraphSchemaInitializer initializer)
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
