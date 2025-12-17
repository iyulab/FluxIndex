using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

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
    /// Adds Qdrant vector store services with basic connection parameters.
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
            options.CollectionName = collectionName;
            options.VectorSize = vectorSize;
        });
    }

    /// <summary>
    /// Adds Qdrant Cloud vector store services.
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
            options.CollectionName = collectionName;
            options.VectorSize = vectorSize;
        });
    }
}
