using FluxIndex.Extensions.FileVault.Adapters;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using FluxIndex.Extensions.FileVault.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FluxIndex.Extensions.FileVault.Extensions;

/// <summary>
/// Extension methods for registering FileVault services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds FileVault services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration action for FileVaultOptions.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFileVault(
        this IServiceCollection services,
        Action<FileVaultOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<FileVaultOptions>(_ => { });
        }

        // Register core services
        services.TryAddSingleton<IContentHasher, ContentHasher>();
        services.TryAddSingleton<IGitService, GitService>();
        services.TryAddSingleton<IVaultPipeline, VaultPipeline>();
        services.TryAddSingleton<IVault, VaultManager>();

        // Register storage service
        services.TryAddSingleton<IVaultStorageService, VaultStorageService>();

        // Register file watcher service
        services.TryAddSingleton<IFileWatcherService, FileWatcherService>();

        // Register queue service
        services.TryAddSingleton<IVaultQueueService, VaultQueueService>();

        return services;
    }

    /// <summary>
    /// Adds FileVault with background queue processing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration action for FileVaultOptions.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFileVaultWithBackgroundProcessing(
        this IServiceCollection services,
        Action<FileVaultOptions>? configureOptions = null)
    {
        services.AddFileVault(options =>
        {
            options.EnableBackgroundProcessing = true;
            configureOptions?.Invoke(options);
        });

        // Register the background service
        services.AddHostedService<VaultBackgroundService>();

        return services;
    }

    /// <summary>
    /// Adds FileVault with custom extractor/chunker/memorizer integration.
    /// Use this when integrating with FileFlux or other processing libraries.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration action for FileVaultOptions.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Before calling this method, register your implementations:
    /// - IExtractor: For content extraction from source files
    /// - IChunker: For content chunking
    /// - IMemorizer: For chunk indexing/memorization
    /// </remarks>
    public static IServiceCollection AddFileVaultWithPipeline(
        this IServiceCollection services,
        Action<FileVaultOptions>? configureOptions = null)
    {
        return services.AddFileVault(configureOptions);
    }

    /// <summary>
    /// Uses a custom content hasher implementation.
    /// </summary>
    /// <typeparam name="THasher">The hasher implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection UseFileVaultHasher<THasher>(this IServiceCollection services)
        where THasher : class, IContentHasher
    {
        services.RemoveAll<IContentHasher>();
        services.AddSingleton<IContentHasher, THasher>();
        return services;
    }

    /// <summary>
    /// Uses a custom Git service implementation.
    /// </summary>
    /// <typeparam name="TGitService">The Git service implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection UseFileVaultGitService<TGitService>(this IServiceCollection services)
        where TGitService : class, IGitService
    {
        services.RemoveAll<IGitService>();
        services.AddSingleton<IGitService, TGitService>();
        return services;
    }

    /// <summary>
    /// Uses a custom vault pipeline implementation.
    /// </summary>
    /// <typeparam name="TPipeline">The pipeline implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection UseFileVaultPipeline<TPipeline>(this IServiceCollection services)
        where TPipeline : class, IVaultPipeline
    {
        services.RemoveAll<IVaultPipeline>();
        services.AddSingleton<IVaultPipeline, TPipeline>();
        return services;
    }

    /// <summary>
    /// Adds FileVault with FileFlux integration for document extraction and chunking.
    /// Requires FileFlux's IDocumentProcessorFactory to be registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration action for FileVaultOptions.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Prerequisites:
    /// - FileFlux services must be registered (IDocumentProcessorFactory)
    /// - For memorization, FluxIndex services must be registered (IVectorStore, IEmbeddingService)
    /// </remarks>
    public static IServiceCollection AddFileVaultWithFileFlux(
        this IServiceCollection services,
        Action<FileVaultOptions>? configureOptions = null)
    {
        services.AddFileVault(configureOptions);

        // Register FileFlux adapters
        services.TryAddSingleton<IExtractor, FileFluxExtractor>();
        services.TryAddSingleton<IChunker, FileFluxChunker>();

        return services;
    }

    /// <summary>
    /// Adds FileVault with full FluxIndex integration for extraction, chunking, and memorization.
    /// Requires both FileFlux and FluxIndex services to be registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration action for FileVaultOptions.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Prerequisites:
    /// - FileFlux services: IDocumentProcessorFactory
    /// - FluxIndex services: IVectorStore, IEmbeddingService
    /// </remarks>
    public static IServiceCollection AddFileVaultWithFluxIndex(
        this IServiceCollection services,
        Action<FileVaultOptions>? configureOptions = null)
    {
        services.AddFileVaultWithFileFlux(configureOptions);

        // Register FluxIndex memorizer adapter (for custom scenarios, VaultPipeline handles indexing internally)
        services.TryAddSingleton<FluxIndexMemorizer>();

        return services;
    }
}
