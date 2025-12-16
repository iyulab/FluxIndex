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
}

/// <summary>
/// SQLite Graph 마이그레이션 서비스
/// </summary>
internal class SQLiteGraphMigrationService : IHostedService
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
        _logger.LogInformation("Starting SQLite graph database migration");

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

            _logger.LogInformation("SQLite graph database migration completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQLite graph database migration failed");
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
            $"PRAGMA synchronous={options.Synchronous.ToString().ToUpper()}", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            $"PRAGMA cache_size={options.CacheSize}", cancellationToken);
#pragma warning restore EF1002

        _logger.LogDebug("Graph database PRAGMA optimizations applied");
    }
}
