namespace FluxIndex.AI.OpenAI;

/// <summary>
/// Configuration options for OpenAI services
/// </summary>
public class OpenAIOptions
{
    /// <summary>
    /// OpenAI API key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Azure OpenAI endpoint (optional, leave empty for OpenAI API)
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Model name (used for embedding, text completion, and metadata extraction)
    /// For embedding: "text-embedding-3-small", "text-embedding-3-large", "text-embedding-ada-002"
    /// For text completion: "gpt-5-nano" (recommended, most cost-effective), "gpt-5-mini", "gpt-5"
    /// Legacy models: "gpt-4o-mini", "gpt-4o" (backward compatibility only)
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
}