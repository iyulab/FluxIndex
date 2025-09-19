using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FluxIndex.Extensions.WebFlux.Models;
using FluxIndex.Extensions.WebFlux.Services;

namespace FluxIndex.Extensions.WebFlux;

/// <summary>
/// WebFlux integration extensions for FluxIndex
/// </summary>
public static class WebFluxServiceExtensions
{
    /// <summary>
    /// Add WebFlux document processing services to FluxIndex
    /// </summary>
    public static IServiceCollection AddWebFluxIntegration(this IServiceCollection services)
    {
        // Add HTTP client for web requests
        services.AddHttpClient();

        // Add WebFlux integration services
        services.TryAddScoped<IWebContentProcessor, SimpleWebContentProcessor>();
        services.TryAddScoped<IDocumentProcessor, WebFluxDocumentProcessor>();

        return services;
    }

    /// <summary>
    /// Add WebFlux with OpenAI services for FluxIndex
    /// </summary>
    public static IServiceCollection AddWebFluxWithOpenAI(this IServiceCollection services)
    {
        // Add basic WebFlux integration
        services.AddWebFluxIntegration();

        // Note: OpenAI services would be added here when WebFlux package is properly integrated
        // For now, use basic implementation

        return services;
    }

    /// <summary>
    /// Add WebFlux with mock AI services for testing
    /// </summary>
    public static IServiceCollection AddWebFluxWithMockAI(this IServiceCollection services)
    {
        // Add basic WebFlux integration with mock services
        services.AddWebFluxIntegration();

        // Add mock AI services
        // services.AddScoped<ITextCompletionService, MockTextCompletionService>();

        return services;
    }
}