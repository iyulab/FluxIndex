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
    /// Stores an entity in the graph store. An entity already stored under the same id is updated, and its chunk ids,
    /// document ids and surface forms are merged with the written ones rather than replaced (see
    /// <see cref="StoreEntitiesBatchAsync"/>); <see cref="UpdateEntityAsync"/> replaces them.
    /// </summary>
    Task<string> StoreEntityAsync(GraphEntity entity, CancellationToken ct = default);

    /// <summary>
    /// Stores multiple entities in batch for efficiency. An entity already stored under the same id is updated, and its
    /// chunk ids, document ids and surface forms are merged with the written ones rather than replaced: the entity graph
    /// build derives an entity's id from its identity, so two builds of one partition running at once write the same
    /// entity, and the second must not erase the first one's provenance. <see cref="UpdateEntityAsync"/> replaces them.
    /// </summary>
    Task<IReadOnlyList<string>> StoreEntitiesBatchAsync(
        IEnumerable<GraphEntity> entities,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves an entity by its ID.
    /// </summary>
    Task<GraphEntity?> GetEntityByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves entities by their canonical name, within one partition.
    /// </summary>
    /// <param name="name">The name to match.</param>
    /// <param name="fuzzyMatch">Whether to match partially rather than exactly.</param>
    /// <param name="partition">The partition to read — see <see cref="GraphEntity.Partition"/>. <see cref="GraphPartition.Default"/> reads the default partition, not every partition.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<GraphEntity>> GetEntitiesByNameAsync(
        string name,
        bool fuzzyMatch = false,
        string partition = GraphPartition.Default,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves every entity in one partition whose <see cref="GraphEntity.NormalizedName"/> is one of
    /// <paramref name="normalizedNames"/>, compared exactly — the lookup an indexing build joins its freshly
    /// extracted entities to, in one round trip rather than one per name.
    /// </summary>
    /// <param name="normalizedNames">Normalized names to match exactly.</param>
    /// <param name="partition">The partition to read — see <see cref="GraphEntity.Partition"/>. <see cref="GraphPartition.Default"/> reads the default partition, not every partition.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<GraphEntity>> GetEntitiesByNormalizedNamesAsync(
        IEnumerable<string> normalizedNames,
        string partition = GraphPartition.Default,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves entities by type, within one partition.
    /// </summary>
    /// <param name="type">The entity type.</param>
    /// <param name="limit">Maximum number of entities returned.</param>
    /// <param name="partition">The partition to read — see <see cref="GraphEntity.Partition"/>. <see cref="GraphPartition.Default"/> reads the default partition, not every partition.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<GraphEntity>> GetEntitiesByTypeAsync(
        NamedEntityType type,
        int limit = 100,
        string partition = GraphPartition.Default,
        CancellationToken ct = default);

    /// <summary>
    /// Updates an existing entity, replacing every field — chunk ids, document ids and surface forms included (the way
    /// to take a chunk away from an entity).
    /// </summary>
    Task<bool> UpdateEntityAsync(GraphEntity entity, CancellationToken ct = default);

    /// <summary>
    /// Deletes an entity and its relationships.
    /// </summary>
    Task<bool> DeleteEntityAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Takes chunk ids away from every entity of a partition that lists them. An entity left with no chunk is deleted,
    /// and its relationships and community memberships with it.
    /// </summary>
    /// <remarks>
    /// Each entity changes as one write against what is stored at that moment, so a build running at the same time that
    /// adds a chunk to one of these entities keeps it — and an entity it has just given a chunk is trimmed, not deleted.
    /// Reading the entities and writing them back with <see cref="UpdateEntityAsync"/> would replace over such a chunk.
    /// This default does exactly that (correct only without concurrent writers); the stores FluxIndex ships override it.
    /// </remarks>
    /// <param name="chunkIds">The chunk ids to take away.</param>
    /// <param name="partition">The partition whose entities are changed — see <see cref="GraphEntity.Partition"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entities that kept at least one chunk and those deleted.</returns>
    async Task<GraphEntityChunkRemoval> RemoveEntityChunksAsync(
        IReadOnlyCollection<string> chunkIds,
        string partition = GraphPartition.Default,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);
        ArgumentNullException.ThrowIfNull(partition);
        var forgotten = chunkIds.ToHashSet(StringComparer.Ordinal);
        if (forgotten.Count == 0) return new GraphEntityChunkRemoval();

        var trimmed = new List<string>();
        var deleted = new List<string>();
        foreach (var entity in await GetEntitiesByChunkIdsAsync(forgotten, partition, ct))
        {
            var remaining = GraphEntityChunks.Without(entity.ChunkIds, forgotten);
            if (remaining.Count == 0)
            {
                if (await DeleteEntityAsync(entity.Id, ct)) deleted.Add(entity.Id);
            }
            else if (await UpdateEntityAsync(entity with { ChunkIds = remaining }, ct))
            {
                trimmed.Add(entity.Id);
            }
        }

        return new GraphEntityChunkRemoval { TrimmedEntityIds = trimmed, DeletedEntityIds = deleted };
    }

    /// <summary>
    /// Renames chunk ids on every entity of a partition that lists a renamed chunk, and replaces one document id with
    /// another on those entities — what moving a document to a new id does to the graph.
    /// </summary>
    /// <remarks>
    /// Each entity changes as one write against what is stored at that moment, as in
    /// <see cref="RemoveEntityChunksAsync"/>; this default reads and writes back (correct only without concurrent
    /// writers), and the stores FluxIndex ships override it.
    /// </remarks>
    /// <param name="chunkIdMap">Old chunk id to new chunk id. Chunk ids not in the map are kept.</param>
    /// <param name="oldDocumentId">The document id to replace on the changed entities.</param>
    /// <param name="newDocumentId">The document id that replaces it.</param>
    /// <param name="partition">The partition whose entities are changed — see <see cref="GraphEntity.Partition"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ids of the entities changed.</returns>
    async Task<IReadOnlyList<string>> RemapEntityChunksAsync(
        IReadOnlyDictionary<string, string> chunkIdMap,
        string oldDocumentId,
        string newDocumentId,
        string partition = GraphPartition.Default,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIdMap);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldDocumentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newDocumentId);
        ArgumentNullException.ThrowIfNull(partition);
        if (chunkIdMap.Count == 0) return [];

        var changed = new List<string>();
        foreach (var entity in await GetEntitiesByChunkIdsAsync(chunkIdMap.Keys, partition, ct))
        {
            if (await UpdateEntityAsync(GraphEntityChunks.Remapped(entity, chunkIdMap, oldDocumentId, newDocumentId), ct))
                changed.Add(entity.Id);
        }

        return changed;
    }

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
    /// Retrieves relationships of a specific type between entities of one partition. A relationship has no
    /// partition of its own: it belongs to the partition of the entities it connects.
    /// </summary>
    /// <param name="type">The relationship type.</param>
    /// <param name="limit">Maximum number of relationships returned.</param>
    /// <param name="partition">The partition to read — see <see cref="GraphEntity.Partition"/>. <see cref="GraphPartition.Default"/> reads the default partition, not every partition.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<GraphRelationship>> GetRelationshipsByTypeAsync(
        RelationType type,
        int limit = 100,
        string partition = GraphPartition.Default,
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
    /// Gets the entities of one partition connected to specific chunks. The same chunk id can exist in two
    /// partitions; only the entities of <paramref name="partition"/> are returned.
    /// </summary>
    /// <param name="chunkIds">Chunk ids to match.</param>
    /// <param name="partition">The partition to read — see <see cref="GraphEntity.Partition"/>. <see cref="GraphPartition.Default"/> reads the default partition, not every partition.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<GraphEntity>> GetEntitiesByChunkIdsAsync(
        IEnumerable<string> chunkIds,
        string partition = GraphPartition.Default,
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
    /// Gets the top communities of one partition by importance.
    /// </summary>
    /// <param name="limit">Maximum number of communities returned.</param>
    /// <param name="partition">The partition to read — see <see cref="GraphEntity.Partition"/>. <see cref="GraphPartition.Default"/> reads the default partition, not every partition.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<GraphCommunity>> GetTopCommunitiesAsync(
        int limit = 10,
        string partition = GraphPartition.Default,
        CancellationToken ct = default);

    /// <summary>
    /// Gets every community that groups at least one of the given chunks — the community-side
    /// counterpart of <see cref="GetEntitiesByChunkIdsAsync"/>, and what a chunk-scoped index load
    /// stands on. Returned communities carry their full <see cref="GraphCommunity.ChunkIds"/>, not
    /// only the ids that matched.
    /// </summary>
    /// <param name="chunkIds">Chunk ids to match.</param>
    /// <param name="partition">The partition to read — see <see cref="GraphEntity.Partition"/>. <see cref="GraphPartition.Default"/> reads the default partition, not every partition.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<GraphCommunity>> GetCommunitiesByChunkIdsAsync(
        IEnumerable<string> chunkIds,
        string partition = GraphPartition.Default,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes the given communities together with their membership rows. Ids that are not stored are ignored.
    /// A GraphRAG build calls it for the communities it supersedes — stored ones that group any of the build's
    /// chunks and are not part of the new hierarchy.
    /// </summary>
    /// <param name="communityIds">Ids of the communities to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of communities deleted.</returns>
    Task<int> DeleteCommunitiesAsync(
        IEnumerable<string> communityIds,
        CancellationToken ct = default);

    #endregion

    #region Statistics and Maintenance

    /// <summary>
    /// Gets statistics about one partition of the graph store.
    /// </summary>
    /// <param name="partition">The partition to count — see <see cref="GraphEntity.Partition"/>. <see cref="GraphPartition.Default"/> counts the default partition, not every partition.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<GraphStoreStatistics> GetStatisticsAsync(string partition = GraphPartition.Default, CancellationToken ct = default);

    /// <summary>
    /// Clears all data from the graph store — every partition.
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);

    #endregion
}

#region Supporting Types

/// <summary>What <see cref="IGraphStore.RemoveEntityChunksAsync"/> changed.</summary>
public record GraphEntityChunkRemoval
{
    /// <summary>Entities that lost chunks and kept at least one.</summary>
    public IReadOnlyList<string> TrimmedEntityIds { get; init; } = [];

    /// <summary>Entities deleted because no chunk was left.</summary>
    public IReadOnlyList<string> DeletedEntityIds { get; init; } = [];
}

/// <summary>
/// The chunk-list arithmetic of <see cref="IGraphStore.RemoveEntityChunksAsync"/> and
/// <see cref="IGraphStore.RemapEntityChunksAsync"/>, shared by the default implementations and the stores that override
/// them so every store computes the same lists.
/// </summary>
public static class GraphEntityChunks
{
    /// <summary>The chunk ids of <paramref name="chunkIds"/> that are not in <paramref name="removed"/>, in order.</summary>
    public static List<string> Without(IEnumerable<string> chunkIds, IReadOnlySet<string> removed)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);
        ArgumentNullException.ThrowIfNull(removed);
        return chunkIds.Where(id => !removed.Contains(id)).ToList();
    }

    /// <summary>
    /// <paramref name="entity"/> with each chunk id renamed through <paramref name="chunkIdMap"/> and
    /// <paramref name="oldDocumentId"/> replaced by <paramref name="newDocumentId"/>, both lists without duplicates.
    /// </summary>
    public static GraphEntity Remapped(
        GraphEntity entity,
        IReadOnlyDictionary<string, string> chunkIdMap,
        string oldDocumentId,
        string newDocumentId)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(chunkIdMap);
        return entity with
        {
            ChunkIds = entity.ChunkIds
                .Select(id => chunkIdMap.TryGetValue(id, out var mapped) ? mapped : id)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            DocumentIds = entity.DocumentIds
                .Select(id => string.Equals(id, oldDocumentId, StringComparison.Ordinal) ? newDocumentId : id)
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };
    }
}

/// <summary>
/// Names the partition a graph store read or write belongs to.
/// </summary>
/// <remarks>
/// A partition keeps the graphs one store instance holds apart: entities merge only with entities of their own
/// partition, and every multi-result read returns one partition. A consumer serving several tenants from one store gives
/// each tenant its own partition value; a consumer that does not partition never passes one and uses
/// <see cref="Default"/> throughout. There is deliberately no "every partition" read: a caller that forgets its partition
/// sees the default partition — empty in a partitioned deployment — rather than every tenant's graph.
/// </remarks>
public static class GraphPartition
{
    /// <summary>The partition of a consumer that does not partition, and of every row written before partitions existed.</summary>
    public const string Default = "";
}

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

    /// <summary>
    /// The partition this entity belongs to (<see cref="GraphPartition"/>). Entities with the same identity in two
    /// partitions are two entities; a relationship connects entities of one partition.
    /// </summary>
    public string Partition { get; init; } = GraphPartition.Default;

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

    /// <summary>The partition this community belongs to (<see cref="GraphPartition"/>).</summary>
    public string Partition { get; init; } = GraphPartition.Default;

    /// <summary>AI-generated summary of the community</summary>
    public string? Summary { get; init; }

    /// <summary>
    /// IDs of the entities that belong to this community — the entities extracted from the chunks the
    /// community groups. This is the membership the relational stores model (a member row references
    /// an entity) and what <see cref="IGraphStore.GetCommunitiesForEntityAsync"/> answers from.
    /// </summary>
    public IReadOnlyList<string> EntityIds { get; init; } = [];

    /// <summary>
    /// IDs of the chunks this community groups. GraphRAG communities are clusters of chunks (Leiden
    /// over chunk embeddings); this list is what a chunk-scoped index load matches on
    /// (<see cref="IGraphStore.GetCommunitiesByChunkIdsAsync"/>). Every store persists it on the
    /// community itself (a node property on Neo4j, a chunk-id column on PostgreSQL and SQLite).
    /// </summary>
    public IReadOnlyList<string> ChunkIds { get; init; } = [];

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
    public double MinWeight { get; init; }

    /// <summary>Direction to traverse</summary>
    public TraversalDirection Direction { get; init; } = TraversalDirection.Outgoing;

    /// <summary>Include entity embeddings in results</summary>
    public bool IncludeEmbeddings { get; init; }

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
