using FluxIndex.AI.LocalEmbedder.Services;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.AI.LocalEmbedder;

/// <summary>
/// Extension methods for registering LocalEmbedder services with dependency injection
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds LocalEmbedder embedding service to the service collection
    /// Uses local ONNX-based models for offline embedding generation
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddLocalEmbedder(
        this IServiceCollection services,
        Action<LocalEmbedderOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            // Use defaults
            services.Configure<LocalEmbedderOptions>(_ => { });
        }

        // Register embedding service as singleton (model is expensive to load)
        services.AddSingleton<IEmbeddingService, LocalEmbedderService>();

        // Add memory cache for embedding caching (if not already registered)
        services.AddMemoryCache();

        return services;
    }

    /// <summary>
    /// Adds LocalEmbedder with a specific model
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="modelId">Model identifier (e.g., "all-MiniLM-L6-v2", "bge-small-en-v1.5")</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddLocalEmbedder(
        this IServiceCollection services,
        string modelId)
    {
        return services.AddLocalEmbedder(options =>
        {
            options.ModelId = modelId;
        });
    }

    /// <summary>
    /// Adds LocalEmbedder with multilingual support
    /// Uses the multilingual-e5-small model (384 dimensions)
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddLocalEmbedderMultilingual(
        this IServiceCollection services)
    {
        return services.AddLocalEmbedder(options =>
        {
            options.ModelId = "multilingual-e5-small";
        });
    }

    /// <summary>
    /// Adds LocalEmbedder with GPU acceleration (CUDA)
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="modelId">Model identifier</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddLocalEmbedderWithCuda(
        this IServiceCollection services,
        string modelId = "all-MiniLM-L6-v2")
    {
        return services.AddLocalEmbedder(options =>
        {
            options.ModelId = modelId;
            options.ExecutionProvider = LocalEmbedderExecutionProvider.CUDA;
        });
    }

    /// <summary>
    /// Adds LocalEmbedder with DirectML GPU acceleration (Windows)
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="modelId">Model identifier</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddLocalEmbedderWithDirectML(
        this IServiceCollection services,
        string modelId = "all-MiniLM-L6-v2")
    {
        return services.AddLocalEmbedder(options =>
        {
            options.ModelId = modelId;
            options.ExecutionProvider = LocalEmbedderExecutionProvider.DirectML;
        });
    }
}
