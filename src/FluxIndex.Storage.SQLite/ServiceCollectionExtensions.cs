using FluxIndex.Application.Interfaces;
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
        var options = new SQLiteOptions();
        configureOptions(options);
        
        return services.AddSQLiteVectorStore(options);
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
            var options = scope.ServiceProvider.GetRequiredService<SQLiteOptions>();
            if (!options.UseInMemory)
            {
                // WAL 모드 활성화 (성능 향상)
                await context.Database.ExecuteSqlRawAsync(
                    "PRAGMA journal_mode=WAL", 
                    cancellationToken);
                
                // 동기화 모드 설정 (성능과 안정성 균형)
                await context.Database.ExecuteSqlRawAsync(
                    "PRAGMA synchronous=NORMAL", 
                    cancellationToken);
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