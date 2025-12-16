using FluxIndex.Core.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Storage.SQLite.Cache;

/// <summary>
/// SQLite Cache 저장소 서비스 등록 확장 메서드
/// </summary>
public static class CacheServiceCollectionExtensions
{
    /// <summary>
    /// SQLite 시맨틱 캐시 등록
    /// </summary>
    public static IServiceCollection AddSQLiteSemanticCache(
        this IServiceCollection services,
        Action<SQLiteCacheOptions> configureOptions)
    {
        services.Configure(configureOptions);

        // DbContext 등록
        services.AddDbContext<SQLiteCacheDbContext>((serviceProvider, dbOptions) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<SQLiteCacheOptions>>().Value;
            dbOptions.UseSqlite(options.GetCacheConnectionString(), sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(options.CommandTimeout);
            });
            dbOptions.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }, ServiceLifetime.Scoped);

        // Cache 서비스 등록
        services.AddScoped<ISemanticCache, SQLiteSemanticCache>();
        services.AddScoped<SQLiteSemanticCache>();

        // 마이그레이션 서비스
        services.AddHostedService<SQLiteCacheMigrationService>();

        return services;
    }

    /// <summary>
    /// SQLite 시맨틱 캐시 등록 (간단한 설정)
    /// </summary>
    public static IServiceCollection AddSQLiteSemanticCache(
        this IServiceCollection services,
        string databasePath = "fluxindex-cache.db")
    {
        return services.AddSQLiteSemanticCache(options =>
        {
            options.CacheDatabasePath = databasePath;
            options.AutoMigrate = true;
        });
    }

    /// <summary>
    /// SQLite 인메모리 시맨틱 캐시 등록 (테스트용)
    /// </summary>
    public static IServiceCollection AddSQLiteInMemorySemanticCache(
        this IServiceCollection services)
    {
        return services.AddSQLiteSemanticCache(options =>
        {
            options.UseInMemory = true;
            options.AutoMigrate = true;
        });
    }
}

/// <summary>
/// SQLite Cache 마이그레이션 서비스
/// </summary>
internal class SQLiteCacheMigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SQLiteCacheMigrationService> _logger;

    public SQLiteCacheMigrationService(
        IServiceProvider serviceProvider,
        ILogger<SQLiteCacheMigrationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting SQLite cache database migration");

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SQLiteCacheDbContext>();

        try
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);

            var options = scope.ServiceProvider.GetRequiredService<IOptions<SQLiteCacheOptions>>().Value;
            if (!options.UseInMemory)
            {
                await ApplyCachePragmasAsync(context, options, cancellationToken);
            }

            _logger.LogInformation("SQLite cache database migration completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQLite cache database migration failed");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ApplyCachePragmasAsync(
        SQLiteCacheDbContext context,
        SQLiteCacheOptions options,
        CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL", cancellationToken);

#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync(
            $"PRAGMA synchronous={options.Synchronous.ToString().ToUpper()}", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            $"PRAGMA cache_size={options.CacheSize}", cancellationToken);
#pragma warning restore EF1002

        _logger.LogDebug("Cache database PRAGMA optimizations applied");
    }
}
