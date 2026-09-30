using FluxIndex.Core.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
#pragma warning disable CS0618 // carry the obsolete value until the property is removed
            opt.IvfflatLists = options.IvfflatLists;
#pragma warning restore CS0618
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
#pragma warning disable CS0618 // carry the obsolete value until the property is removed
            opt.IvfflatLists = options.IvfflatLists;
#pragma warning restore CS0618
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

        // Ensure pgvector extension is created (and reload the data source's type catalogue — see the helper)
        ct.ThrowIfCancellationRequested();
        RelationalSchemaProvisioner.EnsureVectorExtension(context);

        // Create or migrate schema. Provisioning per owned relation rather than EnsureCreated, which
        // creates nothing once the database holds any relation at all — including relations another
        // FluxIndex component put there.
        RelationalSchemaProvisioner.ProvisionTables(context);
        return Task.CompletedTask;
    }
}
