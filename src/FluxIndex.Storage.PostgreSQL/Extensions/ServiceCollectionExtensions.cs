using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Storage.PostgreSQL.Configuration;
using FluxIndex.Storage.PostgreSQL.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System;

namespace FluxIndex.Storage.PostgreSQL.Extensions;

/// <summary>
/// PostgreSQL 벡터 저장소 서비스 등록 확장 메서드
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// PostgreSQL 벡터 저장소 서비스 등록
    /// </summary>
    public static IServiceCollection AddPostgreSQLVectorStore(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<PostgreSQLVectorContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.UseVector()));

        services.AddScoped<IVectorStore, PostgreSQLVectorStore>();

        return services;
    }

    /// <summary>
    /// PostgreSQL 벡터 저장소 서비스 등록 (옵션 구성)
    /// </summary>
    public static IServiceCollection AddPostgreSQLVectorStore(
        this IServiceCollection services,
        Action<PostgreSQLVectorStoreOptions> configure)
    {
        services.Configure(configure);

        services.AddDbContext<PostgreSQLVectorContext>((serviceProvider, options) =>
        {
            var postgresOptions = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PostgreSQLVectorStoreOptions>>().Value;
            options.UseNpgsql(postgresOptions.ConnectionString, npgsqlOptions =>
                npgsqlOptions.UseVector());
        });

        services.AddScoped<IVectorStore, PostgreSQLVectorStore>();

        return services;
    }

    /// <summary>
    /// PostgreSQL 벡터 저장소 서비스 등록 (구성에서)
    /// </summary>
    public static IServiceCollection AddPostgreSQLVectorStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PostgreSQLVectorStoreOptions>(
            configuration.GetSection("PostgreSQLVectorStore"));

        services.AddDbContext<PostgreSQLVectorContext>((serviceProvider, options) =>
        {
            var postgresOptions = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PostgreSQLVectorStoreOptions>>().Value;
            options.UseNpgsql(postgresOptions.ConnectionString, npgsqlOptions =>
                npgsqlOptions.UseVector());
        });

        services.AddScoped<IVectorStore, PostgreSQLVectorStore>();

        return services;
    }

    /// <summary>
    /// PostgreSQL HNSW 인덱스 벤치마킹 서비스 등록
    /// </summary>
    public static IServiceCollection AddPostgreSQLVectorIndexBenchmark(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<IVectorIndexBenchmark>(serviceProvider =>
            new PostgreSQLVectorIndexBenchmark(
                connectionString,
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PostgreSQLVectorIndexBenchmark>>()));

        return services;
    }

    /// <summary>
    /// PostgreSQL HNSW 인덱스 벤치마킹 서비스 등록 (옵션에서)
    /// </summary>
    public static IServiceCollection AddPostgreSQLVectorIndexBenchmark(
        this IServiceCollection services,
        Action<PostgreSQLVectorStoreOptions> configure)
    {
        services.Configure(configure);

        services.AddSingleton<IVectorIndexBenchmark>(serviceProvider =>
        {
            var postgresOptions = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PostgreSQLVectorStoreOptions>>().Value;
            return new PostgreSQLVectorIndexBenchmark(
                postgresOptions.ConnectionString,
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PostgreSQLVectorIndexBenchmark>>());
        });

        return services;
    }

    /// <summary>
    /// PostgreSQL HNSW 인덱스 벤치마킹 서비스 등록 (구성에서)
    /// </summary>
    public static IServiceCollection AddPostgreSQLVectorIndexBenchmark(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PostgreSQLVectorStoreOptions>(
            configuration.GetSection("PostgreSQLVectorStore"));

        services.AddSingleton<IVectorIndexBenchmark>(serviceProvider =>
        {
            var postgresOptions = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PostgreSQLVectorStoreOptions>>().Value;
            return new PostgreSQLVectorIndexBenchmark(
                postgresOptions.ConnectionString,
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PostgreSQLVectorIndexBenchmark>>());
        });

        return services;
    }
}