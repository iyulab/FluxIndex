using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxIndex.MCP.Workspace;

/// <summary>
/// FluxIndex workspace configuration stored in .vault/config.json
/// </summary>
public class WorkspaceConfig
{
    private static readonly JsonSerializerOptions s_saveJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("embedding")]
    public EmbeddingConfig Embedding { get; set; } = new();

    [JsonPropertyName("completion")]
    public CompletionConfig? Completion { get; set; }

    [JsonPropertyName("search")]
    public SearchConfig Search { get; set; } = new();

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public static WorkspaceConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return new WorkspaceConfig();
        }

        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<WorkspaceConfig>(json) ?? new WorkspaceConfig();
    }

    public void Save(string configPath)
    {
        UpdatedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(this, s_saveJsonOptions);
        File.WriteAllText(configPath, json);
    }
}

public class EmbeddingConfig
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "openai";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "text-embedding-3-small";

    [JsonPropertyName("dimensions")]
    public int? Dimensions { get; set; }
}

public class CompletionConfig
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "openai";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "gpt-4o-mini";
}

public class SearchConfig
{
    [JsonPropertyName("strategy")]
    public string Strategy { get; set; } = "Hybrid";

    [JsonPropertyName("top_k")]
    public int TopK { get; set; } = 10;

    [JsonPropertyName("min_score")]
    public float MinScore { get; set; } = 0.5f;
}
