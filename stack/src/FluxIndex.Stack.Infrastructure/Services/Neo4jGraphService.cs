using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Stack.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FluxIndex.Storage.Neo4j;
using CoreGraphEntity = FluxIndex.Core.Application.Interfaces.GraphEntity;
using CoreGraphPath = FluxIndex.Core.Application.Interfaces.GraphPath;
using CoreGraphCommunity = FluxIndex.Core.Application.Interfaces.GraphCommunity;
using CoreGraphRelationship = FluxIndex.Core.Application.Interfaces.GraphRelationship;
using StackGraphEntity = FluxIndex.Stack.Application.Interfaces.Services.GraphEntity;
using StackGraphPath = FluxIndex.Stack.Application.Interfaces.Services.GraphPath;
using StackGraphCommunity = FluxIndex.Stack.Application.Interfaces.Services.GraphCommunity;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Neo4j graph service implementation for Stack.
/// Wraps Core's IGraphStore with Stack-specific functionality.
/// </summary>
public partial class Neo4jGraphService : INeo4jGraphService
{
    private readonly IGraphStore? _graphStore;
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly ILogger<Neo4jGraphService> _logger;
    private readonly bool _isAvailable;

    public Neo4jGraphService(
        IGraphStore? graphStore,
        IEmbeddingProvider? embeddingProvider,
        ILogger<Neo4jGraphService> logger)
    {
        _graphStore = graphStore;
        _embeddingProvider = embeddingProvider;
        _logger = logger;
        _isAvailable = graphStore != null;

        if (_isAvailable)
        {
            LogServiceInitialized(_logger);
        }
        else
        {
            LogServiceNotAvailable(_logger);
        }
    }

    /// <inheritdoc />
    public bool IsAvailable => _isAvailable;

    /// <inheritdoc />
    public async Task<List<EntityRelationship>> GetRelatedEntitiesAsync(
        IEnumerable<string> entityIds,
        int maxHops = 2,
        CancellationToken cancellationToken = default)
    {
        if (!_isAvailable || _graphStore is null)
        {
            LogNeo4jNotAvailableRelatedEntities(_logger);
            return [];
        }

        var relationships = new List<EntityRelationship>();
        var entityIdList = entityIds.ToList();

        try
        {
            foreach (var entityId in entityIdList)
            {
                // Traverse from each entity up to maxHops depth
                var traversalResult = await _graphStore.TraverseAsync(
                    entityId,
                    new GraphStoreTraversalOptions
                    {
                        MaxDepth = maxHops,
                        MaxNodes = 100,
                        Direction = TraversalDirection.Both,
                        IncludeEvidence = false
                    },
                    cancellationToken);

                // Convert relationships to Stack model
                foreach (var rel in traversalResult.Relationships)
                {
                    var stackRel = new EntityRelationship(
                        rel.SourceEntityId,
                        rel.TargetEntityId,
                        rel.Type.ToString(),
                        rel.Properties.ToDictionary(kv => kv.Key, kv => kv.Value)
                    );

                    // Avoid duplicates
                    if (!relationships.Any(r =>
                        r.SourceEntityId == stackRel.SourceEntityId &&
                        r.TargetEntityId == stackRel.TargetEntityId &&
                        r.RelationshipType == stackRel.RelationshipType))
                    {
                        relationships.Add(stackRel);
                    }
                }
            }

            var relationshipCount = relationships.Count;
            var entityCount = entityIdList.Count;
            LogFoundRelatedEntities(_logger, relationshipCount, entityCount, maxHops);
        }
        catch (Exception ex)
        {
            LogGetRelatedEntitiesFailed(_logger, ex);
        }

        return relationships;
    }

    /// <inheritdoc />
    public async Task<List<string>> ExpandQueryWithRelatedEntitiesAsync(
        string query,
        int maxEntities = 5,
        CancellationToken cancellationToken = default)
    {
        if (!_isAvailable || _graphStore is null)
        {
            LogNeo4jNotAvailableQueryExpansion(_logger);
            return [];
        }

        var expandedTerms = new List<string>();

        try
        {
            // Split query into terms and find matching entities
            var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var matchedEntities = new List<CoreGraphEntity>();

            foreach (var term in queryTerms)
            {
                if (term.Length < 2) continue;

                // Use fuzzy match to find entities
                var entities = await _graphStore.GetEntitiesByNameAsync(
                    term,
                    fuzzyMatch: true,
                    cancellationToken);

                matchedEntities.AddRange(entities.Take(3));
            }

            // For each matched entity, get related entities
            foreach (var entity in matchedEntities.Take(maxEntities))
            {
                // Get immediate neighbors
                var neighbors = await _graphStore.GetNeighborsAsync(
                    entity.Id,
                    depth: 1,
                    cancellationToken);

                foreach (var neighbor in neighbors.Take(2))
                {
                    if (!expandedTerms.Contains(neighbor.Name) &&
                        !queryTerms.Contains(neighbor.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        expandedTerms.Add(neighbor.Name);
                    }
                }

                // Also add surface forms if available
                foreach (var surfaceForm in entity.SurfaceForms.Take(2))
                {
                    if (!expandedTerms.Contains(surfaceForm) &&
                        !queryTerms.Contains(surfaceForm, StringComparer.OrdinalIgnoreCase))
                    {
                        expandedTerms.Add(surfaceForm);
                    }
                }
            }

            var expandedCount = expandedTerms.Count;
            LogExpandedQuery(_logger, query, expandedCount);
        }
        catch (Exception ex)
        {
            LogExpandQueryFailed(_logger, ex);
        }

        return expandedTerms.Take(maxEntities).ToList();
    }

    /// <inheritdoc />
    public async Task StoreEntityAsync(
        StackGraphEntity entity,
        CancellationToken cancellationToken = default)
    {
        if (!_isAvailable || _graphStore is null)
        {
            LogCannotStoreEntity(_logger);
            return;
        }

        try
        {
            var coreEntity = MapToCoreEntity(entity);
            await _graphStore.StoreEntityAsync(coreEntity, cancellationToken);

            LogStoredEntity(_logger, entity.Id, entity.Name);
        }
        catch (Exception ex)
        {
            LogStoreEntityFailed(_logger, entity.Id, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StoreRelationshipAsync(
        string sourceEntityId,
        string targetEntityId,
        string relationshipType,
        Dictionary<string, object>? properties = null,
        CancellationToken cancellationToken = default)
    {
        if (!_isAvailable || _graphStore is null)
        {
            LogCannotStoreRelationship(_logger);
            return;
        }

        try
        {
            var relationType = ParseRelationType(relationshipType);
            var relationship = new CoreGraphRelationship
            {
                Id = Guid.NewGuid().ToString(),
                SourceEntityId = sourceEntityId,
                TargetEntityId = targetEntityId,
                Type = relationType,
                Label = relationshipType,
                Confidence = 1.0,
                Weight = 1.0,
                IsDirectional = true,
                Properties = properties?.AsReadOnly() ?? new Dictionary<string, object>().AsReadOnly()
            };

            await _graphStore.StoreRelationshipAsync(relationship, cancellationToken);

            LogStoredRelationship(_logger, sourceEntityId, relationshipType, targetEntityId);
        }
        catch (Exception ex)
        {
            LogStoreRelationshipFailed(_logger, sourceEntityId, targetEntityId, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<StackGraphPath>> FindPathsAsync(
        string sourceEntityId,
        string targetEntityId,
        int maxPathLength = 5,
        CancellationToken cancellationToken = default)
    {
        if (!_isAvailable || _graphStore is null)
        {
            LogNeo4jNotAvailablePaths(_logger);
            return [];
        }

        var paths = new List<StackGraphPath>();

        try
        {
            // Find shortest path
            var shortestPath = await _graphStore.FindShortestPathAsync(
                sourceEntityId,
                targetEntityId,
                maxPathLength,
                cancellationToken);

            if (shortestPath != null)
            {
                paths.Add(MapToStackPath(shortestPath));
            }

            var pathCount = paths.Count;
            LogFoundPaths(_logger, pathCount, sourceEntityId, targetEntityId);
        }
        catch (Exception ex)
        {
            LogFindPathsFailed(_logger, sourceEntityId, targetEntityId, ex);
        }

        return paths;
    }

    /// <inheritdoc />
    public async Task<StackGraphCommunity?> GetEntityCommunityAsync(
        string entityId,
        CancellationToken cancellationToken = default)
    {
        if (!_isAvailable || _graphStore is null)
        {
            LogNeo4jNotAvailableCommunity(_logger);
            return null;
        }

        try
        {
            var communities = await _graphStore.GetCommunitiesForEntityAsync(
                entityId, cancellationToken);

            if (communities.Count == 0)
            {
                return null;
            }

            // Return the first (primary) community
            return MapToStackCommunity(communities[0]);
        }
        catch (Exception ex)
        {
            LogGetCommunityFailed(_logger, entityId, ex);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<List<Guid>> GetChunksForEntitiesAsync(
        IEnumerable<string> entityIds,
        CancellationToken cancellationToken = default)
    {
        if (!_isAvailable || _graphStore is null)
        {
            LogNeo4jNotAvailableChunkList(_logger);
            return [];
        }

        var chunkIds = new HashSet<Guid>();

        try
        {
            foreach (var entityId in entityIds)
            {
                var entity = await _graphStore.GetEntityByIdAsync(entityId, cancellationToken);
                if (entity != null)
                {
                    foreach (var chunkIdStr in entity.ChunkIds)
                    {
                        if (Guid.TryParse(chunkIdStr, out var chunkId))
                        {
                            chunkIds.Add(chunkId);
                        }
                    }
                }
            }

            var chunkCount = chunkIds.Count;
            var entityCount = entityIds.Count();
            LogFoundChunksForEntities(_logger, chunkCount, entityCount);
        }
        catch (Exception ex)
        {
            LogGetChunksForEntitiesFailed(_logger, ex);
        }

        return chunkIds.ToList();
    }

    /// <inheritdoc />
    public async Task LinkChunkToEntitiesAsync(
        Guid chunkId,
        IEnumerable<string> entityIds,
        CancellationToken cancellationToken = default)
    {
        if (!_isAvailable || _graphStore is null)
        {
            LogCannotLinkChunkToEntities(_logger);
            return;
        }

        var chunkIdStr = chunkId.ToString();

        try
        {
            foreach (var entityId in entityIds)
            {
                var entity = await _graphStore.GetEntityByIdAsync(entityId, cancellationToken);
                if (entity != null)
                {
                    // Add chunk ID if not already present
                    var existingChunkIds = entity.ChunkIds.ToList();
                    if (!existingChunkIds.Contains(chunkIdStr))
                    {
                        existingChunkIds.Add(chunkIdStr);
                        var updatedEntity = entity with
                        {
                            ChunkIds = existingChunkIds.AsReadOnly(),
                            UpdatedAt = DateTimeOffset.UtcNow
                        };
                        await _graphStore.UpdateEntityAsync(updatedEntity, cancellationToken);
                    }
                }
            }

            var entityCount = entityIds.Count();
            LogLinkedChunkToEntities(_logger, chunkId, entityCount);
        }
        catch (Exception ex)
        {
            LogLinkChunkFailed(_logger, chunkId, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> RunCommunityDetectionAsync(
        Guid? collectionId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_isAvailable || _graphStore is null)
        {
            LogCannotRunCommunityDetection(_logger);
            return 0;
        }

        try
        {
            // Note: Full community detection requires Neo4j Graph Data Science (GDS) plugin
            // This is a simplified implementation that groups entities by their connections
            var collectionIdStr = collectionId?.ToString() ?? "all";
            LogRunningCommunityDetection(_logger, collectionIdStr);

            // Get statistics to estimate the graph size
            var stats = await _graphStore.GetStatisticsAsync(cancellationToken);

            if (stats.EntityCount == 0)
            {
                LogNoEntitiesForCommunityDetection(_logger);
                return 0;
            }

            // For now, use a simple heuristic based on connected components
            // Full Louvain algorithm would require GDS plugin or custom implementation
            var communitiesCreated = await DetectCommunitiesSimpleAsync(cancellationToken);

            LogCommunityDetectionCompleted(_logger, communitiesCreated);

            return communitiesCreated;
        }
        catch (Exception ex)
        {
            LogCommunityDetectionFailed(_logger, ex);
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<GraphStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isAvailable || _graphStore is null)
        {
            LogNeo4jNotAvailableStatistics(_logger);
            return new GraphStatistics(0, 0, 0, new Dictionary<string, long>(), new Dictionary<string, long>());
        }

        try
        {
            var coreStats = await _graphStore.GetStatisticsAsync(cancellationToken);

            // Convert Core statistics to Stack format
            var nodesByType = coreStats.EntityCountsByType
                .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

            var relsByType = coreStats.RelationshipCountsByType
                .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

            return new GraphStatistics(
                coreStats.EntityCount,
                coreStats.RelationshipCount,
                coreStats.CommunityCount,
                nodesByType,
                relsByType);
        }
        catch (Exception ex)
        {
            LogGetStatisticsFailed(_logger, ex);
            return new GraphStatistics(0, 0, 0, new Dictionary<string, long>(), new Dictionary<string, long>());
        }
    }

    #region Private Helpers

    /// <summary>
    /// Simple community detection using connected components analysis.
    /// For production, use Neo4j GDS Louvain algorithm.
    /// </summary>
    private async Task<int> DetectCommunitiesSimpleAsync(CancellationToken cancellationToken)
    {
        // This is a simplified implementation
        // Full implementation would use Neo4j GDS: CALL gds.louvain.stream(...)

        var visited = new HashSet<string>();
        var communities = new List<List<string>>();

        // Get all entities by type (start with high-importance types)
        var importantTypes = new[] { NamedEntityType.Person, NamedEntityType.Organization, NamedEntityType.Location };

        foreach (var entityType in importantTypes)
        {
            var entities = await _graphStore!.GetEntitiesByTypeAsync(entityType, 100, cancellationToken);

            foreach (var entity in entities)
            {
                if (visited.Contains(entity.Id)) continue;

                // BFS to find connected component
                var component = new List<string>();
                var queue = new Queue<string>();
                queue.Enqueue(entity.Id);

                while (queue.Count > 0 && component.Count < 50)
                {
                    var currentId = queue.Dequeue();
                    if (visited.Contains(currentId)) continue;

                    visited.Add(currentId);
                    component.Add(currentId);

                    // Get neighbors
                    var neighbors = await _graphStore.GetNeighborsAsync(currentId, 1, cancellationToken);
                    foreach (var neighbor in neighbors)
                    {
                        if (!visited.Contains(neighbor.Id))
                        {
                            queue.Enqueue(neighbor.Id);
                        }
                    }
                }

                if (component.Count >= 3)
                {
                    communities.Add(component);
                }
            }
        }

        // Store detected communities
        foreach (var (community, index) in communities.Select((c, i) => (c, i)))
        {
            var communityEntity = new CoreGraphCommunity
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"Community_{index + 1}",
                Summary = $"Auto-detected community with {community.Count} members",
                EntityIds = community.AsReadOnly(),
                Topics = [],
                ImportanceScore = community.Count / 100.0,
                Level = 0
            };

            await _graphStore!.StoreCommunityAsync(communityEntity, cancellationToken);
        }

        return communities.Count;
    }

    private static CoreGraphEntity MapToCoreEntity(StackGraphEntity stackEntity)
    {
        var chunkIds = stackEntity.ChunkId.HasValue
            ? new List<string> { stackEntity.ChunkId.Value.ToString() }
            : new List<string>();

        var documentIds = stackEntity.DocumentId.HasValue
            ? new List<string> { stackEntity.DocumentId.Value.ToString() }
            : new List<string>();

        return new CoreGraphEntity
        {
            Id = stackEntity.Id,
            Name = stackEntity.Name,
            NormalizedName = stackEntity.Name.ToLowerInvariant().Trim(),
            Type = ParseEntityType(stackEntity.Type),
            SurfaceForms = new List<string> { stackEntity.Name }.AsReadOnly(),
            Description = null,
            Embedding = null,
            Confidence = 1.0,
            ImportanceScore = 0.0,
            MentionCount = 1,
            ChunkIds = chunkIds.AsReadOnly(),
            DocumentIds = documentIds.AsReadOnly(),
            Properties = (stackEntity.Properties ?? new Dictionary<string, object>()).AsReadOnly()
        };
    }

    private static StackGraphPath MapToStackPath(CoreGraphPath corePath)
    {
        // Get relationship types from relationship IDs if needed
        // For now, we'll just use the IDs as types since we don't have the full relationship objects
        var relationshipTypes = corePath.RelationshipIds.ToList();

        return new StackGraphPath(
            corePath.EntityIds.ToList(),
            relationshipTypes,
            corePath.TotalWeight);
    }

    private static StackGraphCommunity MapToStackCommunity(CoreGraphCommunity coreCommunity)
    {
        return new StackGraphCommunity(
            coreCommunity.Id,
            coreCommunity.Name,
            coreCommunity.EntityIds.ToList(),
            coreCommunity.Summary,
            coreCommunity.Level);
    }

    private static NamedEntityType ParseEntityType(string type)
    {
        if (Enum.TryParse<NamedEntityType>(type, ignoreCase: true, out var result))
        {
            return result;
        }
        return NamedEntityType.Unknown;
    }

    private static RelationType ParseRelationType(string type)
    {
        // Try parsing as enum
        if (Enum.TryParse<RelationType>(type, ignoreCase: true, out var result))
        {
            return result;
        }

        // Try common string mappings
        return type.ToUpperInvariant() switch
        {
            "PART_OF" => RelationType.PartOf,
            "LOCATED_IN" => RelationType.LocatedIn,
            "WORKS_FOR" => RelationType.WorksFor,
            "FOUNDED_BY" => RelationType.FoundedBy,
            "OWNS" => RelationType.Owns,
            "USES" => RelationType.Uses,
            "RELATED_TO" => RelationType.RelatedTo,
            "CAUSES" => RelationType.Causes,
            "ENABLES" => RelationType.Enables,
            "DEPENDS_ON" => RelationType.DependsOn,
            "INHERITS_FROM" => RelationType.InheritsFrom,
            "IMPLEMENTS" => RelationType.Implements,
            "CONTAINS" => RelationType.Contains,
            "PRECEDES" => RelationType.Precedes,
            "FOLLOWS" => RelationType.Follows,
            "IS_TYPE_OF" => RelationType.IsTypeOf,
            "SYNONYM_OF" => RelationType.SynonymOf,
            "OPPOSITE_OF" => RelationType.OppositeOf,
            "COMPARES_TO" => RelationType.ComparesTo,
            _ => RelationType.Custom
        };
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Neo4j graph service initialized and available")]
    private static partial void LogServiceInitialized(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Neo4j graph service is not available (IGraphStore not registered)")]
    private static partial void LogServiceNotAvailable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Neo4j not available, returning empty related entities")]
    private static partial void LogNeo4jNotAvailableRelatedEntities(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} related entities for {EntityCount} source entities with {MaxHops} max hops")]
    private static partial void LogFoundRelatedEntities(ILogger logger, int count, int entityCount, int maxHops);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting related entities from Neo4j")]
    private static partial void LogGetRelatedEntitiesFailed(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Neo4j not available, returning empty query expansion")]
    private static partial void LogNeo4jNotAvailableQueryExpansion(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Expanded query '{Query}' with {Count} related terms")]
    private static partial void LogExpandedQuery(ILogger logger, string query, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error expanding query with related entities")]
    private static partial void LogExpandQueryFailed(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot store entity: Neo4j is not available")]
    private static partial void LogCannotStoreEntity(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored entity {EntityId} ({EntityName}) in Neo4j")]
    private static partial void LogStoredEntity(ILogger logger, string entityId, string entityName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error storing entity {EntityId} in Neo4j")]
    private static partial void LogStoreEntityFailed(ILogger logger, string entityId, Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot store relationship: Neo4j is not available")]
    private static partial void LogCannotStoreRelationship(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored relationship {Source} -[{Type}]-> {Target} in Neo4j")]
    private static partial void LogStoredRelationship(ILogger logger, string source, string type, string target);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error storing relationship {Source} -> {Target} in Neo4j")]
    private static partial void LogStoreRelationshipFailed(ILogger logger, string source, string target, Exception? exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Neo4j not available, returning empty paths")]
    private static partial void LogNeo4jNotAvailablePaths(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} paths between {Source} and {Target}")]
    private static partial void LogFoundPaths(ILogger logger, int count, string source, string target);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error finding paths between {Source} and {Target}")]
    private static partial void LogFindPathsFailed(ILogger logger, string source, string target, Exception? exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Neo4j not available, returning null community")]
    private static partial void LogNeo4jNotAvailableCommunity(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting community for entity {EntityId}")]
    private static partial void LogGetCommunityFailed(ILogger logger, string entityId, Exception? exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Neo4j not available, returning empty chunk list")]
    private static partial void LogNeo4jNotAvailableChunkList(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} chunks for {EntityCount} entities")]
    private static partial void LogFoundChunksForEntities(ILogger logger, int count, int entityCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting chunks for entities")]
    private static partial void LogGetChunksForEntitiesFailed(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot link chunk to entities: Neo4j is not available")]
    private static partial void LogCannotLinkChunkToEntities(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Linked chunk {ChunkId} to {EntityCount} entities")]
    private static partial void LogLinkedChunkToEntities(ILogger logger, Guid chunkId, int entityCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error linking chunk {ChunkId} to entities")]
    private static partial void LogLinkChunkFailed(ILogger logger, Guid chunkId, Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot run community detection: Neo4j is not available")]
    private static partial void LogCannotRunCommunityDetection(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Running community detection (collection: {CollectionId})")]
    private static partial void LogRunningCommunityDetection(ILogger logger, string collectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "No entities found for community detection")]
    private static partial void LogNoEntitiesForCommunityDetection(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Community detection completed. Created {Count} communities")]
    private static partial void LogCommunityDetectionCompleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error running community detection")]
    private static partial void LogCommunityDetectionFailed(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Neo4j not available, returning empty statistics")]
    private static partial void LogNeo4jNotAvailableStatistics(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting graph statistics")]
    private static partial void LogGetStatisticsFailed(ILogger logger, Exception? exception);

    #endregion
}
