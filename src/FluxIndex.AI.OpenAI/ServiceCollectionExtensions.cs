using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FluxIndex.AI.OpenAI;

/// <summary>
/// Extension methods for registering OpenAI services with dependency injection
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds OpenAI services to the service collection with configuration binding
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFluxIndexOpenAI(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration
        services.Configure<OpenAIConfiguration>(
            configuration.GetSection(OpenAIConfiguration.SectionName));

        // Register services
        services.AddSingleton<ITextCompletionService, OpenAITextCompletionService>();
        services.AddSingleton<IEmbeddingService, OpenAIEmbeddingService>();

        // Add memory cache for embedding caching (if not already registered)
        services.AddMemoryCache();

        return services;
    }

    /// <summary>
    /// Adds OpenAI services with explicit configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFluxIndexOpenAI(
        this IServiceCollection services,
        Action<OpenAIConfiguration> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);

        // Register services
        services.AddSingleton<ITextCompletionService, OpenAITextCompletionService>();
        services.AddSingleton<IEmbeddingService, OpenAIEmbeddingService>();

        // Add memory cache for embedding caching (if not already registered)
        services.AddMemoryCache();

        return services;
    }

    /// <summary>
    /// Adds only OpenAI text completion service
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFluxIndexOpenAITextCompletion(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OpenAIConfiguration>(
            configuration.GetSection(OpenAIConfiguration.SectionName));

        services.AddSingleton<ITextCompletionService, OpenAITextCompletionService>();

        return services;
    }

    /// <summary>
    /// Adds only OpenAI embedding service
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFluxIndexOpenAIEmbeddings(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OpenAIConfiguration>(
            configuration.GetSection(OpenAIConfiguration.SectionName));

        services.AddSingleton<IEmbeddingService, OpenAIEmbeddingService>();
        services.AddMemoryCache();

        return services;
    }

    /// <summary>
    /// Adds OpenAI services with fluent configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Fluent configuration builder</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFluxIndexOpenAI(
        this IServiceCollection services,
        Func<OpenAIConfigurationBuilder, OpenAIConfigurationBuilder> configure)
    {
        var builder = new OpenAIConfigurationBuilder();
        var configuration = configure(builder).Build();

        return services.AddFluxIndexOpenAI(options =>
        {
            options.ApiKey = configuration.ApiKey;
            options.OrganizationId = configuration.OrganizationId;
            options.BaseUrl = configuration.BaseUrl;
            options.TextCompletion = configuration.TextCompletion;
            options.Embedding = configuration.Embedding;
            options.TimeoutSeconds = configuration.TimeoutSeconds;
            options.MaxRetries = configuration.MaxRetries;
            options.EnableDetailedLogging = configuration.EnableDetailedLogging;
        });
    }

    /// <summary>
    /// Validates OpenAI configuration and tests connectivity
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static async Task<IServiceCollection> ValidateOpenAIConfigurationAsync(this IServiceCollection services)
    {
        using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetService<ILogger<OpenAIConfiguration>>();

        try
        {
            // Test text completion service
            var textService = serviceProvider.GetService<ITextCompletionService>();
            if (textService != null)
            {
                var isTextServiceHealthy = await textService.TestConnectionAsync();
                if (!isTextServiceHealthy)
                {
                    logger?.LogWarning("OpenAI text completion service connection test failed");
                }
                else
                {
                    logger?.LogInformation("OpenAI text completion service connection test passed");
                }
            }

            // Test embedding service
            var embeddingService = serviceProvider.GetService<IEmbeddingService>();
            if (embeddingService != null)
            {
                var isEmbeddingServiceHealthy = await embeddingService.TestConnectionAsync();
                if (!isEmbeddingServiceHealthy)
                {
                    logger?.LogWarning("OpenAI embedding service connection test failed");
                }
                else
                {
                    logger?.LogInformation("OpenAI embedding service connection test passed");
                    var dimensions = await embeddingService.GetEmbeddingDimensionsAsync();
                    logger?.LogInformation("OpenAI embedding model dimensions: {Dimensions}", dimensions);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "OpenAI configuration validation failed");
        }

        return services;
    }
}

/// <summary>
/// Fluent configuration builder for OpenAI options
/// </summary>
public class OpenAIConfigurationBuilder
{
    private readonly OpenAIConfiguration _configuration = new();

    public OpenAIConfigurationBuilder WithApiKey(string apiKey)
    {
        _configuration.ApiKey = apiKey;
        return this;
    }

    public OpenAIConfigurationBuilder WithOrganization(string organizationId)
    {
        _configuration.OrganizationId = organizationId;
        return this;
    }

    public OpenAIConfigurationBuilder WithBaseUrl(string baseUrl)
    {
        _configuration.BaseUrl = baseUrl;
        return this;
    }

    public OpenAIConfigurationBuilder WithAzureOpenAI(string endpoint, string apiKey)
    {
        _configuration.BaseUrl = endpoint;
        _configuration.ApiKey = apiKey;
        return this;
    }

    public OpenAIConfigurationBuilder WithTextModel(string model, int maxTokens = 500, float temperature = 0.7f)
    {
        _configuration.TextCompletion.Model = model;
        _configuration.TextCompletion.MaxTokens = maxTokens;
        _configuration.TextCompletion.Temperature = temperature;
        return this;
    }

    public OpenAIConfigurationBuilder WithEmbeddingModel(string model, int? dimensions = null, bool enableCaching = true)
    {
        _configuration.Embedding.Model = model;
        _configuration.Embedding.Dimensions = dimensions;
        _configuration.Embedding.EnableCaching = enableCaching;
        return this;
    }

    public OpenAIConfigurationBuilder WithTimeout(int timeoutSeconds)
    {
        _configuration.TimeoutSeconds = timeoutSeconds;
        return this;
    }

    public OpenAIConfigurationBuilder EnableDetailedLogging(bool enable = true)
    {
        _configuration.EnableDetailedLogging = enable;
        return this;
    }

    public OpenAIConfiguration Build() => _configuration;
}