using System.Text.Json;

namespace FluxIndex.Storage.SQLite.Graph;

/// <summary>
/// SQLite용 청크 계층 엔티티
/// </summary>
public class ChunkHierarchyEntity
{
    public string ChunkId { get; set; } = string.Empty;
    public string? ParentChunkId { get; set; }
    public string ChildChunkIdsJson { get; set; } = "[]";
    public int HierarchyLevel { get; set; }
    public int RecommendedWindowSize { get; set; } = 1;

    // Boundary
    public int BoundaryStartPosition { get; set; }
    public int BoundaryEndPosition { get; set; }
    public string BoundaryType { get; set; } = "Sentence";

    // Metadata
    public int MetadataDepth { get; set; }
    public int MetadataDescendantCount { get; set; }
    public int MetadataSiblingCount { get; set; }
    public double MetadataHierarchyWeight { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation helpers
    public List<string> GetChildChunkIds() =>
        JsonSerializer.Deserialize<List<string>>(ChildChunkIdsJson) ?? new List<string>();

    public void SetChildChunkIds(List<string> ids) =>
        ChildChunkIdsJson = JsonSerializer.Serialize(ids);
}

/// <summary>
/// SQLite용 청크 관계 엔티티
/// </summary>
public class ChunkRelationshipEntity
{
    public string Id { get; set; } = string.Empty;
    public string SourceChunkId { get; set; } = string.Empty;
    public string TargetChunkId { get; set; } = string.Empty;
    public string Type { get; set; } = "Semantic";
    public double Strength { get; set; }
    public string Direction { get; set; } = "Bidirectional";
    public string? Description { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Dictionary<string, object> GetMetadata() =>
        JsonSerializer.Deserialize<Dictionary<string, object>>(MetadataJson) ?? new Dictionary<string, object>();

    public void SetMetadata(Dictionary<string, object> metadata) =>
        MetadataJson = JsonSerializer.Serialize(metadata);
}
