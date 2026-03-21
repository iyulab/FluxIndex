using FileFlux;
using FluxIndex.Extensions.FileVault.Adapters;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using FluxIndex.Extensions.FileVault.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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

        // Register stateless utility services as Singleton
        services.TryAddSingleton<IContentHasher, ContentHasher>();
        services.TryAddSingleton<IGitService, GitService>();
        services.TryAddSingleton<IVaultStorageService, VaultStorageService>();

        // Register file watcher service as Singleton (stateful, one per app)
        services.TryAddSingleton<IFileWatcherService, FileWatcherService>();

        // Register queue service as Singleton (stateful, one per app)
        services.TryAddSingleton<IVaultQueueService, VaultQueueService>();

        // Register pipeline and vault as Scoped (they may depend on scoped services like IVectorStore)
        services.TryAddScoped<IVaultPipeline, VaultPipeline>();
        services.TryAddScoped<IVault, VaultManager>();

        // Background service is always registered.
        // EnableBackgroundProcessing option controls runtime behavior (idle no-op when false).
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, VaultBackgroundService>());

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
    /// Registers FileFlux core services automatically.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration action for FileVaultOptions.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// For memorization, FluxIndex services must be registered (IVectorStore, IEmbeddingService).
    /// </remarks>
    public static IServiceCollection AddFileVaultWithFileFlux(
        this IServiceCollection services,
        Action<FileVaultOptions>? configureOptions = null)
    {
        services.AddFileFlux();
        services.AddFileVault(configureOptions);

        // Register FileFlux adapters as Scoped (they depend on scoped IDocumentProcessorFactory)
        services.TryAddScoped<IExtractor, FileFluxExtractor>();
        services.TryAddScoped<IChunker, FileFluxChunker>();

        return services;
    }

    /// <summary>
    /// Adds FileVault with full FluxIndex integration for extraction, chunking, and memorization.
    /// Registers FileFlux core services automatically.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration action for FileVaultOptions.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// FluxIndex services (IVectorStore, IEmbeddingService) must be registered separately.
    /// </remarks>
    public static IServiceCollection AddFileVaultWithFluxIndex(
        this IServiceCollection services,
        Action<FileVaultOptions>? configureOptions = null)
    {
        services.AddFileVaultWithFileFlux(configureOptions);

        // Register FluxIndex memorizer adapter as Scoped (depends on scoped IVectorStore)
        services.TryAddScoped<FluxIndexMemorizer>();

        return services;
    }

    // ============================================
    // Multi-Tenant Support (IVaultFactory)
    // ============================================

    /// <summary>
    /// Adds FileVault factory for multi-tenant scenarios.
    ///
    /// Use this instead of AddFileVault() when you need isolated vault instances
    /// per tenant/context. Each tenant gets their own:
    /// - .vault/ directory for metadata and extracted content
    /// - queue.db for processing queue persistence
    ///
    /// Shared across all tenants (for efficiency):
    /// - IContentHasher, IGitService (stateless)
    /// - IVectorStore, IEmbeddingService (shared AI services)
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration for default options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// // DI registration
    /// services.AddFileVaultFactory(options =>
    /// {
    ///     options.VaultBasePath = "./data";
    ///     options.EnableBackgroundProcessing = true;
    /// });
    ///
    /// // Usage
    /// public class TenantService
    /// {
    ///     private readonly IVaultFactory _factory;
    ///
    ///     public async Task ProcessTenantFiles(string tenantId)
    ///     {
    ///         var vault = _factory.GetOrCreate(tenantId);
    ///         await vault.MemorizeAsync(filePath);
    ///     }
    /// }
    /// </code>
    /// </example>
    public static IServiceCollection AddFileVaultFactory(
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

        // Register shared services (stateless, reused across all tenants)
        services.TryAddSingleton<IContentHasher, ContentHasher>();
        services.TryAddSingleton<IGitService, GitService>();
        services.TryAddSingleton<IFileWatcherService, FileWatcherService>();

        // Register the factory
        services.TryAddSingleton<IVaultFactory, VaultFactory>();

        return services;
    }

    /// <summary>
    /// Adds FileVault factory with FileFlux integration for multi-tenant scenarios.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration for default options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFileVaultFactoryWithFileFlux(
        this IServiceCollection services,
        Action<FileVaultOptions>? configureOptions = null)
    {
        services.AddFileFlux();
        services.AddFileVaultFactory(configureOptions);

        // Register FileFlux adapters as Scoped (they depend on scoped IDocumentProcessorFactory)
        services.TryAddScoped<IExtractor, FileFluxExtractor>();
        services.TryAddScoped<IChunker, FileFluxChunker>();

        return services;
    }

    /// <summary>
    /// Adds FileVault factory with full FluxIndex integration for multi-tenant scenarios.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration for default options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFileVaultFactoryWithFluxIndex(
        this IServiceCollection services,
        Action<FileVaultOptions>? configureOptions = null)
    {
        services.AddFileVaultFactoryWithFileFlux(configureOptions);
        // Register FluxIndex memorizer adapter as Scoped (depends on scoped IVectorStore)
        services.TryAddScoped<FluxIndexMemorizer>();

        return services;
    }
}
