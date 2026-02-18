using FluxIndex.Core.Application.Interfaces;
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

        // 마이그레이션 서비스
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

        // 마이그레이션 서비스
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
/// SQLite Entity Graph 마이그레이션 서비스
/// </summary>
internal sealed partial class SQLiteEntityGraphMigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SQLiteEntityGraphMigrationService> _logger;

    public SQLiteEntityGraphMigrationService(
        IServiceProvider serviceProvider,
        ILogger<SQLiteEntityGraphMigrationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LogMigrationStarting(_logger);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SQLiteEntityGraphDbContext>();

        try
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);

            var options = scope.ServiceProvider.GetRequiredService<IOptions<SQLiteEntityGraphOptions>>().Value;
            if (!options.UseInMemory)
            {
                await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL", cancellationToken);
            }

            LogMigrationCompleted(_logger);
        }
        catch (Exception ex)
        {
            LogMigrationFailed(_logger, ex);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
/// SQLite Graph 마이그레이션 서비스
/// </summary>
internal sealed partial class SQLiteGraphMigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SQLiteGraphMigrationService> _logger;

    public SQLiteGraphMigrationService(
        IServiceProvider serviceProvider,
        ILogger<SQLiteGraphMigrationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LogMigrationStarting(_logger);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SQLiteGraphDbContext>();

        try
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);

            var options = scope.ServiceProvider.GetRequiredService<IOptions<SQLiteGraphOptions>>().Value;
            if (!options.UseInMemory)
            {
                await ApplyGraphPragmasAsync(context, options, cancellationToken);
            }

            LogMigrationCompleted(_logger);
        }
        catch (Exception ex)
        {
            LogMigrationFailed(_logger, ex);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ApplyGraphPragmasAsync(
        SQLiteGraphDbContext context,
        SQLiteGraphOptions options,
        CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL", cancellationToken);

#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync(
            $"PRAGMA synchronous={options.Synchronous.ToString().ToUpperInvariant()}", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            $"PRAGMA cache_size={options.CacheSize}", cancellationToken);
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
