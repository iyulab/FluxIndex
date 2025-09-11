using System.ComponentModel.DataAnnotations;

namespace FluxIndex.AI.OpenAI;

/// <summary>
/// Configuration options for OpenAI services
/// </summary>
public class OpenAIConfiguration
{
    /// <summary>
    /// Configuration section name for binding
    /// </summary>
    public const string SectionName = "FluxIndex:OpenAI";

    /// <summary>
    /// OpenAI API key
    /// </summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// OpenAI organization ID (optional)
    /// </summary>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// Base URL for OpenAI API (defaults to official OpenAI endpoint)
    /// Set this for Azure OpenAI or other compatible endpoints
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Text completion model configuration
    /// </summary>
    public TextCompletionOptions TextCompletion { get; set; } = new();

    /// <summary>
    /// Embedding model configuration  
    /// </summary>
    public EmbeddingOptions Embedding { get; set; } = new();

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of retries for failed requests
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Enable detailed logging of API requests/responses
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;
}

/// <summary>
/// Configuration for text completion models
/// </summary>
public class TextCompletionOptions
{
    /// <summary>
    /// Default model for text completion
    /// </summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Default maximum tokens for completions
    /// </summary>
    public int MaxTokens { get; set; } = 500;

    /// <summary>
    /// Default temperature for text generation
    /// </summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// Default top-p parameter
    /// </summary>
    public float TopP { get; set; } = 1.0f;

    /// <summary>
    /// Default frequency penalty
    /// </summary>
    public float FrequencyPenalty { get; set; } = 0.0f;

    /// <summary>
    /// Default presence penalty
    /// </summary>
    public float PresencePenalty { get; set; } = 0.0f;
}

/// <summary>
/// Configuration for embedding models
/// </summary>
public class EmbeddingOptions
{
    /// <summary>
    /// Default model for embeddings
    /// </summary>
    public string Model { get; set; } = "text-embedding-3-small";

    /// <summary>
    /// Dimensions for embedding vectors (null = use model default)
    /// </summary>
    public int? Dimensions { get; set; }

    /// <summary>
    /// Batch size for embedding requests
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Enable caching of embeddings to reduce API calls
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// Cache expiry time in hours
    /// </summary>
    public int CacheExpiryHours { get; set; } = 24;
}