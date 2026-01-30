using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Storage.Qdrant;

/// <summary>
/// Extension methods for registering Qdrant vector store services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Qdrant vector store services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure Qdrant options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddQdrantVectorStore(
        this IServiceCollection services,
        Action<QdrantOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddSingleton<IVectorStore, QdrantVectorStore>();

        return services;
    }

    /// <summary>
    /// Adds Qdrant vector store with dynamic dimension adaptation (recommended).
    /// Collection name will automatically include dimension suffix (e.g., "my_chunks_384").
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="host">Qdrant server host.</param>
    /// <param name="port">Qdrant gRPC port.</param>
    /// <param name="baseCollectionName">Base collection name (dimension suffix added automatically).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddQdrantVectorStore(
        this IServiceCollection services,
        string host,
        int port,
        string baseCollectionName)
    {
        return services.AddQdrantVectorStore(options =>
        {
            options.Host = host;
            options.GrpcPort = port;
            options.BaseCollectionName = baseCollectionName;
            options.NamingStrategy = CollectionNamingStrategy.DimensionSuffix;
        });
    }

    /// <summary>
    /// Adds Qdrant vector store services with basic connection parameters.
    /// Uses Fixed naming strategy for backward compatibility.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="host">Qdrant server host.</param>
    /// <param name="port">Qdrant gRPC port.</param>
    /// <param name="collectionName">Collection name for storing vectors.</param>
    /// <param name="vectorSize">Vector dimension size.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddQdrantVectorStore(
        this IServiceCollection services,
        string host = "localhost",
        int port = 6334,
        string collectionName = "fluxindex_chunks",
        int vectorSize = 1536)
    {
        return services.AddQdrantVectorStore(options =>
        {
            options.Host = host;
            options.GrpcPort = port;
            options.BaseCollectionName = collectionName;
            options.VectorSize = vectorSize;
            options.NamingStrategy = CollectionNamingStrategy.Fixed;
        });
    }

    /// <summary>
    /// Adds Qdrant Cloud vector store with dynamic dimension adaptation (recommended).
    /// Collection name will automatically include dimension suffix.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="cloudHost">Qdrant Cloud host (e.g., "xyz-abc.aws.cloud.qdrant.io").</param>
    /// <param name="apiKey">Qdrant Cloud API key.</param>
    /// <param name="baseCollectionName">Base collection name (dimension suffix added automatically).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddQdrantCloudVectorStore(
        this IServiceCollection services,
        string cloudHost,
        string apiKey,
        string baseCollectionName)
    {
        return services.AddQdrantVectorStore(options =>
        {
            options.Host = cloudHost;
            options.ApiKey = apiKey;
            options.UseHttps = true;
            options.BaseCollectionName = baseCollectionName;
            options.NamingStrategy = CollectionNamingStrategy.DimensionSuffix;
        });
    }

    /// <summary>
    /// Adds Qdrant Cloud vector store services.
    /// Uses Fixed naming strategy for backward compatibility.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="cloudHost">Qdrant Cloud host (e.g., "xyz-abc.aws.cloud.qdrant.io").</param>
    /// <param name="apiKey">Qdrant Cloud API key.</param>
    /// <param name="collectionName">Collection name for storing vectors.</param>
    /// <param name="vectorSize">Vector dimension size.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddQdrantCloudVectorStore(
        this IServiceCollection services,
        string cloudHost,
        string apiKey,
        string collectionName = "fluxindex_chunks",
        int vectorSize = 1536)
    {
        return services.AddQdrantVectorStore(options =>
        {
            options.Host = cloudHost;
            options.ApiKey = apiKey;
            options.UseHttps = true;
            options.BaseCollectionName = collectionName;
            options.VectorSize = vectorSize;
            options.NamingStrategy = CollectionNamingStrategy.Fixed;
        });
    }

    /// <summary>
    /// Adds Qdrant vector store with hybrid search capability (Vector + BM25).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure Qdrant options.</param>
    /// <param name="bm25PersistencePath">Optional path for BM25 index persistence.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddQdrantWithHybridSearch(
        this IServiceCollection services,
        Action<QdrantOptions> configureOptions,
        string? bm25PersistencePath = null)
    {
        // Register Qdrant vector store
        services.Configure(configureOptions);
        services.AddSingleton<QdrantVectorStore>();
        services.AddSingleton<IVectorStore>(sp => sp.GetRequiredService<QdrantVectorStore>());

        // Register BM25 sparse retriever
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<BM25SparseRetriever>>();
            return new BM25SparseRetriever(logger, bm25PersistencePath);
        });

        // Register hybrid search service
        services.AddSingleton<IHybridSearchService, QdrantHybridSearchService>();

        return services;
    }

    /// <summary>
    /// Adds Qdrant vector store with hybrid search capability and dynamic dimension adaptation.
    /// </summary>
    public static IServiceCollection AddQdrantWithHybridSearch(
        this IServiceCollection services,
        string host,
        int port,
        string baseCollectionName,
        string? bm25PersistencePath = null)
    {
        return services.AddQdrantWithHybridSearch(options =>
        {
            options.Host = host;
            options.GrpcPort = port;
            options.BaseCollectionName = baseCollectionName;
            options.NamingStrategy = CollectionNamingStrategy.DimensionSuffix;
        }, bm25PersistencePath);
    }

    /// <summary>
    /// Adds Qdrant vector store with hybrid search capability using basic connection parameters.
    /// Uses Fixed naming strategy for backward compatibility.
    /// </summary>
    public static IServiceCollection AddQdrantWithHybridSearch(
        this IServiceCollection services,
        string host = "localhost",
        int port = 6334,
        string collectionName = "fluxindex_chunks",
        int vectorSize = 1536,
        string? bm25PersistencePath = null)
    {
        return services.AddQdrantWithHybridSearch(options =>
        {
            options.Host = host;
            options.GrpcPort = port;
            options.BaseCollectionName = collectionName;
            options.VectorSize = vectorSize;
            options.NamingStrategy = CollectionNamingStrategy.Fixed;
        }, bm25PersistencePath);
    }

    /// <summary>
    /// Adds Qdrant as a specialized vector provider.
    /// Registers IStorageProvider for auto-detection by StorageOrchestrator.
    /// Qdrant takes priority over general-purpose providers for vector operations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddQdrantProvider(this IServiceCollection services)
    {
        services.AddSingleton<IStorageProvider>(sp =>
        {
            var vectorStore = sp.GetRequiredService<QdrantVectorStore>();
            var logger = sp.GetService<ILogger<QdrantProvider>>();
            return new QdrantProvider(vectorStore, logger);
        });

        return services;
    }
}
