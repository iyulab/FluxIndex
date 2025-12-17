using FluxIndex.SDK.AI.Local;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.SDK.Extensions;

/// <summary>
/// Extension methods for FluxIndexContextBuilder to configure LMSupply services.
/// </summary>
public static class LMSupplyBuilderExtensions
{
    /// <summary>
    /// Uses LMSupply embedding service with the specified model.
    /// Available models: default (bge-small), fast (MiniLM), quality (bge-base),
    /// large (nomic-embed), multilingual (e5-base), or any HuggingFace model ID.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="modelId">Model alias or HuggingFace ID (default: "default")</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseLMSupplyEmbeddingAdvanced(
        this FluxIndexContextBuilder builder,
        string modelId = "default")
    {
        builder.ConfigureServices(services =>
        {
            services.AddLMSupplyEmbedding(modelId);
        });

        return builder;
    }

    /// <summary>
    /// Uses LMSupply embedding service with advanced configuration.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseLMSupplyEmbeddingAdvanced(
        this FluxIndexContextBuilder builder,
        Action<LMSupplyEmbeddingOptions> configure)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLMSupplyEmbedding(configure);
        });

        return builder;
    }

    /// <summary>
    /// Uses LMSupply embedding with warmup during startup.
    /// Pre-loads the model to avoid cold start latency.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseLMSupplyEmbeddingWithWarmup(
        this FluxIndexContextBuilder builder,
        Action<LMSupplyEmbeddingOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLMSupplyEmbeddingWithWarmup(configure);
        });

        return builder;
    }

    /// <summary>
    /// Uses LMSupply reranker for semantic reranking.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseLMSupplyReranker(
        this FluxIndexContextBuilder builder,
        Action<LMSupplyRerankerOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLMSupplyReranker(configure);
        });

        return builder;
    }

    /// <summary>
    /// Uses resilient LMSupply reranker with automatic fallback.
    /// Falls back to algorithmic (TF-IDF/BM25) reranking if semantic model unavailable.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseResilientLMSupplyReranker(
        this FluxIndexContextBuilder builder,
        Action<LMSupplyRerankerOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddResilientLMSupplyReranker(configure);
        });

        return builder;
    }

    /// <summary>
    /// Uses resilient LMSupply reranker with warmup and automatic fallback.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseResilientLMSupplyRerankerWithWarmup(
        this FluxIndexContextBuilder builder,
        Action<LMSupplyRerankerOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddResilientLMSupplyRerankerWithWarmup(configure);
        });

        return builder;
    }

    /// <summary>
    /// Enable all LMSupply services: Embedding, Text Completion, and Reranker.
    /// This is the recommended way to enable full AI capabilities without external API keys.
    /// Individual services can be overridden afterward using ConfigureServices().
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Optional configuration for all LMSupply models</param>
    /// <returns>Builder for chaining</returns>
    /// <example>
    /// // Enable all LMSupply services with defaults
    /// var context = FluxIndexContext.CreateBuilder()
    ///     .UseSQLite("data.db")
    ///     .UseLMSupply()
    ///     .Build();
    ///
    /// // With custom models
    /// var context = FluxIndexContext.CreateBuilder()
    ///     .UseSQLite("data.db")
    ///     .UseLMSupply(options => {
    ///         options.EmbeddingModelId = "multilingual";
    ///         options.TextCompletionModelId = "quality";
    ///     })
    ///     .Build();
    ///
    /// // Override specific service after UseLMSupply
    /// var context = FluxIndexContext.CreateBuilder()
    ///     .UseSQLite("data.db")
    ///     .UseLMSupply()
    ///     .ConfigureServices(s => s.AddSingleton&lt;IEmbeddingService&gt;(myCustomEmbedding))
    ///     .Build();
    /// </example>
    public static FluxIndexContextBuilder UseLMSupply(
        this FluxIndexContextBuilder builder,
        Action<UnifiedLMSupplyOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLMSupply(configure);
        });

        return builder;
    }

    /// <summary>
    /// Enable all LMSupply services with model warmup during startup.
    /// Models are loaded immediately to reduce first-request latency.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Optional configuration for all LMSupply models</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseLMSupplyWithWarmup(
        this FluxIndexContextBuilder builder,
        Action<UnifiedLMSupplyOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLMSupplyWithWarmup(configure);
        });

        return builder;
    }

    /// <summary>
    /// Uses LMSupply text completion service for HyDE, metadata enrichment, etc.
    /// </summary>
    /// <param name="builder">The FluxIndex context builder</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseLMSupplyTextCompletion(
        this FluxIndexContextBuilder builder,
        Action<LMSupplyTextCompletionOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLMSupplyTextCompletion(configure);
        });

        return builder;
    }
}
