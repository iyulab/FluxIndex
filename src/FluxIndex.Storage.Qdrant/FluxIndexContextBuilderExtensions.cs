using FluxIndex.Core.Constants;
using FluxIndex.SDK;
using FluxIndex.SDK.Configuration;
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

            // Every Qdrant setting on the builder reaches the store: a self-hosted Qdrant behind TLS (QdrantUseHttps) or
            // with an API key is configured the same way as Qdrant Cloud, whose builder methods set both.
            services.AddQdrantVectorStore(qdrant => ApplyBuilderOptions(qdrant, options.VectorStore));
        });

        return builder;
    }

    /// <summary>Copies the builder's Qdrant settings onto the store options.</summary>
    internal static void ApplyBuilderOptions(QdrantOptions qdrant, VectorStoreOptions store)
    {
        qdrant.Host = store.QdrantHost;
        qdrant.GrpcPort = store.QdrantGrpcPort;
        qdrant.ApiKey = string.IsNullOrEmpty(store.QdrantApiKey) ? null : store.QdrantApiKey;
        qdrant.UseHttps = store.QdrantUseHttps;
        qdrant.BaseCollectionName = store.QdrantCollectionName;
        if (store.QdrantNamingStrategy == "Fixed")
        {
            qdrant.NamingStrategy = CollectionNamingStrategy.Fixed;
            qdrant.VectorSize = store.QdrantVectorSize;
        }
        else
        {
            qdrant.NamingStrategy = CollectionNamingStrategy.ModelFingerprint;
        }
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
