namespace FluxIndex.AI.OpenAI;

/// <summary>
/// API provider type for OpenAI-compatible services
/// </summary>
public enum OpenAIProviderType
{
    /// <summary>
    /// OpenAI API (api.openai.com)
    /// </summary>
    OpenAI,

    /// <summary>
    /// Azure OpenAI Service
    /// </summary>
    AzureOpenAI,

    /// <summary>
    /// GPUStack - OpenAI-compatible self-hosted inference
    /// Supports both GPUStack v1.x and v2.x
    /// </summary>
    GPUStack,

    /// <summary>
    /// Generic OpenAI-compatible API endpoint
    /// Use this for other providers like Ollama, LM Studio, vLLM, etc.
    /// </summary>
    OpenAICompatible
}

/// <summary>
/// Configuration options for OpenAI and OpenAI-compatible services
/// Supports: OpenAI, Azure OpenAI, GPUStack (v1/v2), and other OpenAI-compatible APIs
/// </summary>
public class OpenAIOptions
{
    /// <summary>
    /// API key for authentication
    /// For GPUStack: Use the API key from GPUStack dashboard
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// API endpoint URL
    /// - OpenAI: Leave empty (uses default api.openai.com)
    /// - Azure OpenAI: https://{resource-name}.openai.azure.com
    /// - GPUStack: http://{host}:{port} (e.g., http://localhost:80)
    /// - OpenAI-compatible: Full base URL of the API
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// API provider type. Auto-detected if not specified:
    /// - Empty endpoint → OpenAI
    /// - *.openai.azure.com → AzureOpenAI
    /// - Otherwise → OpenAICompatible (works for GPUStack)
    /// </summary>
    public OpenAIProviderType? ProviderType { get; set; }

    /// <summary>
    /// Model name (used for embedding, text completion, and metadata extraction)
    ///
    /// For OpenAI embedding: "text-embedding-3-small", "text-embedding-3-large", "text-embedding-ada-002"
    /// For OpenAI text completion: "gpt-4o-mini", "gpt-4o", "gpt-4-turbo"
    ///
    /// For GPUStack: Use model names deployed on your GPUStack instance
    /// Example: "Qwen/Qwen2.5-0.5B-Instruct", "BAAI/bge-m3"
    ///
    /// Note: When using different models for embedding and completion, configure separate service instances
    /// </summary>
    public string ModelName { get; set; } = "text-embedding-3-small";

    /// <summary>
    /// Maximum tokens per request
    /// </summary>
    public int MaxTokens { get; set; } = 8192;

    /// <summary>
    /// Embedding dimensions (optional, model default if null)
    /// Only applicable for embedding services
    /// </summary>
    public int? Dimensions { get; set; }

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum retry attempts for failed requests
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets the effective provider type, auto-detecting if not explicitly set
    /// </summary>
    public OpenAIProviderType GetEffectiveProviderType()
    {
        if (ProviderType.HasValue)
            return ProviderType.Value;

        if (string.IsNullOrEmpty(Endpoint))
            return OpenAIProviderType.OpenAI;

        if (Endpoint.Contains(".openai.azure.com", StringComparison.OrdinalIgnoreCase))
            return OpenAIProviderType.AzureOpenAI;

        // GPUStack and other OpenAI-compatible APIs
        return OpenAIProviderType.OpenAICompatible;
    }

    /// <summary>
    /// Validates the configuration options
    /// </summary>
    public void Validate()
    {
        var providerType = GetEffectiveProviderType();

        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("ApiKey is required for all provider types");

        if (providerType != OpenAIProviderType.OpenAI && string.IsNullOrWhiteSpace(Endpoint))
            throw new InvalidOperationException($"Endpoint is required for {providerType} provider");

        if (string.IsNullOrWhiteSpace(ModelName))
            throw new InvalidOperationException("ModelName is required");
    }
}