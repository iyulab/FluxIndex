using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Constants;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// SQLite 벡터 저장소 서비스 등록 확장 메서드
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// sqlite-vec 확장을 사용하는 고성능 SQLite 벡터 저장소 등록
    /// </summary>
    public static IServiceCollection AddSQLiteVecVectorStore(
        this IServiceCollection services,
        Action<SQLiteVecOptions> configureOptions)
    {
        // SQLiteVecOptions 등록
        services.Configure(configureOptions);

        // sqlite-vec 확장 로더 등록 (Singleton: 연결 상태를 보유하지 않으며 path 캐싱 활용)
        services.AddSingleton<ISQLiteVecExtensionLoader, SQLiteVecExtensionLoader>();

        // 폴백용 로더도 등록
        services.AddSingleton<NoOpSQLiteVecExtensionLoader>();

        // SQLiteVecDbContext 등록 (연결 풀링 및 성능 최적화)
        services.AddDbContext<SQLiteVecDbContext>((serviceProvider, dbOptions) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<SQLiteVecOptions>>().Value;
            dbOptions.UseSqlite(options.GetConnectionString(), sqliteOptions =>
            {
                // CommandTimeout 설정 (장시간 쿼리 대비)
                sqliteOptions.CommandTimeout(options.CommandTimeout);
            });

            // 쿼리 추적 비활성화 (읽기 전용 작업 성능 향상 - 20-30%)
            dbOptions.UseQueryTrackingBehavior(Microsoft.EntityFrameworkCore.QueryTrackingBehavior.NoTracking);

            // 민감한 데이터 로깅 비활성화 (프로덕션 보안)
            dbOptions.EnableSensitiveDataLogging(false);

            // 개발 환경에서만 상세 오류 활성화
            #if DEBUG
            dbOptions.EnableDetailedErrors(true);
            dbOptions.EnableSensitiveDataLogging(true);
            #endif
        }, ServiceLifetime.Scoped);

        // 폴백용 기존 SQLite 벡터 저장소 등록
        services.AddScoped<SQLiteDbContext>((serviceProvider) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<SQLiteVecOptions>>().Value;
            var dbOptions = new DbContextOptionsBuilder<SQLiteDbContext>()
                .UseSqlite(options.GetConnectionString())
                .Options;

            return new SQLiteDbContext(dbOptions, Options.Create((SQLiteOptions)options));
        });

        services.AddScoped<Lazy<SQLiteVectorStore>>(serviceProvider =>
            new Lazy<SQLiteVectorStore>(() =>
            {
                var context = serviceProvider.GetRequiredService<SQLiteDbContext>();
                var logger = serviceProvider.GetRequiredService<ILogger<SQLiteVectorStore>>();
                var options = serviceProvider.GetRequiredService<IOptions<SQLiteOptions>>();
                return new SQLiteVectorStore(context, logger, options);
            }));

        // 주 벡터 저장소로 SQLiteVecVectorStore 등록 (IVectorStore + IVectorStoreManager 동일 인스턴스)
        services.AddScoped<SQLiteVecVectorStore>();
        services.AddScoped<IVectorStore>(sp => sp.GetRequiredService<SQLiteVecVectorStore>());
        services.AddScoped<IVectorStoreManager>(sp => sp.GetRequiredService<SQLiteVecVectorStore>());

        // 초기화 서비스 등록
        services.AddHostedService<SQLiteVecMigrationService>();

        return services;
    }

    /// <summary>
    /// sqlite-vec 확장을 사용하는 벡터 저장소 등록 (구성에서 옵션 읽기)
    /// </summary>
    public static IServiceCollection AddSQLiteVecVectorStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddSQLiteVecVectorStore(options =>
        {
            configuration.GetSection("SQLiteVec").Bind(options);
            options.Validate();
        });
    }

    /// <summary>
    /// sqlite-vec 확장을 사용하는 벡터 저장소 등록 (간단한 설정)
    /// </summary>
    public static IServiceCollection AddSQLiteVecVectorStore(
        this IServiceCollection services,
        string databasePath = "fluxindex-vec.db",
        int vectorDimension = EmbeddingDefaults.DefaultVectorDimension)
    {
        return services.AddSQLiteVecVectorStore(options =>
        {
            options.DatabasePath = databasePath;
            options.VectorDimension = vectorDimension;
            options.UseSQLiteVec = true;
            options.AutoMigrate = true;
            options.FallbackToInMemoryOnError = true;
        });
    }

    /// <summary>
    /// sqlite-vec 확장을 사용하는 인메모리 벡터 저장소 등록 (테스트용)
    /// </summary>
    public static IServiceCollection AddSQLiteVecInMemoryVectorStore(
        this IServiceCollection services,
        int vectorDimension = 384)
    {
        return services.AddSQLiteVecVectorStore(options =>
        {
            options.UseInMemory = true;
            options.VectorDimension = vectorDimension;
            options.UseSQLiteVec = false; // 인메모리에서는 확장 사용 안 함
            options.AutoMigrate = true;
            options.FallbackToInMemoryOnError = true;
        });
    }
    /// <summary>
    /// SQLite 벡터 저장소 등록 (구성에서 옵션 읽기)
    /// </summary>
    public static IServiceCollection AddSQLiteVectorStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new SQLiteOptions();
        configuration.GetSection("SQLite").Bind(options);
        
        return services.AddSQLiteVectorStore(options);
    }
    
    /// <summary>
    /// SQLite 벡터 저장소 등록 (옵션 직접 지정)
    /// </summary>
    public static IServiceCollection AddSQLiteVectorStore(
        this IServiceCollection services,
        SQLiteOptions options)
    {
        services.AddSingleton(options);
        
        // DbContext 등록
        services.AddDbContext<SQLiteDbContext>(dbOptions =>
        {
            dbOptions.UseSqlite(options.GetConnectionString(), sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(options.CommandTimeout);
            });
        }, ServiceLifetime.Scoped);
        
        // Vector Store 등록
        services.AddScoped<IVectorStore, SQLiteVectorStore>();
        
        // 자동 마이그레이션 설정
        if (options.AutoMigrate)
        {
            services.AddHostedService<SQLiteMigrationService>();
        }
        
        return services;
    }
    
    /// <summary>
    /// SQLite 벡터 저장소 등록 (간단한 설정)
    /// </summary>
    public static IServiceCollection AddSQLiteVectorStore(
        this IServiceCollection services,
        string databasePath = "fluxindex.db")
    {
        var options = new SQLiteOptions
        {
            DatabasePath = databasePath,
            AutoMigrate = true
        };
        
        return services.AddSQLiteVectorStore(options);
    }
    
    /// <summary>
    /// SQLite 인메모리 벡터 저장소 등록 (테스트용)
    /// </summary>
    public static IServiceCollection AddSQLiteInMemoryVectorStore(
        this IServiceCollection services)
    {
        var options = new SQLiteOptions
        {
            UseInMemory = true,
            AutoMigrate = true
        };
        
        return services.AddSQLiteVectorStore(options);
    }
    
    /// <summary>
    /// SQLite 벡터 저장소 등록 (Action 설정)
    /// </summary>
    public static IServiceCollection AddSQLiteVectorStore(
        this IServiceCollection services,
        Action<SQLiteOptions> configureOptions)
    {
        // Configure options with generic type parameter
        services.Configure<SQLiteOptions>(configureOptions);

        // Use existing SQLiteOptions registration
        services.AddDbContext<SQLiteDbContext>((serviceProvider, dbOptions) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<SQLiteOptions>>().Value;
            dbOptions.UseSqlite(options.GetConnectionString(), sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(options.CommandTimeout);
            });
        }, ServiceLifetime.Scoped);

        // Vector Store 등록
        services.AddScoped<IVectorStore, SQLiteVectorStore>();

        // 자동 마이그레이션 설정
        services.AddHostedService<SQLiteMigrationService>();

        return services;
    }

    /// <summary>
    /// SQLite 양자화 벡터 저장소 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">옵션 설정 액션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddSQLiteQuantizedVectorStore(
        this IServiceCollection services,
        Action<SQLiteQuantizedOptions> configureOptions)
    {
        services.Configure(configureOptions);

        // DbContext 등록
        services.AddDbContext<SQLiteQuantizedDbContext>((serviceProvider, dbOptions) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<SQLiteQuantizedOptions>>().Value;
            dbOptions.UseSqlite(options.GetConnectionString(), sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(options.CommandTimeout);
            });
            dbOptions.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }, ServiceLifetime.Scoped);

        // IQuantizedVectorStore 및 IVectorStore 등록
        services.AddScoped<SQLiteQuantizedVectorStore>();
        services.AddScoped<IQuantizedVectorStore>(sp => sp.GetRequiredService<SQLiteQuantizedVectorStore>());
        services.AddScoped<IVectorStore>(sp => sp.GetRequiredService<SQLiteQuantizedVectorStore>());

        // 마이그레이션 서비스
        services.AddHostedService<SQLiteQuantizedMigrationService>();

        return services;
    }

    /// <summary>
    /// SQLite 양자화 벡터 저장소 등록 (간단한 설정)
    /// </summary>
    public static IServiceCollection AddSQLiteQuantizedVectorStore(
        this IServiceCollection services,
        string databasePath = "fluxindex-quantized.db",
        bool autoQuantize = true)
    {
        return services.AddSQLiteQuantizedVectorStore(options =>
        {
            options.DatabasePath = databasePath;
            options.AutoQuantizeOnStore = autoQuantize;
            options.AutoMigrate = true;
        });
    }

    /// <summary>
    /// SQLite 양자화 벡터 저장소 등록 (인메모리, 테스트용)
    /// </summary>
    public static IServiceCollection AddSQLiteQuantizedInMemoryVectorStore(
        this IServiceCollection services,
        bool autoQuantize = true)
    {
        return services.AddSQLiteQuantizedVectorStore(options =>
        {
            options.UseInMemory = true;
            options.AutoQuantizeOnStore = autoQuantize;
            options.AutoMigrate = true;
        });
    }

    /// <summary>
    /// SQLite 통합 Provider 등록.
    /// IStorageProvider로 등록되어 StorageOrchestrator에서 자동 인식.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddSQLiteUnifiedProvider(this IServiceCollection services)
    {
        services.AddScoped<IStorageProvider>(sp =>
        {
            var vectorStore = sp.GetRequiredService<IVectorStore>();
            var semanticCache = sp.GetService<ISemanticCacheService>();
            var logger = sp.GetService<ILogger<SQLiteUnifiedProvider>>();
            return new SQLiteUnifiedProvider(vectorStore, semanticCache, logger);
        });

        return services;
    }
}

/// <summary>
/// SQLite 양자화 데이터베이스 마이그레이션 서비스
/// </summary>
internal sealed partial class SQLiteQuantizedMigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SQLiteQuantizedMigrationService> _logger;

    public SQLiteQuantizedMigrationService(
        IServiceProvider serviceProvider,
        ILogger<SQLiteQuantizedMigrationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LogMigrationStarting(_logger);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SQLiteQuantizedDbContext>();

        try
        {
            // Per owned table, not EnsureCreated: several components share one database file, and
            // EnsureCreated stops as soon as any table exists.
            SQLiteSchemaProvisioner.Provision(context);

            var options = scope.ServiceProvider.GetRequiredService<IOptions<SQLiteQuantizedOptions>>().Value;
            if (!options.UseInMemory)
            {
                await SQLitePragmaHelper.ApplyPragmaOptimizationsAsync(context, options, _logger, cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting SQLite quantized database migration")]
    private static partial void LogMigrationStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SQLite quantized database migration completed")]
    private static partial void LogMigrationCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "SQLite quantized database migration failed")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception);

    #endregion
}

/// <summary>
/// SQLite PRAGMA 최적화 설정 헬퍼
/// </summary>
internal static partial class SQLitePragmaHelper
{
    /// <summary>
    /// 공통 PRAGMA 최적화 설정을 적용합니다.
    /// </summary>
    public static async Task ApplyPragmaOptimizationsAsync<TContext>(
        TContext context,
        SQLiteOptions options,
        ILogger logger,
        CancellationToken cancellationToken) where TContext : DbContext
    {
        // WAL 모드 활성화 (성능 향상)
        await context.Database.ExecuteSqlRawAsync(
            "PRAGMA journal_mode=WAL",
            cancellationToken);

        // 동기화 모드 설정 (내부적으로 제어되는 enum 값 - SQL injection 안전)
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync(
            $"PRAGMA synchronous={options.Synchronous.ToString().ToUpperInvariant()}",
            cancellationToken);
#pragma warning restore EF1002

        // 메모리 맵 크기 설정
        if (options.MmapSize > 0)
        {
#pragma warning disable EF1002
            await context.Database.ExecuteSqlRawAsync(
                $"PRAGMA mmap_size={options.MmapSize}",
                cancellationToken);
#pragma warning restore EF1002
        }

        // 캐시 크기 설정
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync(
            $"PRAGMA cache_size={options.CacheSize}",
            cancellationToken);
#pragma warning restore EF1002

        // 임시 저장소 설정
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync(
            $"PRAGMA temp_store={options.TempStore.ToString().ToUpperInvariant()}",
            cancellationToken);
#pragma warning restore EF1002

        // 페이지 크기 (새 DB 생성 시에만 적용됨)
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync(
            $"PRAGMA page_size={options.PageSize}",
            cancellationToken);
#pragma warning restore EF1002

        // Busy timeout 설정
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync(
            $"PRAGMA busy_timeout={options.BusyTimeout}",
            cancellationToken);
#pragma warning restore EF1002

        // WAL auto-checkpoint threshold
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync(
            $"PRAGMA wal_autocheckpoint={options.WalAutocheckpoint}",
            cancellationToken);
#pragma warning restore EF1002

        // Auto vacuum 설정
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync(
            $"PRAGMA auto_vacuum={(int)options.AutoVacuum}",
            cancellationToken);
#pragma warning restore EF1002

        var cacheSize = Math.Abs(options.CacheSize);
        LogPragmaOptimizationsApplied(logger, cacheSize, options.MmapSize, options.TempStore);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "SQLite performance optimizations applied: WAL mode, {CacheSize}KB cache, {MmapSize}B mmap, Temp={TempStore}")]
    private static partial void LogPragmaOptimizationsApplied(ILogger logger, int cacheSize, long mmapSize, TempStoreMode tempStore);

    #endregion
}

/// <summary>
/// SQLite 데이터베이스 마이그레이션 서비스
/// </summary>
internal sealed partial class SQLiteMigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SQLiteMigrationService> _logger;

    public SQLiteMigrationService(
        IServiceProvider serviceProvider,
        ILogger<SQLiteMigrationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LogMigrationStarting(_logger);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SQLiteDbContext>();

        try
        {
            // Per owned table, not EnsureCreated: several components share one database file, and
            // EnsureCreated stops as soon as any table exists.
            SQLiteSchemaProvisioner.Provision(context);

            // 추가 초기화 (필요시)
            var options = scope.ServiceProvider.GetRequiredService<IOptions<SQLiteOptions>>().Value;
            if (!options.UseInMemory)
            {
                await SQLitePragmaHelper.ApplyPragmaOptimizationsAsync(context, options, _logger, cancellationToken);
            }

            LogMigrationCompleted(_logger);
        }
        catch (Exception ex)
        {
            LogMigrationFailed(_logger, ex);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting SQLite database migration")]
    private static partial void LogMigrationStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SQLite database migration completed successfully")]
    private static partial void LogMigrationCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "SQLite database migration failed")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception);

    #endregion
}

/// <summary>
/// SQLite-vec 데이터베이스 초기화 및 마이그레이션 서비스
/// </summary>
internal sealed partial class SQLiteVecMigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SQLiteVecMigrationService> _logger;

    public SQLiteVecMigrationService(
        IServiceProvider serviceProvider,
        ILogger<SQLiteVecMigrationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LogVecInitStarting(_logger);

        using var scope = _serviceProvider.CreateScope();

        try
        {
            var context = scope.ServiceProvider.GetRequiredService<SQLiteVecDbContext>();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<SQLiteVecOptions>>().Value;

            // 옵션 유효성 검증
            options.Validate();

            // 데이터베이스 초기화
            await context.InitializeAsync(cancellationToken);

            // 성능 최적화 설정
            if (!options.UseInMemory && options.UseSQLiteVec)
            {
                await SQLitePragmaHelper.ApplyPragmaOptimizationsAsync(context, options, _logger, cancellationToken);
            }

            // Trigger IVectorStore.EnsureInitializedAsync at startup so the vec0 JIT warmup
            // (inside SQLiteVecVectorStore.EnsureInitializedAsync) runs here, not on the first
            // user-triggered batch. VerifyHealthAsync calls EnsureInitializedAsync internally.
            try
            {
                var vectorStore = scope.ServiceProvider.GetRequiredService<IVectorStore>();
                await vectorStore.VerifyHealthAsync(cancellationToken);
            }
            catch (Exception warmupEx)
            {
                // Warmup is best-effort; failure is non-fatal — first user batch will pay cold-start cost.
                LogVecWarmupFailed(_logger, warmupEx);
            }

            // One-time orphan sweep (idempotent via __fluxindex_migrations marker).
            // Cleans up vec0 rows whose vector_chunks parent was deleted under the
            // pre-0.13.7 single-table delete behavior.
            if (options.EnableStartupOrphanSweep)
            {
                try
                {
                    await context.SweepOrphanVectorsAsync(cancellationToken);
                }
                catch (Exception sweepEx)
                {
                    LogOrphanSweepFailed(_logger, sweepEx);
                }
            }

            // Note: the non-destructive cross-fingerprint orphan scan runs inside
            // SQLiteVecVectorStore.EnsureInitializedAsync (triggered by VerifyHealthAsync above),
            // where the effective fingerprint is guaranteed bound. It is intentionally NOT invoked
            // here: this hook can run before the identity binder, leaving the fingerprint unbound
            // and the scan a silent no-op.

            LogVecInitCompleted(_logger);
        }
        catch (Exception ex)
        {
            LogVecInitFailed(_logger, ex);

            // 옵션에 따라 폴백 모드 여부 결정
            var options = scope.ServiceProvider.GetService<IOptions<SQLiteVecOptions>>()?.Value;
            if (options?.FallbackToInMemoryOnError == true)
            {
                LogVecFallbackContinue(_logger);
            }
            else
            {
                throw;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "SQLite-vec database initialization started")]
    private static partial void LogVecInitStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SQLite-vec database initialization completed")]
    private static partial void LogVecInitCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "SQLite-vec database initialization failed")]
    private static partial void LogVecInitFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Continuing in fallback mode")]
    private static partial void LogVecFallbackContinue(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Vector store warmup failed at startup (non-fatal); first batch will pay cold-start cost")]
    private static partial void LogVecWarmupFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Startup orphan sweep failed (non-fatal); orphans will accumulate until the next successful run")]
    private static partial void LogOrphanSweepFailed(ILogger logger, Exception exception);

    #endregion
}