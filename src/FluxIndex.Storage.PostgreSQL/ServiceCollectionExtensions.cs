using FluxIndex.Core.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// Extension methods for registering PostgreSQL vector store with dependency injection
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds PostgreSQL vector store to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddPostgreSQLVectorStore(
        this IServiceCollection services,
        Action<PostgreSQLOptions> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);

        // Register DbContext with NpgsqlDataSource for dynamic JSON support
        services.AddDbContext<FluxIndexDbContext>((serviceProvider, options) =>
        {
            var postgresOptions = serviceProvider.GetRequiredService<IOptions<PostgreSQLOptions>>().Value;

            // Build NpgsqlDataSource with dynamic JSON support for Dictionary<string, object>
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(postgresOptions.ConnectionString);
            dataSourceBuilder.EnableDynamicJson();
            var dataSource = dataSourceBuilder.Build();

            options.UseNpgsql(dataSource, npgsqlOptions =>
            {
                npgsqlOptions.UseVector();
                npgsqlOptions.CommandTimeout(postgresOptions.CommandTimeout);
            });
        });

        // Register vector store
        services.AddScoped<IVectorStore, PostgreSQLVectorStore>();

        return services;
    }

    /// <summary>
    /// Adds PostgreSQL vector store with connection string
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="connectionString">PostgreSQL connection string</param>
    /// <param name="embeddingDimensions">Embedding vector dimensions (default: 1536)</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddPostgreSQLVectorStore(
        this IServiceCollection services,
        string connectionString,
        int embeddingDimensions = 1536)
    {
        return services.AddPostgreSQLVectorStore(options =>
        {
            options.ConnectionString = connectionString;
            options.EmbeddingDimensions = embeddingDimensions;
        });
    }

    /// <summary>
    /// Adds PostgreSQL quantized vector store to the service collection.
    /// Supports both original pgvector embeddings and quantized embeddings.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddPostgreSQLQuantizedVectorStore(
        this IServiceCollection services,
        Action<PostgreSQLQuantizedOptions> configureOptions)
    {
        services.Configure(configureOptions);

        // Register DbContext
        services.AddDbContext<FluxIndexQuantizedDbContext>((serviceProvider, options) =>
        {
            var postgresOptions = serviceProvider.GetRequiredService<IOptions<PostgreSQLQuantizedOptions>>().Value;
            options.UseNpgsql(postgresOptions.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.UseVector();
                npgsqlOptions.CommandTimeout(postgresOptions.CommandTimeout);
            });
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        // Register quantized vector store (implements both interfaces)
        services.AddScoped<PostgreSQLQuantizedVectorStore>();
        services.AddScoped<IQuantizedVectorStore>(sp => sp.GetRequiredService<PostgreSQLQuantizedVectorStore>());
        services.AddScoped<IVectorStore>(sp => sp.GetRequiredService<PostgreSQLQuantizedVectorStore>());

        return services;
    }

    /// <summary>
    /// Adds PostgreSQL quantized vector store with connection string.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="connectionString">PostgreSQL connection string</param>
    /// <param name="embeddingDimensions">Embedding vector dimensions (default: 1536)</param>
    /// <param name="autoQuantize">Automatically quantize embeddings on store</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddPostgreSQLQuantizedVectorStore(
        this IServiceCollection services,
        string connectionString,
        int embeddingDimensions = 1536,
        bool autoQuantize = true)
    {
        return services.AddPostgreSQLQuantizedVectorStore(options =>
        {
            options.ConnectionString = connectionString;
            options.EmbeddingDimensions = embeddingDimensions;
            options.AutoQuantizeOnStore = autoQuantize;
        });
    }

    /// <summary>
    /// PostgreSQL 통합 Provider 등록.
    /// IStorageProvider로 등록되어 StorageOrchestrator에서 자동 인식.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddPostgreSQLUnifiedProvider(this IServiceCollection services)
    {
        services.AddScoped<IStorageProvider>(sp =>
        {
            var vectorStore = sp.GetRequiredService<IVectorStore>();
            var semanticCache = sp.GetService<ISemanticCacheService>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<PostgreSQLUnifiedProvider>>();
            return new PostgreSQLUnifiedProvider(vectorStore, semanticCache, logger);
        });

        return services;
    }
}