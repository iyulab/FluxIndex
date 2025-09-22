using FluxIndex.Domain.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Small-to-Big 검색 인터페이스 - 정밀 검색과 컨텍스트 확장
/// </summary>
public interface ISmallToBigRetriever
{
    /// <summary>
    /// Small-to-Big 검색 실행
    /// 1. Small: 문장 수준 정밀 검색
    /// 2. Big: 관련 컨텍스트 확장
    /// </summary>
    /// <param name="query">검색 쿼리</param>
    /// <param name="options">검색 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>Small-to-Big 검색 결과</returns>
    Task<IReadOnlyList<SmallToBigResult>> SearchAsync(
        string query,
        SmallToBigOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 쿼리 복잡도에 따른 최적 윈도우 크기 결정
    /// </summary>
    /// <param name="query">분석할 쿼리</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>권장 윈도우 크기</returns>
    Task<int> DetermineOptimalWindowSizeAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 쿼리 복잡도 상세 분석
    /// </summary>
    /// <param name="query">분석할 쿼리</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>복잡도 분석 결과</returns>
    Task<QueryComplexityAnalysis> AnalyzeQueryComplexityAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 청크 계층 구조 구축
    /// </summary>
    /// <param name="chunks">계층 구조를 구축할 청크들</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>구축 결과</returns>
    Task<HierarchyBuildResult> BuildChunkHierarchyAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 특정 청크의 컨텍스트 확장
    /// </summary>
    /// <param name="primaryChunk">핵심 청크</param>
    /// <param name="windowSize">윈도우 크기</param>
    /// <param name="expansionOptions">확장 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>확장된 컨텍스트</returns>
    Task<ContextExpansionResult> ExpandContextAsync(
        DocumentChunk primaryChunk,
        int windowSize,
        ContextExpansionOptions? expansionOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 확장 전략 추천
    /// </summary>
    /// <param name="query">쿼리</param>
    /// <param name="primaryChunk">핵심 청크</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추천 확장 전략</returns>
    Task<ExpansionStrategy> RecommendExpansionStrategyAsync(
        string query,
        DocumentChunk primaryChunk,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Small-to-Big 성능 평가
    /// </summary>
    /// <param name="testQueries">테스트 쿼리 목록</param>
    /// <param name="groundTruth">정답 데이터</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>성능 평가 결과</returns>
    Task<SmallToBigPerformanceMetrics> EvaluatePerformanceAsync(
        IReadOnlyList<string> testQueries,
        IReadOnlyList<IReadOnlyList<string>> groundTruth,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 청크 계층 구조 리포지토리 인터페이스
/// </summary>
public interface IChunkHierarchyRepository
{
    /// <summary>
    /// 청크 계층 정보 조회
    /// </summary>
    /// <param name="chunkId">청크 ID</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>계층 정보</returns>
    Task<ChunkHierarchy?> GetHierarchyAsync(string chunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 청크 계층 정보 저장
    /// </summary>
    /// <param name="hierarchy">계층 정보</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task SaveHierarchyAsync(ChunkHierarchy hierarchy, CancellationToken cancellationToken = default);

    /// <summary>
    /// 부모 청크의 모든 자식 조회
    /// </summary>
    /// <param name="parentChunkId">부모 청크 ID</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>자식 계층 목록</returns>
    Task<IReadOnlyList<ChunkHierarchy>> GetChildrenAsync(string parentChunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 특정 레벨의 모든 청크 조회
    /// </summary>
    /// <param name="documentId">문서 ID</param>
    /// <param name="level">계층 레벨</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>해당 레벨 계층 목록</returns>
    Task<IReadOnlyList<ChunkHierarchy>> GetChunksByLevelAsync(string documentId, int level, CancellationToken cancellationToken = default);

    /// <summary>
    /// 청크 관계 저장
    /// </summary>
    /// <param name="relationship">청크 관계</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task SaveRelationshipAsync(ChunkRelationshipExtended relationship, CancellationToken cancellationToken = default);

    /// <summary>
    /// 청크 관계 조회
    /// </summary>
    /// <param name="chunkId">청크 ID</param>
    /// <param name="relationshipTypes">관계 유형 필터</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>관련 청크 관계 목록</returns>
    Task<IReadOnlyList<ChunkRelationshipExtended>> GetRelationshipsAsync(
        string chunkId,
        IEnumerable<RelationshipType>? relationshipTypes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 계층 구조 통계 조회
    /// </summary>
    /// <param name="documentId">문서 ID</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>계층 구조 통계</returns>
    Task<HierarchyStatistics> GetHierarchyStatisticsAsync(string documentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 컨텍스트 확장 옵션
/// </summary>
public class ContextExpansionOptions
{
    /// <summary>
    /// 계층적 확장 활성화
    /// </summary>
    public bool EnableHierarchicalExpansion { get; set; } = true;

    /// <summary>
    /// 순차적 확장 활성화
    /// </summary>
    public bool EnableSequentialExpansion { get; set; } = true;

    /// <summary>
    /// 의미적 확장 활성화
    /// </summary>
    public bool EnableSemanticExpansion { get; set; } = true;

    /// <summary>
    /// 최대 확장 거리 (홉 수)
    /// </summary>
    public int MaxExpansionDistance { get; set; } = 2;

    /// <summary>
    /// 의미적 유사도 임계값
    /// </summary>
    public double SemanticSimilarityThreshold { get; set; } = 0.7;

    /// <summary>
    /// 확장 품질 임계값
    /// </summary>
    public double QualityThreshold { get; set; } = 0.5;

    /// <summary>
    /// 중복 제거 활성화
    /// </summary>
    public bool EnableDeduplication { get; set; } = true;

    /// <summary>
    /// 중복 임계값
    /// </summary>
    public double DeduplicationThreshold { get; set; } = 0.9;
}

/// <summary>
/// 컨텍스트 확장 결과
/// </summary>
public class ContextExpansionResult
{
    /// <summary>
    /// 원본 청크
    /// </summary>
    public DocumentChunk OriginalChunk { get; init; } = new();

    /// <summary>
    /// 확장된 청크들
    /// </summary>
    public List<DocumentChunk> ExpandedChunks { get; init; } = new();

    /// <summary>
    /// 확장 방법별 청크 수
    /// </summary>
    public Dictionary<ExpansionMethod, int> ExpansionBreakdown { get; init; } = new();

    /// <summary>
    /// 확장 품질 점수
    /// </summary>
    public double ExpansionQuality { get; init; }

    /// <summary>
    /// 확장 실행 시간 (밀리초)
    /// </summary>
    public double ExpansionTimeMs { get; init; }

    /// <summary>
    /// 확장 메타데이터
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// 계층 구조 구축 결과
/// </summary>
public class HierarchyBuildResult
{
    /// <summary>
    /// 구축된 계층 수
    /// </summary>
    public int HierarchyCount { get; init; }

    /// <summary>
    /// 구축된 관계 수
    /// </summary>
    public int RelationshipCount { get; init; }

    /// <summary>
    /// 구축 성공률
    /// </summary>
    public double SuccessRate { get; init; }

    /// <summary>
    /// 구축 실행 시간 (밀리초)
    /// </summary>
    public double BuildTimeMs { get; init; }

    /// <summary>
    /// 레벨별 청크 분포
    /// </summary>
    public Dictionary<int, int> LevelDistribution { get; init; } = new();

    /// <summary>
    /// 관계 유형별 분포
    /// </summary>
    public Dictionary<RelationshipType, int> RelationshipDistribution { get; init; } = new();

    /// <summary>
    /// 구축 품질 점수
    /// </summary>
    public double QualityScore { get; init; }

    /// <summary>
    /// 오류 메시지 (있는 경우)
    /// </summary>
    public List<string> Errors { get; init; } = new();
}

/// <summary>
/// Small-to-Big 성능 메트릭
/// </summary>
public class SmallToBigPerformanceMetrics
{
    /// <summary>
    /// 정밀도 (Precision)
    /// </summary>
    public double Precision { get; init; }

    /// <summary>
    /// 재현율 (Recall)
    /// </summary>
    public double Recall { get; init; }

    /// <summary>
    /// F1 점수
    /// </summary>
    public double F1Score { get; init; }

    /// <summary>
    /// 컨텍스트 품질 점수
    /// </summary>
    public double ContextQuality { get; init; }

    /// <summary>
    /// 평균 응답 시간 (밀리초)
    /// </summary>
    public double AverageResponseTime { get; init; }

    /// <summary>
    /// 확장 효율성
    /// </summary>
    public double ExpansionEfficiency { get; init; }

    /// <summary>
    /// 전략별 성능 분포
    /// </summary>
    public Dictionary<ExpansionStrategyType, double> StrategyPerformance { get; init; } = new();

    /// <summary>
    /// 윈도우 크기별 성능 분포
    /// </summary>
    public Dictionary<int, double> WindowSizePerformance { get; init; } = new();
}

/// <summary>
/// 계층 구조 통계
/// </summary>
public class HierarchyStatistics
{
    /// <summary>
    /// 총 청크 수
    /// </summary>
    public int TotalChunks { get; init; }

    /// <summary>
    /// 최대 계층 깊이
    /// </summary>
    public int MaxDepth { get; init; }

    /// <summary>
    /// 평균 브랜치 팩터
    /// </summary>
    public double AverageBranchingFactor { get; init; }

    /// <summary>
    /// 고아 청크 수 (부모 없는 청크)
    /// </summary>
    public int OrphanChunks { get; init; }

    /// <summary>
    /// 잎 청크 수 (자식 없는 청크)
    /// </summary>
    public int LeafChunks { get; init; }

    /// <summary>
    /// 레벨별 분포
    /// </summary>
    public Dictionary<int, int> LevelDistribution { get; init; } = new();

    /// <summary>
    /// 관계 통계
    /// </summary>
    public Dictionary<RelationshipType, RelationshipStats> RelationshipStatistics { get; init; } = new();
}

/// <summary>
/// 관계 통계
/// </summary>
public class RelationshipStats
{
    /// <summary>
    /// 관계 수
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// 평균 강도
    /// </summary>
    public double AverageStrength { get; init; }

    /// <summary>
    /// 최대 강도
    /// </summary>
    public double MaxStrength { get; init; }

    /// <summary>
    /// 최소 강도
    /// </summary>
    public double MinStrength { get; init; }
}