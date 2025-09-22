using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.SDK.Services;

/// <summary>
/// 인메모리 청크 계층 구조 리포지토리 (SDK용)
/// </summary>
public class InMemoryChunkHierarchyRepository : IChunkHierarchyRepository
{
    private readonly ConcurrentDictionary<string, ChunkHierarchy> _hierarchies = new();
    private readonly ConcurrentDictionary<string, ChunkRelationshipExtended> _relationships = new();
    private readonly ILogger<InMemoryChunkHierarchyRepository> _logger;

    public InMemoryChunkHierarchyRepository(ILogger<InMemoryChunkHierarchyRepository>? logger = null)
    {
        _logger = logger ?? new NullLogger<InMemoryChunkHierarchyRepository>();
    }

    /// <summary>
    /// 청크 계층 정보 조회
    /// </summary>
    public Task<ChunkHierarchy?> GetHierarchyAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chunkId))
            throw new ArgumentException("청크 ID는 비어있을 수 없습니다.", nameof(chunkId));

        _hierarchies.TryGetValue(chunkId, out var hierarchy);
        return Task.FromResult(hierarchy);
    }

    /// <summary>
    /// 청크 계층 정보 저장
    /// </summary>
    public Task SaveHierarchyAsync(ChunkHierarchy hierarchy, CancellationToken cancellationToken = default)
    {
        if (hierarchy == null)
            throw new ArgumentNullException(nameof(hierarchy));

        hierarchy.UpdatedAt = DateTime.UtcNow;
        _hierarchies.AddOrUpdate(hierarchy.ChunkId, hierarchy, (key, existing) =>
        {
            // 기존 계층 정보 업데이트
            existing.ParentChunkId = hierarchy.ParentChunkId;
            existing.ChildChunkIds = hierarchy.ChildChunkIds;
            existing.HierarchyLevel = hierarchy.HierarchyLevel;
            existing.RecommendedWindowSize = hierarchy.RecommendedWindowSize;
            existing.Boundary = hierarchy.Boundary;
            existing.Metadata = hierarchy.Metadata;
            existing.UpdatedAt = DateTime.UtcNow;
            return existing;
        });

        _logger.LogDebug("청크 계층 정보 저장 완료: {ChunkId}", hierarchy.ChunkId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 부모 청크의 모든 자식 조회
    /// </summary>
    public Task<IReadOnlyList<ChunkHierarchy>> GetChildrenAsync(string parentChunkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentChunkId))
            throw new ArgumentException("부모 청크 ID는 비어있을 수 없습니다.", nameof(parentChunkId));

        var children = _hierarchies.Values
            .Where(h => h.ParentChunkId == parentChunkId)
            .OrderBy(h => h.HierarchyLevel)
            .ToList();

        return Task.FromResult<IReadOnlyList<ChunkHierarchy>>(children);
    }

    /// <summary>
    /// 특정 레벨의 모든 청크 조회
    /// </summary>
    public Task<IReadOnlyList<ChunkHierarchy>> GetChunksByLevelAsync(string documentId, int level, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("문서 ID는 비어있을 수 없습니다.", nameof(documentId));

        if (level < 0)
            throw new ArgumentException("계층 레벨은 0 이상이어야 합니다.", nameof(level));

        // 간단한 구현: ChunkId에 documentId가 포함되어 있다고 가정
        var chunks = _hierarchies.Values
            .Where(h => h.ChunkId.StartsWith(documentId) && h.HierarchyLevel == level)
            .OrderBy(h => h.Boundary.StartPosition)
            .ToList();

        return Task.FromResult<IReadOnlyList<ChunkHierarchy>>(chunks);
    }

    /// <summary>
    /// 청크 관계 저장
    /// </summary>
    public Task SaveRelationshipAsync(ChunkRelationshipExtended relationship, CancellationToken cancellationToken = default)
    {
        if (relationship == null)
            throw new ArgumentNullException(nameof(relationship));

        _relationships.AddOrUpdate(relationship.Id, relationship, (key, existing) =>
        {
            // 기존 관계 정보 업데이트
            existing.SourceChunkId = relationship.SourceChunkId;
            existing.TargetChunkId = relationship.TargetChunkId;
            existing.Type = relationship.Type;
            existing.Strength = relationship.Strength;
            existing.Direction = relationship.Direction;
            existing.Description = relationship.Description;
            existing.Metadata = relationship.Metadata;
            return existing;
        });

        _logger.LogDebug("청크 관계 저장 완료: {RelationshipId}", relationship.Id);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 청크 관계 조회
    /// </summary>
    public Task<IReadOnlyList<ChunkRelationshipExtended>> GetRelationshipsAsync(
        string chunkId,
        IEnumerable<RelationshipType>? relationshipTypes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chunkId))
            throw new ArgumentException("청크 ID는 비어있을 수 없습니다.", nameof(chunkId));

        var query = _relationships.Values
            .Where(r => r.SourceChunkId == chunkId || r.TargetChunkId == chunkId);

        if (relationshipTypes != null)
        {
            var typesList = relationshipTypes.ToList();
            if (typesList.Count > 0)
            {
                query = query.Where(r => typesList.Contains(r.Type));
            }
        }

        var relationships = query
            .OrderByDescending(r => r.Strength)
            .ToList();

        return Task.FromResult<IReadOnlyList<ChunkRelationshipExtended>>(relationships);
    }

    /// <summary>
    /// 계층 구조 통계 조회
    /// </summary>
    public Task<HierarchyStatistics> GetHierarchyStatisticsAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("문서 ID는 비어있을 수 없습니다.", nameof(documentId));

        // 해당 문서의 모든 계층 정보 조회
        var hierarchies = _hierarchies.Values
            .Where(h => h.ChunkId.StartsWith(documentId))
            .ToList();

        if (!hierarchies.Any())
        {
            return Task.FromResult(new HierarchyStatistics
            {
                TotalChunks = 0,
                MaxDepth = 0,
                AverageBranchingFactor = 0,
                OrphanChunks = 0,
                LeafChunks = 0,
                LevelDistribution = new Dictionary<int, int>(),
                RelationshipStatistics = new Dictionary<RelationshipType, RelationshipStats>()
            });
        }

        // 기본 통계 계산
        var totalChunks = hierarchies.Count;
        var maxDepth = hierarchies.Max(h => h.Metadata.Depth);
        var orphanChunks = hierarchies.Count(h => h.ParentChunkId == null);
        var leafChunks = hierarchies.Count(h => h.ChildChunkIds.Count == 0);

        // 레벨별 분포
        var levelDistribution = hierarchies
            .GroupBy(h => h.HierarchyLevel)
            .ToDictionary(g => g.Key, g => g.Count());

        // 평균 브랜치 팩터 계산
        var parentChunks = hierarchies.Where(h => h.ChildChunkIds.Count > 0).ToList();
        var averageBranchingFactor = parentChunks.Any()
            ? parentChunks.Average(h => h.ChildChunkIds.Count)
            : 0.0;

        // 관계 통계 조회
        var documentChunkIds = hierarchies.Select(h => h.ChunkId).ToHashSet();
        var relationships = _relationships.Values
            .Where(r => documentChunkIds.Contains(r.SourceChunkId))
            .ToList();

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

        return Task.FromResult(new HierarchyStatistics
        {
            TotalChunks = totalChunks,
            MaxDepth = maxDepth,
            AverageBranchingFactor = averageBranchingFactor,
            OrphanChunks = orphanChunks,
            LeafChunks = leafChunks,
            LevelDistribution = levelDistribution,
            RelationshipStatistics = relationshipStats
        });
    }

    /// <summary>
    /// 모든 계층 정보 초기화 (테스트용)
    /// </summary>
    public void Clear()
    {
        _hierarchies.Clear();
        _relationships.Clear();
        _logger.LogDebug("모든 계층 정보가 초기화되었습니다.");
    }

    /// <summary>
    /// 현재 저장된 계층 수 조회
    /// </summary>
    public int GetHierarchyCount() => _hierarchies.Count;

    /// <summary>
    /// 현재 저장된 관계 수 조회
    /// </summary>
    public int GetRelationshipCount() => _relationships.Count;
}