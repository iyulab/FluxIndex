using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.SDK.Tests.AI;

/// <summary>
/// Simple DI extensions for LMSupply services in tests.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds LMSupply embedding service for tests.
    /// </summary>
    public static IServiceCollection AddLMSupplyEmbedding(
        this IServiceCollection services,
        string modelId = "default")
    {
        services.AddSingleton<IEmbeddingService>(sp =>
            LMSupplyEmbedder.CreateAsync(modelId).GetAwaiter().GetResult());
        return services;
    }
}
