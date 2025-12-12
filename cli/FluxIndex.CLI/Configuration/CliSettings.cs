using System.Text.Json;

namespace FluxIndex.CLI.Configuration;

/// <summary>
/// CLI configuration settings stored in user home directory
/// </summary>
public class CliSettings
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".fluxindex");

    private static readonly string SettingsFile = Path.Combine(SettingsDirectory, "settings.json");

    /// <summary>
    /// AI Provider type (openai, azure, gpustack, local)
    /// </summary>
    public string Provider { get; set; } = "local";

    /// <summary>
    /// OpenAI API key
    /// </summary>
    public string? OpenAIApiKey { get; set; }

    /// <summary>
    /// OpenAI model name
    /// </summary>
    public string? OpenAIModelName { get; set; }

    /// <summary>
    /// Azure OpenAI endpoint
    /// </summary>
    public string? AzureEndpoint { get; set; }

    /// <summary>
    /// Azure OpenAI API key
    /// </summary>
    public string? AzureApiKey { get; set; }

    /// <summary>
    /// Azure OpenAI deployment name
    /// </summary>
    public string? AzureDeploymentName { get; set; }

    /// <summary>
    /// GPUStack server endpoint
    /// </summary>
    public string? GPUStackEndpoint { get; set; }

    /// <summary>
    /// GPUStack API key
    /// </summary>
    public string? GPUStackApiKey { get; set; }

    /// <summary>
    /// GPUStack chat model name
    /// </summary>
    public string? GPUStackModelName { get; set; }

    /// <summary>
    /// GPUStack embedding model name (optional, defaults to local-embedder)
    /// </summary>
    public string? GPUStackEmbeddingModelName { get; set; }

    /// <summary>
    /// Default language for document processing
    /// </summary>
    public string? DefaultLanguage { get; set; }

    /// <summary>
    /// Default chunking strategy
    /// </summary>
    public string ChunkingStrategy { get; set; } = "Auto";

    /// <summary>
    /// Maximum chunk size in tokens
    /// </summary>
    public int MaxChunkSize { get; set; } = 1024;

    /// <summary>
    /// Overlap size between chunks
    /// </summary>
    public int OverlapSize { get; set; } = 128;

    /// <summary>
    /// Enable metadata enrichment via LLM
    /// </summary>
    public bool EnableMetadataEnrichment { get; set; } = false;

    /// <summary>
    /// Enable text cleaning/preprocessing before chunking
    /// </summary>
    public bool EnableTextCleaning { get; set; } = false;

    /// <summary>
    /// Enable contextual enrichment (Anthropic Contextual Retrieval)
    /// </summary>
    public bool EnableContextualEnrichment { get; set; } = false;

    /// <summary>
    /// Load settings from file
    /// </summary>
    public static CliSettings Load()
    {
        if (!File.Exists(SettingsFile))
        {
            return new CliSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsFile);
            return JsonSerializer.Deserialize<CliSettings>(json, GetJsonOptions()) ?? new CliSettings();
        }
        catch
        {
            return new CliSettings();
        }
    }

    /// <summary>
    /// Save settings to file
    /// </summary>
    public void Save()
    {
        if (!Directory.Exists(SettingsDirectory))
        {
            Directory.CreateDirectory(SettingsDirectory);
        }

        var json = JsonSerializer.Serialize(this, GetJsonOptions());
        File.WriteAllText(SettingsFile, json);
    }

    /// <summary>
    /// Set a configuration value by key
    /// </summary>
    public bool Set(string key, string value)
    {
        var normalizedKey = key.ToUpperInvariant().Replace("-", "_");

        switch (normalizedKey)
        {
            case "PROVIDER":
                Provider = value.ToLowerInvariant();
                break;
            case "OPENAI_API_KEY":
                OpenAIApiKey = value;
                break;
            case "OPENAI_MODEL_NAME":
                OpenAIModelName = value;
                break;
            case "AZURE_ENDPOINT":
                AzureEndpoint = value;
                break;
            case "AZURE_API_KEY":
                AzureApiKey = value;
                break;
            case "AZURE_DEPLOYMENT_NAME":
                AzureDeploymentName = value;
                break;
            case "GPUSTACK_ENDPOINT":
                GPUStackEndpoint = value;
                break;
            case "GPUSTACK_API_KEY":
                GPUStackApiKey = value;
                break;
            case "GPUSTACK_MODEL_NAME":
                GPUStackModelName = value;
                break;
            case "GPUSTACK_EMBEDDING_MODEL_NAME":
                GPUStackEmbeddingModelName = value;
                break;
            case "DEFAULT_LANGUAGE":
            case "LANGUAGE":
                DefaultLanguage = value;
                break;
            case "CHUNKING_STRATEGY":
                ChunkingStrategy = value;
                break;
            case "MAX_CHUNK_SIZE":
                if (int.TryParse(value, out var maxSize))
                    MaxChunkSize = maxSize;
                else
                    return false;
                break;
            case "OVERLAP_SIZE":
                if (int.TryParse(value, out var overlap))
                    OverlapSize = overlap;
                else
                    return false;
                break;
            case "ENABLE_METADATA_ENRICHMENT":
                EnableMetadataEnrichment = value.ToLowerInvariant() is "true" or "1" or "yes";
                break;
            case "ENABLE_TEXT_CLEANING":
                EnableTextCleaning = value.ToLowerInvariant() is "true" or "1" or "yes";
                break;
            case "ENABLE_CONTEXTUAL_ENRICHMENT":
                EnableContextualEnrichment = value.ToLowerInvariant() is "true" or "1" or "yes";
                break;
            default:
                return false;
        }

        Save();
        return true;
    }

    /// <summary>
    /// Get a configuration value by key
    /// </summary>
    public string? Get(string key)
    {
        var normalizedKey = key.ToUpperInvariant().Replace("-", "_");

        return normalizedKey switch
        {
            "PROVIDER" => Provider,
            "OPENAI_API_KEY" => MaskSensitive(OpenAIApiKey),
            "OPENAI_MODEL_NAME" => OpenAIModelName,
            "AZURE_ENDPOINT" => AzureEndpoint,
            "AZURE_API_KEY" => MaskSensitive(AzureApiKey),
            "AZURE_DEPLOYMENT_NAME" => AzureDeploymentName,
            "GPUSTACK_ENDPOINT" => GPUStackEndpoint,
            "GPUSTACK_API_KEY" => MaskSensitive(GPUStackApiKey),
            "GPUSTACK_MODEL_NAME" => GPUStackModelName,
            "GPUSTACK_EMBEDDING_MODEL_NAME" => GPUStackEmbeddingModelName,
            "DEFAULT_LANGUAGE" or "LANGUAGE" => DefaultLanguage,
            "CHUNKING_STRATEGY" => ChunkingStrategy,
            "MAX_CHUNK_SIZE" => MaxChunkSize.ToString(),
            "OVERLAP_SIZE" => OverlapSize.ToString(),
            "ENABLE_METADATA_ENRICHMENT" => EnableMetadataEnrichment.ToString().ToLowerInvariant(),
            "ENABLE_TEXT_CLEANING" => EnableTextCleaning.ToString().ToLowerInvariant(),
            "ENABLE_CONTEXTUAL_ENRICHMENT" => EnableContextualEnrichment.ToString().ToLowerInvariant(),
            _ => null
        };
    }

    /// <summary>
    /// Get all settings as dictionary (for display)
    /// </summary>
    public Dictionary<string, string?> GetAll()
    {
        return new Dictionary<string, string?>
        {
            ["PROVIDER"] = Provider,
            ["OPENAI_API_KEY"] = MaskSensitive(OpenAIApiKey),
            ["OPENAI_MODEL_NAME"] = OpenAIModelName,
            ["AZURE_ENDPOINT"] = AzureEndpoint,
            ["AZURE_API_KEY"] = MaskSensitive(AzureApiKey),
            ["AZURE_DEPLOYMENT_NAME"] = AzureDeploymentName,
            ["GPUSTACK_ENDPOINT"] = GPUStackEndpoint,
            ["GPUSTACK_API_KEY"] = MaskSensitive(GPUStackApiKey),
            ["GPUSTACK_MODEL_NAME"] = GPUStackModelName,
            ["GPUSTACK_EMBEDDING_MODEL_NAME"] = GPUStackEmbeddingModelName,
            ["DEFAULT_LANGUAGE"] = DefaultLanguage,
            ["CHUNKING_STRATEGY"] = ChunkingStrategy,
            ["MAX_CHUNK_SIZE"] = MaxChunkSize.ToString(),
            ["OVERLAP_SIZE"] = OverlapSize.ToString(),
            ["ENABLE_METADATA_ENRICHMENT"] = EnableMetadataEnrichment.ToString().ToLowerInvariant(),
            ["ENABLE_TEXT_CLEANING"] = EnableTextCleaning.ToString().ToLowerInvariant(),
            ["ENABLE_CONTEXTUAL_ENRICHMENT"] = EnableContextualEnrichment.ToString().ToLowerInvariant()
        };
    }

    private static string? MaskSensitive(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length <= 8) return "****";
        return value[..4] + "****" + value[^4..];
    }

    private static JsonSerializerOptions GetJsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
