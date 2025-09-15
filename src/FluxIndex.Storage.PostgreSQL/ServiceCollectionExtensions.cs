using FluxIndex.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL 벡터 저장소 서비스 등록 확장 메서드
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// PostgreSQL 벡터 저장소 등록
    /// </summary>
    public static IServiceCollection AddPostgreSQLVectorStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new PostgreSQLOptions();
        configuration.GetSection("PostgreSQL").Bind(options);
        
        return services.AddPostgreSQLVectorStore(options);
    }
    
    /// <summary>
    /// PostgreSQL 벡터 저장소 등록 (옵션 직접 지정)
    /// </summary>
    public static IServiceCollection AddPostgreSQLVectorStore(
        this IServiceCollection services,
        PostgreSQLOptions options)
    {
        services.AddSingleton(options);
        
        // DbContext 등록
        services.AddDbContext<FluxIndexDbContext>(dbOptions =>
        {
            dbOptions.UseNpgsql(options.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.UseVector();
                npgsqlOptions.CommandTimeout(options.CommandTimeout);
            });
        }, ServiceLifetime.Scoped);
        
        // Vector Store 등록
        services.AddScoped<IVectorStore, PostgreSQLVectorStore>();
        
        // 자동 마이그레이션 설정
        if (options.AutoMigrate)
        {
            services.AddHostedService<DatabaseMigrationService>();
        }
        
        return services;
    }
    
    /// <summary>
    /// PostgreSQL 벡터 저장소 등록 (연결 문자열만 지정)
    /// </summary>
    public static IServiceCollection AddPostgreSQLVectorStore(
        this IServiceCollection services,
        string connectionString)
    {
        var options = new PostgreSQLOptions
        {
            ConnectionString = connectionString
        };
        
        return services.AddPostgreSQLVectorStore(options);
    }
}

/// <summary>
/// 데이터베이스 마이그레이션 서비스
/// </summary>
internal class DatabaseMigrationService : Microsoft.Extensions.Hosting.IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseMigrationService> _logger;

    public DatabaseMigrationService(
        IServiceProvider serviceProvider,
        ILogger<DatabaseMigrationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting database migration");
        
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FluxIndexDbContext>();
        
        try
        {
            // pgvector 확장 생성
            await context.Database.ExecuteSqlRawAsync(
                "CREATE EXTENSION IF NOT EXISTS vector", 
                cancellationToken);
            
            // 마이그레이션 실행
            await context.Database.MigrateAsync(cancellationToken);
            
            // HNSW 인덱스 생성 (pgvector 0.5.0+)
            var options = scope.ServiceProvider.GetRequiredService<PostgreSQLOptions>();
            if (options.HnswM > 0 && options.HnswEfConstruction > 0)
            {
                try
                {
                    await context.Database.ExecuteSqlRawAsync($@"
                        CREATE INDEX IF NOT EXISTS idx_chunks_embedding_hnsw 
                        ON chunks USING hnsw (embedding vector_cosine_ops)
                        WITH (m = {options.HnswM}, ef_construction = {options.HnswEfConstruction})",
                        cancellationToken);
                    
                    _logger.LogInformation("Created HNSW index with m={M}, ef_construction={EfConstruction}", 
                        options.HnswM, options.HnswEfConstruction);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create HNSW index. This requires pgvector 0.5.0+");
                }
            }
            
            _logger.LogInformation("Database migration completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database migration failed");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}