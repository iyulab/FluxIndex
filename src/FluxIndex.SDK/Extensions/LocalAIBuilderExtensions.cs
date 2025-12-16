using FluxIndex.SDK.AI.Local;
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
    /// Enable all LocalAI services: Embedding, Text Completion, and Reranker.
    /// This is the recommended way to enable full AI capabilities without external API keys.
    /// Individual services can be overridden afterward using ConfigureServices().
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Optional configuration for all LocalAI models</param>
    /// <returns>Builder for chaining</returns>
    /// <example>
    /// // Enable all LocalAI services with defaults
    /// var context = FluxIndexContext.CreateBuilder()
    ///     .UseSQLite("data.db")
    ///     .UseLocalAI()
    ///     .Build();
    ///
    /// // With custom models
    /// var context = FluxIndexContext.CreateBuilder()
    ///     .UseSQLite("data.db")
    ///     .UseLocalAI(options => {
    ///         options.EmbeddingModelId = "multilingual";
    ///         options.TextCompletionModelId = "quality";
    ///     })
    ///     .Build();
    ///
    /// // Override specific service after UseLocalAI
    /// var context = FluxIndexContext.CreateBuilder()
    ///     .UseSQLite("data.db")
    ///     .UseLocalAI()
    ///     .ConfigureServices(s => s.AddSingleton&lt;IEmbeddingService&gt;(myCustomEmbedding))
    ///     .Build();
    /// </example>
    public static FluxIndexContextBuilder UseLocalAI(
        this FluxIndexContextBuilder builder,
        Action<LocalAIOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLocalAI(configure);
        });

        return builder;
    }

    /// <summary>
    /// Enable all LocalAI services with model warmup during startup.
    /// Models are loaded immediately to reduce first-request latency.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Optional configuration for all LocalAI models</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseLocalAIWithWarmup(
        this FluxIndexContextBuilder builder,
        Action<LocalAIOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLocalAIWithWarmup(configure);
        });

        return builder;
    }

    /// <summary>
    /// Uses LocalAI text completion service for HyDE, metadata enrichment, etc.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseLocalAITextCompletion(
        this FluxIndexContextBuilder builder,
        Action<LocalAITextCompletionOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLocalAITextCompletion(configure);
        });

        return builder;
    }
}
