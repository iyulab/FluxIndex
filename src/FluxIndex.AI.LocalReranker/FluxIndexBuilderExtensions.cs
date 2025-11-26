using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.AI.LocalReranker;

/// <summary>
/// Extension methods for integrating LocalReranker with FluxIndexContextBuilder
/// </summary>
public static class FluxIndexBuilderExtensions
{
    /// <summary>
    /// Adds LocalReranker cross-encoder based semantic reranking to FluxIndex.
    /// </summary>
    /// <param name="builder">The FluxIndexContext builder</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Builder for chaining</returns>
    /// <remarks>
    /// Note: This will fail if the model cannot be downloaded.
    /// For resilient fallback behavior, use <see cref="UseResilientLocalReranker"/> instead.
    /// </remarks>
    /// <example>
    /// <code>
    /// var context = FluxIndexContext.CreateBuilder()
    ///     .UseSQLite("fluxindex.db")
    ///     .UseOpenAI(apiKey, "text-embedding-3-small")
    ///     .UseLocalReranker(options => {
    ///         options.ModelId = "quality";
    ///         options.UseGpu = true;
    ///     })
    ///     .Build();
    /// </code>
    /// </example>
    public static FluxIndexContextBuilder UseLocalReranker(
        this FluxIndexContextBuilder builder,
        Action<LocalRerankerOptions>? configureOptions = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLocalReranker(configureOptions);
        });

        return builder;
    }

    /// <summary>
    /// Adds LocalReranker with automatic warmup during application startup.
    /// Use this for production scenarios to avoid cold start latency.
    /// </summary>
    /// <param name="builder">The FluxIndexContext builder</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Builder for chaining</returns>
    /// <remarks>
    /// Note: This will fail if the model cannot be downloaded.
    /// For resilient fallback behavior, use <see cref="UseResilientLocalRerankerWithWarmup"/> instead.
    /// </remarks>
    public static FluxIndexContextBuilder UseLocalRerankerWithWarmup(
        this FluxIndexContextBuilder builder,
        Action<LocalRerankerOptions>? configureOptions = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddLocalRerankerWithWarmup(configureOptions);
        });

        return builder;
    }

    /// <summary>
    /// Adds resilient LocalReranker with automatic fallback to algorithmic reranking.
    /// </summary>
    /// <param name="builder">The FluxIndexContext builder</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Builder for chaining</returns>
    /// <remarks>
    /// Provides graceful degradation when the semantic model is unavailable:
    /// - Primary: Cross-encoder semantic reranking (high quality)
    /// - Fallback: TF-IDF/BM25 algorithmic reranking (lower quality, always available)
    ///
    /// Recommended for production scenarios where availability is more important
    /// than guaranteed semantic quality.
    /// </remarks>
    /// <example>
    /// <code>
    /// var context = FluxIndexContext.CreateBuilder()
    ///     .UseSQLite("fluxindex.db")
    ///     .UseOpenAI(apiKey, "text-embedding-3-small")
    ///     .UseResilientLocalReranker(options => {
    ///         options.ModelId = "quality";
    ///     })
    ///     .Build();
    /// </code>
    /// </example>
    public static FluxIndexContextBuilder UseResilientLocalReranker(
        this FluxIndexContextBuilder builder,
        Action<LocalRerankerOptions>? configureOptions = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddResilientLocalReranker(configureOptions);
        });

        return builder;
    }

    /// <summary>
    /// Adds resilient LocalReranker with warmup and automatic fallback.
    /// </summary>
    /// <param name="builder">The FluxIndexContext builder</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Builder for chaining</returns>
    /// <remarks>
    /// Combines resilient fallback with eager model loading.
    /// If model download fails during warmup, gracefully falls back to algorithmic reranking.
    /// </remarks>
    public static FluxIndexContextBuilder UseResilientLocalRerankerWithWarmup(
        this FluxIndexContextBuilder builder,
        Action<LocalRerankerOptions>? configureOptions = null)
    {
        builder.ConfigureServices(services =>
        {
            services.AddResilientLocalRerankerWithWarmup(configureOptions);
        });

        return builder;
    }
}
