using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Providers.OpenAI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Providers.OpenAI.Extensions;

/// <summary>
/// Extension methods for registering OpenAI-compatible embedding and reranking services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IEmbeddingService"/> backed by an OpenAI-compatible embedding endpoint.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="endpoint">Base API URL (e.g., "https://api.openai.com/v1").</param>
    /// <param name="apiKey">API key for authentication. Null for unauthenticated endpoints.</param>
    /// <param name="model">Embedding model name.</param>
    /// <param name="dimension">Expected embedding vector dimension.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOpenAICompatibleEmbedding(
        this IServiceCollection services,
        string endpoint, string? apiKey, string model, int dimension)
    {
        services.AddSingleton<IEmbeddingService>(sp =>
            new OpenAICompatibleEmbeddingService(
                endpoint, apiKey, model, dimension,
                sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger<OpenAICompatibleEmbeddingService>()));
        return services;
    }

    /// <summary>
    /// Registers an <see cref="IReranker"/> backed by an OpenAI-compatible rerank endpoint.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="endpoint">Base API URL (e.g., "https://api.openai.com/v1").</param>
    /// <param name="apiKey">API key for authentication. Null for unauthenticated endpoints.</param>
    /// <param name="model">Rerank model name.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOpenAICompatibleReranker(
        this IServiceCollection services,
        string endpoint, string? apiKey, string model)
    {
        services.AddSingleton<IReranker>(sp =>
            new OpenAICompatibleRerankerService(
                endpoint, apiKey, model,
                sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger<OpenAICompatibleRerankerService>()));
        return services;
    }
}
