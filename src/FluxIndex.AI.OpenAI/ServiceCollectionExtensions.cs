using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Interfaces;
using FluxIndex.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.AI.OpenAI;

/// <summary>
/// Extension methods for registering OpenAI and OpenAI-compatible services with dependency injection
/// Supports: OpenAI, Azure OpenAI, GPUStack (v1/v2), and other OpenAI-compatible APIs
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

    #region GPUStack Support

    /// <summary>
    /// Adds GPUStack embedding service to the service collection
    /// GPUStack provides OpenAI-compatible APIs for self-hosted inference
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="endpoint">GPUStack server endpoint (e.g., http://localhost:80)</param>
    /// <param name="apiKey">GPUStack API key</param>
    /// <param name="modelName">Embedding model name deployed on GPUStack (e.g., "BAAI/bge-m3")</param>
    /// <param name="dimensions">Optional embedding dimensions</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddGPUStackEmbedding(
        this IServiceCollection services,
        string endpoint,
        string apiKey,
        string modelName,
        int? dimensions = null)
    {
        return services.AddOpenAIEmbedding(options =>
        {
            options.Endpoint = endpoint;
            options.ApiKey = apiKey;
            options.ModelName = modelName;
            options.Dimensions = dimensions;
            options.ProviderType = OpenAIProviderType.GPUStack;
        });
    }

    /// <summary>
    /// Adds GPUStack text completion service to the service collection
    /// GPUStack provides OpenAI-compatible APIs for self-hosted inference
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="endpoint">GPUStack server endpoint (e.g., http://localhost:80)</param>
    /// <param name="apiKey">GPUStack API key</param>
    /// <param name="modelName">Chat model name deployed on GPUStack (e.g., "Qwen/Qwen2.5-0.5B-Instruct")</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddGPUStackTextCompletion(
        this IServiceCollection services,
        string endpoint,
        string apiKey,
        string modelName)
    {
        return services.AddOpenAITextCompletion(options =>
        {
            options.Endpoint = endpoint;
            options.ApiKey = apiKey;
            options.ModelName = modelName;
            options.ProviderType = OpenAIProviderType.GPUStack;
        });
    }

    /// <summary>
    /// Adds all GPUStack services (embedding + text completion)
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="endpoint">GPUStack server endpoint</param>
    /// <param name="apiKey">GPUStack API key</param>
    /// <param name="embeddingModel">Embedding model name</param>
    /// <param name="chatModel">Chat completion model name</param>
    /// <param name="dimensions">Optional embedding dimensions</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddGPUStackServices(
        this IServiceCollection services,
        string endpoint,
        string apiKey,
        string embeddingModel,
        string chatModel,
        int? dimensions = null)
    {
        services.AddGPUStackEmbedding(endpoint, apiKey, embeddingModel, dimensions);
        services.AddGPUStackTextCompletion(endpoint, apiKey, chatModel);
        return services;
    }

    #endregion

    #region Generic OpenAI-Compatible Support

    /// <summary>
    /// Adds OpenAI-compatible embedding service to the service collection
    /// Use this for providers like Ollama, LM Studio, vLLM, etc.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="endpoint">API endpoint URL</param>
    /// <param name="apiKey">API key (may be optional for some providers)</param>
    /// <param name="modelName">Embedding model name</param>
    /// <param name="dimensions">Optional embedding dimensions</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddOpenAICompatibleEmbedding(
        this IServiceCollection services,
        string endpoint,
        string apiKey,
        string modelName,
        int? dimensions = null)
    {
        return services.AddOpenAIEmbedding(options =>
        {
            options.Endpoint = endpoint;
            options.ApiKey = apiKey;
            options.ModelName = modelName;
            options.Dimensions = dimensions;
            options.ProviderType = OpenAIProviderType.OpenAICompatible;
        });
    }

    /// <summary>
    /// Adds OpenAI-compatible text completion service to the service collection
    /// Use this for providers like Ollama, LM Studio, vLLM, etc.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="endpoint">API endpoint URL</param>
    /// <param name="apiKey">API key (may be optional for some providers)</param>
    /// <param name="modelName">Chat model name</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddOpenAICompatibleTextCompletion(
        this IServiceCollection services,
        string endpoint,
        string apiKey,
        string modelName)
    {
        return services.AddOpenAITextCompletion(options =>
        {
            options.Endpoint = endpoint;
            options.ApiKey = apiKey;
            options.ModelName = modelName;
            options.ProviderType = OpenAIProviderType.OpenAICompatible;
        });
    }

    #endregion
}