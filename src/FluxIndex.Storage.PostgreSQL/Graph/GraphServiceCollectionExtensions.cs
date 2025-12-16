using FluxIndex.Core.Application.Interfaces;
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

        // 마이그레이션 서비스
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
/// PostgreSQL Graph 마이그레이션 서비스
/// </summary>
internal class PostgresGraphMigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PostgresGraphMigrationService> _logger;

    public PostgresGraphMigrationService(
        IServiceProvider serviceProvider,
        ILogger<PostgresGraphMigrationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<PostgresGraphOptions>>().Value;

        if (!options.AutoMigrate)
        {
            _logger.LogInformation("PostgreSQL graph auto-migration is disabled");
            return;
        }

        _logger.LogInformation("Starting PostgreSQL graph database migration");

        var context = scope.ServiceProvider.GetRequiredService<PostgresGraphDbContext>();

        try
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);

            // GIN 인덱스가 지원되는지 확인하고 생성
            if (options.UseJsonbIndex)
            {
                await CreateGinIndexesAsync(context, cancellationToken);
            }

            _logger.LogInformation("PostgreSQL graph database migration completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgreSQL graph database migration failed");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CreateGinIndexesAsync(
        PostgresGraphDbContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            // GIN 인덱스 for JSONB 필드 (이미 존재하면 무시)
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE INDEX IF NOT EXISTS idx_chunk_hierarchies_child_ids_gin
                ON chunk_hierarchies USING gin (""ChildChunkIds"");

                CREATE INDEX IF NOT EXISTS idx_chunk_relationships_metadata_gin
                ON chunk_relationships USING gin (""Metadata"");
            ", cancellationToken);

            _logger.LogDebug("GIN indexes created for JSONB columns");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create GIN indexes, continuing without them");
        }
    }
}
