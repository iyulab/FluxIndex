using FluxIndex.AI.Local;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.SDK.Extensions;

/// <summary>
/// Extension methods for FluxIndexContextBuilder to configure LocalAI services.
/// </summary>
public static class LocalAIBuilderExtensions
{
    /// <summary>
    /// Uses LocalAI embedding service with the specified model.
    /// Available models: default (bge-small), fast (MiniLM), quality (bge-base),
    /// large (nomic-embed), multilingual (e5-base), or any HuggingFace model ID.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="modelId">Model alias or HuggingFace ID (default: "default")</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseLocalAIEmbeddingAdvanced(
        this FluxIndexContextBuilder builder,
        string modelId = "default")
    {
        builder.ConfigureServices(services =>
        {
            services.AddLocalAIEmbedding(modelId);
        });

        return builder;
    }

    /// <summary>
    /// Uses LocalAI embedding service with advanced configuration.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseLocalAIEmbeddingAdvanced(
        this FluxIndexContextBuilder builder,
        Action<LocalAIEmbeddingOptions> configure)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLocalAIEmbedding(configure);
        });

        return builder;
    }

    /// <summary>
    /// Uses LocalAI embedding with warmup during startup.
    /// Pre-loads the model to avoid cold start latency.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseLocalAIEmbeddingWithWarmup(
        this FluxIndexContextBuilder builder,
        Action<LocalAIEmbeddingOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLocalAIEmbeddingWithWarmup(configure);
        });

        return builder;
    }

    /// <summary>
    /// Uses LocalAI reranker for semantic reranking.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseLocalAIReranker(
        this FluxIndexContextBuilder builder,
        Action<LocalAIRerankerOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLocalAIReranker(configure);
        });

        return builder;
    }

    /// <summary>
    /// Uses resilient LocalAI reranker with automatic fallback.
    /// Falls back to algorithmic (TF-IDF/BM25) reranking if semantic model unavailable.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseResilientLocalAIReranker(
        this FluxIndexContextBuilder builder,
        Action<LocalAIRerankerOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddResilientLocalAIReranker(configure);
        });

        return builder;
    }

    /// <summary>
    /// Uses resilient LocalAI reranker with warmup and automatic fallback.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseResilientLocalAIRerankerWithWarmup(
        this FluxIndexContextBuilder builder,
        Action<LocalAIRerankerOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddResilientLocalAIRerankerWithWarmup(configure);
        });

        return builder;
    }

    /// <summary>
    /// Uses both LocalAI embedding and resilient reranker services.
    /// Recommended for production scenarios requiring complete local AI stack.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configureEmbedding">Embedding configuration</param>
    /// <param name="configureReranker">Reranker configuration</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseLocalAI(
        this FluxIndexContextBuilder builder,
        Action<LocalAIEmbeddingOptions>? configureEmbedding = null,
        Action<LocalAIRerankerOptions>? configureReranker = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLocalAI(configureEmbedding, configureReranker);
        });

        return builder;
    }

    /// <summary>
    /// Uses both LocalAI embedding and resilient reranker with warmup.
    /// Recommended for production scenarios with predictable first-request latency.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configureEmbedding">Embedding configuration</param>
    /// <param name="configureReranker">Reranker configuration</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseLocalAIWithWarmup(
        this FluxIndexContextBuilder builder,
        Action<LocalAIEmbeddingOptions>? configureEmbedding = null,
        Action<LocalAIRerankerOptions>? configureReranker = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLocalAIWithWarmup(configureEmbedding, configureReranker);
        });

        return builder;
    }
}
