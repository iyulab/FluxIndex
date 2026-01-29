using System.Text.Json;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Storage.SQLite.Graph;

/// <summary>
/// SQLite implementation of IGraphStore for entity graph storage.
/// Provides entity graph storage for local mode.
/// </summary>
public class SQLiteEntityGraphStore : IGraphStore
{
    private readonly SQLiteEntityGraphDbContext _context;
    private readonly SQLiteEntityGraphOptions _options;
    private readonly ILogger<SQLiteEntityGraphStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public SQLiteEntityGraphStore(
        SQLiteEntityGraphDbContext context,
        IOptions<SQLiteEntityGraphOptions> options,
        ILogger<SQLiteEntityGraphStore> logger)
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
                StartEntity = new GraphEntity { Id = startEntityId, Name = "Not Found" },
                Entities = [],
                Relationships = [],
                Paths = new Dictionary<string, GraphPath>(),
                WasTruncated = false
            };
        }

        // Simple BFS traversal using iterative approach
        var visited = new HashSet<string> { startEntityId };
        var traversedRelationshipIds = new HashSet<string>();
        var paths = new Dictionary<string, GraphPath>
        {
            [startEntityId] = new GraphPath
            {
                EntityIds = [startEntityId],
                RelationshipIds = [],
                TotalWeight = 0
            }
        };

        var currentLevel = new List<string> { startEntityId };
        var maxDepth = Math.Min(options.MaxDepth, _options.MaxTraversalDepth);

        for (int depth = 0; depth < maxDepth && currentLevel.Count > 0; depth++)
        {
            if (visited.Count >= options.MaxNodes)
                break;

            var nextLevel = new List<string>();

            foreach (var entityId in currentLevel)
            {
                var entityRelationships = await GetRelationshipsAsync(entityId, options.Direction, ct);

                foreach (var rel in entityRelationships)
                {
                    if (options.RelationTypes.Count > 0 && !options.RelationTypes.Contains(rel.Type))
                        continue;

                    var neighborId = rel.SourceEntityId == entityId ? rel.TargetEntityId : rel.SourceEntityId;

                    if (!visited.Contains(neighborId))
                    {
                        visited.Add(neighborId);
                        traversedRelationshipIds.Add(rel.Id);

                        var parentPath = paths[entityId];
                        paths[neighborId] = new GraphPath
                        {
                            EntityIds = [.. parentPath.EntityIds, neighborId],
                            RelationshipIds = [.. parentPath.RelationshipIds, rel.Id],
                            TotalWeight = parentPath.TotalWeight + rel.Weight
                        };

                        nextLevel.Add(neighborId);

                        if (visited.Count >= options.MaxNodes)
                            break;
                    }
                }

                if (visited.Count >= options.MaxNodes)
                    break;
            }

            currentLevel = nextLevel;
        }

        // Fetch all entities
        var entities = new List<GraphEntity>();
        foreach (var id in visited)
        {
            var entity = await GetEntityByIdAsync(id, ct);
            if (entity != null)
                entities.Add(entity);
        }

        // Fetch all relationships
        var relationships = new List<GraphRelationship>();
        foreach (var relId in traversedRelationshipIds)
        {
            var rel = await _context.Relationships.FindAsync([relId], ct);
            if (rel != null)
                relationships.Add(MapToGraphRelationship(rel));
        }

        return new GraphStoreTraversalResult
        {
            StartEntity = startEntity,
            Entities = entities,
            Relationships = relationships,
            Paths = paths,
            WasTruncated = visited.Count >= options.MaxNodes
        };
    }

    public async Task<GraphPath?> FindShortestPathAsync(
        string sourceEntityId,
        string targetEntityId,
        int maxDepth = 5,
        CancellationToken ct = default)
    {
        // Simple BFS for shortest path
        if (sourceEntityId == targetEntityId)
        {
            return new GraphPath
            {
                EntityIds = [sourceEntityId],
                RelationshipIds = [],
                TotalWeight = 0
            };
        }

        var visited = new Dictionary<string, (string? Parent, string? RelId, double Weight)>
        {
            [sourceEntityId] = (null, null, 0)
        };

        var queue = new Queue<string>();
        queue.Enqueue(sourceEntityId);

        int depth = 0;
        int levelSize = 1;
        int nextLevelSize = 0;

        while (queue.Count > 0 && depth < maxDepth)
        {
            var currentId = queue.Dequeue();
            levelSize--;

            var relationships = await GetRelationshipsAsync(currentId, TraversalDirection.Both, ct);

            foreach (var rel in relationships)
            {
                var neighborId = rel.SourceEntityId == currentId ? rel.TargetEntityId : rel.SourceEntityId;

                if (!visited.ContainsKey(neighborId))
                {
                    var parentWeight = visited[currentId].Weight;
                    visited[neighborId] = (currentId, rel.Id, parentWeight + rel.Weight);
                    queue.Enqueue(neighborId);
                    nextLevelSize++;

                    if (neighborId == targetEntityId)
                    {
                        // Found target - reconstruct path
                        var pathEntities = new List<string>();
                        var pathRels = new List<string>();
                        var current = targetEntityId;

                        while (current != null)
                        {
                            pathEntities.Insert(0, current);
                            var (parent, relId, _) = visited[current];
                            if (relId != null)
                                pathRels.Insert(0, relId);
                            current = parent;
                        }

                        return new GraphPath
                        {
                            EntityIds = pathEntities,
                            RelationshipIds = pathRels,
                            TotalWeight = visited[targetEntityId].Weight
                        };
                    }
                }
            }

            if (levelSize == 0)
            {
                levelSize = nextLevelSize;
                nextLevelSize = 0;
                depth++;
            }
        }

        return null; // No path found
    }

    public async Task<IReadOnlyList<GraphEntity>> GetNeighborsAsync(
        string entityId,
        int depth = 1,
        CancellationToken ct = default)
    {
        var relationships = await GetRelationshipsAsync(entityId, TraversalDirection.Both, ct);
        var neighborIds = relationships
            .Select(r => r.SourceEntityId == entityId ? r.TargetEntityId : r.SourceEntityId)
            .Distinct();

        var neighbors = new List<GraphEntity>();
        foreach (var id in neighborIds)
        {
            var entity = await GetEntityByIdAsync(id, ct);
            if (entity != null)
                neighbors.Add(entity);
        }

        return neighbors;
    }

    public async Task<IReadOnlyList<GraphEntity>> GetEntitiesByChunkIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken ct = default)
    {
        var chunkIdSet = chunkIds.ToHashSet();
        var entities = new List<GraphEntity>();

        var allEntities = await _context.Entities
            .Take(_options.DefaultPageSize * 10)
            .ToListAsync(ct);

        foreach (var dbEntity in allEntities)
        {
            var entityChunkIds = JsonSerializer.Deserialize<List<string>>(dbEntity.ChunkIdsJson, _jsonOptions) ?? [];
            if (entityChunkIds.Any(c => chunkIdSet.Contains(c)))
            {
                entities.Add(MapToGraphEntity(dbEntity));
            }
        }

        return entities;
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

        // Store member relationships
        foreach (var memberId in community.EntityIds)
        {
            var memberEntity = new SQLiteEntityCommunityMemberEntity
            {
                EntityId = memberId,
                CommunityId = community.Id,
                MembershipScore = 1.0,
                JoinedAt = DateTimeOffset.UtcNow
            };

            var existingMember = await _context.CommunityMembers
                .FirstOrDefaultAsync(m => m.EntityId == memberId && m.CommunityId == community.Id, ct);

            if (existingMember == null)
            {
                _context.CommunityMembers.Add(memberEntity);
            }
        }

        await _context.SaveChangesAsync(ct);
        return community.Id;
    }

    public async Task<GraphCommunity?> GetCommunityByIdAsync(
        string communityId,
        CancellationToken ct = default)
    {
        var dbCommunity = await _context.Communities.FindAsync([communityId], ct);
        if (dbCommunity == null) return null;

        // Get member entity IDs
        var memberIds = await _context.CommunityMembers
            .Where(m => m.CommunityId == communityId)
            .Select(m => m.EntityId)
            .ToListAsync(ct);

        return MapToGraphCommunity(dbCommunity, memberIds);
    }

    public async Task<IReadOnlyList<GraphCommunity>> GetCommunitiesForEntityAsync(
        string entityId,
        CancellationToken ct = default)
    {
        var communityIds = await _context.CommunityMembers
            .Where(m => m.EntityId == entityId)
            .Select(m => m.CommunityId)
            .ToListAsync(ct);

        var communities = new List<GraphCommunity>();
        foreach (var id in communityIds)
        {
            var community = await GetCommunityByIdAsync(id, ct);
            if (community != null)
                communities.Add(community);
        }

        return communities;
    }

    public async Task<IReadOnlyList<GraphCommunity>> GetTopCommunitiesAsync(
        int limit = 10,
        CancellationToken ct = default)
    {
        var dbCommunities = await _context.Communities
            .OrderByDescending(c => c.ImportanceScore)
            .Take(limit)
            .ToListAsync(ct);

        return dbCommunities.Select(c => MapToGraphCommunity(c)).ToList();
    }

    #endregion

    #region Statistics & Maintenance

    public async Task<GraphStoreStatistics> GetStatisticsAsync(CancellationToken ct = default)
    {
        var entityCount = await _context.Entities.CountAsync(ct);
        var relationshipCount = await _context.Relationships.CountAsync(ct);
        var communityCount = await _context.Communities.CountAsync(ct);

        var avgRelPerEntity = entityCount > 0
            ? (double)relationshipCount / entityCount
            : 0;

        return new GraphStoreStatistics
        {
            EntityCount = entityCount,
            RelationshipCount = relationshipCount,
            CommunityCount = communityCount,
            AverageRelationshipsPerEntity = avgRelPerEntity,
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        _context.CommunityMembers.RemoveRange(_context.CommunityMembers);
        _context.Communities.RemoveRange(_context.Communities);
        _context.Relationships.RemoveRange(_context.Relationships);
        _context.Entities.RemoveRange(_context.Entities);

        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("SQLite entity graph store cleared");
    }

    #endregion

    #region Mapping Methods

    private SQLiteEntityGraphEntity MapToDbEntity(GraphEntity entity)
    {
        return new SQLiteEntityGraphEntity
        {
            Id = entity.Id,
            Name = entity.Name,
            NormalizedName = entity.Name.ToLowerInvariant().Trim(),
            EntityType = (int)entity.Type,
            Description = entity.Description,
            Embedding = entity.Embedding != null ? VectorToBytes(entity.Embedding) : null,
            Confidence = entity.Confidence,
            ImportanceScore = entity.ImportanceScore,
            MentionCount = entity.MentionCount,
            SurfaceFormsJson = JsonSerializer.Serialize(entity.SurfaceForms ?? [], _jsonOptions),
            ChunkIdsJson = JsonSerializer.Serialize(entity.ChunkIds ?? [], _jsonOptions),
            DocumentIdsJson = JsonSerializer.Serialize(entity.DocumentIds ?? [], _jsonOptions),
            ExternalLinksJson = JsonSerializer.Serialize(entity.ExternalLinks ?? new Dictionary<string, string>(), _jsonOptions),
            PropertiesJson = JsonSerializer.Serialize(entity.Properties ?? new Dictionary<string, object>(), _jsonOptions),
            CreatedAt = entity.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private GraphEntity MapToGraphEntity(SQLiteEntityGraphEntity dbEntity)
    {
        return new GraphEntity
        {
            Id = dbEntity.Id,
            Name = dbEntity.Name,
            Type = (NamedEntityType)dbEntity.EntityType,
            Description = dbEntity.Description,
            Embedding = dbEntity.Embedding != null ? BytesToVector(dbEntity.Embedding) : null,
            Confidence = dbEntity.Confidence,
            ImportanceScore = dbEntity.ImportanceScore,
            MentionCount = dbEntity.MentionCount,
            SurfaceForms = JsonSerializer.Deserialize<List<string>>(dbEntity.SurfaceFormsJson, _jsonOptions) ?? [],
            ChunkIds = JsonSerializer.Deserialize<List<string>>(dbEntity.ChunkIdsJson, _jsonOptions) ?? [],
            DocumentIds = JsonSerializer.Deserialize<List<string>>(dbEntity.DocumentIdsJson, _jsonOptions) ?? [],
            ExternalLinks = JsonSerializer.Deserialize<Dictionary<string, string>>(dbEntity.ExternalLinksJson, _jsonOptions) ?? new(),
            Properties = JsonSerializer.Deserialize<Dictionary<string, object>>(dbEntity.PropertiesJson, _jsonOptions) ?? new(),
            CreatedAt = dbEntity.CreatedAt
        };
    }

    private SQLiteEntityGraphRelationshipEntity MapToDbRelationship(GraphRelationship relationship)
    {
        return new SQLiteEntityGraphRelationshipEntity
        {
            Id = relationship.Id,
            SourceEntityId = relationship.SourceEntityId,
            TargetEntityId = relationship.TargetEntityId,
            RelationType = (int)relationship.Type,
            Label = relationship.Label ?? string.Empty,
            Confidence = relationship.Confidence,
            Weight = relationship.Weight,
            IsDirectional = relationship.IsDirectional,
            EvidenceChunkIdsJson = JsonSerializer.Serialize(relationship.EvidenceChunkIds ?? [], _jsonOptions),
            EvidenceTextsJson = JsonSerializer.Serialize(relationship.EvidenceTexts ?? [], _jsonOptions),
            PropertiesJson = JsonSerializer.Serialize(relationship.Properties ?? new Dictionary<string, object>(), _jsonOptions),
            CreatedAt = relationship.CreatedAt
        };
    }

    private GraphRelationship MapToGraphRelationship(SQLiteEntityGraphRelationshipEntity dbRel)
    {
        return new GraphRelationship
        {
            Id = dbRel.Id,
            SourceEntityId = dbRel.SourceEntityId,
            TargetEntityId = dbRel.TargetEntityId,
            Type = (RelationType)dbRel.RelationType,
            Label = dbRel.Label,
            Confidence = dbRel.Confidence,
            Weight = dbRel.Weight,
            IsDirectional = dbRel.IsDirectional,
            EvidenceChunkIds = JsonSerializer.Deserialize<List<string>>(dbRel.EvidenceChunkIdsJson, _jsonOptions) ?? [],
            EvidenceTexts = JsonSerializer.Deserialize<List<string>>(dbRel.EvidenceTextsJson, _jsonOptions) ?? [],
            Properties = JsonSerializer.Deserialize<Dictionary<string, object>>(dbRel.PropertiesJson, _jsonOptions) ?? new(),
            CreatedAt = dbRel.CreatedAt
        };
    }

    private SQLiteEntityCommunityEntity MapToDbCommunity(GraphCommunity community)
    {
        return new SQLiteEntityCommunityEntity
        {
            Id = community.Id,
            Name = community.Name,
            Summary = community.Summary,
            ImportanceScore = community.ImportanceScore,
            Level = community.Level,
            ParentCommunityId = community.ParentCommunityId,
            Embedding = community.Embedding != null ? VectorToBytes(community.Embedding) : null,
            TopicsJson = JsonSerializer.Serialize(community.Topics ?? [], _jsonOptions),
            CreatedAt = community.CreatedAt
        };
    }

    private GraphCommunity MapToGraphCommunity(SQLiteEntityCommunityEntity dbCommunity, IReadOnlyList<string>? entityIds = null)
    {
        return new GraphCommunity
        {
            Id = dbCommunity.Id,
            Name = dbCommunity.Name,
            Summary = dbCommunity.Summary,
            ImportanceScore = dbCommunity.ImportanceScore,
            Level = dbCommunity.Level,
            ParentCommunityId = dbCommunity.ParentCommunityId,
            Embedding = dbCommunity.Embedding != null ? BytesToVector(dbCommunity.Embedding) : null,
            Topics = JsonSerializer.Deserialize<List<string>>(dbCommunity.TopicsJson, _jsonOptions) ?? [],
            EntityIds = entityIds ?? [],
            CreatedAt = dbCommunity.CreatedAt
        };
    }

    private static byte[] VectorToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToVector(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    #endregion
}
