using FluxIndex.SDK.AI.Local.Services;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FluxIndex.SDK.AI.Local;

/// <summary>
/// Extension methods for registering LMSupply services with dependency injection
/// </summary>
public static class ServiceCollectionExtensions
{
    #region Embedding Services

    /// <summary>
    /// Adds LMSupply embedding service to the service collection.
    /// Uses local ONNX-based models for offline embedding generation.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddLMSupplyEmbedding(
        this IServiceCollection services,
        Action<LMSupplyEmbeddingOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.TryAddSingleton(Options.Create(new LMSupplyEmbeddingOptions()));
        }

        services.AddMemoryCache();
        services.AddSingleton<LMSupplyEmbeddingService>();
        services.AddSingleton<IEmbeddingService>(sp => sp.GetRequiredService<LMSupplyEmbeddingService>());

        return services;
    }

    /// <summary>
    /// Adds LMSupply embedding with a specific model alias.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="modelId">Model alias (default, fast, quality, large, multilingual) or HuggingFace ID</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddLMSupplyEmbedding(
        this IServiceCollection services,
        string modelId)
    {
        return services.AddLMSupplyEmbedding(options => options.ModelId = modelId);
    }

    /// <summary>
    /// Adds LMSupply embedding with warmup during startup.
    /// </summary>
    public static IServiceCollection AddLMSupplyEmbeddingWithWarmup(
        this IServiceCollection services,
        Action<LMSupplyEmbeddingOptions>? configureOptions = null)
    {
        var wrappedConfigure = (LMSupplyEmbeddingOptions opts) =>
        {
            configureOptions?.Invoke(opts);
            opts.WarmupOnStartup = true;
        };

        services.AddLMSupplyEmbedding(wrappedConfigure);
        services.AddHostedService<LMSupplyEmbeddingWarmupService>();

        return services;
    }

    #endregion

    #region Reranker Services

    /// <summary>
    /// Adds LMSupply reranker service.
    /// Provides cross-encoder based semantic reranking using local ONNX models.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// This registration will fail if the model cannot be downloaded.
    /// For resilient fallback behavior, use <see cref="AddResilientLMSupplyReranker"/> instead.
    /// </remarks>
    public static IServiceCollection AddLMSupplyReranker(
        this IServiceCollection services,
        Action<LMSupplyRerankerOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.TryAddSingleton(Options.Create(new LMSupplyRerankerOptions()));
        }

        services.AddSingleton<LMSupplyRerankerAdapter>();
        services.AddSingleton<IReranker>(sp => sp.GetRequiredService<LMSupplyRerankerAdapter>());

        return services;
    }

    /// <summary>
    /// Adds LMSupply reranker with warmup during startup.
    /// </summary>
    public static IServiceCollection AddLMSupplyRerankerWithWarmup(
        this IServiceCollection services,
        Action<LMSupplyRerankerOptions>? configureOptions = null)
    {
        var wrappedConfigure = (LMSupplyRerankerOptions opts) =>
        {
            configureOptions?.Invoke(opts);
            opts.WarmupOnStartup = true;
        };

        services.AddLMSupplyReranker(wrappedConfigure);
        services.AddHostedService<LMSupplyRerankerWarmupService>();

        return services;
    }

    /// <summary>
    /// Adds resilient LMSupply reranker with automatic fallback to algorithmic reranking.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// Provides graceful degradation when the semantic model is unavailable:
    /// - Primary: Cross-encoder semantic reranking (high quality)
    /// - Fallback: TF-IDF/BM25 algorithmic reranking (lower quality, always available)
    /// </remarks>
    public static IServiceCollection AddResilientLMSupplyReranker(
        this IServiceCollection services,
        Action<LMSupplyRerankerOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.TryAddSingleton(Options.Create(new LMSupplyRerankerOptions()));
        }

        services.AddSingleton<ResilientLMSupplyReranker>();
        services.AddSingleton<IReranker>(sp => sp.GetRequiredService<ResilientLMSupplyReranker>());

        return services;
    }

    /// <summary>
    /// Adds resilient LMSupply reranker with warmup and automatic fallback.
    /// </summary>
    public static IServiceCollection AddResilientLMSupplyRerankerWithWarmup(
        this IServiceCollection services,
        Action<LMSupplyRerankerOptions>? configureOptions = null)
    {
        var wrappedConfigure = (LMSupplyRerankerOptions opts) =>
        {
            configureOptions?.Invoke(opts);
            opts.WarmupOnStartup = true;
        };

        return services.AddResilientLMSupplyReranker(wrappedConfigure);
    }

    #endregion

    #region Text Completion Services

    /// <summary>
    /// Adds LMSupply text completion (generator) service to the service collection.
    /// Uses local ONNX-based models for offline text generation.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddLMSupplyTextCompletion(
        this IServiceCollection services,
        Action<LMSupplyTextCompletionOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.TryAddSingleton(Options.Create(new LMSupplyTextCompletionOptions()));
        }

        services.AddSingleton<LMSupplyTextCompletionService>();
        services.AddSingleton<ITextCompletionService>(sp => sp.GetRequiredService<LMSupplyTextCompletionService>());

        return services;
    }

    /// <summary>
    /// Adds LMSupply text completion with a specific model.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="modelId">Model alias (default, fast, quality, large) or HuggingFace ID</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddLMSupplyTextCompletion(
        this IServiceCollection services,
        string modelId)
    {
        return services.AddLMSupplyTextCompletion(options => options.ModelId = modelId);
    }

    #endregion

    #region Combined Services

    /// <summary>
    /// Adds all LMSupply services: Embedding, Text Completion, and Reranker.
    /// This is the recommended way to enable full AI capabilities without external API keys.
    /// Individual services can be overridden after calling this method.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Optional configuration for all LMSupply options</param>
    /// <returns>Service collection for chaining</returns>
    /// <example>
    /// // Enable all LMSupply services with defaults
    /// services.AddLMSupply();
    ///
    /// // With custom configuration
    /// services.AddLMSupply(options => {
    ///     options.EmbeddingModelId = "multilingual";
    ///     options.TextCompletionModelId = "quality";
    /// });
    ///
    /// // Override specific service after AddLMSupply
    /// services.AddLMSupply();
    /// services.AddSingleton&lt;IEmbeddingService&gt;(myCustomEmbedding);
    /// </example>
    public static IServiceCollection AddLMSupply(
        this IServiceCollection services,
        Action<UnifiedLMSupplyOptions>? configure = null)
    {
        var options = new UnifiedLMSupplyOptions();
        configure?.Invoke(options);

        // Embedding (default: bge-small-en-v1.5)
        services.AddLMSupplyEmbedding(opts =>
        {
            opts.ModelId = options.EmbeddingModelId ?? "default";
        });

        // Text Completion (default: Qwen2.5-0.5B)
        services.AddLMSupplyTextCompletion(opts =>
        {
            opts.ModelId = options.TextCompletionModelId ?? "default";
        });

        // Reranker with fallback (default: cross-encoder)
        services.AddResilientLMSupplyReranker(opts =>
        {
            opts.ModelId = options.RerankerModelId ?? "default";
        });

        return services;
    }

    /// <summary>
    /// Adds all LMSupply services with model warmup during startup.
    /// Models are loaded immediately to reduce first-request latency.
    /// </summary>
    public static IServiceCollection AddLMSupplyWithWarmup(
        this IServiceCollection services,
        Action<UnifiedLMSupplyOptions>? configure = null)
    {
        var options = new UnifiedLMSupplyOptions();
        configure?.Invoke(options);

        services.AddLMSupplyEmbeddingWithWarmup(opts =>
        {
            opts.ModelId = options.EmbeddingModelId ?? "default";
        });

        services.AddLMSupplyTextCompletion(opts =>
        {
            opts.ModelId = options.TextCompletionModelId ?? "default";
        });

        services.AddResilientLMSupplyRerankerWithWarmup(opts =>
        {
            opts.ModelId = options.RerankerModelId ?? "default";
        });

        return services;
    }

    #endregion
}

/// <summary>
/// Combined options for all LMSupply services in FluxIndex.
/// Named UnifiedLMSupplyOptions to avoid collision with FileFlux.Infrastructure.Services.LMSupply.LMSupplyOptions.
/// </summary>
public class UnifiedLMSupplyOptions
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
