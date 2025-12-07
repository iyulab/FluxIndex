namespace FluxIndex.Stack.Domain.Entities;

/// <summary>
/// Represents AI provider configuration settings.
/// Stores API keys and model selections for various AI providers.
/// </summary>
public class AiProviderSettings
{
    public Guid Id { get; private set; }

    /// <summary>
    /// Provider name (e.g., "OpenAI", "Anthropic", "Azure", "Cohere", "Local")
    /// </summary>
    public string ProviderName { get; private set; } = string.Empty;

    /// <summary>
    /// Display name for UI
    /// </summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>
    /// Encrypted API key for the provider
    /// </summary>
    public string? ApiKey { get; private set; }

    /// <summary>
    /// Whether this provider is enabled
    /// </summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// Whether this provider is the default for embedding
    /// </summary>
    public bool IsDefaultEmbedding { get; private set; }

    /// <summary>
    /// Whether this provider is the default for LLM/completion
    /// </summary>
    public bool IsDefaultLlm { get; private set; }

    /// <summary>
    /// Selected embedding model for this provider
    /// </summary>
    public string? EmbeddingModel { get; private set; }

    /// <summary>
    /// Selected LLM model for this provider
    /// </summary>
    public string? LlmModel { get; private set; }

    /// <summary>
    /// Additional endpoint URL (for Azure OpenAI, custom endpoints)
    /// </summary>
    public string? EndpointUrl { get; private set; }

    /// <summary>
    /// Additional configuration as JSON
    /// </summary>
    public string? AdditionalConfig { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private AiProviderSettings() { }

    public static AiProviderSettings Create(
        string providerName,
        string displayName,
        string? apiKey = null,
        string? embeddingModel = null,
        string? llmModel = null,
        string? endpointUrl = null)
    {
        return new AiProviderSettings
        {
            Id = Guid.NewGuid(),
            ProviderName = providerName,
            DisplayName = displayName,
            ApiKey = apiKey,
            IsEnabled = !string.IsNullOrWhiteSpace(apiKey),
            IsDefaultEmbedding = false,
            IsDefaultLlm = false,
            EmbeddingModel = embeddingModel,
            LlmModel = llmModel,
            EndpointUrl = endpointUrl,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void UpdateApiKey(string? apiKey)
    {
        ApiKey = apiKey;
        IsEnabled = !string.IsNullOrWhiteSpace(apiKey);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetEmbeddingModel(string? model)
    {
        EmbeddingModel = model;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetLlmModel(string? model)
    {
        LlmModel = model;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetAsDefaultEmbedding(bool isDefault)
    {
        IsDefaultEmbedding = isDefault;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetAsDefaultLlm(bool isDefault)
    {
        IsDefaultLlm = isDefault;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetEndpointUrl(string? url)
    {
        EndpointUrl = url;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetAdditionalConfig(string? config)
    {
        AdditionalConfig = config;
        UpdatedAt = DateTime.UtcNow;
    }
}
