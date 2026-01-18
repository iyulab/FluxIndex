using FluxIndex.Extensions.FileVault.Options;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Extensions.FileVault.Extensions;

/// <summary>
/// Extension methods for FluxIndexContextBuilder to add FileVault support.
/// </summary>
public static class FluxIndexContextBuilderExtensions
{
    /// <summary>
    /// Adds FileVault support to FluxIndex with default options.
    /// </summary>
    /// <typeparam name="TBuilder">Builder type that has ConfigureServices method.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <param name="configureOptions">Optional configuration action for FileVaultOptions.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// This method uses reflection to call ConfigureServices since FluxIndex.SDK
    /// cannot be directly referenced from this extension package.
    /// </remarks>
    public static TBuilder UseFileVault<TBuilder>(
        this TBuilder builder,
        Action<FileVaultOptions>? configureOptions = null)
        where TBuilder : class
    {
        // Use reflection to call ConfigureServices since we can't reference FluxIndex.SDK
        var configureMethod = builder.GetType().GetMethod("ConfigureServices");
        if (configureMethod == null)
        {
            throw new InvalidOperationException(
                "Builder must have a ConfigureServices(Action<IServiceCollection>) method");
        }

        configureMethod.Invoke(builder, new object[]
        {
            (Action<IServiceCollection>)(services =>
            {
                services.AddFileVault(configureOptions);
            })
        });

        return builder;
    }
}
