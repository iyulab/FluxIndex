using FluxIndex.Core.Application.Interfaces;
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

        // sqlite-vec 확장 로더 등록
        services.AddScoped<ISQLiteVecExtensionLoader, SQLiteVecExtensionLoader>();

        // 폴백용 로더도 등록
        services.AddScoped<NoOpSQLiteVecExtensionLoader>();

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

        // 주 벡터 저장소로 SQLiteVecVectorStore 등록
        services.AddScoped<IVectorStore, SQLiteVecVectorStore>();

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
        int vectorDimension = 1536)
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
}

/// <summary>
/// SQLite PRAGMA 최적화 설정 헬퍼
/// </summary>
internal static class SQLitePragmaHelper
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
            $"PRAGMA synchronous={options.Synchronous.ToString().ToUpper()}",
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
            $"PRAGMA temp_store={options.TempStore.ToString().ToUpper()}",
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

        // Auto vacuum 설정
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync(
            $"PRAGMA auto_vacuum={(int)options.AutoVacuum}",
            cancellationToken);
#pragma warning restore EF1002

        logger.LogInformation(
            "SQLite 성능 최적화 설정 완료: WAL 모드, {CacheSize}KB 캐시, {MmapSize}B mmap, Temp={TempStore}",
            Math.Abs(options.CacheSize), options.MmapSize, options.TempStore);
    }
}

/// <summary>
/// SQLite 데이터베이스 마이그레이션 서비스
/// </summary>
internal class SQLiteMigrationService : IHostedService
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
        _logger.LogInformation("Starting SQLite database migration");

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SQLiteDbContext>();

        try
        {
            // 데이터베이스 생성 및 마이그레이션
            await context.Database.EnsureCreatedAsync(cancellationToken);

            // 추가 초기화 (필요시)
            var options = scope.ServiceProvider.GetRequiredService<IOptions<SQLiteOptions>>().Value;
            if (!options.UseInMemory)
            {
                await SQLitePragmaHelper.ApplyPragmaOptimizationsAsync(context, options, _logger, cancellationToken);
            }

            _logger.LogInformation("SQLite database migration completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQLite database migration failed");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// SQLite-vec 데이터베이스 초기화 및 마이그레이션 서비스
/// </summary>
internal class SQLiteVecMigrationService : IHostedService
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
        _logger.LogInformation("SQLite-vec 데이터베이스 초기화 시작");

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

            _logger.LogInformation("SQLite-vec 데이터베이스 초기화 완료");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQLite-vec 데이터베이스 초기화 실패");

            // 옵션에 따라 폴백 모드 여부 결정
            var options = scope.ServiceProvider.GetService<IOptions<SQLiteVecOptions>>()?.Value;
            if (options?.FallbackToInMemoryOnError == true)
            {
                _logger.LogInformation("폴백 모드로 계속 진행");
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
}