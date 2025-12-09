namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Interface for Neo4j graph database integration.
/// Provides GraphRAG capabilities for relationship-aware search.
/// </summary>
public interface INeo4jGraphService
{
    /// <summary>
    /// Check if Neo4j service is available.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Get entities related to the given entity IDs.
    /// </summary>
    /// <param name="entityIds">Source entity IDs</param>
    /// <param name="maxHops">Maximum relationship hops (default: 2)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of related entities with relationships</returns>
    Task<List<EntityRelationship>> GetRelatedEntitiesAsync(
        IEnumerable<string> entityIds,
        int maxHops = 2,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Expand a query by finding related entities in the knowledge graph.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="maxEntities">Maximum entities to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of related entity names for query expansion</returns>
    Task<List<string>> ExpandQueryWithRelatedEntitiesAsync(
        string query,
        int maxEntities = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Store an entity and its relationships in the graph.
    /// </summary>
    /// <param name="entity">Entity to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StoreEntityAsync(
        GraphEntity entity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Store a relationship between two entities.
    /// </summary>
    /// <param name="sourceEntityId">Source entity ID</param>
    /// <param name="targetEntityId">Target entity ID</param>
    /// <param name="relationshipType">Type of relationship</param>
    /// <param name="properties">Optional relationship properties</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StoreRelationshipAsync(
        string sourceEntityId,
        string targetEntityId,
        string relationshipType,
        Dictionary<string, object>? properties = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Find paths between two entities.
    /// </summary>
    /// <param name="sourceEntityId">Source entity ID</param>
    /// <param name="targetEntityId">Target entity ID</param>
    /// <param name="maxPathLength">Maximum path length</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of paths connecting the entities</returns>
    Task<List<GraphPath>> FindPathsAsync(
        string sourceEntityId,
        string targetEntityId,
        int maxPathLength = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the community (cluster) for an entity.
    /// </summary>
    /// <param name="entityId">Entity ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Community information</returns>
    Task<GraphCommunity?> GetEntityCommunityAsync(
        string entityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get chunks associated with entities.
    /// </summary>
    /// <param name="entityIds">Entity IDs</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of chunk IDs associated with the entities</returns>
    Task<List<Guid>> GetChunksForEntitiesAsync(
        IEnumerable<string> entityIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Link a chunk to entities found within it.
    /// </summary>
    /// <param name="chunkId">Chunk ID</param>
    /// <param name="entityIds">Entity IDs found in the chunk</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task LinkChunkToEntitiesAsync(
        Guid chunkId,
        IEnumerable<string> entityIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run community detection algorithm on the graph.
    /// </summary>
    /// <param name="collectionId">Optional collection to limit scope</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of communities detected</returns>
    Task<int> RunCommunityDetectionAsync(
        Guid? collectionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get graph statistics.
    /// </summary>
    Task<GraphStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Entity relationship from graph traversal.
/// </summary>
public record EntityRelationship(
    string SourceEntityId,
    string TargetEntityId,
    string RelationshipType,
    Dictionary<string, object>? Properties = null);

/// <summary>
/// Graph entity representation.
/// </summary>
public record GraphEntity(
    string Id,
    string Name,
    string Type,
    Dictionary<string, object>? Properties = null,
    Guid? ChunkId = null,
    Guid? DocumentId = null);

/// <summary>
/// Path in the knowledge graph.
/// </summary>
public record GraphPath(
    List<string> EntityIds,
    List<string> RelationshipTypes,
    double PathWeight = 1.0);

/// <summary>
/// Community/cluster in the graph.
/// </summary>
public record GraphCommunity(
    string CommunityId,
    string Name,
    List<string> MemberEntityIds,
    string? Summary = null,
    int Level = 0);

/// <summary>
/// Graph database statistics.
/// </summary>
public record GraphStatistics(
    long TotalNodes,
    long TotalRelationships,
    long TotalCommunities,
    Dictionary<string, long> NodesByType,
    Dictionary<string, long> RelationshipsByType);
