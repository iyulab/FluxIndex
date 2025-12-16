namespace FluxIndex.Storage.PostgreSQL.Graph;

/// <summary>
/// PostgreSQL용 청크 계층 엔티티 (JSONB 활용)
/// </summary>
public class ChunkHierarchyEntity
{
    public string ChunkId { get; set; } = string.Empty;
    public string? ParentChunkId { get; set; }

    /// <summary>
    /// 자식 청크 ID 목록 (JSONB 배열)
    /// </summary>
    public List<string> ChildChunkIds { get; set; } = new();

    public int HierarchyLevel { get; set; }
    public int RecommendedWindowSize { get; set; }

    // Boundary 정보
    public int BoundaryStartPosition { get; set; }
    public int BoundaryEndPosition { get; set; }
    public string BoundaryType { get; set; } = "Sentence";

    // Metadata 정보
    public int MetadataDepth { get; set; }
    public int MetadataDescendantCount { get; set; }
    public int MetadataSiblingCount { get; set; }
    public double MetadataHierarchyWeight { get; set; }

    // 추가 메타데이터 (JSONB)
    public Dictionary<string, object>? ExtendedMetadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// PostgreSQL용 청크 관계 엔티티 (JSONB 활용)
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

    /// <summary>
    /// 관계 메타데이터 (JSONB)
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
