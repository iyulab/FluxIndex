using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Leiden algorithm implementation for hierarchical community detection.
/// The Leiden algorithm improves on Louvain by adding a refinement phase
/// that ensures all communities are well-connected.
///
/// Reference: Traag, V.A., Waltman, L. &amp; van Eck, N.J. From Louvain to Leiden:
/// guaranteeing well-connected communities. Sci Rep 9, 5233 (2019).
/// </summary>
public partial class LeidenCommunityService : ILeidenCommunityService
{
    private static readonly char[] WordSplitSeparators = [' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '"', '\''];

    private readonly ITextCompletionService? _llmService;
    private readonly ILogger<LeidenCommunityService> _logger;
    private Random _random = new();

    public LeidenCommunityService(
        ILogger<LeidenCommunityService> logger,
        ITextCompletionService? llmService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _llmService = llmService;
    }

    /// <inheritdoc />
    public async Task<CommunityHierarchy> DetectHierarchicalCommunitiesAsync(
        IEnumerable<LeidenChunk> chunks,
        LeidenOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LeidenOptions();
        var stopwatch = Stopwatch.StartNew();

        var chunkList = chunks.ToList();
        if (chunkList.Count == 0)
        {
            return new CommunityHierarchy
            {
                Levels = Array.Empty<CommunityLevel>(),
                TotalChunks = 0,
                Options = options
            };
        }

        // Initialize random with seed if provided
        _random = options.RandomSeed.HasValue
            ? new Random(options.RandomSeed.Value)
            : new Random();

        LogLeidenCommunity4(_logger, chunkList.Count);

        // Step 1: Build similarity graph
        var graph = BuildSimilarityGraph(chunkList, options);

        // Step 2: Detect communities at multiple levels
        var levels = new List<CommunityLevel>();
        var currentPartition = InitializePartition(chunkList);
        var modularityHistory = new List<double>();

        for (int level = 0; level < options.MaxHierarchyLevels; level++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Run Leiden algorithm on current graph
            var (partition, modularity, iterations) = RunLeidenIteration(
                graph,
                currentPartition,
                options.Resolution,
                options.MaxIterations,
                options.MinModularityGain,
                options.UseRefinement);

            modularityHistory.Add(modularity);

            // Build communities from partition
            var communities = BuildCommunitiesFromPartition(
                partition,
                chunkList,
                level,
                options.MinCommunitySize);

            if (communities.Count == 0)
            {
                break;
            }

            levels.Add(new CommunityLevel
            {
                LevelIndex = level,
                Communities = communities,
                Modularity = modularity,
                Resolution = options.Resolution
            });

            if (_logger.IsEnabled(LogLevel.Debug))
                LogLeidenCommunity3(_logger, level, communities.Count, modularity);

            // Check if we should stop
            if (communities.Count == 1 ||
                (level > 0 && communities.Count == levels[level - 1].CommunityCount))
            {
                break;
            }

            // Aggregate graph for next level
            graph = AggregateGraph(graph, partition);
            currentPartition = ResetPartition(communities.Count);
        }

        // Link parent-child relationships between levels
        LinkHierarchyLevels(levels);

        stopwatch.Stop();

        var hierarchy = new CommunityHierarchy
        {
            Levels = levels,
            TotalChunks = chunkList.Count,
            Options = options,
            Statistics = new LeidenStatistics
            {
                TotalIterations = levels.Count,
                FinalModularity = modularityHistory.LastOrDefault(),
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                GraphEdges = graph.Values.Sum(neighbors => neighbors.Count) / 2,
                AverageCommunitySize = levels.FirstOrDefault()?.Communities.Average(c => c.Size) ?? 0,
                ModularityByLevel = modularityHistory
            }
        };

        // Generate summaries if requested
        if (options.GenerateSummariesOnDetection && _llmService != null)
        {
            for (int level = 0; level < hierarchy.LevelCount; level++)
            {
                await GenerateSummariesAsync(hierarchy, level, cancellationToken);
            }
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            LogLeidenCommunity2(_logger, hierarchy.LevelCount, levels.FirstOrDefault()?.CommunityCount ?? 0, stopwatch.ElapsedMilliseconds);

        return hierarchy;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeidenCommunitySummary>> GenerateSummariesAsync(
        CommunityHierarchy hierarchy,
        int level,
        CancellationToken cancellationToken = default)
    {
        if (level < 0 || level >= hierarchy.LevelCount)
        {
            return Array.Empty<LeidenCommunitySummary>();
        }

        var summaries = new List<LeidenCommunitySummary>();
        var communities = hierarchy.Levels[level].Communities;

        foreach (var community in communities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var summary = await GenerateCommunitySummaryAsync(community, level, cancellationToken);
            if (summary != null)
            {
                summaries.Add(summary);
            }
        }

        return summaries;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LeidenCommunityMatch>> FindRelevantCommunitiesAsync(
        EmbeddingVector queryEmbedding,
        CommunityHierarchy hierarchy,
        int level = 0,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        if (level < 0 || level >= hierarchy.LevelCount)
        {
            return Task.FromResult<IReadOnlyList<LeidenCommunityMatch>>(Array.Empty<LeidenCommunityMatch>());
        }

        var communities = hierarchy.Levels[level].Communities;
        var matches = new List<LeidenCommunityMatch>();

        foreach (var community in communities)
        {
            if (community.Centroid == null)
            {
                continue;
            }

            var similarity = CalculateCosineSimilarity(queryEmbedding, community.Centroid);
            matches.Add(new LeidenCommunityMatch
            {
                Community = community,
                Level = level,
                Similarity = similarity
            });
        }

        var result = matches
            .OrderByDescending(m => m.Similarity)
            .Take(topK)
            .ToList();

        return Task.FromResult<IReadOnlyList<LeidenCommunityMatch>>(result);
    }

    /// <inheritdoc />
    public async Task<CommunityHierarchy> UpdateHierarchyAsync(
        CommunityHierarchy hierarchy,
        IEnumerable<LeidenChunk> newChunks,
        LeidenOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Simple implementation: rebuild with all chunks
        // A more sophisticated implementation would incrementally update
        var allChunks = new List<LeidenChunk>();

        // Note: In production, we would need to maintain the original chunks
        // For now, we just detect on new chunks
        return await DetectHierarchicalCommunitiesAsync(
            newChunks,
            options ?? hierarchy.Options,
            cancellationToken);
    }

    #region Private Methods

    /// <summary>
    /// Builds a k-NN similarity graph from chunk embeddings
    /// </summary>
    private static Dictionary<int, List<(int neighbor, double weight)>> BuildSimilarityGraph(
        List<LeidenChunk> chunks,
        LeidenOptions options)
    {
        var graph = new Dictionary<int, List<(int, double)>>();

        for (int i = 0; i < chunks.Count; i++)
        {
            graph[i] = new List<(int, double)>();
        }

        // Calculate similarities and build graph
        for (int i = 0; i < chunks.Count; i++)
        {
            var similarities = new List<(int index, double sim)>();

            for (int j = 0; j < chunks.Count; j++)
            {
                if (i == j) continue;

                var sim = CalculateCosineSimilarity(chunks[i].Embedding, chunks[j].Embedding);
                if (sim >= options.SimilarityThreshold)
                {
                    similarities.Add((j, sim));
                }
            }

            // Keep top-k neighbors
            var topNeighbors = similarities
                .OrderByDescending(s => s.sim)
                .Take(options.MaxNeighbors)
                .ToList();

            foreach (var (neighbor, weight) in topNeighbors)
            {
                graph[i].Add((neighbor, weight));
                // Add reverse edge if not exists
                if (!graph[neighbor].Any(e => e.Item1 == i))
                {
                    graph[neighbor].Add((i, weight));
                }
            }
        }

        return graph;
    }

    /// <summary>
    /// Initializes each node in its own community
    /// </summary>
    private static Dictionary<int, int> InitializePartition(List<LeidenChunk> chunks)
    {
        return chunks.Select((_, i) => i).ToDictionary(i => i, i => i);
    }

    /// <summary>
    /// Resets partition for aggregated graph
    /// </summary>
    private static Dictionary<int, int> ResetPartition(int count)
    {
        return Enumerable.Range(0, count).ToDictionary(i => i, i => i);
    }

    /// <summary>
    /// Runs one iteration of the Leiden algorithm
    /// </summary>
    private (Dictionary<int, int> partition, double modularity, int iterations) RunLeidenIteration(
        Dictionary<int, List<(int neighbor, double weight)>> graph,
        Dictionary<int, int> initialPartition,
        double resolution,
        int maxIterations,
        double minGain,
        bool useRefinement)
    {
        var partition = new Dictionary<int, int>(initialPartition);
        var totalWeight = graph.Values.SelectMany(e => e).Sum(e => e.weight) / 2;

        if (totalWeight == 0)
        {
            totalWeight = 1; // Avoid division by zero
        }

        double modularity = CalculateModularity(graph, partition, resolution, totalWeight);
        int iterations = 0;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            iterations++;
            var improved = false;

            // Phase 1: Local moving
            var nodes = graph.Keys.ToList();
            Shuffle(nodes);

            foreach (var node in nodes)
            {
                var currentCommunity = partition[node];
                var bestCommunity = currentCommunity;
                var bestGain = 0.0;

                // Get neighboring communities
                var neighboringCommunities = graph[node]
                    .Select(e => partition[e.neighbor])
                    .Distinct()
                    .Where(c => c != currentCommunity)
                    .ToList();

                foreach (var community in neighboringCommunities)
                {
                    var gain = CalculateModularityGain(
                        graph, partition, node, community, resolution, totalWeight);

                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        bestCommunity = community;
                    }
                }

                if (bestGain > minGain)
                {
                    partition[node] = bestCommunity;
                    improved = true;
                }
            }

            // Phase 2: Refinement (if enabled)
            if (useRefinement && improved)
            {
                partition = RefinePartition(graph, partition, resolution, totalWeight);
            }

            var newModularity = CalculateModularity(graph, partition, resolution, totalWeight);

            if (!improved || newModularity - modularity < minGain)
            {
                break;
            }

            modularity = newModularity;
        }

        // Renumber communities to be consecutive
        partition = RenumberCommunities(partition);

        return (partition, modularity, iterations);
    }

    /// <summary>
    /// Refines partition to ensure well-connected communities
    /// </summary>
    private static Dictionary<int, int> RefinePartition(
        Dictionary<int, List<(int neighbor, double weight)>> graph,
        Dictionary<int, int> partition,
        double resolution,
        double totalWeight)
    {
        var refined = new Dictionary<int, int>(partition);
        var communities = partition.Values.Distinct().ToList();

        foreach (var community in communities)
        {
            var nodes = partition.Where(kvp => kvp.Value == community).Select(kvp => kvp.Key).ToList();

            if (nodes.Count <= 1) continue;

            // Check connectivity within community
            var subgraph = new Dictionary<int, List<int>>();
            foreach (var node in nodes)
            {
                subgraph[node] = graph[node]
                    .Where(e => partition[e.neighbor] == community)
                    .Select(e => e.neighbor)
                    .ToList();
            }

            // Find connected components
            var components = FindConnectedComponents(subgraph);

            if (components.Count > 1)
            {
                // Split into connected components
                for (int i = 1; i < components.Count; i++)
                {
                    var newCommunity = partition.Values.Max() + 1;
                    foreach (var node in components[i])
                    {
                        refined[node] = newCommunity;
                    }
                }
            }
        }

        return refined;
    }

    /// <summary>
    /// Finds connected components in a subgraph
    /// </summary>
    private static List<List<int>> FindConnectedComponents(Dictionary<int, List<int>> subgraph)
    {
        var visited = new HashSet<int>();
        var components = new List<List<int>>();

        foreach (var node in subgraph.Keys)
        {
            if (visited.Contains(node)) continue;

            var component = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(node);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (visited.Contains(current)) continue;

                visited.Add(current);
                component.Add(current);

                foreach (var neighbor in subgraph[current])
                {
                    if (!visited.Contains(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }

    /// <summary>
    /// Calculates modularity of the current partition
    /// </summary>
    private static double CalculateModularity(
        Dictionary<int, List<(int neighbor, double weight)>> graph,
        Dictionary<int, int> partition,
        double resolution,
        double totalWeight)
    {
        double modularity = 0;

        // Group by community
        var communities = partition.GroupBy(kvp => kvp.Value);

        foreach (var community in communities)
        {
            var nodes = community.Select(kvp => kvp.Key).ToHashSet();

            double internalWeight = 0;
            double totalDegree = 0;

            foreach (var node in nodes)
            {
                var edges = graph[node];
                totalDegree += edges.Sum(e => e.weight);

                foreach (var (neighbor, weight) in edges)
                {
                    if (nodes.Contains(neighbor))
                    {
                        internalWeight += weight;
                    }
                }
            }

            internalWeight /= 2; // Each internal edge counted twice

            modularity += internalWeight / totalWeight -
                          resolution * (totalDegree / (2 * totalWeight)) * (totalDegree / (2 * totalWeight));
        }

        return modularity;
    }

    /// <summary>
    /// Calculates modularity gain for moving a node to a new community
    /// </summary>
    private static double CalculateModularityGain(
        Dictionary<int, List<(int neighbor, double weight)>> graph,
        Dictionary<int, int> partition,
        int node,
        int newCommunity,
        double resolution,
        double totalWeight)
    {
        var currentCommunity = partition[node];
        var edges = graph[node];
        var nodeDegree = edges.Sum(e => e.weight);

        // Calculate edges to current and new community
        double edgesToCurrent = 0;
        double edgesToNew = 0;
        double currentCommunityDegree = 0;
        double newCommunityDegree = 0;

        foreach (var (neighbor, weight) in edges)
        {
            var neighborCommunity = partition[neighbor];
            if (neighborCommunity == currentCommunity)
            {
                edgesToCurrent += weight;
            }
            if (neighborCommunity == newCommunity)
            {
                edgesToNew += weight;
            }
        }

        // Calculate community degrees
        foreach (var kvp in partition)
        {
            var nodeEdges = graph[kvp.Key];
            var degree = nodeEdges.Sum(e => e.weight);

            if (kvp.Value == currentCommunity && kvp.Key != node)
            {
                currentCommunityDegree += degree;
            }
            if (kvp.Value == newCommunity)
            {
                newCommunityDegree += degree;
            }
        }

        // Calculate gain
        var gain = (edgesToNew - edgesToCurrent) / totalWeight +
                   resolution * nodeDegree * (currentCommunityDegree - newCommunityDegree) /
                   (2 * totalWeight * totalWeight);

        return gain;
    }

    /// <summary>
    /// Renumbers communities to be consecutive starting from 0
    /// </summary>
    private static Dictionary<int, int> RenumberCommunities(Dictionary<int, int> partition)
    {
        var communityMap = partition.Values.Distinct().Select((c, i) => (c, i))
            .ToDictionary(x => x.c, x => x.i);

        return partition.ToDictionary(kvp => kvp.Key, kvp => communityMap[kvp.Value]);
    }

    /// <summary>
    /// Aggregates graph by combining nodes in the same community
    /// </summary>
    private static Dictionary<int, List<(int neighbor, double weight)>> AggregateGraph(
        Dictionary<int, List<(int neighbor, double weight)>> graph,
        Dictionary<int, int> partition)
    {
        var communityCount = partition.Values.Distinct().Count();
        var aggregated = new Dictionary<int, List<(int, double)>>();

        for (int i = 0; i < communityCount; i++)
        {
            aggregated[i] = new List<(int, double)>();
        }

        // Aggregate edges between communities
        var edgeWeights = new Dictionary<(int, int), double>();

        foreach (var kvp in graph)
        {
            var sourceCommunity = partition[kvp.Key];
            foreach (var (neighbor, weight) in kvp.Value)
            {
                var targetCommunity = partition[neighbor];
                if (sourceCommunity == targetCommunity) continue;

                var edge = (Math.Min(sourceCommunity, targetCommunity),
                           Math.Max(sourceCommunity, targetCommunity));

                if (!edgeWeights.ContainsKey(edge))
                {
                    edgeWeights[edge] = 0;
                }
                edgeWeights[edge] += weight;
            }
        }

        // Build aggregated graph
        foreach (var (edge, weight) in edgeWeights)
        {
            aggregated[edge.Item1].Add((edge.Item2, weight / 2)); // Divide by 2 because we counted twice
            aggregated[edge.Item2].Add((edge.Item1, weight / 2));
        }

        return aggregated;
    }

    /// <summary>
    /// Builds communities from the final partition
    /// </summary>
    private static List<LeidenCommunity> BuildCommunitiesFromPartition(
        Dictionary<int, int> partition,
        List<LeidenChunk> chunks,
        int level,
        int minCommunitySize)
    {
        var communities = new List<LeidenCommunity>();
        var groups = partition.GroupBy(kvp => kvp.Value);

        foreach (var group in groups)
        {
            var nodeIndices = group.Select(kvp => kvp.Key).ToList();

            if (nodeIndices.Count < minCommunitySize)
            {
                continue;
            }

            var chunkIds = nodeIndices.Select(i => chunks[i].Id).ToList();
            var embeddings = nodeIndices.Select(i => chunks[i].Embedding).ToList();

            // Calculate centroid
            var centroid = CalculateCentroid(embeddings);

            // Calculate cohesion (average pairwise similarity)
            var cohesion = CalculateCohesion(embeddings);

            // Extract keywords from content
            var contents = nodeIndices.Select(i => chunks[i].Content).ToList();
            var keywords = ExtractKeywords(contents);

            // Select representative chunks (closest to centroid)
            var representatives = centroid is not null
                ? SelectRepresentatives(embeddings, centroid, chunkIds, 3)
                : chunkIds.Take(3).ToList();

            communities.Add(new LeidenCommunity
            {
                Index = group.Key,
                ChunkIds = chunkIds,
                Centroid = centroid,
                Cohesion = cohesion,
                Keywords = keywords,
                RepresentativeChunkIds = representatives
            });
        }

        return communities;
    }

    /// <summary>
    /// Links parent-child relationships between hierarchy levels
    /// </summary>
    private static void LinkHierarchyLevels(List<CommunityLevel> levels)
    {
        for (int i = 0; i < levels.Count - 1; i++)
        {
            var finerLevel = levels[i];
            var coarserLevel = levels[i + 1];

            // For each community in the finer level, find parent in coarser level
            // This is a simplified approach - in practice we'd track this during aggregation
            foreach (var finerCommunity in finerLevel.Communities)
            {
                var centroid = finerCommunity.Centroid;
                if (centroid == null) continue;

                // Find closest community in coarser level
                var bestParent = coarserLevel.Communities
                    .Where(c => c.Centroid != null)
                    .OrderByDescending(c => CalculateCosineSimilarity(centroid, c.Centroid!))
                    .FirstOrDefault();

                // Note: Would need mutable communities to set ParentCommunityId
                // In practice, this would be tracked during the algorithm
            }
        }
    }

    /// <summary>
    /// Generates a summary for a community using LLM
    /// </summary>
    private async Task<LeidenCommunitySummary?> GenerateCommunitySummaryAsync(
        LeidenCommunity community,
        int level,
        CancellationToken cancellationToken)
    {
        if (_llmService == null || community.RepresentativeChunkIds.Count == 0)
        {
            // Return a simple keyword-based summary
            return new LeidenCommunitySummary
            {
                CommunityId = community.Id,
                Level = level,
                Summary = $"Community of {community.Size} chunks about: {string.Join(", ", community.Keywords.Take(5))}",
                Themes = community.Keywords.ToList(),
                Confidence = 0.5
            };
        }

        try
        {
            var prompt = BuildSummaryPrompt(community);
            var response = await _llmService.CompleteAsync(
                prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = 500, Temperature = 0.3f }, cancellationToken);

            return new LeidenCommunitySummary
            {
                CommunityId = community.Id,
                Level = level,
                Summary = response.Trim(),
                Themes = community.Keywords.ToList(),
                Confidence = 0.8
            };
        }
        catch (Exception ex)
        {
            LogLeidenCommunity1(_logger, ex, community.Id);
            return null;
        }
    }

    private static string BuildSummaryPrompt(LeidenCommunity community)
    {
        return $"""
            Summarize the main theme of this document cluster in 1-2 sentences.

            Keywords: {string.Join(", ", community.Keywords.Take(10))}
            Size: {community.Size} documents

            Provide a concise summary that captures the central topic.
            """;
    }

    /// <summary>
    /// Calculates cosine similarity between two embedding vectors
    /// </summary>
    private static double CalculateCosineSimilarity(EmbeddingVector a, EmbeddingVector b)
    {
        var values1 = a.Values;
        var values2 = b.Values;

        if (values1.Length != values2.Length)
        {
            return 0;
        }

        double dot = 0, mag1 = 0, mag2 = 0;

        for (int i = 0; i < values1.Length; i++)
        {
            dot += values1[i] * values2[i];
            mag1 += values1[i] * values1[i];
            mag2 += values2[i] * values2[i];
        }

        var denominator = Math.Sqrt(mag1) * Math.Sqrt(mag2);
        return denominator > 0 ? dot / denominator : 0;
    }

    /// <summary>
    /// Calculates centroid of embedding vectors
    /// </summary>
    private static EmbeddingVector? CalculateCentroid(List<EmbeddingVector> embeddings)
    {
        if (embeddings.Count == 0)
        {
            return null;
        }

        var dimension = embeddings[0].Values.Length;
        var centroid = new float[dimension];

        foreach (var embedding in embeddings)
        {
            var values = embedding.Values;
            for (int i = 0; i < dimension; i++)
            {
                centroid[i] += values[i];
            }
        }

        for (int i = 0; i < dimension; i++)
        {
            centroid[i] /= embeddings.Count;
        }

        // Normalize
        var magnitude = Math.Sqrt(centroid.Sum(v => v * v));
        if (magnitude > 0)
        {
            for (int i = 0; i < dimension; i++)
            {
                centroid[i] = (float)(centroid[i] / magnitude);
            }
        }

        return new EmbeddingVector(centroid, "leiden-centroid");
    }

    /// <summary>
    /// Calculates cohesion (average pairwise similarity)
    /// </summary>
    private static double CalculateCohesion(List<EmbeddingVector> embeddings)
    {
        if (embeddings.Count <= 1)
        {
            return 1.0;
        }

        double totalSimilarity = 0;
        int pairs = 0;

        for (int i = 0; i < embeddings.Count; i++)
        {
            for (int j = i + 1; j < embeddings.Count; j++)
            {
                totalSimilarity += CalculateCosineSimilarity(embeddings[i], embeddings[j]);
                pairs++;
            }
        }

        return pairs > 0 ? totalSimilarity / pairs : 0;
    }

    /// <summary>
    /// Extracts keywords from content using simple TF approach
    /// </summary>
    private static List<string> ExtractKeywords(List<string> contents, int topK = 10)
    {
        var wordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did", "will", "would", "could", "should",
            "may", "might", "can", "this", "that", "these", "those", "it", "its",
            "of", "in", "to", "for", "with", "on", "at", "by", "from", "as", "or", "and"
        };

        foreach (var content in contents)
        {
            var words = content.Split(WordSplitSeparators,
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                var cleanWord = word.ToLowerInvariant().Trim();
                if (cleanWord.Length > 2 && !stopWords.Contains(cleanWord) && !double.TryParse(cleanWord, out _))
                {
                    wordCounts[cleanWord] = wordCounts.GetValueOrDefault(cleanWord, 0) + 1;
                }
            }
        }

        return wordCounts
            .OrderByDescending(kvp => kvp.Value)
            .Take(topK)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// Selects representative chunks closest to centroid
    /// </summary>
    private static List<string> SelectRepresentatives(
        List<EmbeddingVector> embeddings,
        EmbeddingVector centroid,
        List<string> chunkIds,
        int count)
    {
        return embeddings
            .Select((e, i) => (id: chunkIds[i], sim: CalculateCosineSimilarity(e, centroid)))
            .OrderByDescending(x => x.sim)
            .Take(count)
            .Select(x => x.id)
            .ToList();
    }

    /// <summary>
    /// Fisher-Yates shuffle
    /// </summary>
    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting Leiden community detection on {Count} chunks")]
    private static partial void LogLeidenCommunity4(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Level {Level}: {Communities} communities, modularity {Modularity:F4}")]
    private static partial void LogLeidenCommunity3(ILogger logger, int level, int communities, double modularity);
    [LoggerMessage(Level = LogLevel.Information, Message = "Leiden detection complete: {Levels} levels, {Communities} communities at finest level in {Time}ms")]
    private static partial void LogLeidenCommunity2(ILogger logger, int levels, int communities, long time);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to generate summary for community {Id}")]
    private static partial void LogLeidenCommunity1(ILogger logger, Exception exception, string id);

    #endregion
}
