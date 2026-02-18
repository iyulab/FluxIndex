using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RelationshipType = FluxIndex.Core.Domain.Models.RelationshipType;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// 그래프 탐색 서비스 구현 - RAG를 위한 청크 관계 그래프 탐색
/// </summary>
public partial class GraphTraversalService : IGraphTraversalService
{
    private readonly IChunkHierarchyRepository _hierarchyRepository;
    private readonly ILogger<GraphTraversalService> _logger;

    public GraphTraversalService(
        IChunkHierarchyRepository hierarchyRepository,
        ILogger<GraphTraversalService> logger)
    {
        _hierarchyRepository = hierarchyRepository;
        _logger = logger;
    }

    // ============================================================
    // 기본 탐색 알고리즘
    // ============================================================

    /// <inheritdoc/>
    public async Task<GraphTraversalResult> TraverseBfsAsync(
        string startChunkId,
        GraphTraversalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new GraphTraversalOptions();
        var stopwatch = Stopwatch.StartNew();

        var visited = new HashSet<string>();
        var chunksByLevel = new Dictionary<int, List<string>>();
        var traversedRelationships = new List<ChunkRelationshipExtended>();
        var queue = new Queue<(string ChunkId, int Level)>();

        queue.Enqueue((startChunkId, 0));
        visited.Add(startChunkId);
        chunksByLevel[0] = new List<string> { startChunkId };

        string? terminationReason = null;
        var wasTerminatedEarly = false;

        while (queue.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var (currentChunkId, currentLevel) = queue.Dequeue();

            if (currentLevel >= options.MaxDepth)
            {
                continue;
            }

            if (visited.Count >= options.MaxNodes)
            {
                terminationReason = $"Maximum nodes limit ({options.MaxNodes}) reached";
                wasTerminatedEarly = true;
                break;
            }

            var relationships = await GetFilteredRelationshipsAsync(
                currentChunkId, options, cancellationToken);

            foreach (var rel in relationships)
            {
                // MaxNodes 체크 - foreach 루프 내에서도 확인
                if (visited.Count >= options.MaxNodes)
                {
                    terminationReason = $"Maximum nodes limit ({options.MaxNodes}) reached";
                    wasTerminatedEarly = true;
                    break;
                }

                var neighborId = rel.SourceChunkId == currentChunkId
                    ? rel.TargetChunkId
                    : rel.SourceChunkId;

                if (visited.Contains(neighborId))
                    continue;

                // 양방향 관계이거나 현재 청크가 소스인 경우만 진행
                if (options.DirectedOnly && rel.SourceChunkId != currentChunkId &&
                    rel.Direction != RelationshipDirection.Bidirectional)
                    continue;

                visited.Add(neighborId);
                traversedRelationships.Add(rel);

                var nextLevel = currentLevel + 1;
                if (!chunksByLevel.TryGetValue(nextLevel, out var levelList))
                {
                    levelList = new List<string>();
                    chunksByLevel[nextLevel] = levelList;
                }
                levelList.Add(neighborId);

                queue.Enqueue((neighborId, nextLevel));
            }

            // foreach에서 MaxNodes 도달시 외부 루프도 종료
            if (wasTerminatedEarly)
                break;
        }

        stopwatch.Stop();

        return new GraphTraversalResult
        {
            StartChunkId = startChunkId,
            VisitedChunkIds = visited.ToList(),
            ChunksByLevel = chunksByLevel.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<string>)kvp.Value),
            TraversedRelationships = traversedRelationships,
            Statistics = new TraversalStatistics
            {
                VisitedNodes = visited.Count,
                TraversedEdges = traversedRelationships.Count,
                MaxDepthReached = chunksByLevel.Keys.DefaultIfEmpty(0).Max(),
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                WasTerminatedEarly = wasTerminatedEarly,
                TerminationReason = terminationReason
            }
        };
    }

    /// <inheritdoc/>
    public async Task<GraphTraversalResult> TraverseDfsAsync(
        string startChunkId,
        GraphTraversalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new GraphTraversalOptions();
        var stopwatch = Stopwatch.StartNew();

        var visited = new HashSet<string>();
        var chunksByLevel = new Dictionary<int, List<string>>();
        var traversedRelationships = new List<ChunkRelationshipExtended>();
        var stack = new Stack<(string ChunkId, int Level)>();

        stack.Push((startChunkId, 0));

        string? terminationReason = null;
        var wasTerminatedEarly = false;

        while (stack.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var (currentChunkId, currentLevel) = stack.Pop();

            if (visited.Contains(currentChunkId))
                continue;

            visited.Add(currentChunkId);

            if (!chunksByLevel.TryGetValue(currentLevel, out var levelList))
            {
                levelList = new List<string>();
                chunksByLevel[currentLevel] = levelList;
            }
            levelList.Add(currentChunkId);

            if (visited.Count >= options.MaxNodes)
            {
                terminationReason = $"Maximum nodes limit ({options.MaxNodes}) reached";
                wasTerminatedEarly = true;
                break;
            }

            if (currentLevel >= options.MaxDepth)
                continue;

            var relationships = await GetFilteredRelationshipsAsync(
                currentChunkId, options, cancellationToken);

            foreach (var rel in relationships)
            {
                var neighborId = rel.SourceChunkId == currentChunkId
                    ? rel.TargetChunkId
                    : rel.SourceChunkId;

                if (visited.Contains(neighborId))
                    continue;

                if (options.DirectedOnly && rel.SourceChunkId != currentChunkId &&
                    rel.Direction != RelationshipDirection.Bidirectional)
                    continue;

                traversedRelationships.Add(rel);
                stack.Push((neighborId, currentLevel + 1));
            }
        }

        stopwatch.Stop();

        return new GraphTraversalResult
        {
            StartChunkId = startChunkId,
            VisitedChunkIds = visited.ToList(),
            ChunksByLevel = chunksByLevel.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<string>)kvp.Value),
            TraversedRelationships = traversedRelationships,
            Statistics = new TraversalStatistics
            {
                VisitedNodes = visited.Count,
                TraversedEdges = traversedRelationships.Count,
                MaxDepthReached = chunksByLevel.Keys.DefaultIfEmpty(0).Max(),
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                WasTerminatedEarly = wasTerminatedEarly,
                TerminationReason = terminationReason
            }
        };
    }

    // ============================================================
    // 경로 탐색 알고리즘
    // ============================================================

    /// <inheritdoc/>
    public async Task<PathFindingResult> FindShortestPathAsync(
        string sourceChunkId,
        string targetChunkId,
        PathFindingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new PathFindingOptions();
        var stopwatch = Stopwatch.StartNew();

        // BFS 기반 최단 경로 탐색
        var visited = new Dictionary<string, (string? Parent, ChunkRelationshipExtended? Relationship)>();
        var queue = new Queue<string>();

        queue.Enqueue(sourceChunkId);
        visited[sourceChunkId] = (null, null);

        while (queue.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var currentChunkId = queue.Dequeue();

            if (currentChunkId == targetChunkId)
            {
                // 경로 역추적
                return BuildPathResult(sourceChunkId, targetChunkId, visited, stopwatch);
            }

            var currentPath = ReconstructPath(visited, currentChunkId);
            if (currentPath.Count > options.MaxPathLength)
                continue;

            var relationships = await GetFilteredRelationshipsForPathAsync(
                currentChunkId, options, cancellationToken);

            foreach (var rel in relationships)
            {
                var neighborId = rel.SourceChunkId == currentChunkId
                    ? rel.TargetChunkId
                    : rel.SourceChunkId;

                if (visited.ContainsKey(neighborId))
                    continue;

                visited[neighborId] = (currentChunkId, rel);
                queue.Enqueue(neighborId);
            }
        }

        stopwatch.Stop();

        // 경로 없음
        return new PathFindingResult
        {
            PathExists = false,
            SourceChunkId = sourceChunkId,
            TargetChunkId = targetChunkId,
            Path = Array.Empty<string>(),
            Relationships = Array.Empty<ChunkRelationshipExtended>(),
            ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PathFindingResult>> FindKShortestPathsAsync(
        string sourceChunkId,
        string targetChunkId,
        int k,
        PathFindingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new PathFindingOptions();
        var stopwatch = Stopwatch.StartNew();
        var results = new List<PathFindingResult>();

        // Yen's K-최단 경로 알고리즘 간소화 버전
        var firstPath = await FindShortestPathAsync(
            sourceChunkId, targetChunkId, options, cancellationToken);

        if (!firstPath.PathExists)
            return results;

        results.Add(firstPath);

        var potentialPaths = new SortedSet<(int Length, List<string> Path, List<ChunkRelationshipExtended> Rels)>(
            Comparer<(int Length, List<string> Path, List<ChunkRelationshipExtended> Rels)>.Create(
                (a, b) => a.Length.CompareTo(b.Length)));

        var excludedEdges = new HashSet<(string, string)>();

        for (var i = 1; i < k && !cancellationToken.IsCancellationRequested; i++)
        {
            var previousPath = results[results.Count - 1];

            for (var j = 0; j < previousPath.Path.Count - 1; j++)
            {
                var spurNode = previousPath.Path[j];
                var rootPath = previousPath.Path.Take(j + 1).ToList();

                // 이 spur 노드에서 다른 경로 탐색
                excludedEdges.Add((previousPath.Path[j], previousPath.Path[j + 1]));

                var alternativePath = await FindPathExcludingEdgesAsync(
                    spurNode, targetChunkId, excludedEdges, options, cancellationToken);

                if (alternativePath.PathExists)
                {
                    var fullPath = rootPath.Take(rootPath.Count - 1)
                        .Concat(alternativePath.Path).ToList();

                    var fullRels = previousPath.Relationships.Take(j)
                        .Concat(alternativePath.Relationships).ToList();

                    potentialPaths.Add((fullPath.Count, fullPath, fullRels));
                }
            }

            if (potentialPaths.Count == 0)
                break;

            var shortest = potentialPaths.First();
            potentialPaths.Remove(shortest);

            results.Add(new PathFindingResult
            {
                PathExists = true,
                SourceChunkId = sourceChunkId,
                TargetChunkId = targetChunkId,
                Path = shortest.Path,
                Relationships = shortest.Rels,
                TotalWeight = shortest.Rels.Sum(r => r.Strength),
                AverageRelationshipStrength = shortest.Rels.Count > 0
                    ? shortest.Rels.Average(r => r.Strength) : 0,
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
            });
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<PathFindingResult> FindStrongestPathAsync(
        string sourceChunkId,
        string targetChunkId,
        PathFindingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new PathFindingOptions { UseRelationshipStrength = true };
        var stopwatch = Stopwatch.StartNew();

        // Dijkstra 변형 - 관계 강도 최대화 (강도 역수를 비용으로 사용)
        var distances = new Dictionary<string, double> { [sourceChunkId] = 0 };
        var parents = new Dictionary<string, (string? Parent, ChunkRelationshipExtended? Relationship)>
        {
            [sourceChunkId] = (null, null)
        };
        var priorityQueue = new SortedSet<(double Cost, string ChunkId)>(
            Comparer<(double Cost, string ChunkId)>.Create((a, b) =>
            {
                var cmp = a.Cost.CompareTo(b.Cost);
                return cmp != 0 ? cmp : string.Compare(a.ChunkId, b.ChunkId, StringComparison.Ordinal);
            }))
        {
            (0.0, sourceChunkId)
        };

        while (priorityQueue.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var (currentCost, currentChunkId) = priorityQueue.Min;
            priorityQueue.Remove(priorityQueue.Min);

            if (currentChunkId == targetChunkId)
            {
                return BuildPathResultFromParents(sourceChunkId, targetChunkId, parents, stopwatch);
            }

            var relationships = await GetFilteredRelationshipsForPathAsync(
                currentChunkId, options, cancellationToken);

            foreach (var rel in relationships)
            {
                var neighborId = rel.SourceChunkId == currentChunkId
                    ? rel.TargetChunkId
                    : rel.SourceChunkId;

                // 강도가 높을수록 비용이 낮음 (1 - strength)
                var edgeCost = 1.0 - rel.Strength;
                var newCost = currentCost + edgeCost;

                if (!distances.TryGetValue(neighborId, out var existingCost) || newCost < existingCost)
                {
                    if (existingCost > 0)
                        priorityQueue.Remove((existingCost, neighborId));

                    distances[neighborId] = newCost;
                    parents[neighborId] = (currentChunkId, rel);
                    priorityQueue.Add((newCost, neighborId));
                }
            }
        }

        stopwatch.Stop();

        return new PathFindingResult
        {
            PathExists = false,
            SourceChunkId = sourceChunkId,
            TargetChunkId = targetChunkId,
            Path = Array.Empty<string>(),
            Relationships = Array.Empty<ChunkRelationshipExtended>(),
            ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PathFindingResult>> FindAllPathsAsync(
        string sourceChunkId,
        string targetChunkId,
        int maxDepth = 5,
        PathFindingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new PathFindingOptions { MaxPathLength = maxDepth };
        var stopwatch = Stopwatch.StartNew();
        var allPaths = new List<PathFindingResult>();

        // DFS 기반 모든 경로 탐색
        var currentPath = new List<string> { sourceChunkId };
        var currentRels = new List<ChunkRelationshipExtended>();
        var visited = new HashSet<string> { sourceChunkId };

        await FindAllPathsDfsAsync(
            sourceChunkId, targetChunkId, visited, currentPath, currentRels,
            allPaths, options, stopwatch, cancellationToken);

        return allPaths;
    }

    private async Task FindAllPathsDfsAsync(
        string currentChunkId,
        string targetChunkId,
        HashSet<string> visited,
        List<string> currentPath,
        List<ChunkRelationshipExtended> currentRels,
        List<PathFindingResult> allPaths,
        PathFindingOptions options,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        if (currentChunkId == targetChunkId)
        {
            allPaths.Add(new PathFindingResult
            {
                PathExists = true,
                SourceChunkId = currentPath[0],
                TargetChunkId = targetChunkId,
                Path = currentPath.ToList(),
                Relationships = currentRels.ToList(),
                TotalWeight = currentRels.Sum(r => r.Strength),
                AverageRelationshipStrength = currentRels.Count > 0
                    ? currentRels.Average(r => r.Strength) : 0,
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
            });
            return;
        }

        if (currentPath.Count > options.MaxPathLength)
            return;

        var relationships = await GetFilteredRelationshipsForPathAsync(
            currentChunkId, options, cancellationToken);

        foreach (var rel in relationships)
        {
            var neighborId = rel.SourceChunkId == currentChunkId
                ? rel.TargetChunkId
                : rel.SourceChunkId;

            if (visited.Contains(neighborId))
                continue;

            visited.Add(neighborId);
            currentPath.Add(neighborId);
            currentRels.Add(rel);

            await FindAllPathsDfsAsync(
                neighborId, targetChunkId, visited, currentPath, currentRels,
                allPaths, options, stopwatch, cancellationToken);

            visited.Remove(neighborId);
            currentPath.RemoveAt(currentPath.Count - 1);
            currentRels.RemoveAt(currentRels.Count - 1);
        }
    }

    // ============================================================
    // 관계 분석 알고리즘
    // ============================================================

    /// <inheritdoc/>
    public async Task<NeighborhoodResult> GetNeighborhoodAsync(
        string chunkId,
        int maxHops = 2,
        GraphTraversalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new GraphTraversalOptions { MaxDepth = maxHops };
        var stopwatch = Stopwatch.StartNew();

        var traversalResult = await TraverseBfsAsync(chunkId, options, cancellationToken);

        var neighborsByHop = new Dictionary<int, List<NeighborInfo>>();

        foreach (var (level, chunks) in traversalResult.ChunksByLevel)
        {
            if (level == 0) continue; // 시작 노드 제외

            var neighbors = new List<NeighborInfo>();
            foreach (var neighborChunkId in chunks)
            {
                var relToNeighbor = traversalResult.TraversedRelationships
                    .FirstOrDefault(r => r.TargetChunkId == neighborChunkId || r.SourceChunkId == neighborChunkId);

                neighbors.Add(new NeighborInfo
                {
                    ChunkId = neighborChunkId,
                    Distance = level,
                    RelationshipType = relToNeighbor?.Type ?? RelationshipType.Sequential,
                    RelationshipStrength = relToNeighbor?.Strength ?? 0,
                    PathFromCenter = ReconstructPathToChunk(traversalResult, neighborChunkId)
                });
            }
            neighborsByHop[level] = neighbors;
        }

        stopwatch.Stop();

        return new NeighborhoodResult
        {
            CenterChunkId = chunkId,
            NeighborsByHop = neighborsByHop.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<NeighborInfo>)kvp.Value),
            TotalNeighbors = traversalResult.VisitedChunkIds.Count - 1, // 시작 노드 제외
            Statistics = traversalResult.Statistics
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AncestorInfo>> FindCommonAncestorsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        var chunkIdList = chunkIds.ToList();
        if (chunkIdList.Count == 0)
            return Array.Empty<AncestorInfo>();

        var ancestorsPerChunk = new Dictionary<string, Dictionary<string, int>>();

        // 각 청크의 조상 탐색
        foreach (var chunkId in chunkIdList)
        {
            var ancestors = new Dictionary<string, int>();
            await TraverseAncestorsAsync(chunkId, ancestors, 0, cancellationToken);
            ancestorsPerChunk[chunkId] = ancestors;
        }

        // 공통 조상 찾기
        var allAncestors = ancestorsPerChunk.Values
            .SelectMany(a => a.Keys)
            .Distinct()
            .ToList();

        var commonAncestors = new List<AncestorInfo>();

        foreach (var ancestorId in allAncestors)
        {
            var distances = new Dictionary<string, int>();
            var isCommonToAll = true;

            foreach (var chunkId in chunkIdList)
            {
                if (ancestorsPerChunk[chunkId].TryGetValue(ancestorId, out var dist))
                {
                    distances[chunkId] = dist;
                }
                else
                {
                    isCommonToAll = false;
                }
            }

            if (distances.Count > 1) // 적어도 2개의 청크와 연결
            {
                var hierarchy = await _hierarchyRepository.GetHierarchyAsync(ancestorId, cancellationToken);
                commonAncestors.Add(new AncestorInfo
                {
                    ChunkId = ancestorId,
                    DistancesFromDescendants = distances,
                    IsCommonToAll = isCommonToAll,
                    HierarchyLevel = hierarchy?.HierarchyLevel ?? 0
                });
            }
        }

        return commonAncestors.OrderBy(a => a.HierarchyLevel).ToList();
    }

    private async Task TraverseAncestorsAsync(
        string chunkId,
        Dictionary<string, int> ancestors,
        int currentDepth,
        CancellationToken cancellationToken,
        int maxDepth = 10)
    {
        if (currentDepth > maxDepth)
            return;

        var hierarchy = await _hierarchyRepository.GetHierarchyAsync(chunkId, cancellationToken);
        if (hierarchy?.ParentChunkId == null)
            return;

        if (!ancestors.TryGetValue(hierarchy.ParentChunkId, out var existingDepth) ||
            existingDepth > currentDepth + 1)
        {
            ancestors[hierarchy.ParentChunkId] = currentDepth + 1;
            await TraverseAncestorsAsync(
                hierarchy.ParentChunkId, ancestors, currentDepth + 1, cancellationToken, maxDepth);
        }
    }

    /// <inheritdoc/>
    public async Task<TransitiveClosureResult> ComputeTransitiveClosureAsync(
        string chunkId,
        IEnumerable<RelationshipType>? relationshipTypes = null,
        int maxDepth = 5,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var options = new GraphTraversalOptions
        {
            MaxDepth = maxDepth,
            RelationshipTypes = relationshipTypes?.ToList()
        };

        var traversalResult = await TraverseBfsAsync(chunkId, options, cancellationToken);

        // 직접 연결 (1-hop)
        var directlyConnected = traversalResult.ChunksByLevel
            .Where(kvp => kvp.Key == 1)
            .SelectMany(kvp => kvp.Value)
            .ToList();

        // 전이적 연결 (2+ hop)
        var transitiveConnections = new List<TransitiveConnection>();
        foreach (var (level, chunks) in traversalResult.ChunksByLevel.Where(kvp => kvp.Key > 1))
        {
            foreach (var targetChunkId in chunks)
            {
                var path = await FindShortestPathAsync(chunkId, targetChunkId, new PathFindingOptions
                {
                    RelationshipTypes = relationshipTypes?.ToList(),
                    MaxPathLength = maxDepth
                }, cancellationToken);

                if (path.PathExists)
                {
                    transitiveConnections.Add(new TransitiveConnection
                    {
                        TargetChunkId = targetChunkId,
                        ShortestPathLength = path.PathLength,
                        IntermediateRelationships = path.Relationships.Select(r => r.Type).ToList(),
                        InferredStrength = path.Relationships.Count > 0
                            ? path.Relationships.Select(r => r.Strength).Aggregate((a, b) => a * b)
                            : 0
                    });
                }
            }
        }

        stopwatch.Stop();

        return new TransitiveClosureResult
        {
            StartChunkId = chunkId,
            DirectlyConnected = directlyConnected,
            TransitiveConnections = transitiveConnections,
            TotalReachable = traversalResult.VisitedChunkIds.Count - 1,
            Statistics = traversalResult.Statistics
        };
    }

    // ============================================================
    // 그래프 분석 알고리즘
    // ============================================================

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConnectedComponent>> FindConnectedComponentsAsync(
        string? documentId = null,
        CancellationToken cancellationToken = default)
    {
        var allChunkIds = await GetAllChunkIdsAsync(documentId, cancellationToken);
        var visited = new HashSet<string>();
        var components = new List<ConnectedComponent>();
        var componentId = 0;

        foreach (var chunkId in allChunkIds)
        {
            if (visited.Contains(chunkId))
                continue;

            // BFS로 연결된 모든 청크 찾기
            var componentChunks = new List<string>();
            var queue = new Queue<string>();
            queue.Enqueue(chunkId);
            visited.Add(chunkId);

            var edgeCount = 0;
            var degreeMap = new Dictionary<string, int>();

            while (queue.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                var current = queue.Dequeue();
                componentChunks.Add(current);

                var relationships = await _hierarchyRepository.GetRelationshipsAsync(
                    current, null, cancellationToken);

                degreeMap[current] = relationships.Count;
                edgeCount += relationships.Count;

                foreach (var rel in relationships)
                {
                    var neighbor = rel.SourceChunkId == current
                        ? rel.TargetChunkId
                        : rel.SourceChunkId;

                    if (!visited.Contains(neighbor) && allChunkIds.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // 에지는 양방향으로 카운트되므로 2로 나눔
            edgeCount /= 2;

            var n = componentChunks.Count;
            var maxEdges = n > 1 ? n * (n - 1) / 2 : 1;
            var density = (double)edgeCount / maxEdges;

            var representative = degreeMap.Count > 0
                ? degreeMap.OrderByDescending(kvp => kvp.Value).First().Key
                : componentChunks.FirstOrDefault();

            components.Add(new ConnectedComponent
            {
                ComponentId = componentId++,
                ChunkIds = componentChunks,
                RepresentativeChunkId = representative,
                InternalEdgeCount = edgeCount,
                Density = density
            });
        }

        return components.OrderByDescending(c => c.Size).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BridgeChunkInfo>> FindBridgeChunksAsync(
        string? documentId = null,
        CancellationToken cancellationToken = default)
    {
        var bridges = new List<BridgeChunkInfo>();
        var allChunkIds = await GetAllChunkIdsAsync(documentId, cancellationToken);

        // 원래 컴포넌트 수 계산
        var originalComponents = await FindConnectedComponentsAsync(documentId, cancellationToken);
        var originalCount = originalComponents.Count;

        foreach (var chunkId in allChunkIds)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // 이 청크를 제거했을 때 컴포넌트 수 변화 계산
            var remainingChunks = allChunkIds.Where(c => c != chunkId).ToHashSet();
            var visited = new HashSet<string>();
            var componentCount = 0;

            foreach (var startChunk in remainingChunks)
            {
                if (visited.Contains(startChunk))
                    continue;

                componentCount++;
                var queue = new Queue<string>();
                queue.Enqueue(startChunk);
                visited.Add(startChunk);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    var relationships = await _hierarchyRepository.GetRelationshipsAsync(
                        current, null, cancellationToken);

                    foreach (var rel in relationships)
                    {
                        var neighbor = rel.SourceChunkId == current
                            ? rel.TargetChunkId
                            : rel.SourceChunkId;

                        if (!visited.Contains(neighbor) && remainingChunks.Contains(neighbor))
                        {
                            visited.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            if (componentCount > originalCount)
            {
                bridges.Add(new BridgeChunkInfo
                {
                    ChunkId = chunkId,
                    ConnectedComponentIds = originalComponents
                        .Where(c => c.ChunkIds.Contains(chunkId))
                        .Select(c => c.ComponentId)
                        .ToList(),
                    BridgeScore = (double)(componentCount - originalCount) / originalCount,
                    DisconnectedComponentsOnRemoval = componentCount - originalCount
                });
            }
        }

        return bridges.OrderByDescending(b => b.BridgeScore).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, double>> ComputeChunkImportanceAsync(
        string? documentId = null,
        ImportanceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ImportanceOptions();

        var allChunkIds = (await GetAllChunkIdsAsync(documentId, cancellationToken)).ToList();
        var n = allChunkIds.Count;

        if (n == 0)
            return new Dictionary<string, double>();

        // 인접 리스트 구축
        var adjacency = new Dictionary<string, List<(string Target, double Weight)>>();
        var outDegree = new Dictionary<string, double>();

        foreach (var chunkId in allChunkIds)
        {
            var relationships = await _hierarchyRepository.GetRelationshipsAsync(
                chunkId, null, cancellationToken);

            adjacency[chunkId] = relationships
                .Select(r => (
                    Target: r.SourceChunkId == chunkId ? r.TargetChunkId : r.SourceChunkId,
                    Weight: options.UseRelationshipWeights ? r.Strength : 1.0))
                .Where(t => allChunkIds.Contains(t.Target))
                .ToList();

            outDegree[chunkId] = adjacency[chunkId].Sum(a => a.Weight);
            if (outDegree[chunkId] == 0)
                outDegree[chunkId] = 1; // Dangling node 처리
        }

        // PageRank 초기화
        var pageRank = allChunkIds.ToDictionary(id => id, _ => 1.0 / n);
        var dampingFactor = options.DampingFactor;
        var teleport = (1 - dampingFactor) / n;

        // 반복 계산
        for (var iter = 0; iter < options.MaxIterations; iter++)
        {
            var newPageRank = new Dictionary<string, double>();
            var diff = 0.0;

            foreach (var chunkId in allChunkIds)
            {
                var sum = 0.0;

                // 이 청크를 가리키는 모든 청크로부터 점수 수집
                foreach (var sourceId in allChunkIds)
                {
                    var outlinks = adjacency[sourceId];
                    var linkToThis = outlinks.FirstOrDefault(o => o.Target == chunkId);
                    if (linkToThis.Target != null)
                    {
                        sum += (pageRank[sourceId] * linkToThis.Weight) / outDegree[sourceId];
                    }
                }

                newPageRank[chunkId] = teleport + dampingFactor * sum;
                diff += Math.Abs(newPageRank[chunkId] - pageRank[chunkId]);
            }

            pageRank = newPageRank;

            if (diff < options.ConvergenceThreshold)
            {
                LogPageRankConverged(_logger, iter + 1);
                break;
            }
        }

        return pageRank;
    }

    /// <inheritdoc/>
    public async Task<GraphDensityAnalysis> AnalyzeGraphDensityAsync(
        IEnumerable<string>? chunkIds = null,
        CancellationToken cancellationToken = default)
    {
        var targetChunks = chunkIds?.ToHashSet()
            ?? (await GetAllChunkIdsAsync(null, cancellationToken)).ToHashSet();

        var n = targetChunks.Count;
        if (n == 0)
        {
            return new GraphDensityAnalysis
            {
                TotalNodes = 0,
                TotalEdges = 0,
                Density = 0,
                AverageDegree = 0,
                MaxDegree = 0,
                MinDegree = 0,
                IsolatedNodes = 0
            };
        }

        var edgesByType = new Dictionary<RelationshipType, int>();
        var degreeDistribution = new Dictionary<int, int>();
        var degrees = new Dictionary<string, int>();
        var totalEdges = 0;
        var isolatedNodes = 0;

        foreach (var chunkId in targetChunks)
        {
            var relationships = await _hierarchyRepository.GetRelationshipsAsync(
                chunkId, null, cancellationToken);

            var relevantRels = relationships
                .Where(r => targetChunks.Contains(r.SourceChunkId) && targetChunks.Contains(r.TargetChunkId))
                .ToList();

            var degree = relevantRels.Count;
            degrees[chunkId] = degree;

            if (degree == 0)
                isolatedNodes++;

            if (!degreeDistribution.ContainsKey(degree))
                degreeDistribution[degree] = 0;
            degreeDistribution[degree]++;

            foreach (var rel in relevantRels.Where(r => r.SourceChunkId == chunkId))
            {
                if (!edgesByType.ContainsKey(rel.Type))
                    edgesByType[rel.Type] = 0;
                edgesByType[rel.Type]++;
                totalEdges++;
            }
        }

        var maxPossibleEdges = n > 1 ? n * (n - 1) / 2 : 1;
        var actualEdges = totalEdges; // 단방향 카운트

        return new GraphDensityAnalysis
        {
            TotalNodes = n,
            TotalEdges = actualEdges,
            Density = (double)actualEdges / maxPossibleEdges,
            AverageDegree = degrees.Values.Average(),
            MaxDegree = degrees.Values.DefaultIfEmpty(0).Max(),
            MinDegree = degrees.Values.DefaultIfEmpty(0).Min(),
            IsolatedNodes = isolatedNodes,
            EdgesByType = edgesByType,
            DegreeDistribution = degreeDistribution
        };
    }

    // ============================================================
    // 사이클 및 일관성 검사
    // ============================================================

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CyclePath>> DetectCyclesAsync(
        string? documentId = null,
        CancellationToken cancellationToken = default)
    {
        var cycles = new List<CyclePath>();
        var allChunkIds = await GetAllChunkIdsAsync(documentId, cancellationToken);

        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        var path = new List<string>();
        var pathRels = new List<RelationshipType>();

        foreach (var chunkId in allChunkIds)
        {
            if (!visited.Contains(chunkId))
            {
                await DetectCyclesDfsAsync(
                    chunkId, visited, recursionStack, path, pathRels, cycles, cancellationToken);
            }
        }

        return cycles;
    }

    private async Task DetectCyclesDfsAsync(
        string chunkId,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        List<string> path,
        List<RelationshipType> pathRels,
        List<CyclePath> cycles,
        CancellationToken cancellationToken)
    {
        visited.Add(chunkId);
        recursionStack.Add(chunkId);
        path.Add(chunkId);

        var relationships = await _hierarchyRepository.GetRelationshipsAsync(chunkId, null, cancellationToken);

        foreach (var rel in relationships.Where(r => r.SourceChunkId == chunkId))
        {
            var neighbor = rel.TargetChunkId;

            if (!visited.Contains(neighbor))
            {
                pathRels.Add(rel.Type);
                await DetectCyclesDfsAsync(
                    neighbor, visited, recursionStack, path, pathRels, cycles, cancellationToken);
                pathRels.RemoveAt(pathRels.Count - 1);
            }
            else if (recursionStack.Contains(neighbor))
            {
                // 사이클 발견
                var cycleStart = path.IndexOf(neighbor);
                var cyclePath = path.Skip(cycleStart).Append(neighbor).ToList();
                var cycleRels = pathRels.Skip(cycleStart).Append(rel.Type).ToList();

                cycles.Add(new CyclePath
                {
                    ChunkIds = cyclePath,
                    RelationshipTypes = cycleRels
                });
            }
        }

        path.RemoveAt(path.Count - 1);
        recursionStack.Remove(chunkId);
    }

    /// <inheritdoc/>
    public async Task<GraphConsistencyResult> CheckConsistencyAsync(
        string? documentId = null,
        CancellationToken cancellationToken = default)
    {
        var allChunkIds = (await GetAllChunkIdsAsync(documentId, cancellationToken)).ToHashSet();
        var orphanChunks = new List<string>();
        var brokenRelationships = new List<string>();
        var duplicateRelationships = new List<DuplicateRelationship>();
        var hierarchyInconsistencies = new List<HierarchyInconsistency>();

        var relationshipCounts = new Dictionary<(string, string, RelationshipType), int>();

        foreach (var chunkId in allChunkIds)
        {
            // 계층 구조 확인
            var hierarchy = await _hierarchyRepository.GetHierarchyAsync(chunkId, cancellationToken);

            // 부모가 있는지 확인
            if (hierarchy?.ParentChunkId != null && !allChunkIds.Contains(hierarchy.ParentChunkId))
            {
                brokenRelationships.Add($"Chunk {chunkId} references non-existent parent {hierarchy.ParentChunkId}");
            }

            // 부모-자식 일관성 확인
            if (hierarchy?.ParentChunkId != null)
            {
                var parentHierarchy = await _hierarchyRepository.GetHierarchyAsync(
                    hierarchy.ParentChunkId, cancellationToken);

                if (parentHierarchy != null && !parentHierarchy.ChildChunkIds.Contains(chunkId))
                {
                    hierarchyInconsistencies.Add(new HierarchyInconsistency
                    {
                        ParentChunkId = hierarchy.ParentChunkId,
                        ChildChunkId = chunkId,
                        InconsistencyType = "ParentChildMismatch",
                        Description = $"Chunk {chunkId} claims {hierarchy.ParentChunkId} as parent, but parent doesn't list it as child"
                    });
                }
            }

            // 관계 확인
            var relationships = await _hierarchyRepository.GetRelationshipsAsync(chunkId, null, cancellationToken);

            foreach (var rel in relationships)
            {
                // 대상 청크 존재 확인
                if (!allChunkIds.Contains(rel.TargetChunkId))
                {
                    brokenRelationships.Add($"Relationship from {rel.SourceChunkId} to non-existent {rel.TargetChunkId}");
                }

                // 중복 관계 확인
                var key = (rel.SourceChunkId, rel.TargetChunkId, rel.Type);
                if (!relationshipCounts.ContainsKey(key))
                    relationshipCounts[key] = 0;
                relationshipCounts[key]++;
            }

            // 고아 청크 확인 (관계 없고 부모 없는 비루트 청크)
            var hasRelationships = relationships.Count > 0;
            var hasParent = hierarchy?.ParentChunkId != null;
            var hasChildren = hierarchy?.ChildChunkIds.Count > 0;

            if (!hasRelationships && !hasParent && !hasChildren && hierarchy?.HierarchyLevel > 0)
            {
                orphanChunks.Add(chunkId);
            }
        }

        // 중복 관계 수집
        foreach (var (key, count) in relationshipCounts.Where(kvp => kvp.Value > 1))
        {
            duplicateRelationships.Add(new DuplicateRelationship
            {
                SourceChunkId = key.Item1,
                TargetChunkId = key.Item2,
                RelationshipType = key.Item3,
                DuplicateCount = count
            });
        }

        var totalIssues = orphanChunks.Count + brokenRelationships.Count +
                         duplicateRelationships.Count + hierarchyInconsistencies.Count;

        var score = allChunkIds.Count > 0
            ? Math.Max(0, 1.0 - ((double)totalIssues / allChunkIds.Count))
            : 1.0;

        return new GraphConsistencyResult
        {
            IsConsistent = totalIssues == 0,
            ConsistencyScore = score,
            OrphanChunks = orphanChunks,
            BrokenRelationships = brokenRelationships,
            DuplicateRelationships = duplicateRelationships,
            HierarchyInconsistencies = hierarchyInconsistencies,
            Summary = $"Checked {allChunkIds.Count} chunks: {orphanChunks.Count} orphans, " +
                     $"{brokenRelationships.Count} broken relationships, " +
                     $"{duplicateRelationships.Count} duplicate relationships, " +
                     $"{hierarchyInconsistencies.Count} hierarchy inconsistencies"
        };
    }

    // ============================================================
    // 헬퍼 메서드
    // ============================================================

    private async Task<IEnumerable<ChunkRelationshipExtended>> GetFilteredRelationshipsAsync(
        string chunkId,
        GraphTraversalOptions options,
        CancellationToken cancellationToken)
    {
        var relationships = await _hierarchyRepository.GetRelationshipsAsync(
            chunkId, options.RelationshipTypes, cancellationToken);

        return relationships
            .Where(r => r.Strength >= options.MinRelationshipStrength)
            .Where(r => !options.HierarchicalOnly || r.Type == RelationshipType.Hierarchical);
    }

    private async Task<IEnumerable<ChunkRelationshipExtended>> GetFilteredRelationshipsForPathAsync(
        string chunkId,
        PathFindingOptions options,
        CancellationToken cancellationToken)
    {
        var relationships = await _hierarchyRepository.GetRelationshipsAsync(
            chunkId, options.RelationshipTypes, cancellationToken);

        return relationships.Where(r => r.Strength >= options.MinRelationshipStrength);
    }

    private async Task<PathFindingResult> FindPathExcludingEdgesAsync(
        string sourceChunkId,
        string targetChunkId,
        HashSet<(string, string)> excludedEdges,
        PathFindingOptions options,
        CancellationToken cancellationToken)
    {
        var visited = new Dictionary<string, (string? Parent, ChunkRelationshipExtended? Relationship)>();
        var queue = new Queue<string>();

        queue.Enqueue(sourceChunkId);
        visited[sourceChunkId] = (null, null);

        while (queue.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var currentChunkId = queue.Dequeue();

            if (currentChunkId == targetChunkId)
            {
                return BuildPathResult(sourceChunkId, targetChunkId, visited, Stopwatch.StartNew());
            }

            var relationships = await GetFilteredRelationshipsForPathAsync(
                currentChunkId, options, cancellationToken);

            foreach (var rel in relationships)
            {
                var neighborId = rel.SourceChunkId == currentChunkId
                    ? rel.TargetChunkId
                    : rel.SourceChunkId;

                if (visited.ContainsKey(neighborId))
                    continue;

                if (excludedEdges.Contains((currentChunkId, neighborId)) ||
                    excludedEdges.Contains((neighborId, currentChunkId)))
                    continue;

                visited[neighborId] = (currentChunkId, rel);
                queue.Enqueue(neighborId);
            }
        }

        return new PathFindingResult { PathExists = false };
    }

    private static PathFindingResult BuildPathResult(
        string sourceChunkId,
        string targetChunkId,
        Dictionary<string, (string? Parent, ChunkRelationshipExtended? Relationship)> visited,
        Stopwatch stopwatch)
    {
        var path = new List<string>();
        var relationships = new List<ChunkRelationshipExtended>();
        var current = targetChunkId;

        while (current != null)
        {
            path.Add(current);
            var (parent, rel) = visited[current];
            if (rel != null)
                relationships.Add(rel);
            current = parent;
        }

        path.Reverse();
        relationships.Reverse();

        stopwatch.Stop();

        return new PathFindingResult
        {
            PathExists = true,
            SourceChunkId = sourceChunkId,
            TargetChunkId = targetChunkId,
            Path = path,
            Relationships = relationships,
            TotalWeight = relationships.Sum(r => r.Strength),
            AverageRelationshipStrength = relationships.Count > 0
                ? relationships.Average(r => r.Strength) : 0,
            ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
        };
    }

    private static PathFindingResult BuildPathResultFromParents(
        string sourceChunkId,
        string targetChunkId,
        Dictionary<string, (string? Parent, ChunkRelationshipExtended? Relationship)> parents,
        Stopwatch stopwatch)
    {
        return BuildPathResult(sourceChunkId, targetChunkId, parents, stopwatch);
    }

    private static List<string> ReconstructPath(
        Dictionary<string, (string? Parent, ChunkRelationshipExtended? Relationship)> visited,
        string chunkId)
    {
        var path = new List<string>();
        var current = chunkId;

        while (current != null && visited.TryGetValue(current, out var info))
        {
            path.Add(current);
            current = info.Parent;
        }

        path.Reverse();
        return path;
    }

    private static List<string> ReconstructPathToChunk(
        GraphTraversalResult traversalResult,
        string targetChunkId)
    {
        // BFS 결과에서 경로 역추적은 복잡하므로 간단하게 반환
        // 실제 구현에서는 부모 추적 정보가 필요
        return new List<string> { traversalResult.StartChunkId, targetChunkId };
    }

    private async Task<HashSet<string>> GetAllChunkIdsAsync(
        string? documentId,
        CancellationToken cancellationToken)
    {
        var chunkIds = new HashSet<string>();

        // 문서별로 계층 수준 0~10까지 청크 수집
        for (var level = 0; level <= 10; level++)
        {
            var chunks = await _hierarchyRepository.GetChunksByLevelAsync(
                documentId ?? string.Empty, level, cancellationToken);

            foreach (var chunk in chunks)
            {
                chunkIds.Add(chunk.ChunkId);
            }

            if (chunks.Count == 0 && level > 0)
                break;
        }

        return chunkIds;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "PageRank converged after {Iterations} iterations")]
    private static partial void LogPageRankConverged(ILogger logger, int iterations);

    #endregion
}
