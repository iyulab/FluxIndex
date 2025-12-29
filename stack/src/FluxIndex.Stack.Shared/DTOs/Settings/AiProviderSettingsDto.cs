namespace FluxIndex.Stack.Shared.DTOs.Settings;

/// <summary>
/// DTO for AI provider settings display
/// </summary>
public class AiProviderSettingsDto
{
    public Guid Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool HasApiKey { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsDefaultEmbedding { get; set; }
    public bool IsDefaultLlm { get; set; }
    public string? EmbeddingModel { get; set; }
    public string? LlmModel { get; set; }
    public string? EndpointUrl { get; set; }

    /// <summary>
    /// Whether this is a local provider that doesn't require an API key (e.g., LMSupply)
    /// </summary>
    public bool IsLocalProvider { get; set; }

    /// <summary>
    /// Whether this provider requires a custom endpoint URL (e.g., Azure, GPUStack)
    /// </summary>
    public bool RequiresEndpoint { get; set; }

    public List<string> AvailableEmbeddingModels { get; set; } = new();
    public List<string> AvailableLlmModels { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Request to update AI provider settings
/// </summary>
public class UpdateAiProviderRequest
{
    public string? ApiKey { get; set; }
    public string? EmbeddingModel { get; set; }
    public string? LlmModel { get; set; }
    public string? EndpointUrl { get; set; }
    public bool? IsEnabled { get; set; }
    public bool? IsDefaultEmbedding { get; set; }
    public bool? IsDefaultLlm { get; set; }
}

/// <summary>
/// Response for overall AI configuration status
/// </summary>
public class AiConfigurationStatusDto
{
    public bool HasEmbeddingProvider { get; set; }
    public bool HasLlmProvider { get; set; }
    public string? DefaultEmbeddingProvider { get; set; }
    public string? DefaultEmbeddingModel { get; set; }
    public string? DefaultLlmProvider { get; set; }
    public string? DefaultLlmModel { get; set; }
    public List<AiProviderSettingsDto> Providers { get; set; } = new();
}

/// <summary>
/// Available model information for a provider
/// </summary>
public class AvailableModelsDto
{
    public string ProviderName { get; set; } = string.Empty;
    public List<ModelInfoDto> EmbeddingModels { get; set; } = new();
    public List<ModelInfoDto> LlmModels { get; set; } = new();
}

/// <summary>
/// Model information
/// </summary>
public class ModelInfoDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? MaxTokens { get; set; }
    public int? Dimensions { get; set; }
}
