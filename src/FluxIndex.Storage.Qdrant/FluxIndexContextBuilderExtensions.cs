using FluxIndex.Core.Constants;
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Storage.Qdrant;

/// <summary>
/// Extension methods for FluxIndexContextBuilder to add Qdrant storage support.
/// Consumers must reference FluxIndex.Storage.Qdrant and call these methods.
/// </summary>
public static class FluxIndexContextBuilderExtensions
{
    /// <summary>
    /// Register Qdrant vector store services using the options already set on the builder
    /// (via UseQdrant(), UseQdrantFixed(), UseQdrantCloud(), etc.).
    /// </summary>
    public static FluxIndexContextBuilder AddQdrantStorage(this FluxIndexContextBuilder builder)
    {
        builder.RegisterStorageServices(services =>
        {
            var options = builder.Options;
            var vectorProvider = options.VectorStore.Provider?.ToLowerInvariant();

            if (vectorProvider != "qdrant")
                return;

            if (!string.IsNullOrEmpty(options.VectorStore.QdrantApiKey))
            {
                // Qdrant Cloud
                if (options.VectorStore.QdrantNamingStrategy != "Fixed")
                {
                    services.AddQdrantCloudVectorStore(
                        options.VectorStore.QdrantHost,
                        options.VectorStore.QdrantApiKey,
                        options.VectorStore.QdrantCollectionName);
                }
                else
                {
                    services.AddQdrantCloudVectorStore(
                        options.VectorStore.QdrantHost,
                        options.VectorStore.QdrantApiKey,
                        options.VectorStore.QdrantCollectionName,
                        options.VectorStore.QdrantVectorSize);
                }
            }
            else
            {
                // Local Qdrant
                if (options.VectorStore.QdrantNamingStrategy != "Fixed")
                {
                    services.AddQdrantVectorStore(
                        options.VectorStore.QdrantHost,
                        options.VectorStore.QdrantGrpcPort,
                        options.VectorStore.QdrantCollectionName);
                }
                else
                {
                    services.AddQdrantVectorStore(
                        options.VectorStore.QdrantHost,
                        options.VectorStore.QdrantGrpcPort,
                        options.VectorStore.QdrantCollectionName,
                        options.VectorStore.QdrantVectorSize);
                }
            }
        });

        return builder;
    }

    /// <summary>
    /// Register Qdrant vector store with explicit options configuration.
    /// </summary>
    public static FluxIndexContextBuilder AddQdrantStorage(
        this FluxIndexContextBuilder builder,
        Action<QdrantOptions> configure)
    {
        builder.RegisterStorageServices(services =>
        {
            services.AddQdrantVectorStore(configure);
        });

        // Set provider in options for downstream awareness
        builder.Options.VectorStore.Provider = "Qdrant";

        return builder;
    }
}
