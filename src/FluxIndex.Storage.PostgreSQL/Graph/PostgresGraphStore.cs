using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Storage.PostgreSQL.Graph;

/// <summary>
/// PostgreSQL 기반 그래프 저장소 (IChunkHierarchyRepository 구현)
/// JSONB + 재귀 CTE를 활용한 고성능 구현
/// </summary>
public partial class PostgresGraphStore : IChunkHierarchyRepository
{
    private readonly PostgresGraphDbContext _context;
    private readonly PostgresGraphOptions _options;
    private readonly ILogger<PostgresGraphStore> _logger;

    public PostgresGraphStore(
        PostgresGraphDbContext context,
        IOptions<PostgresGraphOptions> options,
        ILogger<PostgresGraphStore> logger)
    {
        _context = context;
        _options = options.Value;
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
        var leafChunks = hierarchies.Count(h => h.ChildChunkIds.Count == 0);

        var levelDistribution = hierarchies
            .GroupBy(h => h.HierarchyLevel)
            .ToDictionary(g => g.Key, g => g.Count());

        var parentChunks = hierarchies.Where(h => h.ChildChunkIds.Count > 0).ToList();
        var averageBranchingFactor = parentChunks.Count != 0
            ? parentChunks.Average(h => h.ChildChunkIds.Count)
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

    #region Graph Traversal (PostgreSQL 재귀 CTE)

    /// <summary>
    /// 재귀 CTE를 사용한 조상 청크 조회
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAncestorsAsync(
        string chunkId,
        int maxDepth = 10,
        CancellationToken cancellationToken = default)
    {
        var effectiveMaxDepth = Math.Min(maxDepth, _options.MaxRecursionDepth);

        var sql = $@"
            WITH RECURSIVE ancestors AS (
                SELECT ""ChunkId"", ""ParentChunkId"", 1 as depth
                FROM chunk_hierarchies
                WHERE ""ChunkId"" = {{0}}

                UNION ALL

                SELECT h.""ChunkId"", h.""ParentChunkId"", a.depth + 1
                FROM chunk_hierarchies h
                INNER JOIN ancestors a ON h.""ChunkId"" = a.""ParentChunkId""
                WHERE a.depth < {{1}}
            )
            SELECT DISTINCT ""ParentChunkId""
            FROM ancestors
            WHERE ""ParentChunkId"" IS NOT NULL
            ORDER BY ""ParentChunkId""";

        var ancestors = await _context.Database
            .SqlQueryRaw<string>(sql, chunkId, effectiveMaxDepth)
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
        var effectiveMaxDepth = Math.Min(maxDepth, _options.MaxRecursionDepth);

        var sql = $@"
            WITH RECURSIVE descendants AS (
                SELECT ""ChunkId"", ""ChildChunkIds"", 1 as depth
                FROM chunk_hierarchies
                WHERE ""ChunkId"" = {{0}}

                UNION ALL

                SELECT h.""ChunkId"", h.""ChildChunkIds"", d.depth + 1
                FROM chunk_hierarchies h
                INNER JOIN descendants d ON h.""ParentChunkId"" = d.""ChunkId""
                WHERE d.depth < {{1}}
            )
            SELECT ""ChunkId""
            FROM descendants
            WHERE ""ChunkId"" != {{0}}
            ORDER BY depth, ""ChunkId""";

        var descendants = await _context.Database
            .SqlQueryRaw<string>(sql, chunkId, effectiveMaxDepth)
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
        var effectiveMaxHops = Math.Min(maxHops, _options.MaxRecursionDepth);

        var sql = $@"
            WITH RECURSIVE connected AS (
                SELECT ""SourceChunkId"" as chunk_id, 0 as hops
                FROM chunk_relationships
                WHERE ""SourceChunkId"" = {{0}} OR ""TargetChunkId"" = {{0}}

                UNION

                SELECT ""TargetChunkId"" as chunk_id, 0 as hops
                FROM chunk_relationships
                WHERE ""SourceChunkId"" = {{0}} OR ""TargetChunkId"" = {{0}}

                UNION ALL

                SELECT
                    CASE
                        WHEN r.""SourceChunkId"" = c.chunk_id THEN r.""TargetChunkId""
                        ELSE r.""SourceChunkId""
                    END as chunk_id,
                    c.hops + 1
                FROM chunk_relationships r
                INNER JOIN connected c ON r.""SourceChunkId"" = c.chunk_id OR r.""TargetChunkId"" = c.chunk_id
                WHERE c.hops < {{1}}
            )
            SELECT DISTINCT chunk_id
            FROM connected
            WHERE chunk_id != {{0}}
            ORDER BY chunk_id";

        var connected = await _context.Database
            .SqlQueryRaw<string>(sql, chunkId, effectiveMaxHops)
            .ToListAsync(cancellationToken);

        return connected;
    }

    /// <summary>
    /// PostgreSQL 고급 기능: 최단 경로 탐색
    /// </summary>
    public async Task<IReadOnlyList<string>> GetShortestPathAsync(
        string sourceChunkId,
        string targetChunkId,
        int maxDepth = 10,
        CancellationToken cancellationToken = default)
    {
        var effectiveMaxDepth = Math.Min(maxDepth, _options.MaxRecursionDepth);

        var sql = $@"
            WITH RECURSIVE path_search AS (
                SELECT
                    ""SourceChunkId"" as current_node,
                    ARRAY[""SourceChunkId""] as path,
                    1 as depth
                FROM chunk_relationships
                WHERE ""SourceChunkId"" = {{0}}

                UNION ALL

                SELECT
                    CASE
                        WHEN r.""SourceChunkId"" = p.current_node THEN r.""TargetChunkId""
                        ELSE r.""SourceChunkId""
                    END,
                    p.path || CASE
                        WHEN r.""SourceChunkId"" = p.current_node THEN r.""TargetChunkId""
                        ELSE r.""SourceChunkId""
                    END,
                    p.depth + 1
                FROM chunk_relationships r
                INNER JOIN path_search p ON
                    (r.""SourceChunkId"" = p.current_node OR r.""TargetChunkId"" = p.current_node)
                    AND NOT (CASE
                        WHEN r.""SourceChunkId"" = p.current_node THEN r.""TargetChunkId""
                        ELSE r.""SourceChunkId""
                    END = ANY(p.path))
                WHERE p.depth < {{2}}
            )
            SELECT unnest(path) as node
            FROM path_search
            WHERE current_node = {{1}}
            ORDER BY depth
            LIMIT 1";

        try
        {
            var path = await _context.Database
                .SqlQueryRaw<string>(sql, sourceChunkId, targetChunkId, effectiveMaxDepth)
                .ToListAsync(cancellationToken);

            return path;
        }
        catch (Exception ex)
        {
            LogShortestPathFailed(_logger, ex, sourceChunkId, targetChunkId);
            return Array.Empty<string>();
        }
    }

    #endregion

    #region Mapping Methods

    private static ChunkHierarchy MapToChunkHierarchy(ChunkHierarchyEntity entity)
    {
        return new ChunkHierarchy
        {
            ChunkId = entity.ChunkId,
            ParentChunkId = entity.ParentChunkId,
            ChildChunkIds = entity.ChildChunkIds,
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
        return new ChunkHierarchyEntity
        {
            ChunkId = hierarchy.ChunkId,
            ParentChunkId = hierarchy.ParentChunkId,
            ChildChunkIds = hierarchy.ChildChunkIds.ToList(),
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
    }

    private static void UpdateHierarchyEntity(ChunkHierarchyEntity entity, ChunkHierarchy hierarchy)
    {
        entity.ParentChunkId = hierarchy.ParentChunkId;
        entity.ChildChunkIds = hierarchy.ChildChunkIds.ToList();
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
            Metadata = entity.Metadata ?? new Dictionary<string, object>(),
            CreatedAt = entity.CreatedAt
        };
    }

    private static ChunkRelationshipEntity MapToEntity(ChunkRelationshipExtended relationship)
    {
        return new ChunkRelationshipEntity
        {
            Id = relationship.Id,
            SourceChunkId = relationship.SourceChunkId,
            TargetChunkId = relationship.TargetChunkId,
            Type = relationship.Type.ToString(),
            Strength = relationship.Strength,
            Direction = relationship.Direction.ToString(),
            Description = relationship.Description,
            Metadata = relationship.Metadata,
            CreatedAt = relationship.CreatedAt
        };
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
        entity.Metadata = relationship.Metadata;
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hierarchy saved: {ChunkId}")]
    private static partial void LogHierarchySaved(ILogger logger, string chunkId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Relationship saved: {Id}")]
    private static partial void LogRelationshipSaved(ILogger logger, string id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shortest path search failed from {Source} to {Target}")]
    private static partial void LogShortestPathFailed(ILogger logger, Exception exception, string source, string target);

    #endregion
}
