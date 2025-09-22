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
    /// Model name for embeddings (e.g., "text-embedding-3-small")
    /// </summary>
    public string ModelName { get; set; } = "text-embedding-3-small";

    /// <summary>
    /// Maximum tokens per request
    /// </summary>
    public int MaxTokens { get; set; } = 8192;

    /// <summary>
    /// Embedding dimensions (optional, model default if null)
    /// </summary>
    public int? Dimensions { get; set; }

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum retry attempts
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}