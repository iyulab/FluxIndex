using FluxIndex.Stack.Vault.Interfaces;
using FluxIndex.Stack.Vault.Options;
using FluxIndex.Stack.Vault.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Stack.Vault.Extensions;

/// <summary>
/// Extension methods for registering Vault services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds FluxIndex.Vault services to the service collection.
    /// </summary>
    public static IServiceCollection AddFluxIndexVault(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register options
        services.Configure<VaultOptions>(configuration.GetSection(VaultOptions.SectionName));

        // Register services
        services.AddSingleton<IContentHashService, ContentHashService>();
        services.AddSingleton<IVaultStorageService, VaultStorageService>();
        services.AddSingleton<IFileWatcherService, FileWatcherService>();
        services.AddScoped<IVaultService, VaultService>();

        return services;
    }

    /// <summary>
    /// Adds FluxIndex.Vault services with custom options.
    /// </summary>
    public static IServiceCollection AddFluxIndexVault(
        this IServiceCollection services,
        Action<VaultOptions> configureOptions)
    {
        services.Configure(configureOptions);

        services.AddSingleton<IContentHashService, ContentHashService>();
        services.AddSingleton<IVaultStorageService, VaultStorageService>();
        services.AddSingleton<IFileWatcherService, FileWatcherService>();
        services.AddScoped<IVaultService, VaultService>();

        return services;
    }
}
