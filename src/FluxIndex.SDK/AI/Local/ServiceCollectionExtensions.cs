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
    /// Adds all LocalAI services: Embedding, Text Completion, and Reranker.
    /// This is the recommended way to enable full AI capabilities without external API keys.
    /// Individual services can be overridden after calling this method.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Optional configuration for all LocalAI options</param>
    /// <returns>Service collection for chaining</returns>
    /// <example>
    /// // Enable all LocalAI services with defaults
    /// services.AddLocalAI();
    ///
    /// // With custom configuration
    /// services.AddLocalAI(options => {
    ///     options.EmbeddingModelId = "multilingual";
    ///     options.TextCompletionModelId = "quality";
    /// });
    ///
    /// // Override specific service after AddLocalAI
    /// services.AddLocalAI();
    /// services.AddSingleton&lt;IEmbeddingService&gt;(myCustomEmbedding);
    /// </example>
    public static IServiceCollection AddLocalAI(
        this IServiceCollection services,
        Action<LocalAIOptions>? configure = null)
    {
        var options = new LocalAIOptions();
        configure?.Invoke(options);

        // Embedding (default: bge-small-en-v1.5)
        services.AddLocalAIEmbedding(opts =>
        {
            opts.ModelId = options.EmbeddingModelId ?? "default";
        });

        // Text Completion (default: Qwen2.5-0.5B)
        services.AddLocalAITextCompletion(opts =>
        {
            opts.ModelId = options.TextCompletionModelId ?? "default";
        });

        // Reranker with fallback (default: cross-encoder)
        services.AddResilientLocalAIReranker(opts =>
        {
            opts.ModelId = options.RerankerModelId ?? "default";
        });

        return services;
    }

    /// <summary>
    /// Adds all LocalAI services with model warmup during startup.
    /// Models are loaded immediately to reduce first-request latency.
    /// </summary>
    public static IServiceCollection AddLocalAIWithWarmup(
        this IServiceCollection services,
        Action<LocalAIOptions>? configure = null)
    {
        var options = new LocalAIOptions();
        configure?.Invoke(options);

        services.AddLocalAIEmbeddingWithWarmup(opts =>
        {
            opts.ModelId = options.EmbeddingModelId ?? "default";
        });

        services.AddLocalAITextCompletion(opts =>
        {
            opts.ModelId = options.TextCompletionModelId ?? "default";
        });

        services.AddResilientLocalAIRerankerWithWarmup(opts =>
        {
            opts.ModelId = options.RerankerModelId ?? "default";
        });

        return services;
    }

    #endregion
}

/// <summary>
/// Combined options for all LocalAI services.
/// </summary>
public class LocalAIOptions
{
    /// <summary>
    /// Embedding model ID. Available: default, fast, quality, large, multilingual, or HuggingFace ID.
    /// Default: "default" (bge-small-en-v1.5)
    /// </summary>
    public string? EmbeddingModelId { get; set; }

    /// <summary>
    /// Text completion model ID. Available: default, fast, quality, large, or HuggingFace ID.
    /// Default: "default" (Qwen2.5-0.5B)
    /// </summary>
    public string? TextCompletionModelId { get; set; }

    /// <summary>
    /// Reranker model ID. Default: "default" (ms-marco-MiniLM)
    /// </summary>
    public string? RerankerModelId { get; set; }
}
