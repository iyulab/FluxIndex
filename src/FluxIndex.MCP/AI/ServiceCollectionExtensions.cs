using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.MCP.AI;

/// <summary>
/// Simple DI extensions for LMSupply services in MCP.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds LMSupply embedding service to MCP.
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
