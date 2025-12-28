namespace FluxIndex.Stack.Shared.DTOs.Graph;

/// <summary>
/// Response containing graph statistics.
/// </summary>
public class GraphStatisticsResponse
{
    /// <summary>
    /// Whether the graph service is available.
    /// </summary>
    public bool IsAvailable { get; set; }

    /// <summary>
    /// Total number of entity nodes in the graph.
    /// </summary>
    public long TotalNodes { get; set; }

    /// <summary>
    /// Total number of relationships between entities.
    /// </summary>
    public long TotalRelationships { get; set; }

    /// <summary>
    /// Total number of detected communities.
    /// </summary>
    public long TotalCommunities { get; set; }

    /// <summary>
    /// Node counts grouped by entity type.
    /// </summary>
    public Dictionary<string, long> NodesByType { get; set; } = new();

    /// <summary>
    /// Relationship counts grouped by relationship type.
    /// </summary>
    public Dictionary<string, long> RelationshipsByType { get; set; } = new();
}

/// <summary>
/// Request to get related entities.
/// </summary>
public class GetRelatedEntitiesRequest
{
    /// <summary>
    /// List of entity IDs to find relations for.
    /// </summary>
    public List<string> EntityIds { get; set; } = new();

    /// <summary>
    /// Maximum number of relationship hops to traverse.
    /// </summary>
    public int MaxHops { get; set; } = 2;
}

/// <summary>
/// Represents a relationship between two entities.
/// </summary>
public class EntityRelationshipDto
{
    /// <summary>
    /// Source entity ID.
    /// </summary>
    public string SourceEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Target entity ID.
    /// </summary>
    public string TargetEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Type of relationship (e.g., WORKS_FOR, LOCATED_IN).
    /// </summary>
    public string RelationshipType { get; set; } = string.Empty;

    /// <summary>
    /// Optional additional properties.
    /// </summary>
    public Dictionary<string, object>? Properties { get; set; }
}

/// <summary>
/// Response containing related entities.
/// </summary>
public class GetRelatedEntitiesResponse
{
    /// <summary>
    /// List of relationships found.
    /// </summary>
    public List<EntityRelationshipDto> Relationships { get; set; } = new();

    /// <summary>
    /// Total number of relationships found.
    /// </summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// Request to expand a query with related entities.
/// </summary>
public class QueryExpansionRequest
{
    /// <summary>
    /// The query to expand.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of entities to use for expansion.
    /// </summary>
    public int MaxEntities { get; set; } = 5;
}

/// <summary>
/// Response containing expanded query terms.
/// </summary>
public class QueryExpansionResponse
{
    /// <summary>
    /// Original query.
    /// </summary>
    public string OriginalQuery { get; set; } = string.Empty;

    /// <summary>
    /// Related terms found in the knowledge graph.
    /// </summary>
    public List<string> RelatedTerms { get; set; } = new();

    /// <summary>
    /// Expanded query incorporating related terms.
    /// </summary>
    public string ExpandedQuery { get; set; } = string.Empty;
}

/// <summary>
/// Request to find paths between entities.
/// </summary>
public class FindPathsRequest
{
    /// <summary>
    /// Starting entity ID.
    /// </summary>
    public string SourceEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Target entity ID.
    /// </summary>
    public string TargetEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Maximum path length (number of hops).
    /// </summary>
    public int MaxPathLength { get; set; } = 5;
}

/// <summary>
/// Represents a path through the knowledge graph.
/// </summary>
public class GraphPathDto
{
    /// <summary>
    /// Ordered list of entity IDs in the path.
    /// </summary>
    public List<string> EntityIds { get; set; } = new();

    /// <summary>
    /// Ordered list of relationship types connecting entities.
    /// </summary>
    public List<string> RelationshipTypes { get; set; } = new();

    /// <summary>
    /// Total weight of the path.
    /// </summary>
    public double PathWeight { get; set; }

    /// <summary>
    /// Path length (number of hops).
    /// </summary>
    public int Length => EntityIds.Count > 0 ? EntityIds.Count - 1 : 0;
}

/// <summary>
/// Response containing paths between entities.
/// </summary>
public class FindPathsResponse
{
    /// <summary>
    /// List of paths found.
    /// </summary>
    public List<GraphPathDto> Paths { get; set; } = new();

    /// <summary>
    /// Source entity ID.
    /// </summary>
    public string SourceEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Target entity ID.
    /// </summary>
    public string TargetEntityId { get; set; } = string.Empty;
}

/// <summary>
/// Request to store an entity in the graph.
/// </summary>
public class StoreEntityRequest
{
    /// <summary>
    /// Unique entity identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Entity name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Entity type (e.g., Person, Organization, Location).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Optional additional properties.
    /// </summary>
    public Dictionary<string, object>? Properties { get; set; }

    /// <summary>
    /// Optional associated chunk ID.
    /// </summary>
    public Guid? ChunkId { get; set; }

    /// <summary>
    /// Optional associated document ID.
    /// </summary>
    public Guid? DocumentId { get; set; }
}

/// <summary>
/// Request to store a relationship between entities.
/// </summary>
public class StoreRelationshipRequest
{
    /// <summary>
    /// Source entity ID.
    /// </summary>
    public string SourceEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Target entity ID.
    /// </summary>
    public string TargetEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Type of relationship.
    /// </summary>
    public string RelationshipType { get; set; } = string.Empty;

    /// <summary>
    /// Optional additional properties.
    /// </summary>
    public Dictionary<string, object>? Properties { get; set; }
}

/// <summary>
/// Request to link a chunk to entities.
/// </summary>
public class LinkChunkToEntitiesRequest
{
    /// <summary>
    /// The chunk ID to link.
    /// </summary>
    public Guid ChunkId { get; set; }

    /// <summary>
    /// Entity IDs to link to the chunk.
    /// </summary>
    public List<string> EntityIds { get; set; } = new();
}

/// <summary>
/// Request to get chunks for entities.
/// </summary>
public class GetChunksForEntitiesRequest
{
    /// <summary>
    /// Entity IDs to find chunks for.
    /// </summary>
    public List<string> EntityIds { get; set; } = new();
}

/// <summary>
/// Response containing chunk IDs for entities.
/// </summary>
public class GetChunksForEntitiesResponse
{
    /// <summary>
    /// List of chunk IDs associated with the entities.
    /// </summary>
    public List<Guid> ChunkIds { get; set; } = new();

    /// <summary>
    /// Total number of chunks found.
    /// </summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// Represents a community/cluster in the graph.
/// </summary>
public class GraphCommunityDto
{
    /// <summary>
    /// Community ID.
    /// </summary>
    public string CommunityId { get; set; } = string.Empty;

    /// <summary>
    /// Community name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// List of member entity IDs.
    /// </summary>
    public List<string> MemberEntityIds { get; set; } = new();

    /// <summary>
    /// AI-generated summary of the community.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Hierarchy level (0 = top level).
    /// </summary>
    public int Level { get; set; }
}

/// <summary>
/// Request to run community detection.
/// </summary>
public class RunCommunityDetectionRequest
{
    /// <summary>
    /// Optional collection ID to limit scope.
    /// </summary>
    public Guid? CollectionId { get; set; }
}

/// <summary>
/// Response from community detection.
/// </summary>
public class RunCommunityDetectionResponse
{
    /// <summary>
    /// Number of communities detected.
    /// </summary>
    public int CommunitiesDetected { get; set; }

    /// <summary>
    /// Execution time in milliseconds.
    /// </summary>
    public double ExecutionTimeMs { get; set; }
}
