using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FluxIndex.AI.LocalReranker;

/// <summary>
/// Extension methods for registering LocalReranker services with dependency injection
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds LocalReranker cross-encoder based semantic reranking service.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// LocalReranker is registered as a singleton because:
    /// 1. Model loading is expensive (cold start ~3s)
    /// 2. The reranker is thread-safe
    /// 3. Memory usage is ~200MB per instance
    ///
    /// Note: This registration will fail if the model cannot be downloaded.
    /// For resilient fallback behavior, use <see cref="AddResilientLocalReranker"/> instead.
    /// </remarks>
    public static IServiceCollection AddLocalReranker(
        this IServiceCollection services,
        Action<LocalRerankerOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.TryAddSingleton(Options.Create(new LocalRerankerOptions()));
        }

        // Register adapter as singleton (thread-safe, expensive initialization)
        services.AddSingleton<LocalRerankerAdapter>();
        services.AddSingleton<IReranker>(sp => sp.GetRequiredService<LocalRerankerAdapter>());

        return services;
    }

    /// <summary>
    /// Adds LocalReranker with automatic warmup during service registration.
    /// Use this for production scenarios to avoid cold start latency on first request.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// Note: This registration will fail if the model cannot be downloaded.
    /// For resilient fallback behavior, use <see cref="AddResilientLocalRerankerWithWarmup"/> instead.
    /// </remarks>
    public static IServiceCollection AddLocalRerankerWithWarmup(
        this IServiceCollection services,
        Action<LocalRerankerOptions>? configureOptions = null)
    {
        services.AddLocalReranker(configureOptions);

        // Add hosted service for warmup
        services.AddHostedService<LocalRerankerWarmupService>();

        return services;
    }

    /// <summary>
    /// Adds resilient LocalReranker with automatic fallback to algorithmic reranking.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// This provides graceful degradation when the semantic model is unavailable:
    /// - Primary: Cross-encoder semantic reranking (high quality)
    /// - Fallback: TF-IDF/BM25 algorithmic reranking (lower quality, always available)
    ///
    /// Fallback is triggered when:
    /// - Model download fails (network issues)
    /// - Model loading fails (disk/memory issues)
    /// - Runtime inference fails (unexpected errors)
    ///
    /// Use this for production scenarios where availability is more important than
    /// guaranteed semantic quality.
    /// </remarks>
    public static IServiceCollection AddResilientLocalReranker(
        this IServiceCollection services,
        Action<LocalRerankerOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.TryAddSingleton(Options.Create(new LocalRerankerOptions()));
        }

        // Register resilient adapter as singleton
        services.AddSingleton<ResilientRerankerAdapter>();
        services.AddSingleton<IReranker>(sp => sp.GetRequiredService<ResilientRerankerAdapter>());

        return services;
    }

    /// <summary>
    /// Adds resilient LocalReranker with warmup and automatic fallback.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// Combines resilient fallback with eager model loading.
    /// If model download fails during warmup, gracefully falls back to algorithmic reranking.
    /// </remarks>
    public static IServiceCollection AddResilientLocalRerankerWithWarmup(
        this IServiceCollection services,
        Action<LocalRerankerOptions>? configureOptions = null)
    {
        // Ensure warmup is enabled in options
        var wrappedConfigure = (LocalRerankerOptions opts) =>
        {
            configureOptions?.Invoke(opts);
            opts.WarmupOnStartup = true;
        };

        services.AddResilientLocalReranker(wrappedConfigure);

        return services;
    }
}
