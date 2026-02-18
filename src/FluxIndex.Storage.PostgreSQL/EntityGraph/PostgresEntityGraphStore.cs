using System.Text.Json;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FluxIndex.Storage.PostgreSQL.EntityGraph;

/// <summary>
/// PostgreSQL implementation of IGraphStore using adjacency list + recursive CTEs.
/// Provides entity graph storage for the Hybrid Tier in polyglot persistence.
/// </summary>
public partial class PostgresEntityGraphStore : IGraphStore
{
    private readonly EntityGraphDbContext _context;
    private readonly EntityGraphOptions _options;
    private readonly ILogger<PostgresEntityGraphStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public PostgresEntityGraphStore(
        EntityGraphDbContext context,
        IOptions<EntityGraphOptions> options,
        ILogger<PostgresEntityGraphStore> logger)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    #region Entity Operations

    public async Task<string> StoreEntityAsync(GraphEntity entity, CancellationToken ct = default)
    {
        var dbEntity = MapToDbEntity(entity);

        var existing = await _context.Entities.FindAsync([entity.Id], ct);
        if (existing != null)
        {
            _context.Entry(existing).CurrentValues.SetValues(dbEntity);
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            _context.Entities.Add(dbEntity);
        }

        await _context.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<IReadOnlyList<string>> StoreEntitiesBatchAsync(
        IEnumerable<GraphEntity> entities,
        CancellationToken ct = default)
    {
        var ids = new List<string>();
        var dbEntities = entities.Select(MapToDbEntity).ToList();

        foreach (var dbEntity in dbEntities)
        {
            var existing = await _context.Entities.FindAsync([dbEntity.Id], ct);
            if (existing != null)
            {
                _context.Entry(existing).CurrentValues.SetValues(dbEntity);
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _context.Entities.Add(dbEntity);
            }
            ids.Add(dbEntity.Id);
        }

        await _context.SaveChangesAsync(ct);
        return ids;
    }

    public async Task<GraphEntity?> GetEntityByIdAsync(string id, CancellationToken ct = default)
    {
        var dbEntity = await _context.Entities.FindAsync([id], ct);
        return dbEntity != null ? MapToGraphEntity(dbEntity) : null;
    }

    public async Task<IReadOnlyList<GraphEntity>> GetEntitiesByNameAsync(
        string name,
        bool fuzzyMatch = false,
        CancellationToken ct = default)
    {
        var normalized = name.ToLowerInvariant().Trim();

        var query = fuzzyMatch
            ? _context.Entities.Where(e => e.NormalizedName.Contains(normalized))
            : _context.Entities.Where(e => e.NormalizedName == normalized);

        var dbEntities = await query.Take(_options.DefaultPageSize).ToListAsync(ct);
        return dbEntities.Select(MapToGraphEntity).ToList();
    }

    public async Task<IReadOnlyList<GraphEntity>> GetEntitiesByTypeAsync(
        NamedEntityType type,
        int limit = 100,
        CancellationToken ct = default)
    {
        var dbEntities = await _context.Entities
            .Where(e => e.EntityType == (int)type)
            .OrderByDescending(e => e.ImportanceScore)
            .Take(limit)
            .ToListAsync(ct);

        return dbEntities.Select(MapToGraphEntity).ToList();
    }

    public async Task<bool> UpdateEntityAsync(GraphEntity entity, CancellationToken ct = default)
    {
        var existing = await _context.Entities.FindAsync([entity.Id], ct);
        if (existing == null) return false;

        var dbEntity = MapToDbEntity(entity);
        _context.Entry(existing).CurrentValues.SetValues(dbEntity);
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteEntityAsync(string id, CancellationToken ct = default)
    {
        var entity = await _context.Entities.FindAsync([id], ct);
        if (entity == null) return false;

        _context.Entities.Remove(entity);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    #endregion

    #region Relationship Operations

    public async Task<string> StoreRelationshipAsync(
        GraphRelationship relationship,
        CancellationToken ct = default)
    {
        var dbEntity = MapToDbRelationship(relationship);

        var existing = await _context.Relationships.FindAsync([relationship.Id], ct);
        if (existing != null)
        {
            _context.Entry(existing).CurrentValues.SetValues(dbEntity);
        }
        else
        {
            _context.Relationships.Add(dbEntity);
        }

        await _context.SaveChangesAsync(ct);
        return relationship.Id;
    }

    public async Task<IReadOnlyList<string>> StoreRelationshipsBatchAsync(
        IEnumerable<GraphRelationship> relationships,
        CancellationToken ct = default)
    {
        var ids = new List<string>();
        var dbRelationships = relationships.Select(MapToDbRelationship).ToList();

        foreach (var dbRel in dbRelationships)
        {
            var existing = await _context.Relationships.FindAsync([dbRel.Id], ct);
            if (existing != null)
            {
                _context.Entry(existing).CurrentValues.SetValues(dbRel);
            }
            else
            {
                _context.Relationships.Add(dbRel);
            }
            ids.Add(dbRel.Id);
        }

        await _context.SaveChangesAsync(ct);
        return ids;
    }

    public async Task<IReadOnlyList<GraphRelationship>> GetRelationshipsAsync(
        string entityId,
        TraversalDirection direction = TraversalDirection.Both,
        CancellationToken ct = default)
    {
        var query = direction switch
        {
            TraversalDirection.Outgoing => _context.Relationships
                .Where(r => r.SourceEntityId == entityId),
            TraversalDirection.Incoming => _context.Relationships
                .Where(r => r.TargetEntityId == entityId),
            _ => _context.Relationships
                .Where(r => r.SourceEntityId == entityId || r.TargetEntityId == entityId)
        };

        var dbRelationships = await query.ToListAsync(ct);
        return dbRelationships.Select(MapToGraphRelationship).ToList();
    }

    public async Task<IReadOnlyList<GraphRelationship>> GetRelationshipsByTypeAsync(
        RelationType type,
        int limit = 100,
        CancellationToken ct = default)
    {
        var dbRelationships = await _context.Relationships
            .Where(r => r.RelationType == (int)type)
            .OrderByDescending(r => r.Weight)
            .Take(limit)
            .ToListAsync(ct);

        return dbRelationships.Select(MapToGraphRelationship).ToList();
    }

    public async Task<bool> DeleteRelationshipAsync(string relationshipId, CancellationToken ct = default)
    {
        var relationship = await _context.Relationships.FindAsync([relationshipId], ct);
        if (relationship == null) return false;

        _context.Relationships.Remove(relationship);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    #endregion

    #region Traversal Operations

    public async Task<GraphStoreTraversalResult> TraverseAsync(
        string startEntityId,
        GraphStoreTraversalOptions options,
        CancellationToken ct = default)
    {
        var startEntity = await GetEntityByIdAsync(startEntityId, ct);
        if (startEntity == null)
        {
            return new GraphStoreTraversalResult
            {
                StartEntity = new GraphEntity
                {
                    Id = startEntityId,
                    Name = "Not Found"
                },
                Entities = [],
                Relationships = [],
                Paths = new Dictionary<string, GraphPath>(),
                WasTruncated = false
            };
        }

        // Use recursive CTE for traversal
        var sql = BuildTraversalCte(startEntityId, options);

        var traversedEntityIds = new HashSet<string> { startEntityId };
        var traversedRelationshipIds = new HashSet<string>();
        var paths = new Dictionary<string, GraphPath>();
        int maxDepthReached = 0;
        bool wasTruncated = false;

        try
        {
            // Execute raw SQL for recursive CTE
            var results = await _context.Database
                .SqlQueryRaw<TraversalResultRow>(sql)
                .ToListAsync(ct);

            foreach (var row in results)
            {
                traversedEntityIds.Add(row.EntityId);
                if (!string.IsNullOrEmpty(row.RelationshipId))
                {
                    traversedRelationshipIds.Add(row.RelationshipId);
                }
                maxDepthReached = Math.Max(maxDepthReached, row.Depth);

                // Build path
                if (!paths.ContainsKey(row.EntityId))
                {
                    var pathEntityIds = row.Path?.Split(',').ToList() ?? [row.EntityId];
                    paths[row.EntityId] = new GraphPath
                    {
                        EntityIds = pathEntityIds,
                        RelationshipIds = [], // Would need more complex query to track
                        TotalWeight = row.TotalWeight
                    };
                }

                if (traversedEntityIds.Count >= options.MaxNodes)
                {
                    wasTruncated = true;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LogTraversalCteFailed(_logger, ex);
            // Fallback to iterative BFS
            await TraverseIterativeAsync(
                startEntityId, options, traversedEntityIds,
                traversedRelationshipIds, ct);
        }

        // Load full entities
        var entities = await _context.Entities
            .Where(e => traversedEntityIds.Contains(e.Id))
            .ToListAsync(ct);

        var relationships = await _context.Relationships
            .Where(r => traversedRelationshipIds.Contains(r.Id) ||
                       (traversedEntityIds.Contains(r.SourceEntityId) &&
                        traversedEntityIds.Contains(r.TargetEntityId)))
            .ToListAsync(ct);

        return new GraphStoreTraversalResult
        {
            StartEntity = startEntity,
            Entities = entities.Select(MapToGraphEntity).ToList(),
            Relationships = relationships.Select(MapToGraphRelationship).ToList(),
            Paths = paths,
            MaxDepthReached = maxDepthReached,
            WasTruncated = wasTruncated
        };
    }

    public async Task<GraphPath?> FindShortestPathAsync(
        string sourceEntityId,
        string targetEntityId,
        int maxDepth = 5,
        CancellationToken ct = default)
    {
        // Dijkstra-like shortest path using recursive CTE
        var sql = $@"
WITH RECURSIVE shortest_path AS (
    -- Base case: start from source
    SELECT
        r.target_entity_id as entity_id,
        r.id as relationship_id,
        r.weight as total_weight,
        1 as depth,
        ARRAY['{sourceEntityId}', r.target_entity_id] as path,
        ARRAY[r.id] as rel_path
    FROM entity_graph_relationships r
    WHERE r.source_entity_id = '{sourceEntityId}'

    UNION ALL

    -- Recursive case
    SELECT
        r.target_entity_id,
        r.id,
        sp.total_weight + r.weight,
        sp.depth + 1,
        sp.path || r.target_entity_id,
        sp.rel_path || r.id
    FROM shortest_path sp
    JOIN entity_graph_relationships r ON r.source_entity_id = sp.entity_id
    WHERE sp.depth < {maxDepth}
      AND NOT r.target_entity_id = ANY(sp.path)
)
SELECT
    path,
    rel_path,
    total_weight
FROM shortest_path
WHERE entity_id = '{targetEntityId}'
ORDER BY total_weight
LIMIT 1";

        try
        {
            var result = await _context.Database
                .SqlQueryRaw<ShortestPathRow>(sql)
                .FirstOrDefaultAsync(ct);

            if (result == null) return null;

            return new GraphPath
            {
                EntityIds = result.Path?.ToList() ?? [],
                RelationshipIds = result.RelPath?.ToList() ?? [],
                TotalWeight = result.TotalWeight
            };
        }
        catch (Exception ex)
        {
            LogShortestPathCteFailed(_logger, ex);
            return null;
        }
    }

    public async Task<IReadOnlyList<GraphEntity>> GetNeighborsAsync(
        string entityId,
        int depth = 1,
        CancellationToken ct = default)
    {
        var result = await TraverseAsync(entityId, new GraphStoreTraversalOptions
        {
            MaxDepth = depth,
            MaxNodes = 100,
            Direction = TraversalDirection.Both
        }, ct);

        return result.Entities.Where(e => e.Id != entityId).ToList();
    }

    public async Task<IReadOnlyList<GraphEntity>> GetEntitiesByChunkIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken ct = default)
    {
        var chunkIdSet = chunkIds.ToHashSet();

        // Query entities that have any of the chunk IDs in their chunk_ids JSON array
        var entities = await _context.Entities
            .Where(e => chunkIdSet.Any(id => e.ChunkIdsJson.Contains(id)))
            .ToListAsync(ct);

        return entities.Select(MapToGraphEntity).ToList();
    }

    #endregion

    #region Community Operations

    public async Task<string> StoreCommunityAsync(
        GraphCommunity community,
        CancellationToken ct = default)
    {
        var dbEntity = MapToDbCommunity(community);

        var existing = await _context.Communities.FindAsync([community.Id], ct);
        if (existing != null)
        {
            _context.Entry(existing).CurrentValues.SetValues(dbEntity);
        }
        else
        {
            _context.Communities.Add(dbEntity);
        }

        // Update memberships
        var existingMembers = await _context.CommunityMembers
            .Where(m => m.CommunityId == community.Id)
            .ToListAsync(ct);

        _context.CommunityMembers.RemoveRange(existingMembers);

        foreach (var entityId in community.EntityIds)
        {
            _context.CommunityMembers.Add(new EntityCommunityMemberEntity
            {
                CommunityId = community.Id,
                EntityId = entityId,
                MembershipScore = 1.0,
                JoinedAt = DateTimeOffset.UtcNow
            });
        }

        await _context.SaveChangesAsync(ct);
        return community.Id;
    }

    public async Task<GraphCommunity?> GetCommunityByIdAsync(
        string communityId,
        CancellationToken ct = default)
    {
        var dbCommunity = await _context.Communities
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Id == communityId, ct);

        return dbCommunity != null ? MapToGraphCommunity(dbCommunity) : null;
    }

    public async Task<IReadOnlyList<GraphCommunity>> GetCommunitiesForEntityAsync(
        string entityId,
        CancellationToken ct = default)
    {
        var communityIds = await _context.CommunityMembers
            .Where(m => m.EntityId == entityId)
            .Select(m => m.CommunityId)
            .ToListAsync(ct);

        var communities = await _context.Communities
            .Include(c => c.Members)
            .Where(c => communityIds.Contains(c.Id))
            .ToListAsync(ct);

        return communities.Select(MapToGraphCommunity).ToList();
    }

    public async Task<IReadOnlyList<GraphCommunity>> GetTopCommunitiesAsync(
        int limit = 10,
        CancellationToken ct = default)
    {
        var communities = await _context.Communities
            .Include(c => c.Members)
            .OrderByDescending(c => c.ImportanceScore)
            .Take(limit)
            .ToListAsync(ct);

        return communities.Select(MapToGraphCommunity).ToList();
    }

    #endregion

    #region Statistics and Maintenance

    public async Task<GraphStoreStatistics> GetStatisticsAsync(CancellationToken ct = default)
    {
        var entityCount = await _context.Entities.LongCountAsync(ct);
        var relationshipCount = await _context.Relationships.LongCountAsync(ct);
        var communityCount = await _context.Communities.LongCountAsync(ct);

        var entityCountsByType = await _context.Entities
            .GroupBy(e => e.EntityType)
            .Select(g => new { Type = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(x => (NamedEntityType)x.Type, x => x.Count, ct);

        var relationshipCountsByType = await _context.Relationships
            .GroupBy(r => r.RelationType)
            .Select(g => new { Type = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(x => (RelationType)x.Type, x => x.Count, ct);

        return new GraphStoreStatistics
        {
            EntityCount = entityCount,
            RelationshipCount = relationshipCount,
            CommunityCount = communityCount,
            EntityCountsByType = entityCountsByType,
            RelationshipCountsByType = relationshipCountsByType,
            AverageRelationshipsPerEntity = entityCount > 0
                ? (double)relationshipCount / entityCount
                : 0,
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _context.CommunityMembers.ExecuteDeleteAsync(ct);
        await _context.Communities.ExecuteDeleteAsync(ct);
        await _context.Relationships.ExecuteDeleteAsync(ct);
        await _context.Entities.ExecuteDeleteAsync(ct);
    }

    #endregion

    #region Private Helpers

    private string BuildTraversalCte(string startEntityId, GraphStoreTraversalOptions options)
    {
        var directionCondition = options.Direction switch
        {
            TraversalDirection.Outgoing => "r.source_entity_id = t.entity_id",
            TraversalDirection.Incoming => "r.target_entity_id = t.entity_id",
            _ => "(r.source_entity_id = t.entity_id OR r.target_entity_id = t.entity_id)"
        };

        var nextEntityExpr = options.Direction switch
        {
            TraversalDirection.Outgoing => "r.target_entity_id",
            TraversalDirection.Incoming => "r.source_entity_id",
            _ => "CASE WHEN r.source_entity_id = t.entity_id THEN r.target_entity_id ELSE r.source_entity_id END"
        };

        var typeFilter = options.RelationTypes.Count > 0
            ? $"AND r.relation_type IN ({string.Join(",", options.RelationTypes.Select(t => (int)t))})"
            : "";

        var weightFilter = options.MinWeight > 0
            ? $"AND r.weight >= {options.MinWeight}"
            : "";

        return $@"
WITH RECURSIVE traversal AS (
    -- Base case: start entity
    SELECT
        '{startEntityId}'::text as entity_id,
        NULL::text as relationship_id,
        0 as depth,
        '{startEntityId}'::text as path,
        0.0::float8 as total_weight

    UNION ALL

    -- Recursive case: traverse relationships
    SELECT
        {nextEntityExpr} as entity_id,
        r.id as relationship_id,
        t.depth + 1 as depth,
        t.path || ',' || {nextEntityExpr} as path,
        t.total_weight + r.weight as total_weight
    FROM traversal t
    JOIN entity_graph_relationships r ON {directionCondition}
    WHERE t.depth < {Math.Min(options.MaxDepth, _options.MaxTraversalDepth)}
      AND NOT t.path LIKE '%' || {nextEntityExpr} || '%'
      {typeFilter}
      {weightFilter}
)
SELECT DISTINCT ON (entity_id)
    entity_id as EntityId,
    relationship_id as RelationshipId,
    depth as Depth,
    path as Path,
    total_weight as TotalWeight
FROM traversal
ORDER BY entity_id, depth
LIMIT {options.MaxNodes}";
    }

    private async Task TraverseIterativeAsync(
        string startEntityId,
        GraphStoreTraversalOptions options,
        HashSet<string> visitedEntities,
        HashSet<string> visitedRelationships,
        CancellationToken ct)
    {
        var queue = new Queue<(string EntityId, int Depth)>();
        queue.Enqueue((startEntityId, 0));

        while (queue.Count > 0 && visitedEntities.Count < options.MaxNodes)
        {
            var (currentId, depth) = queue.Dequeue();

            if (depth >= options.MaxDepth) continue;

            var relationships = await GetRelationshipsAsync(currentId, options.Direction, ct);

            foreach (var rel in relationships)
            {
                visitedRelationships.Add(rel.Id);

                var nextId = rel.SourceEntityId == currentId
                    ? rel.TargetEntityId
                    : rel.SourceEntityId;

                if (visitedEntities.Add(nextId))
                {
                    queue.Enqueue((nextId, depth + 1));
                }
            }
        }
    }

    private EntityGraphEntity MapToDbEntity(GraphEntity entity)
    {
        return new EntityGraphEntity
        {
            Id = entity.Id,
            Name = entity.Name,
            NormalizedName = entity.NormalizedName.Length > 0
                ? entity.NormalizedName
                : entity.Name.ToLowerInvariant().Trim(),
            EntityType = (int)entity.Type,
            Description = entity.Description,
            Embedding = entity.Embedding != null ? new Vector(entity.Embedding) : null,
            Confidence = entity.Confidence,
            ImportanceScore = entity.ImportanceScore,
            MentionCount = entity.MentionCount,
            SurfaceFormsJson = JsonSerializer.Serialize(entity.SurfaceForms, _jsonOptions),
            ChunkIdsJson = JsonSerializer.Serialize(entity.ChunkIds, _jsonOptions),
            DocumentIdsJson = JsonSerializer.Serialize(entity.DocumentIds, _jsonOptions),
            ExternalLinksJson = JsonSerializer.Serialize(entity.ExternalLinks, _jsonOptions),
            PropertiesJson = JsonSerializer.Serialize(entity.Properties, _jsonOptions),
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private GraphEntity MapToGraphEntity(EntityGraphEntity db)
    {
        return new GraphEntity
        {
            Id = db.Id,
            Name = db.Name,
            NormalizedName = db.NormalizedName,
            Type = (NamedEntityType)db.EntityType,
            Description = db.Description,
            Embedding = db.Embedding?.ToArray(),
            Confidence = db.Confidence,
            ImportanceScore = db.ImportanceScore,
            MentionCount = db.MentionCount,
            SurfaceForms = JsonSerializer.Deserialize<List<string>>(db.SurfaceFormsJson, _jsonOptions) ?? [],
            ChunkIds = JsonSerializer.Deserialize<List<string>>(db.ChunkIdsJson, _jsonOptions) ?? [],
            DocumentIds = JsonSerializer.Deserialize<List<string>>(db.DocumentIdsJson, _jsonOptions) ?? [],
            ExternalLinks = JsonSerializer.Deserialize<Dictionary<string, string>>(db.ExternalLinksJson, _jsonOptions) ?? new(),
            Properties = JsonSerializer.Deserialize<Dictionary<string, object>>(db.PropertiesJson, _jsonOptions) ?? new(),
            CreatedAt = db.CreatedAt,
            UpdatedAt = db.UpdatedAt
        };
    }

    private EntityGraphRelationshipEntity MapToDbRelationship(GraphRelationship rel)
    {
        return new EntityGraphRelationshipEntity
        {
            Id = rel.Id,
            SourceEntityId = rel.SourceEntityId,
            TargetEntityId = rel.TargetEntityId,
            RelationType = (int)rel.Type,
            Label = rel.Label,
            Confidence = rel.Confidence,
            Weight = rel.Weight,
            IsDirectional = rel.IsDirectional,
            EvidenceChunkIdsJson = JsonSerializer.Serialize(rel.EvidenceChunkIds, _jsonOptions),
            EvidenceTextsJson = JsonSerializer.Serialize(rel.EvidenceTexts, _jsonOptions),
            PropertiesJson = JsonSerializer.Serialize(rel.Properties, _jsonOptions),
            CreatedAt = rel.CreatedAt
        };
    }

    private GraphRelationship MapToGraphRelationship(EntityGraphRelationshipEntity db)
    {
        return new GraphRelationship
        {
            Id = db.Id,
            SourceEntityId = db.SourceEntityId,
            TargetEntityId = db.TargetEntityId,
            Type = (RelationType)db.RelationType,
            Label = db.Label,
            Confidence = db.Confidence,
            Weight = db.Weight,
            IsDirectional = db.IsDirectional,
            EvidenceChunkIds = JsonSerializer.Deserialize<List<string>>(db.EvidenceChunkIdsJson, _jsonOptions) ?? [],
            EvidenceTexts = JsonSerializer.Deserialize<List<string>>(db.EvidenceTextsJson, _jsonOptions) ?? [],
            Properties = JsonSerializer.Deserialize<Dictionary<string, object>>(db.PropertiesJson, _jsonOptions) ?? new(),
            CreatedAt = db.CreatedAt
        };
    }

    private EntityCommunityEntity MapToDbCommunity(GraphCommunity community)
    {
        return new EntityCommunityEntity
        {
            Id = community.Id,
            Name = community.Name,
            Summary = community.Summary,
            ImportanceScore = community.ImportanceScore,
            Level = community.Level,
            ParentCommunityId = community.ParentCommunityId,
            Embedding = community.Embedding != null ? new Vector(community.Embedding) : null,
            TopicsJson = JsonSerializer.Serialize(community.Topics, _jsonOptions),
            CreatedAt = community.CreatedAt
        };
    }

    private GraphCommunity MapToGraphCommunity(EntityCommunityEntity db)
    {
        return new GraphCommunity
        {
            Id = db.Id,
            Name = db.Name,
            Summary = db.Summary,
            ImportanceScore = db.ImportanceScore,
            Level = db.Level,
            ParentCommunityId = db.ParentCommunityId,
            Embedding = db.Embedding?.ToArray(),
            Topics = JsonSerializer.Deserialize<List<string>>(db.TopicsJson, _jsonOptions) ?? [],
            EntityIds = db.Members.Select(m => m.EntityId).ToList(),
            CreatedAt = db.CreatedAt
        };
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "Traversal CTE failed, falling back to iterative traversal")]
    private static partial void LogTraversalCteFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shortest path CTE failed")]
    private static partial void LogShortestPathCteFailed(ILogger logger, Exception exception);

    #endregion

    #region Helper Types

    private sealed class TraversalResultRow
    {
        public string EntityId { get; set; } = string.Empty;
        public string? RelationshipId { get; set; }
        public int Depth { get; set; }
        public string? Path { get; set; }
        public double TotalWeight { get; set; }
    }

    private sealed class ShortestPathRow
    {
        public string[]? Path { get; set; }
        public string[]? RelPath { get; set; }
        public double TotalWeight { get; set; }
    }

    #endregion
}
