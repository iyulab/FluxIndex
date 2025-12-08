using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Factory interface for creating text completion service providers.
/// </summary>
public interface ITextCompletionServiceFactory
{
    /// <summary>
    /// Gets the list of supported provider names.
    /// </summary>
    IReadOnlyList<string> SupportedProviders { get; }

    /// <summary>
    /// Creates a text completion provider based on configuration.
    /// </summary>
    /// <param name="providerName">Provider name (OpenAI, Azure, etc.)</param>
    /// <param name="apiKey">API key for the provider.</param>
    /// <param name="modelName">Model name to use.</param>
    /// <param name="endpointUrl">Optional endpoint URL (required for Azure).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Text completion service instance.</returns>
    Task<ITextCompletionService> CreateProviderAsync(
        string providerName,
        string? apiKey,
        string? modelName,
        string? endpointUrl = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for managing text completion provider cache.
/// </summary>
public interface ITextCompletionProviderCache
{
    /// <summary>
    /// Invalidates the cached provider instance.
    /// </summary>
    void InvalidateCache();

    /// <summary>
    /// Gets provider status information.
    /// </summary>
    Task<TextCompletionProviderStatus> GetProviderStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Status information for text completion provider.
/// </summary>
public class TextCompletionProviderStatus
{
    /// <summary>
    /// Provider name (OpenAI, Azure, Local, etc.)
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Model name in use.
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the provider is available and configured.
    /// </summary>
    public bool IsAvailable { get; set; }

    /// <summary>
    /// Error message if provider is not available.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
