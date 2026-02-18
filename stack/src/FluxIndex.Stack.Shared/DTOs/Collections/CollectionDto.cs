namespace FluxIndex.Stack.Shared.DTOs.Collections;

/// <summary>
/// Data transfer object for Collection entity.
/// </summary>
public class CollectionDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public CollectionSettingsDto Settings { get; init; } = new();
    public int DocumentCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Collection settings DTO.
/// </summary>
public class CollectionSettingsDto
{
    public int ChunkSize { get; init; } = 1000;
    public int ChunkOverlap { get; init; } = 200;
    public string ChunkingStrategy { get; init; } = "intelligent";
    public bool EnableQAGeneration { get; init; }
    public bool EnableEnrichment { get; init; }
    public Dictionary<string, object> CustomSettings { get; init; } = new();
}

/// <summary>
/// Request to create a new collection.
/// </summary>
public class CreateCollectionRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public CollectionSettingsDto? Settings { get; set; }
}

/// <summary>
/// Request to update an existing collection.
/// </summary>
public class UpdateCollectionRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public CollectionSettingsDto? Settings { get; set; }
}
