using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Storage.PostgreSQL.EntityGraph;

/// <summary>
/// Extension methods for registering PostgreSQL entity graph services.
/// </summary>
public static class EntityGraphServiceCollectionExtensions
{
    /// <summary>
    /// Adds PostgreSQL entity graph storage (IGraphStore implementation).
    /// Uses adjacency list with recursive CTEs for graph traversal.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="connectionString">PostgreSQL connection string</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddPostgresEntityGraph(
        this IServiceCollection services,
        string connectionString,
        Action<EntityGraphOptions>? configure = null)
    {
        var options = new EntityGraphOptions
        {
            ConnectionString = connectionString
        };
        configure?.Invoke(options);

        services.Configure<EntityGraphOptions>(opt =>
        {
            opt.ConnectionString = options.ConnectionString;
            opt.EmbeddingDimension = options.EmbeddingDimension;
            opt.MaxTraversalDepth = options.MaxTraversalDepth;
            opt.DefaultPageSize = options.DefaultPageSize;
            opt.AutoMigrate = options.AutoMigrate;
        });

        // A factory, not a scoped context: the store opens a context per operation, so one store instance is safe for concurrent callers. The context type itself
        // stays resolvable (scoped).
        services.AddDbContextFactory<EntityGraphDbContext>((sp, dbOptions) =>
        {
            dbOptions.UseNpgsql(options.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.UseVector();
            });
        });

        services.AddScoped<IGraphStore, PostgresEntityGraphStore>();

        // Schema provisioning at host start, gated by AutoMigrate — the same shape as the SQLite entity graph and the
        // PostgreSQL graph store. Before 0.72.0 AutoMigrate was copied here and never read: nothing provisioned the schema
        // unless the application called EnsureEntityGraphSchemaAsync.
        services.AddSingleton<PostgresEntityGraphSchemaInitializer>();
        services.AddHostedService<PostgresEntityGraphMigrationService>();

        return services;
    }

    /// <summary>
    /// Adds PostgreSQL entity graph storage with options object.
    /// </summary>
    public static IServiceCollection AddPostgresEntityGraph(
        this IServiceCollection services,
        EntityGraphOptions options)
    {
        return services.AddPostgresEntityGraph(options.ConnectionString, opt =>
        {
            opt.EmbeddingDimension = options.EmbeddingDimension;
            opt.MaxTraversalDepth = options.MaxTraversalDepth;
            opt.DefaultPageSize = options.DefaultPageSize;
            opt.AutoMigrate = options.AutoMigrate;
        });
    }

    /// <summary>
    /// Ensures the entity graph database schema is created.
    /// Call this during application startup.
    /// </summary>
    public static Task EnsureEntityGraphSchemaAsync(
        this IServiceProvider services,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EntityGraphDbContext>();
        ct.ThrowIfCancellationRequested();
        ProvisionSchema(context);
        return Task.CompletedTask;
    }

    internal static void ProvisionSchema(EntityGraphDbContext context)
    {
        // Ensure pgvector extension is created (and reload the data source's type catalogue — see the helper)
        RelationalSchemaProvisioner.EnsureVectorExtension(context);

        // Create or migrate schema. Provisioning per owned relation rather than EnsureCreated, which
        // creates nothing once the database holds any relation at all — including relations another
        // FluxIndex component put there.
        RelationalSchemaProvisioner.ProvisionTables(context);
    }
}

/// <summary>
/// Provisions the PostgreSQL entity graph schema when <see cref="EntityGraphOptions.AutoMigrate"/> is on. An
/// <see cref="IStorageInitializer"/>, hosted by <see cref="PostgresEntityGraphMigrationService"/> for applications that
/// register the store into their own service collection.
/// </summary>
internal sealed partial class PostgresEntityGraphSchemaInitializer : IStorageInitializer
{
    private readonly ILogger<PostgresEntityGraphSchemaInitializer> _logger;

    public PostgresEntityGraphSchemaInitializer(ILogger<PostgresEntityGraphSchemaInitializer> logger)
    {
        _logger = logger;
    }

    public void InitializeSync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<EntityGraphOptions>>().Value;
        if (!options.AutoMigrate)
        {
            LogMigrationSkipped(_logger);
            return;
        }

        LogMigrationStarting(_logger);
        try
        {
            EntityGraphServiceCollectionExtensions.ProvisionSchema(scope.ServiceProvider.GetRequiredService<EntityGraphDbContext>());
            LogMigrationCompleted(_logger);
        }
        catch (Exception ex)
        {
            LogMigrationFailed(_logger, ex);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgreSQL entity graph schema provisioning skipped: AutoMigrate is false")]
    private static partial void LogMigrationSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting PostgreSQL entity graph schema provisioning")]
    private static partial void LogMigrationStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgreSQL entity graph schema provisioning completed")]
    private static partial void LogMigrationCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "PostgreSQL entity graph schema provisioning failed")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception);
}

/// <summary>Runs <see cref="PostgresEntityGraphSchemaInitializer"/> at host start.</summary>
internal sealed class PostgresEntityGraphMigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PostgresEntityGraphSchemaInitializer _initializer;

    public PostgresEntityGraphMigrationService(IServiceProvider serviceProvider, PostgresEntityGraphSchemaInitializer initializer)
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
