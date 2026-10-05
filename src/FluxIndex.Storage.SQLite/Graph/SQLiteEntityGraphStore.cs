using System.Text.Json;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FluxIndex.Core.Application.Utilities;

namespace FluxIndex.Storage.SQLite.Graph;

/// <summary>
/// SQLite implementation of IGraphStore for entity graph storage.
/// Provides entity graph storage for local mode.
/// </summary>
public partial class SQLiteEntityGraphStore : IGraphStore
{
    private readonly IDbContextFactory<SQLiteEntityGraphDbContext> _contextFactory;
    private readonly SQLiteEntityGraphOptions _options;
    private readonly ILogger<SQLiteEntityGraphStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public SQLiteEntityGraphStore(
        IDbContextFactory<SQLiteEntityGraphDbContext> contextFactory,
        IOptions<SQLiteEntityGraphOptions> options,
        ILogger<SQLiteEntityGraphStore> logger)
    {
        _contextFactory = contextFactory;
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
        await StoreEntitiesBatchAsync([entity], ct);
        return entity.Id;
    }

    /// <remarks>
    /// A write merges into a stored row of the same id: the row keeps every chunk, document and surface form it already
    /// lists and gains the written ones (the other fields are replaced). The entity graph build derives a node's id from
    /// its identity, so two builds of one partition running at once write the same row; without the merge the second
    /// would erase the first one's provenance, and without the retry below its insert would fail on the key. The merge reads
    /// the stored row and writes it back; a concurrent write between the two moves the row's <c>UpdatedAt</c> token, so this
    /// write fails and is retried over a fresh read instead of dropping what the other writer added. Removing a chunk from an
    /// entity is <see cref="UpdateEntityAsync"/>'s job.
    /// </remarks>
    public async Task<IReadOnlyList<string>> StoreEntitiesBatchAsync(
        IEnumerable<GraphEntity> entities,
        CancellationToken ct = default)
    {
        var list = entities.ToList();
        return await RetryLostRaceAsync(() => StoreEntitiesOnceAsync(list, ct), insertsRace: true);
    }

    /// <summary>
    /// Attempts per entity write. A write retries when it lost a race - another writer changed a row between this write's
    /// read and its save, or inserted the same id first - so the last of N writers converging on one row needs N attempts.
    /// Ten covers the concurrent builds of one partition a host runs; a conflict on any row of a batch retries the whole
    /// batch over a fresh context.
    /// </summary>
    private const int MaxWriteAttempts = 10;

    private static async Task<T> RetryLostRaceAsync<T>(Func<Task<T>> write, bool insertsRace)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await write();
            }
            catch (DbUpdateException ex) when (attempt < MaxWriteAttempts && (insertsRace || ex is DbUpdateConcurrencyException))
            {
                // Read again and apply the write over what the other writer left.
            }
        }
    }

    private async Task<IReadOnlyList<string>> StoreEntitiesOnceAsync(IReadOnlyList<GraphEntity> entities, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var ids = new List<string>();

        foreach (var entity in entities)
        {
            // AsTracking is load-bearing here and at every SetValues below: the context is registered NoTracking, and
            // SetValues on a detached instance changes nothing SaveChanges writes — an update of an existing row would be
            // dropped without an error while inserts still land.
            var existing = await context.Entities.AsTracking().FirstOrDefaultAsync(e => e.Id == entity.Id, ct);
            if (existing != null)
            {
                context.Entry(existing).CurrentValues.SetValues(MapToDbEntity(WithStoredLists(entity, MapToGraphEntity(existing))));
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                context.Entities.Add(MapToDbEntity(entity));
            }
            ids.Add(entity.Id);
        }

        await context.SaveChangesAsync(ct);
        return ids;
    }

    private static GraphEntity WithStoredLists(GraphEntity written, GraphEntity stored) => written with
    {
        ChunkIds = stored.ChunkIds.Union(written.ChunkIds).ToList(),
        DocumentIds = stored.DocumentIds.Union(written.DocumentIds).ToList(),
        SurfaceForms = stored.SurfaceForms.Union(written.SurfaceForms).ToList(),
    };

    public async Task<GraphEntity?> GetEntityByIdAsync(string id, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await GetEntityByIdAsync(context, id, ct);
    }

    private async Task<GraphEntity?> GetEntityByIdAsync(SQLiteEntityGraphDbContext context, string id, CancellationToken ct)
    {
        var dbEntity = await context.Entities.FindAsync([id], ct);
        return dbEntity != null ? MapToGraphEntity(dbEntity) : null;
    }

    public async Task<IReadOnlyList<GraphEntity>> GetEntitiesByNameAsync(
        string name,
        bool fuzzyMatch = false,
        string partition = GraphPartition.Default,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        ArgumentNullException.ThrowIfNull(partition);
        var normalized = name.ToLowerInvariant().Trim();

        var inPartition = context.Entities.Where(e => e.Partition == partition);
        var query = fuzzyMatch
            ? inPartition.Where(e => e.NormalizedName.Contains(normalized))
            : inPartition.Where(e => e.NormalizedName == normalized);

        var dbEntities = await query.Take(_options.DefaultPageSize).ToListAsync(ct);
        return dbEntities.Select(MapToGraphEntity).ToList();
    }

    public async Task<IReadOnlyList<GraphEntity>> GetEntitiesByNormalizedNamesAsync(
        IEnumerable<string> normalizedNames,
        string partition = GraphPartition.Default,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        ArgumentNullException.ThrowIfNull(partition);
        var names = normalizedNames.Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0) return [];

        var dbEntities = await context.Entities
            .Where(e => e.Partition == partition && names.Contains(e.NormalizedName))
            .ToListAsync(ct);

        return dbEntities.Select(MapToGraphEntity).ToList();
    }

    public async Task<IReadOnlyList<GraphEntity>> GetEntitiesByTypeAsync(
        NamedEntityType type,
        int limit = 100,
        string partition = GraphPartition.Default,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        ArgumentNullException.ThrowIfNull(partition);
        var dbEntities = await context.Entities
            .Where(e => e.Partition == partition && e.EntityType == (int)type)
            .OrderByDescending(e => e.ImportanceScore)
            .Take(limit)
            .ToListAsync(ct);

        return dbEntities.Select(MapToGraphEntity).ToList();
    }

    /// <remarks>
    /// Replaces every field, so it is how a chunk is taken away from an entity. A write that lands between this call's read
    /// and its save is retried over, not merged: the caller's entity wins. A caller that computed the entity from an earlier
    /// read (as forgetting and reassigning chunks do) can therefore replace over chunks a concurrent build added since.
    /// </remarks>
    public Task<bool> UpdateEntityAsync(GraphEntity entity, CancellationToken ct = default) =>
        RetryLostRaceAsync(async () =>
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            var existing = await context.Entities.AsTracking().FirstOrDefaultAsync(e => e.Id == entity.Id, ct);
            if (existing == null) return false;

            var dbEntity = MapToDbEntity(entity);
            context.Entry(existing).CurrentValues.SetValues(dbEntity);
            existing.UpdatedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(ct);
            return true;
        }, insertsRace: false);

    public Task<bool> DeleteEntityAsync(string id, CancellationToken ct = default) =>
        RetryLostRaceAsync(async () =>
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            var entity = await context.Entities.FindAsync([id], ct);
            if (entity == null) return false;

            context.Entities.Remove(entity);
            await context.SaveChangesAsync(ct);
            return true;
        }, insertsRace: false);

    #endregion

    #region Relationship Operations

    public async Task<string> StoreRelationshipAsync(
        GraphRelationship relationship,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var dbEntity = MapToDbRelationship(relationship);

        var existing = await context.Relationships.AsTracking().FirstOrDefaultAsync(r => r.Id == relationship.Id, ct);
        if (existing != null)
        {
            context.Entry(existing).CurrentValues.SetValues(dbEntity);
        }
        else
        {
            context.Relationships.Add(dbEntity);
        }

        await context.SaveChangesAsync(ct);
        return relationship.Id;
    }

    public async Task<IReadOnlyList<string>> StoreRelationshipsBatchAsync(
        IEnumerable<GraphRelationship> relationships,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var ids = new List<string>();
        var dbRelationships = relationships.Select(MapToDbRelationship).ToList();

        foreach (var dbRel in dbRelationships)
        {
            var existing = await context.Relationships.AsTracking().FirstOrDefaultAsync(r => r.Id == dbRel.Id, ct);
            if (existing != null)
            {
                context.Entry(existing).CurrentValues.SetValues(dbRel);
            }
            else
            {
                context.Relationships.Add(dbRel);
            }
            ids.Add(dbRel.Id);
        }

        await context.SaveChangesAsync(ct);
        return ids;
    }

    public async Task<IReadOnlyList<GraphRelationship>> GetRelationshipsAsync(
        string entityId,
        TraversalDirection direction = TraversalDirection.Both,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await GetRelationshipsAsync(context, entityId, direction, ct);
    }

    private async Task<IReadOnlyList<GraphRelationship>> GetRelationshipsAsync(
        SQLiteEntityGraphDbContext context,
        string entityId,
        TraversalDirection direction,
        CancellationToken ct)
    {
        var query = direction switch
        {
            TraversalDirection.Outgoing => context.Relationships
                .Where(r => r.SourceEntityId == entityId),
            TraversalDirection.Incoming => context.Relationships
                .Where(r => r.TargetEntityId == entityId),
            _ => context.Relationships
                .Where(r => r.SourceEntityId == entityId || r.TargetEntityId == entityId)
        };

        var dbRelationships = await query.ToListAsync(ct);
        return dbRelationships.Select(MapToGraphRelationship).ToList();
    }

    public async Task<IReadOnlyList<GraphRelationship>> GetRelationshipsByTypeAsync(
        RelationType type,
        int limit = 100,
        string partition = GraphPartition.Default,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        ArgumentNullException.ThrowIfNull(partition);
        // A relationship belongs to the partition of the entities it connects; its source decides.
        var dbRelationships = await context.Relationships
            .Where(r => r.RelationType == (int)type
                && context.Entities.Any(e => e.Id == r.SourceEntityId && e.Partition == partition))
            .OrderByDescending(r => r.Weight)
            .Take(limit)
            .ToListAsync(ct);

        return dbRelationships.Select(MapToGraphRelationship).ToList();
    }

    public async Task<bool> DeleteRelationshipAsync(string relationshipId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var relationship = await context.Relationships.FindAsync([relationshipId], ct);
        if (relationship == null) return false;

        context.Relationships.Remove(relationship);
        await context.SaveChangesAsync(ct);
        return true;
    }

    #endregion

    #region Traversal Operations

    public async Task<GraphStoreTraversalResult> TraverseAsync(
        string startEntityId,
        GraphStoreTraversalOptions options,
        CancellationToken ct = default)
    {
        // One context for the whole walk rather than one per lookup.
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var startEntity = await GetEntityByIdAsync(context, startEntityId, ct);
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
                var entityRelationships = await GetRelationshipsAsync(context, entityId, options.Direction, ct);

                foreach (var rel in entityRelationships)
                {
                    if (options.RelationTypes.Count > 0 && !options.RelationTypes.Contains(rel.Type))
                        continue;

                    var neighborId = rel.SourceEntityId == entityId ? rel.TargetEntityId : rel.SourceEntityId;

                    if (visited.Add(neighborId))
                    {
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
            var entity = await GetEntityByIdAsync(context, id, ct);
            if (entity != null)
                entities.Add(entity);
        }

        // Fetch all relationships
        var relationships = new List<GraphRelationship>();
        foreach (var relId in traversedRelationshipIds)
        {
            var rel = await context.Relationships.FindAsync([relId], ct);
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
        // One context for the whole walk rather than one per lookup.
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
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

            var relationships = await GetRelationshipsAsync(context, currentId, TraversalDirection.Both, ct);

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
        // One context for the whole walk rather than one per lookup.
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var relationships = await GetRelationshipsAsync(context, entityId, TraversalDirection.Both, ct);
        var neighborIds = relationships
            .Select(r => r.SourceEntityId == entityId ? r.TargetEntityId : r.SourceEntityId)
            .Distinct();

        var neighbors = new List<GraphEntity>();
        foreach (var id in neighborIds)
        {
            var entity = await GetEntityByIdAsync(context, id, ct);
            if (entity != null)
                neighbors.Add(entity);
        }

        return neighbors;
    }

    public async Task<IReadOnlyList<GraphEntity>> GetEntitiesByChunkIdsAsync(
        IEnumerable<string> chunkIds,
        string partition = GraphPartition.Default,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        ArgumentNullException.ThrowIfNull(partition);
        var chunkIdList = chunkIds.Distinct().ToList();
        if (chunkIdList.Count == 0) return [];

        // Primitive-collection membership translates to json_each on the chunk_ids column, so the
        // scope is exact for any graph size (no page window, no substring match).
        var dbEntities = await context.Entities
            .Where(e => e.Partition == partition && e.ChunkIds.Any(id => chunkIdList.Contains(id)))
            .ToListAsync(ct);

        return dbEntities.Select(MapToGraphEntity).ToList();
    }

    #endregion

    #region Community Operations

    public async Task<string> StoreCommunityAsync(
        GraphCommunity community,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var dbEntity = MapToDbCommunity(community);

        var existing = await context.Communities.AsTracking().FirstOrDefaultAsync(c => c.Id == community.Id, ct);
        if (existing != null)
        {
            context.Entry(existing).CurrentValues.SetValues(dbEntity);
        }
        else
        {
            context.Communities.Add(dbEntity);
        }

        // Membership rows reference entities (foreign key). Replace the set so a re-stored community
        // does not keep members it no longer has. Chunk membership is the community row's own column.
        var staleMembers = await context.CommunityMembers
            .Where(m => m.CommunityId == community.Id)
            .ToListAsync(ct);
        context.CommunityMembers.RemoveRange(staleMembers);

        foreach (var entityId in community.EntityIds.Distinct())
        {
            context.CommunityMembers.Add(new SQLiteEntityCommunityMemberEntity
            {
                EntityId = entityId,
                CommunityId = community.Id,
                MembershipScore = 1.0,
                JoinedAt = DateTimeOffset.UtcNow
            });
        }

        await context.SaveChangesAsync(ct);
        return community.Id;
    }

    public async Task<int> DeleteCommunitiesAsync(IEnumerable<string> communityIds, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        ArgumentNullException.ThrowIfNull(communityIds);
        var ids = communityIds.Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return 0;

        var communities = await context.Communities.AsTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        if (communities.Count == 0) return 0;

        context.CommunityMembers.RemoveRange(
            await context.CommunityMembers.Where(m => ids.Contains(m.CommunityId)).ToListAsync(ct));
        context.Communities.RemoveRange(communities);
        await context.SaveChangesAsync(ct);
        return communities.Count;
    }

    public async Task<GraphCommunity?> GetCommunityByIdAsync(
        string communityId,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var dbCommunity = await context.Communities.FindAsync([communityId], ct);
        if (dbCommunity == null) return null;

        return (await MapWithMembersAsync(context, [dbCommunity], ct)).Single();
    }

    public async Task<IReadOnlyList<GraphCommunity>> GetCommunitiesForEntityAsync(
        string entityId,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var communityIds = await context.CommunityMembers
            .Where(m => m.EntityId == entityId)
            .Select(m => m.CommunityId)
            .Distinct()
            .ToListAsync(ct);
        if (communityIds.Count == 0) return [];

        var dbCommunities = await context.Communities
            .Where(c => communityIds.Contains(c.Id))
            .ToListAsync(ct);

        return await MapWithMembersAsync(context, dbCommunities, ct);
    }

    public async Task<IReadOnlyList<GraphCommunity>> GetTopCommunitiesAsync(
        int limit = 10,
        string partition = GraphPartition.Default,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        ArgumentNullException.ThrowIfNull(partition);
        var dbCommunities = await context.Communities
            .Where(c => c.Partition == partition)
            .OrderByDescending(c => c.ImportanceScore)
            .Take(limit)
            .ToListAsync(ct);

        return await MapWithMembersAsync(context, dbCommunities, ct);
    }

    public async Task<IReadOnlyList<GraphCommunity>> GetCommunitiesByChunkIdsAsync(
        IEnumerable<string> chunkIds,
        string partition = GraphPartition.Default,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        ArgumentNullException.ThrowIfNull(partition);
        var chunkIdList = chunkIds.Distinct().ToList();
        if (chunkIdList.Count == 0) return [];

        // Primitive-collection membership translates to json_each on the chunk_ids column.
        var dbCommunities = await context.Communities
            .Where(c => c.Partition == partition && c.ChunkIds != null && c.ChunkIds.Any(id => chunkIdList.Contains(id)))
            .ToListAsync(ct);

        return await MapWithMembersAsync(context, dbCommunities, ct);
    }

    private async Task<IReadOnlyList<GraphCommunity>> MapWithMembersAsync(
        SQLiteEntityGraphDbContext context,
        List<SQLiteEntityCommunityEntity> dbCommunities,
        CancellationToken ct)
    {
        if (dbCommunities.Count == 0) return [];

        var ids = dbCommunities.Select(c => c.Id).ToList();
        var membersByCommunity = (await context.CommunityMembers
                .Where(m => ids.Contains(m.CommunityId))
                .Select(m => new { m.CommunityId, m.EntityId })
                .ToListAsync(ct))
            .GroupBy(m => m.CommunityId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(m => m.EntityId).Distinct().ToList());

        return dbCommunities
            .Select(c => MapToGraphCommunity(c, membersByCommunity.GetValueOrDefault(c.Id)))
            .ToList();
    }

    #endregion

    #region Statistics & Maintenance

    public async Task<GraphStoreStatistics> GetStatisticsAsync(string partition = GraphPartition.Default, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        ArgumentNullException.ThrowIfNull(partition);
        var entityCount = await context.Entities.CountAsync(e => e.Partition == partition, ct);
        var relationshipCount = await context.Relationships
            .CountAsync(r => context.Entities.Any(e => e.Id == r.SourceEntityId && e.Partition == partition), ct);
        var communityCount = await context.Communities.CountAsync(c => c.Partition == partition, ct);

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
        // Set-based deletes (the PostgreSQL store's shape): a per-row DELETE would carry the entity's UpdatedAt token and
        // fail when a build wrote a row while the store was being cleared.
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await context.CommunityMembers.ExecuteDeleteAsync(ct);
        await context.Communities.ExecuteDeleteAsync(ct);
        await context.Relationships.ExecuteDeleteAsync(ct);
        await context.Entities.ExecuteDeleteAsync(ct);
        LogStoreCleared(_logger);
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "SQLite entity graph store cleared")]
    private static partial void LogStoreCleared(ILogger logger);

    #endregion

    #region Mapping Methods

    private SQLiteEntityGraphEntity MapToDbEntity(GraphEntity entity)
    {
        return new SQLiteEntityGraphEntity
        {
            Id = entity.Id,
            Name = entity.Name,
            NormalizedName = entity.NormalizedName.Length > 0 ? entity.NormalizedName : entity.Name.ToLowerInvariant().Trim(),
            Partition = entity.Partition ?? throw new ArgumentException("GraphEntity.Partition must not be null.", nameof(entity)),
            EntityType = (int)entity.Type,
            Description = entity.Description,
            Embedding = entity.Embedding != null ? VectorToBytes(entity.Embedding) : null,
            Confidence = entity.Confidence,
            ImportanceScore = entity.ImportanceScore,
            MentionCount = entity.MentionCount,
            SurfaceFormsJson = JsonSerializer.Serialize(entity.SurfaceForms ?? [], _jsonOptions),
            ChunkIds = (entity.ChunkIds ?? []).Distinct().ToList(),
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
            NormalizedName = dbEntity.NormalizedName,
            Partition = dbEntity.Partition,
            Type = (NamedEntityType)dbEntity.EntityType,
            Description = dbEntity.Description,
            Embedding = dbEntity.Embedding != null ? BytesToVector(dbEntity.Embedding) : null,
            Confidence = dbEntity.Confidence,
            ImportanceScore = dbEntity.ImportanceScore,
            MentionCount = dbEntity.MentionCount,
            SurfaceForms = JsonSerializer.Deserialize<List<string>>(dbEntity.SurfaceFormsJson, _jsonOptions) ?? [],
            ChunkIds = dbEntity.ChunkIds ?? [],
            DocumentIds = JsonSerializer.Deserialize<List<string>>(dbEntity.DocumentIdsJson, _jsonOptions) ?? [],
            ExternalLinks = JsonSerializer.Deserialize<Dictionary<string, string>>(dbEntity.ExternalLinksJson, _jsonOptions) ?? new(),
            Properties = MetadataValues.Deserialize(dbEntity.PropertiesJson),
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
            Properties = MetadataValues.Deserialize(dbRel.PropertiesJson),
            CreatedAt = dbRel.CreatedAt
        };
    }

    private SQLiteEntityCommunityEntity MapToDbCommunity(GraphCommunity community)
    {
        return new SQLiteEntityCommunityEntity
        {
            Id = community.Id,
            Name = community.Name,
            Partition = community.Partition ?? throw new ArgumentException("GraphCommunity.Partition must not be null.", nameof(community)),
            Summary = community.Summary,
            ImportanceScore = community.ImportanceScore,
            Level = community.Level,
            ParentCommunityId = community.ParentCommunityId,
            Embedding = community.Embedding != null ? VectorToBytes(community.Embedding) : null,
            TopicsJson = JsonSerializer.Serialize(community.Topics ?? [], _jsonOptions),
            ChunkIds = community.ChunkIds.Distinct().ToList(),
            CreatedAt = community.CreatedAt
        };
    }

    private GraphCommunity MapToGraphCommunity(SQLiteEntityCommunityEntity dbCommunity, IReadOnlyList<string>? entityIds = null)
    {
        return new GraphCommunity
        {
            Id = dbCommunity.Id,
            Name = dbCommunity.Name,
            Partition = dbCommunity.Partition,
            Summary = dbCommunity.Summary,
            ImportanceScore = dbCommunity.ImportanceScore,
            Level = dbCommunity.Level,
            ParentCommunityId = dbCommunity.ParentCommunityId,
            Embedding = dbCommunity.Embedding != null ? BytesToVector(dbCommunity.Embedding) : null,
            Topics = JsonSerializer.Deserialize<List<string>>(dbCommunity.TopicsJson, _jsonOptions) ?? [],
            EntityIds = entityIds ?? [],
            ChunkIds = dbCommunity.ChunkIds ?? [],
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
