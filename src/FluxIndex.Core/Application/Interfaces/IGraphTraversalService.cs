using FluxIndex.Core.Domain.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RelationshipType = FluxIndex.Core.Domain.Models.RelationshipType;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 그래프 탐색 서비스 인터페이스 - RAG를 위한 청크 관계 그래프 탐색
/// </summary>
public interface IGraphTraversalService
{
    // ============================================================
    // 기본 탐색 알고리즘
    // ============================================================

    /// <summary>
    /// 너비 우선 탐색 (BFS) - 레벨별 관계 탐색
    /// </summary>
    /// <param name="startChunkId">시작 청크 ID</param>
    /// <param name="options">탐색 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>탐색 결과 (레벨별 청크 목록)</returns>
    Task<GraphTraversalResult> TraverseBfsAsync(
        string startChunkId,
        GraphTraversalOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 깊이 우선 탐색 (DFS) - 깊은 관계 경로 탐색
    /// </summary>
    /// <param name="startChunkId">시작 청크 ID</param>
    /// <param name="options">탐색 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>탐색 결과 (경로별 청크 목록)</returns>
    Task<GraphTraversalResult> TraverseDfsAsync(
        string startChunkId,
        GraphTraversalOptions? options = null,
        CancellationToken cancellationToken = default);

    // ============================================================
    // 경로 탐색 알고리즘
    // ============================================================

    /// <summary>
    /// 두 청크 간 최단 경로 탐색
    /// </summary>
    /// <param name="sourceChunkId">출발 청크 ID</param>
    /// <param name="targetChunkId">목표 청크 ID</param>
    /// <param name="options">탐색 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>최단 경로 결과</returns>
    Task<PathFindingResult> FindShortestPathAsync(
        string sourceChunkId,
        string targetChunkId,
        PathFindingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 두 청크 간 K개의 최단 경로 탐색
    /// </summary>
    /// <param name="sourceChunkId">출발 청크 ID</param>
    /// <param name="targetChunkId">목표 청크 ID</param>
    /// <param name="k">찾을 경로 수</param>
    /// <param name="options">탐색 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>K개 최단 경로 목록</returns>
    Task<IReadOnlyList<PathFindingResult>> FindKShortestPathsAsync(
        string sourceChunkId,
        string targetChunkId,
        int k,
        PathFindingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 가중치 기반 최적 경로 탐색 (관계 강도 고려)
    /// </summary>
    /// <param name="sourceChunkId">출발 청크 ID</param>
    /// <param name="targetChunkId">목표 청크 ID</param>
    /// <param name="options">탐색 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>최적 경로 결과</returns>
    Task<PathFindingResult> FindStrongestPathAsync(
        string sourceChunkId,
        string targetChunkId,
        PathFindingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 두 청크 간 모든 경로 탐색 (최대 깊이 제한)
    /// </summary>
    /// <param name="sourceChunkId">출발 청크 ID</param>
    /// <param name="targetChunkId">목표 청크 ID</param>
    /// <param name="maxDepth">최대 탐색 깊이</param>
    /// <param name="options">탐색 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>모든 경로 목록</returns>
    Task<IReadOnlyList<PathFindingResult>> FindAllPathsAsync(
        string sourceChunkId,
        string targetChunkId,
        int maxDepth = 5,
        PathFindingOptions? options = null,
        CancellationToken cancellationToken = default);

    // ============================================================
    // 관계 분석 알고리즘
    // ============================================================

    /// <summary>
    /// 특정 청크로부터 N-hop 내의 모든 관련 청크 탐색
    /// </summary>
    /// <param name="chunkId">중심 청크 ID</param>
    /// <param name="maxHops">최대 홉 수</param>
    /// <param name="options">탐색 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>거리별 관련 청크 목록</returns>
    Task<NeighborhoodResult> GetNeighborhoodAsync(
        string chunkId,
        int maxHops = 2,
        GraphTraversalOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 공통 조상 청크 탐색
    /// </summary>
    /// <param name="chunkIds">청크 ID 목록</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>공통 조상 청크 목록</returns>
    Task<IReadOnlyList<AncestorInfo>> FindCommonAncestorsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 전이적 폐쇄 계산 (Transitive Closure)
    /// A→B→C 관계에서 A→C 추론
    /// </summary>
    /// <param name="chunkId">시작 청크 ID</param>
    /// <param name="relationshipTypes">추적할 관계 유형</param>
    /// <param name="maxDepth">최대 깊이</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>전이적 관계 목록</returns>
    Task<TransitiveClosureResult> ComputeTransitiveClosureAsync(
        string chunkId,
        IEnumerable<RelationshipType>? relationshipTypes = null,
        int maxDepth = 5,
        CancellationToken cancellationToken = default);

    // ============================================================
    // 그래프 분석 알고리즘
    // ============================================================

    /// <summary>
    /// 연결 컴포넌트 탐색
    /// </summary>
    /// <param name="documentId">문서 ID (선택적)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>연결 컴포넌트 목록</returns>
    Task<IReadOnlyList<ConnectedComponent>> FindConnectedComponentsAsync(
        string? documentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 브릿지 청크 탐색 (연결 컴포넌트 사이의 중요한 연결점)
    /// </summary>
    /// <param name="documentId">문서 ID (선택적)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>브릿지 청크 목록</returns>
    Task<IReadOnlyList<BridgeChunkInfo>> FindBridgeChunksAsync(
        string? documentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 청크 중요도 계산 (PageRank 스타일)
    /// </summary>
    /// <param name="documentId">문서 ID (선택적)</param>
    /// <param name="options">계산 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>청크별 중요도 점수</returns>
    Task<IReadOnlyDictionary<string, double>> ComputeChunkImportanceAsync(
        string? documentId = null,
        ImportanceOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 그래프 밀도 분석
    /// </summary>
    /// <param name="chunkIds">분석할 청크 ID 목록 (선택적, 없으면 전체)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>그래프 밀도 분석 결과</returns>
    Task<GraphDensityAnalysis> AnalyzeGraphDensityAsync(
        IEnumerable<string>? chunkIds = null,
        CancellationToken cancellationToken = default);

    // ============================================================
    // 사이클 및 일관성 검사
    // ============================================================

    /// <summary>
    /// 순환 관계 탐지
    /// </summary>
    /// <param name="documentId">문서 ID (선택적)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>순환 경로 목록</returns>
    Task<IReadOnlyList<CyclePath>> DetectCyclesAsync(
        string? documentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 그래프 일관성 검사
    /// </summary>
    /// <param name="documentId">문서 ID (선택적)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>일관성 검사 결과</returns>
    Task<GraphConsistencyResult> CheckConsistencyAsync(
        string? documentId = null,
        CancellationToken cancellationToken = default);
}

// ============================================================
// 옵션 클래스
// ============================================================

/// <summary>
/// 그래프 탐색 옵션
/// </summary>
public class GraphTraversalOptions
{
    /// <summary>
    /// 최대 탐색 깊이
    /// </summary>
    public int MaxDepth { get; set; } = 5;

    /// <summary>
    /// 최대 방문 노드 수
    /// </summary>
    public int MaxNodes { get; set; } = 1000;

    /// <summary>
    /// 탐색할 관계 유형 (null이면 모든 유형)
    /// </summary>
    public IReadOnlyList<RelationshipType>? RelationshipTypes { get; set; }

    /// <summary>
    /// 최소 관계 강도
    /// </summary>
    public double MinRelationshipStrength { get; set; } = 0.0;

    /// <summary>
    /// 방향 고려 여부 (true: 단방향만, false: 양방향 포함)
    /// </summary>
    public bool DirectedOnly { get; set; } = false;

    /// <summary>
    /// 계층적 관계만 탐색
    /// </summary>
    public bool HierarchicalOnly { get; set; } = false;

    /// <summary>
    /// 문서 ID 필터 (null이면 모든 문서)
    /// </summary>
    public string? DocumentIdFilter { get; set; }
}

/// <summary>
/// 경로 탐색 옵션
/// </summary>
public class PathFindingOptions
{
    /// <summary>
    /// 최대 경로 길이
    /// </summary>
    public int MaxPathLength { get; set; } = 10;

    /// <summary>
    /// 탐색할 관계 유형 (null이면 모든 유형)
    /// </summary>
    public IReadOnlyList<RelationshipType>? RelationshipTypes { get; set; }

    /// <summary>
    /// 최소 관계 강도
    /// </summary>
    public double MinRelationshipStrength { get; set; } = 0.0;

    /// <summary>
    /// 가중치 계산 방식
    /// </summary>
    public PathWeightType WeightType { get; set; } = PathWeightType.Hop;

    /// <summary>
    /// 경로 가중치에 관계 강도 반영
    /// </summary>
    public bool UseRelationshipStrength { get; set; } = false;

    /// <summary>
    /// 탐색 타임아웃 (밀리초)
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;
}

/// <summary>
/// 경로 가중치 유형
/// </summary>
public enum PathWeightType
{
    /// <summary>홉 수 기반 (단순 거리)</summary>
    Hop,
    /// <summary>관계 강도 기반 (강도가 높을수록 가까움)</summary>
    Strength,
    /// <summary>관계 강도 역수 기반 (강도가 높을수록 비용 낮음)</summary>
    InverseStrength
}

/// <summary>
/// 중요도 계산 옵션
/// </summary>
public class ImportanceOptions
{
    /// <summary>
    /// 댐핑 팩터 (PageRank 알고리즘용)
    /// </summary>
    public double DampingFactor { get; set; } = 0.85;

    /// <summary>
    /// 최대 반복 횟수
    /// </summary>
    public int MaxIterations { get; set; } = 100;

    /// <summary>
    /// 수렴 임계값
    /// </summary>
    public double ConvergenceThreshold { get; set; } = 1e-6;

    /// <summary>
    /// 관계 강도 가중치 적용
    /// </summary>
    public bool UseRelationshipWeights { get; set; } = true;
}

// ============================================================
// 결과 클래스
// ============================================================

/// <summary>
/// 그래프 탐색 결과
/// </summary>
public class GraphTraversalResult
{
    /// <summary>
    /// 시작 청크 ID
    /// </summary>
    public string StartChunkId { get; init; } = string.Empty;

    /// <summary>
    /// 방문한 청크 ID 목록 (탐색 순서)
    /// </summary>
    public IReadOnlyList<string> VisitedChunkIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 레벨(깊이)별 청크 분포
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<string>> ChunksByLevel { get; init; }
        = new Dictionary<int, IReadOnlyList<string>>();

    /// <summary>
    /// 탐색된 관계 목록
    /// </summary>
    public IReadOnlyList<ChunkRelationshipExtended> TraversedRelationships { get; init; }
        = Array.Empty<ChunkRelationshipExtended>();

    /// <summary>
    /// 탐색 통계
    /// </summary>
    public TraversalStatistics Statistics { get; init; } = new();
}

/// <summary>
/// 탐색 통계
/// </summary>
public class TraversalStatistics
{
    /// <summary>
    /// 방문한 노드 수
    /// </summary>
    public int VisitedNodes { get; init; }

    /// <summary>
    /// 탐색된 관계 수
    /// </summary>
    public int TraversedEdges { get; init; }

    /// <summary>
    /// 최대 도달 깊이
    /// </summary>
    public int MaxDepthReached { get; init; }

    /// <summary>
    /// 탐색 실행 시간 (밀리초)
    /// </summary>
    public double ExecutionTimeMs { get; init; }

    /// <summary>
    /// 조기 종료 여부
    /// </summary>
    public bool WasTerminatedEarly { get; init; }

    /// <summary>
    /// 조기 종료 사유
    /// </summary>
    public string? TerminationReason { get; init; }
}

/// <summary>
/// 경로 탐색 결과
/// </summary>
public class PathFindingResult
{
    /// <summary>
    /// 경로 존재 여부
    /// </summary>
    public bool PathExists { get; init; }

    /// <summary>
    /// 출발 청크 ID
    /// </summary>
    public string SourceChunkId { get; init; } = string.Empty;

    /// <summary>
    /// 목표 청크 ID
    /// </summary>
    public string TargetChunkId { get; init; } = string.Empty;

    /// <summary>
    /// 경로상의 청크 ID 목록 (출발 → 목표)
    /// </summary>
    public IReadOnlyList<string> Path { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 경로상의 관계 목록
    /// </summary>
    public IReadOnlyList<ChunkRelationshipExtended> Relationships { get; init; }
        = Array.Empty<ChunkRelationshipExtended>();

    /// <summary>
    /// 경로 길이 (홉 수)
    /// </summary>
    public int PathLength => Path.Count > 0 ? Path.Count - 1 : 0;

    /// <summary>
    /// 경로 총 가중치 (관계 강도 합)
    /// </summary>
    public double TotalWeight { get; init; }

    /// <summary>
    /// 평균 관계 강도
    /// </summary>
    public double AverageRelationshipStrength { get; init; }

    /// <summary>
    /// 탐색 실행 시간 (밀리초)
    /// </summary>
    public double ExecutionTimeMs { get; init; }
}

/// <summary>
/// 이웃 청크 탐색 결과
/// </summary>
public class NeighborhoodResult
{
    /// <summary>
    /// 중심 청크 ID
    /// </summary>
    public string CenterChunkId { get; init; } = string.Empty;

    /// <summary>
    /// 홉별 이웃 청크 목록
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<NeighborInfo>> NeighborsByHop { get; init; }
        = new Dictionary<int, IReadOnlyList<NeighborInfo>>();

    /// <summary>
    /// 총 이웃 청크 수
    /// </summary>
    public int TotalNeighbors { get; init; }

    /// <summary>
    /// 탐색 통계
    /// </summary>
    public TraversalStatistics Statistics { get; init; } = new();
}

/// <summary>
/// 이웃 청크 정보
/// </summary>
public class NeighborInfo
{
    /// <summary>
    /// 청크 ID
    /// </summary>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>
    /// 중심으로부터의 거리 (홉 수)
    /// </summary>
    public int Distance { get; init; }

    /// <summary>
    /// 연결 관계 유형
    /// </summary>
    public RelationshipType RelationshipType { get; init; }

    /// <summary>
    /// 관계 강도
    /// </summary>
    public double RelationshipStrength { get; init; }

    /// <summary>
    /// 경유 경로 (중심 → 이웃)
    /// </summary>
    public IReadOnlyList<string> PathFromCenter { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 조상 정보
/// </summary>
public class AncestorInfo
{
    /// <summary>
    /// 조상 청크 ID
    /// </summary>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>
    /// 자손 청크들로부터의 거리 목록
    /// </summary>
    public IReadOnlyDictionary<string, int> DistancesFromDescendants { get; init; }
        = new Dictionary<string, int>();

    /// <summary>
    /// 모든 자손에 대한 공통 조상 여부
    /// </summary>
    public bool IsCommonToAll { get; init; }

    /// <summary>
    /// 계층 레벨
    /// </summary>
    public int HierarchyLevel { get; init; }
}

/// <summary>
/// 전이적 폐쇄 결과
/// </summary>
public class TransitiveClosureResult
{
    /// <summary>
    /// 시작 청크 ID
    /// </summary>
    public string StartChunkId { get; init; } = string.Empty;

    /// <summary>
    /// 직접 연결된 청크
    /// </summary>
    public IReadOnlyList<string> DirectlyConnected { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 전이적으로 연결된 청크 (직접 연결 제외)
    /// </summary>
    public IReadOnlyList<TransitiveConnection> TransitiveConnections { get; init; }
        = Array.Empty<TransitiveConnection>();

    /// <summary>
    /// 도달 가능한 모든 청크 수
    /// </summary>
    public int TotalReachable { get; init; }

    /// <summary>
    /// 탐색 통계
    /// </summary>
    public TraversalStatistics Statistics { get; init; } = new();
}

/// <summary>
/// 전이적 연결 정보
/// </summary>
public class TransitiveConnection
{
    /// <summary>
    /// 대상 청크 ID
    /// </summary>
    public string TargetChunkId { get; init; } = string.Empty;

    /// <summary>
    /// 최단 경로 길이
    /// </summary>
    public int ShortestPathLength { get; init; }

    /// <summary>
    /// 경유 관계 유형들
    /// </summary>
    public IReadOnlyList<RelationshipType> IntermediateRelationships { get; init; }
        = Array.Empty<RelationshipType>();

    /// <summary>
    /// 추론된 관계 강도
    /// </summary>
    public double InferredStrength { get; init; }
}

/// <summary>
/// 연결 컴포넌트
/// </summary>
public class ConnectedComponent
{
    /// <summary>
    /// 컴포넌트 ID
    /// </summary>
    public int ComponentId { get; init; }

    /// <summary>
    /// 컴포넌트 내 청크 ID 목록
    /// </summary>
    public IReadOnlyList<string> ChunkIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 컴포넌트 크기
    /// </summary>
    public int Size => ChunkIds.Count;

    /// <summary>
    /// 대표 청크 ID (가장 많은 연결을 가진 청크)
    /// </summary>
    public string? RepresentativeChunkId { get; init; }

    /// <summary>
    /// 컴포넌트 내 관계 수
    /// </summary>
    public int InternalEdgeCount { get; init; }

    /// <summary>
    /// 컴포넌트 밀도 (실제 관계 / 가능한 관계)
    /// </summary>
    public double Density { get; init; }
}

/// <summary>
/// 브릿지 청크 정보
/// </summary>
public class BridgeChunkInfo
{
    /// <summary>
    /// 브릿지 청크 ID
    /// </summary>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>
    /// 연결하는 컴포넌트 ID들
    /// </summary>
    public IReadOnlyList<int> ConnectedComponentIds { get; init; } = Array.Empty<int>();

    /// <summary>
    /// 브릿지 중요도 점수
    /// </summary>
    public double BridgeScore { get; init; }

    /// <summary>
    /// 제거 시 분리되는 컴포넌트 수
    /// </summary>
    public int DisconnectedComponentsOnRemoval { get; init; }
}

/// <summary>
/// 그래프 밀도 분석 결과
/// </summary>
public class GraphDensityAnalysis
{
    /// <summary>
    /// 총 노드(청크) 수
    /// </summary>
    public int TotalNodes { get; init; }

    /// <summary>
    /// 총 엣지(관계) 수
    /// </summary>
    public int TotalEdges { get; init; }

    /// <summary>
    /// 그래프 밀도 (실제 관계 / 가능한 관계)
    /// </summary>
    public double Density { get; init; }

    /// <summary>
    /// 평균 연결 차수
    /// </summary>
    public double AverageDegree { get; init; }

    /// <summary>
    /// 최대 연결 차수
    /// </summary>
    public int MaxDegree { get; init; }

    /// <summary>
    /// 최소 연결 차수
    /// </summary>
    public int MinDegree { get; init; }

    /// <summary>
    /// 고립 노드 수 (연결 없음)
    /// </summary>
    public int IsolatedNodes { get; init; }

    /// <summary>
    /// 관계 유형별 분포
    /// </summary>
    public IReadOnlyDictionary<RelationshipType, int> EdgesByType { get; init; }
        = new Dictionary<RelationshipType, int>();

    /// <summary>
    /// 연결 차수 분포
    /// </summary>
    public IReadOnlyDictionary<int, int> DegreeDistribution { get; init; }
        = new Dictionary<int, int>();
}

/// <summary>
/// 순환 경로
/// </summary>
public class CyclePath
{
    /// <summary>
    /// 순환 경로상의 청크 ID 목록
    /// </summary>
    public IReadOnlyList<string> ChunkIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 순환 길이
    /// </summary>
    public int CycleLength => ChunkIds.Count;

    /// <summary>
    /// 순환에 포함된 관계 유형들
    /// </summary>
    public IReadOnlyList<RelationshipType> RelationshipTypes { get; init; }
        = Array.Empty<RelationshipType>();
}

/// <summary>
/// 그래프 일관성 검사 결과
/// </summary>
public class GraphConsistencyResult
{
    /// <summary>
    /// 전체 일관성 여부
    /// </summary>
    public bool IsConsistent { get; init; }

    /// <summary>
    /// 일관성 점수 (0.0 ~ 1.0)
    /// </summary>
    public double ConsistencyScore { get; init; }

    /// <summary>
    /// 고아 청크 (참조되지 않는 청크)
    /// </summary>
    public IReadOnlyList<string> OrphanChunks { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 끊어진 관계 (대상 청크가 없는 관계)
    /// </summary>
    public IReadOnlyList<string> BrokenRelationships { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 중복 관계
    /// </summary>
    public IReadOnlyList<DuplicateRelationship> DuplicateRelationships { get; init; }
        = Array.Empty<DuplicateRelationship>();

    /// <summary>
    /// 계층 불일치 (부모-자식 관계 불일치)
    /// </summary>
    public IReadOnlyList<HierarchyInconsistency> HierarchyInconsistencies { get; init; }
        = Array.Empty<HierarchyInconsistency>();

    /// <summary>
    /// 검사 요약
    /// </summary>
    public string Summary { get; init; } = string.Empty;
}

/// <summary>
/// 중복 관계 정보
/// </summary>
public class DuplicateRelationship
{
    /// <summary>
    /// 출발 청크 ID
    /// </summary>
    public string SourceChunkId { get; init; } = string.Empty;

    /// <summary>
    /// 대상 청크 ID
    /// </summary>
    public string TargetChunkId { get; init; } = string.Empty;

    /// <summary>
    /// 관계 유형
    /// </summary>
    public RelationshipType RelationshipType { get; init; }

    /// <summary>
    /// 중복 횟수
    /// </summary>
    public int DuplicateCount { get; init; }
}

/// <summary>
/// 계층 불일치 정보
/// </summary>
public class HierarchyInconsistency
{
    /// <summary>
    /// 부모 청크 ID
    /// </summary>
    public string ParentChunkId { get; init; } = string.Empty;

    /// <summary>
    /// 자식 청크 ID
    /// </summary>
    public string ChildChunkId { get; init; } = string.Empty;

    /// <summary>
    /// 불일치 유형
    /// </summary>
    public string InconsistencyType { get; init; } = string.Empty;

    /// <summary>
    /// 상세 설명
    /// </summary>
    public string Description { get; init; } = string.Empty;
}
