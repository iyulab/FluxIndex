using FluxIndex.SDK.AI.Local.Services;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FluxIndex.SDK.AI.Local;

/// <summary>
/// Extension methods for registering LocalAI services with dependency injection
/// </summary>
public static class ServiceCollectionExtensions
{
    #region Embedding Services

    /// <summary>
    /// Adds LocalAI embedding service to the service collection.
    /// Uses local ONNX-based models for offline embedding generation.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddLocalAIEmbedding(
        this IServiceCollection services,
        Action<LocalAIEmbeddingOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.TryAddSingleton(Options.Create(new LocalAIEmbeddingOptions()));
        }

        services.AddMemoryCache();
        services.AddSingleton<LocalAIEmbeddingService>();
        services.AddSingleton<IEmbeddingService>(sp => sp.GetRequiredService<LocalAIEmbeddingService>());

        return services;
    }

    /// <summary>
    /// Adds LocalAI embedding with a specific model alias.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="modelId">Model alias (default, fast, quality, large, multilingual) or HuggingFace ID</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddLocalAIEmbedding(
        this IServiceCollection services,
        string modelId)
    {
        return services.AddLocalAIEmbedding(options => options.ModelId = modelId);
    }

    /// <summary>
    /// Adds LocalAI embedding with warmup during startup.
    /// </summary>
    public static IServiceCollection AddLocalAIEmbeddingWithWarmup(
        this IServiceCollection services,
        Action<LocalAIEmbeddingOptions>? configureOptions = null)
    {
        var wrappedConfigure = (LocalAIEmbeddingOptions opts) =>
        {
            configureOptions?.Invoke(opts);
            opts.WarmupOnStartup = true;
        };

        services.AddLocalAIEmbedding(wrappedConfigure);
        services.AddHostedService<LocalAIEmbeddingWarmupService>();

        return services;
    }

    #endregion

    #region Reranker Services

    /// <summary>
    /// Adds LocalAI reranker service.
    /// Provides cross-encoder based semantic reranking using local ONNX models.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// This registration will fail if the model cannot be downloaded.
    /// For resilient fallback behavior, use <see cref="AddResilientLocalAIReranker"/> instead.
    /// </remarks>
    public static IServiceCollection AddLocalAIReranker(
        this IServiceCollection services,
        Action<LocalAIRerankerOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.TryAddSingleton(Options.Create(new LocalAIRerankerOptions()));
        }

        services.AddSingleton<LocalAIRerankerAdapter>();
        services.AddSingleton<IReranker>(sp => sp.GetRequiredService<LocalAIRerankerAdapter>());

        return services;
    }

    /// <summary>
    /// Adds LocalAI reranker with warmup during startup.
    /// </summary>
    public static IServiceCollection AddLocalAIRerankerWithWarmup(
        this IServiceCollection services,
        Action<LocalAIRerankerOptions>? configureOptions = null)
    {
        var wrappedConfigure = (LocalAIRerankerOptions opts) =>
        {
            configureOptions?.Invoke(opts);
            opts.WarmupOnStartup = true;
        };

        services.AddLocalAIReranker(wrappedConfigure);
        services.AddHostedService<LocalAIRerankerWarmupService>();

        return services;
    }

    /// <summary>
    /// Adds resilient LocalAI reranker with automatic fallback to algorithmic reranking.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// Provides graceful degradation when the semantic model is unavailable:
    /// - Primary: Cross-encoder semantic reranking (high quality)
    /// - Fallback: TF-IDF/BM25 algorithmic reranking (lower quality, always available)
    /// </remarks>
    public static IServiceCollection AddResilientLocalAIReranker(
        this IServiceCollection services,
        Action<LocalAIRerankerOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.TryAddSingleton(Options.Create(new LocalAIRerankerOptions()));
        }

        services.AddSingleton<ResilientLocalAIReranker>();
        services.AddSingleton<IReranker>(sp => sp.GetRequiredService<ResilientLocalAIReranker>());

        return services;
    }

    /// <summary>
    /// Adds resilient LocalAI reranker with warmup and automatic fallback.
    /// </summary>
    public static IServiceCollection AddResilientLocalAIRerankerWithWarmup(
        this IServiceCollection services,
        Action<LocalAIRerankerOptions>? configureOptions = null)
    {
        var wrappedConfigure = (LocalAIRerankerOptions opts) =>
        {
            configureOptions?.Invoke(opts);
            opts.WarmupOnStartup = true;
        };

        return services.AddResilientLocalAIReranker(wrappedConfigure);
    }

    #endregion

    #region Text Completion Services

    /// <summary>
    /// Adds LocalAI text completion (generator) service to the service collection.
    /// Uses local ONNX-based models for offline text generation.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddLocalAITextCompletion(
        this IServiceCollection services,
        Action<LocalAITextCompletionOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.TryAddSingleton(Options.Create(new LocalAITextCompletionOptions()));
        }

        services.AddSingleton<LocalAITextCompletionService>();
        services.AddSingleton<ITextCompletionService>(sp => sp.GetRequiredService<LocalAITextCompletionService>());

        return services;
    }

    /// <summary>
    /// Adds LocalAI text completion with a specific model.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="modelId">Model alias (default, fast, quality, large) or HuggingFace ID</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddLocalAITextCompletion(
        this IServiceCollection services,
        string modelId)
    {
        return services.AddLocalAITextCompletion(options => options.ModelId = modelId);
    }

    #endregion

    #region Combined Services

    /// <summary>
    /// Adds both LocalAI embedding and resilient reranker services.
    /// Recommended for production scenarios.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureEmbedding">Embedding configuration</param>
    /// <param name="configureReranker">Reranker configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddLocalAI(
        this IServiceCollection services,
        Action<LocalAIEmbeddingOptions>? configureEmbedding = null,
        Action<LocalAIRerankerOptions>? configureReranker = null)
    {
        services.AddLocalAIEmbedding(configureEmbedding);
        services.AddResilientLocalAIReranker(configureReranker);

        return services;
    }

    /// <summary>
    /// Adds both LocalAI embedding and resilient reranker with warmup.
    /// </summary>
    public static IServiceCollection AddLocalAIWithWarmup(
        this IServiceCollection services,
        Action<LocalAIEmbeddingOptions>? configureEmbedding = null,
        Action<LocalAIRerankerOptions>? configureReranker = null)
    {
        services.AddLocalAIEmbeddingWithWarmup(configureEmbedding);
        services.AddResilientLocalAIRerankerWithWarmup(configureReranker);

        return services;
    }

    #endregion
}
