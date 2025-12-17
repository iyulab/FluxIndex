using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Interface for persistent graph storage operations.
/// Supports entity storage, relationship management, and graph traversal.
/// Implementations: PostgreSQL (adjacency list + CTEs), Neo4j (native graph).
/// </summary>
public interface IGraphStore
{
    #region Entity Operations

    /// <summary>
    /// Stores an entity in the graph store.
    /// </summary>
    Task<string> StoreEntityAsync(GraphEntity entity, CancellationToken ct = default);

    /// <summary>
    /// Stores multiple entities in batch for efficiency.
    /// </summary>
    Task<IReadOnlyList<string>> StoreEntitiesBatchAsync(
        IEnumerable<GraphEntity> entities,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves an entity by its ID.
    /// </summary>
    Task<GraphEntity?> GetEntityByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves entities by their canonical name.
    /// </summary>
    Task<IReadOnlyList<GraphEntity>> GetEntitiesByNameAsync(
        string name,
        bool fuzzyMatch = false,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves entities by type.
    /// </summary>
    Task<IReadOnlyList<GraphEntity>> GetEntitiesByTypeAsync(
        NamedEntityType type,
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Updates an existing entity.
    /// </summary>
    Task<bool> UpdateEntityAsync(GraphEntity entity, CancellationToken ct = default);

    /// <summary>
    /// Deletes an entity and its relationships.
    /// </summary>
    Task<bool> DeleteEntityAsync(string id, CancellationToken ct = default);

    #endregion

    #region Relationship Operations

    /// <summary>
    /// Stores a relationship between entities.
    /// </summary>
    Task<string> StoreRelationshipAsync(
        GraphRelationship relationship,
        CancellationToken ct = default);

    /// <summary>
    /// Stores multiple relationships in batch.
    /// </summary>
    Task<IReadOnlyList<string>> StoreRelationshipsBatchAsync(
        IEnumerable<GraphRelationship> relationships,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves all relationships for an entity.
    /// </summary>
    Task<IReadOnlyList<GraphRelationship>> GetRelationshipsAsync(
        string entityId,
        TraversalDirection direction = TraversalDirection.Both,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves relationships of a specific type.
    /// </summary>
    Task<IReadOnlyList<GraphRelationship>> GetRelationshipsByTypeAsync(
        RelationType type,
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a relationship.
    /// </summary>
    Task<bool> DeleteRelationshipAsync(string relationshipId, CancellationToken ct = default);

    #endregion

    #region Traversal Operations

    /// <summary>
    /// Traverses the graph from a starting entity.
    /// </summary>
    Task<GraphStoreTraversalResult> TraverseAsync(
        string startEntityId,
        GraphStoreTraversalOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Finds the shortest path between two entities.
    /// </summary>
    Task<GraphPath?> FindShortestPathAsync(
        string sourceEntityId,
        string targetEntityId,
        int maxDepth = 5,
        CancellationToken ct = default);

    /// <summary>
    /// Finds entities within N hops of a starting entity.
    /// </summary>
    Task<IReadOnlyList<GraphEntity>> GetNeighborsAsync(
        string entityId,
        int depth = 1,
        CancellationToken ct = default);

    /// <summary>
    /// Gets entities connected to specific chunks.
    /// </summary>
    Task<IReadOnlyList<GraphEntity>> GetEntitiesByChunkIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken ct = default);

    #endregion

    #region Community Operations

    /// <summary>
    /// Stores a detected community.
    /// </summary>
    Task<string> StoreCommunityAsync(
        GraphCommunity community,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves a community by ID.
    /// </summary>
    Task<GraphCommunity?> GetCommunityByIdAsync(
        string communityId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all communities an entity belongs to.
    /// </summary>
    Task<IReadOnlyList<GraphCommunity>> GetCommunitiesForEntityAsync(
        string entityId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets top communities by importance.
    /// </summary>
    Task<IReadOnlyList<GraphCommunity>> GetTopCommunitiesAsync(
        int limit = 10,
        CancellationToken ct = default);

    #endregion

    #region Statistics and Maintenance

    /// <summary>
    /// Gets statistics about the graph store.
    /// </summary>
    Task<GraphStoreStatistics> GetStatisticsAsync(CancellationToken ct = default);

    /// <summary>
    /// Clears all data from the graph store.
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);

    #endregion
}

#region Supporting Types

/// <summary>
/// Entity for persistent graph storage.
/// </summary>
public record GraphEntity
{
    /// <summary>Unique identifier</summary>
    public required string Id { get; init; }

    /// <summary>Canonical entity name</summary>
    public required string Name { get; init; }

    /// <summary>Normalized name for matching (lowercase, trimmed)</summary>
    public string NormalizedName { get; init; } = string.Empty;

    /// <summary>Entity type from extraction</summary>
    public NamedEntityType Type { get; init; } = NamedEntityType.Unknown;

    /// <summary>All surface forms (aliases, variations)</summary>
    public IReadOnlyList<string> SurfaceForms { get; init; } = [];

    /// <summary>Entity description for context</summary>
    public string? Description { get; init; }

    /// <summary>Entity embedding vector for similarity search</summary>
    public float[]? Embedding { get; init; }

    /// <summary>Confidence score from extraction (0-1)</summary>
    public double Confidence { get; init; }

    /// <summary>Importance score (PageRank-style)</summary>
    public double ImportanceScore { get; init; }

    /// <summary>Number of mentions across documents</summary>
    public int MentionCount { get; init; }

    /// <summary>IDs of chunks where this entity appears</summary>
    public IReadOnlyList<string> ChunkIds { get; init; } = [];

    /// <summary>IDs of documents where this entity appears</summary>
    public IReadOnlyList<string> DocumentIds { get; init; } = [];

    /// <summary>External knowledge base links (e.g., Wikidata, DBpedia)</summary>
    public IReadOnlyDictionary<string, string> ExternalLinks { get; init; } = new Dictionary<string, string>();

    /// <summary>Additional properties</summary>
    public IReadOnlyDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>();

    /// <summary>Creation timestamp</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Last update timestamp</summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Relationship between entities for persistent storage.
/// </summary>
public record GraphRelationship
{
    /// <summary>Unique identifier</summary>
    public required string Id { get; init; }

    /// <summary>Source entity ID</summary>
    public required string SourceEntityId { get; init; }

    /// <summary>Target entity ID</summary>
    public required string TargetEntityId { get; init; }

    /// <summary>Relationship type</summary>
    public RelationType Type { get; init; } = RelationType.RelatedTo;

    /// <summary>Human-readable label</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Confidence score (0-1)</summary>
    public double Confidence { get; init; }

    /// <summary>Relationship weight/strength</summary>
    public double Weight { get; init; } = 1.0;

    /// <summary>Whether the relationship is directional</summary>
    public bool IsDirectional { get; init; } = true;

    /// <summary>IDs of chunks that evidence this relationship</summary>
    public IReadOnlyList<string> EvidenceChunkIds { get; init; } = [];

    /// <summary>Text excerpts evidencing this relationship</summary>
    public IReadOnlyList<string> EvidenceTexts { get; init; } = [];

    /// <summary>Additional properties</summary>
    public IReadOnlyDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>();

    /// <summary>Creation timestamp</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Community of related entities (detected via clustering/community detection).
/// </summary>
public record GraphCommunity
{
    /// <summary>Unique identifier</summary>
    public required string Id { get; init; }

    /// <summary>Community name/title</summary>
    public required string Name { get; init; }

    /// <summary>AI-generated summary of the community</summary>
    public string? Summary { get; init; }

    /// <summary>IDs of entities in this community</summary>
    public IReadOnlyList<string> EntityIds { get; init; } = [];

    /// <summary>Key topics/themes in this community</summary>
    public IReadOnlyList<string> Topics { get; init; } = [];

    /// <summary>Importance score for the community</summary>
    public double ImportanceScore { get; init; }

    /// <summary>Hierarchy level (0 = top-level)</summary>
    public int Level { get; init; }

    /// <summary>Parent community ID (for hierarchical communities)</summary>
    public string? ParentCommunityId { get; init; }

    /// <summary>Community embedding for similarity search</summary>
    public float[]? Embedding { get; init; }

    /// <summary>Creation timestamp</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Options for graph store traversal operations.
/// </summary>
public record GraphStoreTraversalOptions
{
    /// <summary>Maximum traversal depth</summary>
    public int MaxDepth { get; init; } = 3;

    /// <summary>Maximum number of nodes to return</summary>
    public int MaxNodes { get; init; } = 100;

    /// <summary>Relationship types to follow (empty = all)</summary>
    public IReadOnlyList<RelationType> RelationTypes { get; init; } = [];

    /// <summary>Entity types to include (empty = all)</summary>
    public IReadOnlyList<NamedEntityType> EntityTypes { get; init; } = [];

    /// <summary>Minimum relationship weight to traverse</summary>
    public double MinWeight { get; init; } = 0.0;

    /// <summary>Direction to traverse</summary>
    public TraversalDirection Direction { get; init; } = TraversalDirection.Outgoing;

    /// <summary>Include entity embeddings in results</summary>
    public bool IncludeEmbeddings { get; init; } = false;

    /// <summary>Include relationship evidence</summary>
    public bool IncludeEvidence { get; init; } = true;
}

/// <summary>
/// Result of a graph store traversal operation.
/// </summary>
public record GraphStoreTraversalResult
{
    /// <summary>Starting entity</summary>
    public required GraphEntity StartEntity { get; init; }

    /// <summary>All discovered entities</summary>
    public IReadOnlyList<GraphEntity> Entities { get; init; } = [];

    /// <summary>All traversed relationships</summary>
    public IReadOnlyList<GraphRelationship> Relationships { get; init; } = [];

    /// <summary>Paths from start to each entity (entity ID -> path)</summary>
    public IReadOnlyDictionary<string, GraphPath> Paths { get; init; } = new Dictionary<string, GraphPath>();

    /// <summary>Maximum depth reached</summary>
    public int MaxDepthReached { get; init; }

    /// <summary>Whether traversal was truncated due to limits</summary>
    public bool WasTruncated { get; init; }
}

/// <summary>
/// A path through the graph.
/// </summary>
public record GraphPath
{
    /// <summary>Ordered list of entity IDs in the path</summary>
    public IReadOnlyList<string> EntityIds { get; init; } = [];

    /// <summary>Ordered list of relationship IDs connecting the entities</summary>
    public IReadOnlyList<string> RelationshipIds { get; init; } = [];

    /// <summary>Total path weight (sum of relationship weights)</summary>
    public double TotalWeight { get; init; }

    /// <summary>Path length (number of hops)</summary>
    public int Length => EntityIds.Count > 0 ? EntityIds.Count - 1 : 0;
}

/// <summary>
/// Direction for graph store traversal and relationship queries.
/// </summary>
public enum TraversalDirection
{
    /// <summary>Outgoing relationships (from source)</summary>
    Outgoing,

    /// <summary>Incoming relationships (to target)</summary>
    Incoming,

    /// <summary>Both directions</summary>
    Both
}

/// <summary>
/// Statistics about the graph store.
/// </summary>
public record GraphStoreStatistics
{
    /// <summary>Total number of entities</summary>
    public long EntityCount { get; init; }

    /// <summary>Total number of relationships</summary>
    public long RelationshipCount { get; init; }

    /// <summary>Total number of communities</summary>
    public long CommunityCount { get; init; }

    /// <summary>Entity counts by type</summary>
    public IReadOnlyDictionary<NamedEntityType, long> EntityCountsByType { get; init; } = new Dictionary<NamedEntityType, long>();

    /// <summary>Relationship counts by type</summary>
    public IReadOnlyDictionary<RelationType, long> RelationshipCountsByType { get; init; } = new Dictionary<RelationType, long>();

    /// <summary>Average relationships per entity</summary>
    public double AverageRelationshipsPerEntity { get; init; }

    /// <summary>Last update timestamp</summary>
    public DateTimeOffset LastUpdated { get; init; }
}

#endregion
