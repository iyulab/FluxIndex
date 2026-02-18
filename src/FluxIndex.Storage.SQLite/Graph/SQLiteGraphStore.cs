using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Storage.SQLite.Graph;

/// <summary>
/// SQLite 기반 그래프 저장소 (IChunkHierarchyRepository 구현)
/// </summary>
public partial class SQLiteGraphStore : IChunkHierarchyRepository
{
    private readonly SQLiteGraphDbContext _context;
    private readonly ILogger<SQLiteGraphStore> _logger;

    public SQLiteGraphStore(
        SQLiteGraphDbContext context,
        ILogger<SQLiteGraphStore> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Hierarchy Operations

    public async Task<ChunkHierarchy?> GetHierarchyAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.ChunkHierarchies
            .FirstOrDefaultAsync(h => h.ChunkId == chunkId, cancellationToken);

        return entity == null ? null : MapToChunkHierarchy(entity);
    }

    public async Task SaveHierarchyAsync(
        ChunkHierarchy hierarchy,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.ChunkHierarchies
            .FirstOrDefaultAsync(h => h.ChunkId == hierarchy.ChunkId, cancellationToken);

        if (existing != null)
        {
            UpdateHierarchyEntity(existing, hierarchy);
            _context.ChunkHierarchies.Update(existing);
        }
        else
        {
            var entity = MapToEntity(hierarchy);
            await _context.ChunkHierarchies.AddAsync(entity, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        LogHierarchySaved(_logger, hierarchy.ChunkId);
    }

    public async Task<IReadOnlyList<ChunkHierarchy>> GetChildrenAsync(
        string parentChunkId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _context.ChunkHierarchies
            .Where(h => h.ParentChunkId == parentChunkId)
            .OrderBy(h => h.HierarchyLevel)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToChunkHierarchy).ToList();
    }

    public async Task<IReadOnlyList<ChunkHierarchy>> GetChunksByLevelAsync(
        string documentId,
        int level,
        CancellationToken cancellationToken = default)
    {
        var entities = await _context.ChunkHierarchies
            .Where(h => h.ChunkId.StartsWith(documentId) && h.HierarchyLevel == level)
            .OrderBy(h => h.BoundaryStartPosition)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToChunkHierarchy).ToList();
    }

    #endregion

    #region Relationship Operations

    public async Task SaveRelationshipAsync(
        ChunkRelationshipExtended relationship,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.ChunkRelationships
            .FirstOrDefaultAsync(r => r.Id == relationship.Id, cancellationToken);

        if (existing != null)
        {
            UpdateRelationshipEntity(existing, relationship);
            _context.ChunkRelationships.Update(existing);
        }
        else
        {
            var entity = MapToEntity(relationship);
            await _context.ChunkRelationships.AddAsync(entity, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        LogRelationshipSaved(_logger, relationship.Id);
    }

    public async Task<IReadOnlyList<ChunkRelationshipExtended>> GetRelationshipsAsync(
        string chunkId,
        IEnumerable<RelationshipType>? relationshipTypes = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ChunkRelationships
            .Where(r => r.SourceChunkId == chunkId || r.TargetChunkId == chunkId);

        if (relationshipTypes != null)
        {
            var typeStrings = relationshipTypes.Select(t => t.ToString()).ToList();
            if (typeStrings.Count > 0)
            {
                query = query.Where(r => typeStrings.Contains(r.Type));
            }
        }

        var entities = await query
            .OrderByDescending(r => r.Strength)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToRelationship).ToList();
    }

    #endregion

    #region Statistics

    public async Task<HierarchyStatistics> GetHierarchyStatisticsAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var hierarchies = await _context.ChunkHierarchies
            .Where(h => h.ChunkId.StartsWith(documentId))
            .ToListAsync(cancellationToken);

        if (hierarchies.Count == 0)
        {
            return new HierarchyStatistics
            {
                TotalChunks = 0,
                MaxDepth = 0,
                AverageBranchingFactor = 0,
                OrphanChunks = 0,
                LeafChunks = 0,
                LevelDistribution = new Dictionary<int, int>(),
                RelationshipStatistics = new Dictionary<RelationshipType, RelationshipStats>()
            };
        }

        var totalChunks = hierarchies.Count;
        var maxDepth = hierarchies.Max(h => h.MetadataDepth);
        var orphanChunks = hierarchies.Count(h => h.ParentChunkId == null);
        var leafChunks = hierarchies.Count(h => h.GetChildChunkIds().Count == 0);

        var levelDistribution = hierarchies
            .GroupBy(h => h.HierarchyLevel)
            .ToDictionary(g => g.Key, g => g.Count());

        var parentChunks = hierarchies.Where(h => h.GetChildChunkIds().Count > 0).ToList();
        var averageBranchingFactor = parentChunks.Count != 0
            ? parentChunks.Average(h => h.GetChildChunkIds().Count)
            : 0.0;

        // 관계 통계
        var chunkIds = hierarchies.Select(h => h.ChunkId).ToHashSet();
        var relationships = await _context.ChunkRelationships
            .Where(r => chunkIds.Contains(r.SourceChunkId))
            .ToListAsync(cancellationToken);

        var relationshipStats = relationships
            .GroupBy(r => Enum.TryParse<RelationshipType>(r.Type, out var t) ? t : RelationshipType.Semantic)
            .ToDictionary(
                g => g.Key,
                g => new RelationshipStats
                {
                    Count = g.Count(),
                    AverageStrength = g.Average(r => r.Strength),
                    MaxStrength = g.Max(r => r.Strength),
                    MinStrength = g.Min(r => r.Strength)
                });

        return new HierarchyStatistics
        {
            TotalChunks = totalChunks,
            MaxDepth = maxDepth,
            AverageBranchingFactor = averageBranchingFactor,
            OrphanChunks = orphanChunks,
            LeafChunks = leafChunks,
            LevelDistribution = levelDistribution,
            RelationshipStatistics = relationshipStats
        };
    }

    #endregion

    #region Graph Traversal (재귀 CTE 활용)

    /// <summary>
    /// 재귀 CTE를 사용한 조상 청크 조회
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAncestorsAsync(
        string chunkId,
        int maxDepth = 10,
        CancellationToken cancellationToken = default)
    {
        var sql = @"
            WITH RECURSIVE ancestors AS (
                SELECT ChunkId, ParentChunkId, 1 as depth
                FROM chunk_hierarchies
                WHERE ChunkId = {0}

                UNION ALL

                SELECT h.ChunkId, h.ParentChunkId, a.depth + 1
                FROM chunk_hierarchies h
                INNER JOIN ancestors a ON h.ChunkId = a.ParentChunkId
                WHERE a.depth < {1}
            )
            SELECT DISTINCT ParentChunkId
            FROM ancestors
            WHERE ParentChunkId IS NOT NULL
            ORDER BY depth";

        var ancestors = await _context.Database
            .SqlQueryRaw<string>(sql, chunkId, maxDepth)
            .ToListAsync(cancellationToken);

        return ancestors;
    }

    /// <summary>
    /// 재귀 CTE를 사용한 자손 청크 조회
    /// </summary>
    public async Task<IReadOnlyList<string>> GetDescendantsAsync(
        string chunkId,
        int maxDepth = 10,
        CancellationToken cancellationToken = default)
    {
        var sql = @"
            WITH RECURSIVE descendants AS (
                SELECT ChunkId, ChildChunkIdsJson, 1 as depth
                FROM chunk_hierarchies
                WHERE ChunkId = {0}

                UNION ALL

                SELECT h.ChunkId, h.ChildChunkIdsJson, d.depth + 1
                FROM chunk_hierarchies h
                INNER JOIN descendants d ON h.ParentChunkId = d.ChunkId
                WHERE d.depth < {1}
            )
            SELECT ChunkId
            FROM descendants
            WHERE ChunkId != {0}
            ORDER BY depth";

        var descendants = await _context.Database
            .SqlQueryRaw<string>(sql, chunkId, maxDepth)
            .ToListAsync(cancellationToken);

        return descendants;
    }

    /// <summary>
    /// 재귀 CTE를 사용한 연결된 청크 조회 (관계 기반)
    /// </summary>
    public async Task<IReadOnlyList<string>> GetConnectedChunksAsync(
        string chunkId,
        int maxHops = 3,
        CancellationToken cancellationToken = default)
    {
        var sql = @"
            WITH RECURSIVE connected AS (
                SELECT SourceChunkId as ChunkId, 0 as hops
                FROM chunk_relationships
                WHERE SourceChunkId = {0} OR TargetChunkId = {0}

                UNION

                SELECT TargetChunkId as ChunkId, 0 as hops
                FROM chunk_relationships
                WHERE SourceChunkId = {0} OR TargetChunkId = {0}

                UNION ALL

                SELECT
                    CASE
                        WHEN r.SourceChunkId = c.ChunkId THEN r.TargetChunkId
                        ELSE r.SourceChunkId
                    END as ChunkId,
                    c.hops + 1
                FROM chunk_relationships r
                INNER JOIN connected c ON r.SourceChunkId = c.ChunkId OR r.TargetChunkId = c.ChunkId
                WHERE c.hops < {1}
            )
            SELECT DISTINCT ChunkId
            FROM connected
            WHERE ChunkId != {0}
            ORDER BY ChunkId";

        var connected = await _context.Database
            .SqlQueryRaw<string>(sql, chunkId, maxHops)
            .ToListAsync(cancellationToken);

        return connected;
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hierarchy saved: {ChunkId}")]
    private static partial void LogHierarchySaved(ILogger logger, string chunkId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Relationship saved: {Id}")]
    private static partial void LogRelationshipSaved(ILogger logger, string id);

    #endregion

    #region Mapping Methods

    private static ChunkHierarchy MapToChunkHierarchy(ChunkHierarchyEntity entity)
    {
        return new ChunkHierarchy
        {
            ChunkId = entity.ChunkId,
            ParentChunkId = entity.ParentChunkId,
            ChildChunkIds = entity.GetChildChunkIds(),
            HierarchyLevel = entity.HierarchyLevel,
            RecommendedWindowSize = entity.RecommendedWindowSize,
            Boundary = new ChunkBoundary
            {
                StartPosition = entity.BoundaryStartPosition,
                EndPosition = entity.BoundaryEndPosition,
                Type = Enum.TryParse<BoundaryType>(entity.BoundaryType, out var bt)
                    ? bt : BoundaryType.Sentence
            },
            Metadata = new HierarchyMetadata
            {
                Depth = entity.MetadataDepth,
                DescendantCount = entity.MetadataDescendantCount,
                SiblingCount = entity.MetadataSiblingCount,
                HierarchyWeight = entity.MetadataHierarchyWeight
            },
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private static ChunkHierarchyEntity MapToEntity(ChunkHierarchy hierarchy)
    {
        var entity = new ChunkHierarchyEntity
        {
            ChunkId = hierarchy.ChunkId,
            ParentChunkId = hierarchy.ParentChunkId,
            HierarchyLevel = hierarchy.HierarchyLevel,
            RecommendedWindowSize = hierarchy.RecommendedWindowSize,
            BoundaryStartPosition = hierarchy.Boundary.StartPosition,
            BoundaryEndPosition = hierarchy.Boundary.EndPosition,
            BoundaryType = hierarchy.Boundary.Type.ToString(),
            MetadataDepth = hierarchy.Metadata.Depth,
            MetadataDescendantCount = hierarchy.Metadata.DescendantCount,
            MetadataSiblingCount = hierarchy.Metadata.SiblingCount,
            MetadataHierarchyWeight = hierarchy.Metadata.HierarchyWeight,
            CreatedAt = hierarchy.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };
        entity.SetChildChunkIds(hierarchy.ChildChunkIds);
        return entity;
    }

    private static void UpdateHierarchyEntity(ChunkHierarchyEntity entity, ChunkHierarchy hierarchy)
    {
        entity.ParentChunkId = hierarchy.ParentChunkId;
        entity.SetChildChunkIds(hierarchy.ChildChunkIds);
        entity.HierarchyLevel = hierarchy.HierarchyLevel;
        entity.RecommendedWindowSize = hierarchy.RecommendedWindowSize;
        entity.BoundaryStartPosition = hierarchy.Boundary.StartPosition;
        entity.BoundaryEndPosition = hierarchy.Boundary.EndPosition;
        entity.BoundaryType = hierarchy.Boundary.Type.ToString();
        entity.MetadataDepth = hierarchy.Metadata.Depth;
        entity.MetadataDescendantCount = hierarchy.Metadata.DescendantCount;
        entity.MetadataSiblingCount = hierarchy.Metadata.SiblingCount;
        entity.MetadataHierarchyWeight = hierarchy.Metadata.HierarchyWeight;
        entity.UpdatedAt = DateTime.UtcNow;
    }

    private static ChunkRelationshipExtended MapToRelationship(ChunkRelationshipEntity entity)
    {
        return new ChunkRelationshipExtended
        {
            Id = entity.Id,
            SourceChunkId = entity.SourceChunkId,
            TargetChunkId = entity.TargetChunkId,
            Type = Enum.TryParse<RelationshipType>(entity.Type, out var t)
                ? t : RelationshipType.Semantic,
            Strength = entity.Strength,
            Direction = Enum.TryParse<RelationshipDirection>(entity.Direction, out var d)
                ? d : RelationshipDirection.Bidirectional,
            Description = entity.Description ?? string.Empty,
            Metadata = entity.GetMetadata(),
            CreatedAt = entity.CreatedAt
        };
    }

    private static ChunkRelationshipEntity MapToEntity(ChunkRelationshipExtended relationship)
    {
        var entity = new ChunkRelationshipEntity
        {
            Id = relationship.Id,
            SourceChunkId = relationship.SourceChunkId,
            TargetChunkId = relationship.TargetChunkId,
            Type = relationship.Type.ToString(),
            Strength = relationship.Strength,
            Direction = relationship.Direction.ToString(),
            Description = relationship.Description,
            CreatedAt = relationship.CreatedAt
        };
        entity.SetMetadata(relationship.Metadata);
        return entity;
    }

    private static void UpdateRelationshipEntity(
        ChunkRelationshipEntity entity,
        ChunkRelationshipExtended relationship)
    {
        entity.SourceChunkId = relationship.SourceChunkId;
        entity.TargetChunkId = relationship.TargetChunkId;
        entity.Type = relationship.Type.ToString();
        entity.Strength = relationship.Strength;
        entity.Direction = relationship.Direction.ToString();
        entity.Description = relationship.Description;
        entity.SetMetadata(relationship.Metadata);
    }

    #endregion
}
