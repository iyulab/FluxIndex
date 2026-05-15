using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.CLI.AI;

/// <summary>
/// Simple DI extensions for LMSupply services in CLI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds LMSupply embedding service to CLI.
    /// </summary>
    public static IServiceCollection AddLMSupplyEmbedding(
        this IServiceCollection services,
        string modelId = "default")
    {
        services.AddSingleton<IEmbeddingService>(sp =>
            LMSupplyEmbedder.CreateAsync(modelId).GetAwaiter().GetResult());
        return services;
    }

    /// <summary>
    /// Adds LMSupply text completion service to CLI.
    /// </summary>
    public static IServiceCollection AddLMSupplyTextCompletion(
        this IServiceCollection services,
        string modelId = "default")
    {
        services.AddSingleton<ITextCompletionService>(sp =>
            LMSupplyGenerator.CreateAsync(modelId).GetAwaiter().GetResult());
        return services;
    }
}
