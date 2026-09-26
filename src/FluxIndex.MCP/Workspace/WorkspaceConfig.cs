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

/// <summary>
/// Embedding for the workspace. The MCP workspace has one built-in provider: local LMSupply (<c>"lmsupply"</c> or
/// <c>"local"</c>), loaded on first use. Any other provider value falls back to it with a warning. Workspaces created
/// before 0.55.0 have <c>"openai"</c> / <c>"text-embedding-3-small"</c> written as defaults; they have always run the
/// local <c>default</c> model.
/// </summary>
public class EmbeddingConfig
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "lmsupply";

    /// <summary>The LMSupply model id or alias (used when <see cref="Provider"/> is local).</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "default";
}
