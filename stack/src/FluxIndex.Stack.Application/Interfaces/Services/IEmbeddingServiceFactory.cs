namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Factory interface for creating embedding providers based on configuration.
/// Supports multiple AI providers and local embedder fallback.
/// </summary>
public interface IEmbeddingServiceFactory
{
    /// <summary>
    /// Creates an embedding provider for the specified AI provider.
    /// </summary>
    /// <param name="providerName">Provider name (OpenAI, Azure, Cohere, Google, Local)</param>
    /// <param name="apiKey">API key for the provider</param>
    /// <param name="modelName">Model name to use for embeddings</param>
    /// <param name="endpointUrl">Optional custom endpoint URL (for Azure, GPUStack, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Configured embedding provider</returns>
    Task<IEmbeddingProvider> CreateProviderAsync(
        string providerName,
        string? apiKey,
        string? modelName,
        string? endpointUrl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a local embedding provider (no API key required).
    /// Uses LocalEmbedder with ONNX-based models.
    /// </summary>
    /// <param name="modelName">Optional model name (defaults to all-MiniLM-L6-v2)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Local embedding provider</returns>
    Task<IEmbeddingProvider> CreateLocalProviderAsync(
        string? modelName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about supported providers.
    /// </summary>
    IReadOnlyList<string> SupportedProviders { get; }
}
