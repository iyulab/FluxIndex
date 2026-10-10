using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // DbContext 등록 — a factory, not a scoped context: the cache opens a context per operation, so one store instance is safe for concurrent callers. The context type itself
        // stays resolvable (scoped).
        services.AddDbContextFactory<SQLiteCacheDbContext>((serviceProvider, dbOptions) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<SQLiteCacheOptions>>().Value;
            // Per-connection PRAGMAs (busy_timeout, cache_size, ...) on every connection, not only the migration's.
            dbOptions.AddInterceptors(new SQLiteConnectionPragmaInterceptor(options));
            dbOptions.UseSqlite(options.GetCacheConnectionString(), sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(options.CommandTimeout);
            });
            dbOptions.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        // The semantic cache search consults. Singleton: it opens a context per operation (the factory above), so one
        // instance serves every caller. TryAddEnumerable keeps a repeated call idempotent while leaving a different
        // ISemanticCacheService visible next to this one, so the SDK builder can refuse the ambiguity instead of
        // letting registration order pick.
        services.TryAddSingleton<SQLiteSemanticCache>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISemanticCacheService, SQLiteSemanticCache>(
            sp => sp.GetRequiredService<SQLiteSemanticCache>()));

        // 마이그레이션 — 두 경로(SDK 빌더 Build(), 앱 호스트 시작)가 같은 루틴을 공유한다.
        services.AddSingleton<SQLiteCacheSchemaInitializer>();
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
/// Provisions the SQLite semantic cache schema. An <see cref="IStorageInitializer"/> so the SDK
/// builder runs it during Build(), hosted by <see cref="SQLiteCacheMigrationService"/> for consumers
/// that register the cache directly into an application's service collection.
/// </summary>
internal sealed partial class SQLiteCacheSchemaInitializer : IStorageInitializer
{
    private readonly ILogger<SQLiteCacheSchemaInitializer> _logger;

    public SQLiteCacheSchemaInitializer(ILogger<SQLiteCacheSchemaInitializer> logger)
    {
        _logger = logger;
    }

    public void InitializeSync(IServiceProvider serviceProvider)
    {
        LogMigrationStarting(_logger);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SQLiteCacheDbContext>();

        try
        {
            SQLiteSchemaProvisioner.Provision(context);

            var options = scope.ServiceProvider.GetRequiredService<IOptions<SQLiteCacheOptions>>().Value;
            if (!options.UseInMemory)
            {
                ApplyCachePragmas(context);
            }

            LogMigrationCompleted(_logger);
        }
        catch (Exception ex)
        {
            LogMigrationFailed(_logger, ex);
            throw;
        }
    }

    private void ApplyCachePragmas(SQLiteCacheDbContext context)
    {
        // WAL is stored in the file; the per-connection settings (synchronous, cache_size, ...) are applied on every
        // connection by SQLiteConnectionPragmaInterceptor.
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL");

        LogPragmaApplied(_logger);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting SQLite cache database migration")]
    private static partial void LogMigrationStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SQLite cache database migration completed")]
    private static partial void LogMigrationCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "SQLite cache database migration failed")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache database PRAGMA optimizations applied")]
    private static partial void LogPragmaApplied(ILogger logger);

    #endregion
}

/// <summary>
/// Runs <see cref="SQLiteCacheSchemaInitializer"/> at host start for consumers that register the
/// semantic cache directly (the SDK builder path runs the initializer itself during Build()).
/// </summary>
internal sealed class SQLiteCacheMigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SQLiteCacheSchemaInitializer _initializer;

    public SQLiteCacheMigrationService(
        IServiceProvider serviceProvider,
        SQLiteCacheSchemaInitializer initializer)
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
