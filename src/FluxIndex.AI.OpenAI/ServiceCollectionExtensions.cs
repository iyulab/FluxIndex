using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.AI.OpenAI;

/// <summary>
/// Extension methods for registering OpenAI services with dependency injection
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds OpenAI embedding service to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddOpenAIEmbedding(
        this IServiceCollection services,
        Action<OpenAIOptions> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);

        // Register embedding service
        services.AddSingleton<IEmbeddingService, OpenAIEmbeddingService>();

        // Add memory cache for embedding caching (if not already registered)
        services.AddMemoryCache();

        return services;
    }

    /// <summary>
    /// Adds Azure OpenAI embedding service to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAzureOpenAIEmbedding(
        this IServiceCollection services,
        Action<OpenAIOptions> configureOptions)
    {
        return services.AddOpenAIEmbedding(configureOptions);
    }
}