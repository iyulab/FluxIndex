using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Providers.LMSupply.Services;
using LMSupply.Embedder;
using LMSupply.Reranker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LMGenerator = LMSupply.Generator;

namespace FluxIndex.Providers.LMSupply.Extensions;

/// <summary>
/// Extension methods for registering LMSupply-based AI services with dependency injection.
/// All services use local ONNX inference — no API key required.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IEmbeddingService"/> backed by a local LMSupply ONNX embedding model.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="modelId">Embedding model ID (e.g., "all-MiniLM-L6-v2") or "default".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLMSupplyEmbedding(
        this IServiceCollection services,
        string modelId = "default")
    {
        services.AddSingleton<IEmbeddingService>(sp =>
            LMSupplyEmbeddingService.CreateAsync(modelId).GetAwaiter().GetResult());
        return services;
    }

    /// <summary>
    /// Registers an <see cref="IReranker"/> backed by a local LMSupply ONNX cross-encoder model.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="modelId">Reranker model ID (e.g., "ms-marco-MiniLM-L6-v2") or "default".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLMSupplyReranker(
        this IServiceCollection services,
        string modelId = "default")
    {
        services.AddSingleton<IReranker>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<LMSupplyRerankerService>();
            var model = LocalReranker.LoadAsync(modelId).GetAwaiter().GetResult();
            return new LMSupplyRerankerService(model, logger);
        });
        return services;
    }

    /// <summary>
    /// Registers an <see cref="ITextCompletionService"/> backed by a local LMSupply ONNX generator model.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="modelId">Generator model ID or "default".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLMSupplyTextCompletion(
        this IServiceCollection services,
        string modelId = "default")
    {
        services.AddSingleton<ITextCompletionService>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<LMSupplyTextCompletionService>();
            var generator = LMGenerator.LocalGenerator.LoadAsync(modelId).GetAwaiter().GetResult();
            return new LMSupplyTextCompletionService(generator, logger);
        });
        return services;
    }
}
