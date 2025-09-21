using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Storage.PostgreSQL.Repositories;

/// <summary>
/// PostgreSQL 기반 청크 계층 구조 리포지토리 구현
/// </summary>
public class ChunkHierarchyRepository : IChunkHierarchyRepository
{
    private readonly FluxIndexDbContext _context;
    private readonly ILogger<ChunkHierarchyRepository> _logger;

    public ChunkHierarchyRepository(
        FluxIndexDbContext context,
        ILogger<ChunkHierarchyRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 청크 계층 정보 조회
    /// </summary>
    public async Task<ChunkHierarchy?> GetHierarchyAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chunkId))
            throw new ArgumentException("청크 ID는 비어있을 수 없습니다.", nameof(chunkId));

        try
        {
            var entity = await _context.ChunkHierarchies
                .Where(h => h.ChunkId == chunkId)
                .FirstOrDefaultAsync(cancellationToken);

            return entity?.ToDomainModel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "청크 계층 정보 조회 실패: {ChunkId}", chunkId);
            throw;
        }
    }

    /// <summary>
    /// 청크 계층 정보 저장
    /// </summary>
    public async Task SaveHierarchyAsync(ChunkHierarchy hierarchy, CancellationToken cancellationToken = default)
    {
        if (hierarchy == null)
            throw new ArgumentNullException(nameof(hierarchy));

        try
        {
            var existing = await _context.ChunkHierarchies
                .Where(h => h.ChunkId == hierarchy.ChunkId)
                .FirstOrDefaultAsync(cancellationToken);

            if (existing != null)
            {
                // 기존 엔터티 업데이트
                existing.UpdateFromDomainModel(hierarchy);
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                // 새 엔터티 추가
                var entity = ChunkHierarchyEntity.FromDomainModel(hierarchy);
                _context.ChunkHierarchies.Add(entity);
            }

            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("청크 계층 정보 저장 완료: {ChunkId}", hierarchy.ChunkId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "청크 계층 정보 저장 실패: {ChunkId}", hierarchy.ChunkId);
            throw;
        }
    }

    /// <summary>
    /// 부모 청크의 모든 자식 조회
    /// </summary>
    public async Task<IReadOnlyList<ChunkHierarchy>> GetChildrenAsync(string parentChunkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentChunkId))
            throw new ArgumentException("부모 청크 ID는 비어있을 수 없습니다.", nameof(parentChunkId));

        try
        {
            var entities = await _context.ChunkHierarchies
                .Where(h => h.ParentChunkId == parentChunkId)
                .OrderBy(h => h.HierarchyLevel)
                .ToListAsync(cancellationToken);

            return entities.Select(e => e.ToDomainModel()).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "자식 청크 조회 실패: {ParentChunkId}", parentChunkId);
            throw;
        }
    }

    /// <summary>
    /// 특정 레벨의 모든 청크 조회
    /// </summary>
    public async Task<IReadOnlyList<ChunkHierarchy>> GetChunksByLevelAsync(string documentId, int level, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("문서 ID는 비어있을 수 없습니다.", nameof(documentId));

        if (level < 0)
            throw new ArgumentException("계층 레벨은 0 이상이어야 합니다.", nameof(level));

        try
        {
            // DocumentChunk 테이블과 조인하여 해당 문서의 청크만 필터링
            var entities = await _context.ChunkHierarchies
                .Join(_context.DocumentChunks,
                    h => h.ChunkId,
                    c => c.Id,
                    (h, c) => new { Hierarchy = h, Chunk = c })
                .Where(x => x.Chunk.DocumentId == documentId && x.Hierarchy.HierarchyLevel == level)
                .Select(x => x.Hierarchy)
                .OrderBy(h => h.StartPosition)
                .ToListAsync(cancellationToken);

            return entities.Select(e => e.ToDomainModel()).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "레벨별 청크 조회 실패: {DocumentId}, Level: {Level}", documentId, level);
            throw;
        }
    }

    /// <summary>
    /// 청크 관계 저장
    /// </summary>
    public async Task SaveRelationshipAsync(ChunkRelationshipExtended relationship, CancellationToken cancellationToken = default)
    {
        if (relationship == null)
            throw new ArgumentNullException(nameof(relationship));

        try
        {
            var existing = await _context.ChunkRelationships
                .Where(r => r.Id == relationship.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existing != null)
            {
                existing.UpdateFromDomainModel(relationship);
            }
            else
            {
                var entity = ChunkRelationshipEntity.FromDomainModel(relationship);
                _context.ChunkRelationships.Add(entity);
            }

            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("청크 관계 저장 완료: {RelationshipId}", relationship.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "청크 관계 저장 실패: {RelationshipId}", relationship.Id);
            throw;
        }
    }

    /// <summary>
    /// 청크 관계 조회
    /// </summary>
    public async Task<IReadOnlyList<ChunkRelationshipExtended>> GetRelationshipsAsync(
        string chunkId,
        IEnumerable<RelationshipType>? relationshipTypes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chunkId))
            throw new ArgumentException("청크 ID는 비어있을 수 없습니다.", nameof(chunkId));

        try
        {
            var query = _context.ChunkRelationships
                .Where(r => r.SourceChunkId == chunkId || r.TargetChunkId == chunkId);

            if (relationshipTypes != null)
            {
                var typesList = relationshipTypes.ToList();
                if (typesList.Count > 0)
                {
                    query = query.Where(r => typesList.Contains(r.Type));
                }
            }

            var entities = await query
                .OrderByDescending(r => r.Strength)
                .ToListAsync(cancellationToken);

            return entities.Select(e => e.ToDomainModel()).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "청크 관계 조회 실패: {ChunkId}", chunkId);
            throw;
        }
    }

    /// <summary>
    /// 계층 구조 통계 조회
    /// </summary>
    public async Task<HierarchyStatistics> GetHierarchyStatisticsAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("문서 ID는 비어있을 수 없습니다.", nameof(documentId));

        try
        {
            // 해당 문서의 모든 청크 계층 정보 조회
            var hierarchies = await _context.ChunkHierarchies
                .Join(_context.DocumentChunks,
                    h => h.ChunkId,
                    c => c.Id,
                    (h, c) => new { Hierarchy = h, Chunk = c })
                .Where(x => x.Chunk.DocumentId == documentId)
                .Select(x => x.Hierarchy)
                .ToListAsync(cancellationToken);

            if (!hierarchies.Any())
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

            // 기본 통계 계산
            var totalChunks = hierarchies.Count;
            var maxDepth = hierarchies.Max(h => h.Depth);
            var orphanChunks = hierarchies.Count(h => h.ParentChunkId == null);
            var leafChunks = hierarchies.Count(h => !h.ChildChunkIds.Any());

            // 레벨별 분포
            var levelDistribution = hierarchies
                .GroupBy(h => h.HierarchyLevel)
                .ToDictionary(g => g.Key, g => g.Count());

            // 평균 브랜치 팩터 계산
            var parentChunks = hierarchies.Where(h => h.ChildChunkIds.Any()).ToList();
            var averageBranchingFactor = parentChunks.Any()
                ? parentChunks.Average(h => h.ChildChunkIds.Count)
                : 0.0;

            // 관계 통계 조회
            var relationships = await _context.ChunkRelationships
                .Join(_context.DocumentChunks,
                    r => r.SourceChunkId,
                    c => c.Id,
                    (r, c) => new { Relationship = r, Chunk = c })
                .Where(x => x.Chunk.DocumentId == documentId)
                .Select(x => x.Relationship)
                .ToListAsync(cancellationToken);

            var relationshipStats = relationships
                .GroupBy(r => r.Type)
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "계층 구조 통계 조회 실패: {DocumentId}", documentId);
            throw;
        }
    }
}