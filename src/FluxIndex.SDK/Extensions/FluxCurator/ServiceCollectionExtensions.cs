using FluxCurator;
using FluxCurator.Core.Core;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK.Extensions.FluxCurator.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.SDK.Extensions.FluxCurator;

/// <summary>
/// Extension methods for registering FluxCurator integration services with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the FluxCurator embedding adapter that bridges FluxIndex's IEmbeddingService.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method requires FluxIndex's IEmbeddingService to be already registered.
    /// The adapter is registered as a singleton implementing FluxCurator's IEmbedder interface.
    /// </remarks>
    public static IServiceCollection AddFluxCuratorEmbeddingAdapter(this IServiceCollection services)
    {
        services.AddSingleton<IEmbedder>(provider =>
        {
            var embeddingService = provider.GetRequiredService<IEmbeddingService>();
            return new EmbeddingServiceAdapter(embeddingService);
        });

        return services;
    }

    /// <summary>
    /// Registers FluxCurator with FluxIndex's embedding service for semantic chunking support.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">Optional configuration for FluxCurator options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method requires FluxIndex's IEmbeddingService to be already registered.
    /// </para>
    /// <para>
    /// Registers:
    /// <list type="bullet">
    /// <item><description>EmbeddingServiceAdapter - bridges FluxIndex embedding to FluxCurator</description></item>
    /// <item><description>FluxCurator instance - configured with the embedding adapter</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddFluxIndexFluxCurator(
        this IServiceCollection services,
        Action<FluxCuratorOptions>? configure = null)
    {
        // Register the embedding adapter
        services.AddFluxCuratorEmbeddingAdapter();

        // Register FluxCurator with the embedding adapter
        services.AddSingleton(provider =>
        {
            var embedder = provider.GetRequiredService<IEmbedder>();
            var options = new FluxCuratorOptions();
            configure?.Invoke(options);

            var curator = global::FluxCurator.FluxCurator.Create();

            // Configure PII masking if enabled
            if (options.EnablePIIMasking)
            {
                if (options.PIIMaskingOptions is not null)
                    curator.WithPIIMasking(options.PIIMaskingOptions);
                else
                    curator.WithPIIMasking();
            }

            // Configure content filtering if enabled
            if (options.EnableContentFiltering)
            {
                if (options.ContentFilterOptions is not null)
                    curator.WithContentFiltering(options.ContentFilterOptions);
                else
                    curator.WithContentFiltering();
            }

            // Configure chunking options if provided
            if (options.DefaultChunkOptions != null)
            {
                curator.WithChunkingOptions(options.DefaultChunkOptions);
            }

            // Connect the FluxIndex embedding service
            curator.UseEmbedder(embedder);

            return curator.Build();
        });

        return services;
    }

    /// <summary>
    /// Registers FluxCurator without embedding support (rule-based chunking only).
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">Optional configuration for FluxCurator options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Use this method when you don't need semantic chunking and want to use
    /// rule-based chunking strategies (paragraph, sentence, token-based).
    /// </remarks>
    public static IServiceCollection AddFluxCuratorBasic(
        this IServiceCollection services,
        Action<FluxCuratorOptions>? configure = null)
    {
        services.AddSingleton(provider =>
        {
            var options = new FluxCuratorOptions();
            configure?.Invoke(options);

            var curator = global::FluxCurator.FluxCurator.Create();

            if (options.EnablePIIMasking)
            {
                if (options.PIIMaskingOptions is not null)
                    curator.WithPIIMasking(options.PIIMaskingOptions);
                else
                    curator.WithPIIMasking();
            }

            if (options.EnableContentFiltering)
            {
                if (options.ContentFilterOptions is not null)
                    curator.WithContentFiltering(options.ContentFilterOptions);
                else
                    curator.WithContentFiltering();
            }

            if (options.DefaultChunkOptions != null)
            {
                curator.WithChunkingOptions(options.DefaultChunkOptions);
            }

            return curator.Build();
        });

        return services;
    }
}
