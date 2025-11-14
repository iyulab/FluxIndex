using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Interfaces;
using FluxIndex.Core.Services;
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

    /// <summary>
    /// Adds OpenAI metadata extraction services to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddOpenAIMetadataExtractor(
        this IServiceCollection services,
        Action<OpenAIOptions> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);

        // Register rule-based extractor (fallback)
        services.AddSingleton<IRuleBasedMetadataExtractor, RuleBasedMetadataExtractor>();

        // Register AI metadata extractor
        services.AddSingleton<IMetadataExtractor, OpenAIMetadataExtractor>();

        // Add memory cache for metadata caching (if not already registered)
        services.AddMemoryCache();

        return services;
    }

    /// <summary>
    /// Adds Azure OpenAI metadata extraction services to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAzureOpenAIMetadataExtractor(
        this IServiceCollection services,
        Action<OpenAIOptions> configureOptions)
    {
        return services.AddOpenAIMetadataExtractor(configureOptions);
    }

    /// <summary>
    /// Adds OpenAI text completion service to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddOpenAITextCompletion(
        this IServiceCollection services,
        Action<OpenAIOptions> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);

        // Register text completion service
        services.AddSingleton<ITextCompletionService, OpenAITextCompletionService>();

        // Add memory cache for completion caching (if not already registered)
        services.AddMemoryCache();

        return services;
    }

    /// <summary>
    /// Adds Azure OpenAI text completion service to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAzureOpenAITextCompletion(
        this IServiceCollection services,
        Action<OpenAIOptions> configureOptions)
    {
        return services.AddOpenAITextCompletion(configureOptions);
    }

    /// <summary>
    /// Adds complete OpenAI services (embedding + metadata extraction) to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddOpenAIServices(
        this IServiceCollection services,
        Action<OpenAIOptions> configureOptions)
    {
        services.AddOpenAIEmbedding(configureOptions);
        services.AddOpenAIMetadataExtractor(configureOptions);
        return services;
    }

    /// <summary>
    /// Adds complete OpenAI services including text completion (embedding + metadata extraction + text completion)
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAllOpenAIServices(
        this IServiceCollection services,
        Action<OpenAIOptions> configureOptions)
    {
        services.AddOpenAIEmbedding(configureOptions);
        services.AddOpenAIMetadataExtractor(configureOptions);
        services.AddOpenAITextCompletion(configureOptions);
        return services;
    }
}