using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Community Detection Service for GraphRAG.
/// Implements embedding-based clustering and graph-based community detection
/// to organize documents into thematic groups for improved retrieval.
/// </summary>
public class CommunityDetectionService : ICommunityDetectionService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IGraphTraversalService? _graphService;
    private readonly ITextCompletionService? _completionService;
    private readonly CommunityDetectionOptions _options;
    private readonly ILogger<CommunityDetectionService> _logger;

    public CommunityDetectionService(
        IEmbeddingService embeddingService,
        IGraphTraversalService? graphService,
        ITextCompletionService? completionService,
        IOptions<CommunityDetectionOptions> options,
        ILogger<CommunityDetectionService> logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _graphService = graphService;
        _completionService = completionService;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CommunityDetectionResult> DetectCommunitiesAsync(
        IReadOnlyList<ChunkWithEmbedding> chunks,
        CommunityDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        if (chunks.Count == 0)
        {
            return CommunityDetectionResult.Empty;
        }

        options ??= _options;

        _logger.LogInformation(
            "Detecting communities for {Count} chunks using {Algorithm}",
            chunks.Count, options.Algorithm);

        var startTime = DateTime.UtcNow;

        var communities = options.Algorithm switch
        {
            ClusteringAlgorithm.KMeans => await DetectWithKMeansAsync(chunks, options, cancellationToken),
            ClusteringAlgorithm.DBSCAN => await DetectWithDBSCANAsync(chunks, options, cancellationToken),
            ClusteringAlgorithm.Hierarchical => await DetectWithHierarchicalAsync(chunks, options, cancellationToken),
            ClusteringAlgorithm.LabelPropagation => await DetectWithLabelPropagationAsync(chunks, options, cancellationToken),
            _ => await DetectWithKMeansAsync(chunks, options, cancellationToken)
        };

        // Generate community summaries if enabled
        if (options.GenerateSummaries && _completionService != null)
        {
            communities = await GenerateCommunitySummariesAsync(communities, chunks, cancellationToken);
        }

        // Calculate community metrics
        var metrics = CalculateCommunityMetrics(communities, chunks);

        var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

        _logger.LogInformation(
            "Detected {Count} communities in {Elapsed}ms (silhouette: {Silhouette:F3})",
            communities.Count, elapsedMs, metrics.SilhouetteScore);

        return new CommunityDetectionResult
        {
            Communities = communities,
            Metrics = metrics,
            Algorithm = options.Algorithm,
            ExecutionTimeMs = elapsedMs
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Community>> MergeCommunitiesAsync(
        IReadOnlyList<Community> communities,
        double similarityThreshold = 0.8,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(communities);

        if (communities.Count <= 1)
            return communities;

        _logger.LogDebug("Merging communities with similarity threshold {Threshold}", similarityThreshold);

        var mergedCommunities = new List<Community>(communities);
        bool merged;

        do
        {
            merged = false;
            cancellationToken.ThrowIfCancellationRequested();

            for (int i = 0; i < mergedCommunities.Count; i++)
            {
                for (int j = i + 1; j < mergedCommunities.Count; j++)
                {
                    var similarity = CalculateCommunitySimilarity(
                        mergedCommunities[i],
                        mergedCommunities[j]);

                    if (similarity >= similarityThreshold)
                    {
                        var mergedCommunity = MergeTwoCommunities(
                            mergedCommunities[i],
                            mergedCommunities[j]);

                        mergedCommunities[i] = mergedCommunity;
                        mergedCommunities.RemoveAt(j);
                        merged = true;
                        break;
                    }
                }

                if (merged) break;
            }
        } while (merged);

        // Re-index communities
        for (int i = 0; i < mergedCommunities.Count; i++)
        {
            mergedCommunities[i] = mergedCommunities[i] with { CommunityId = i };
        }

        _logger.LogDebug("Merged to {Count} communities", mergedCommunities.Count);
        return mergedCommunities.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<Community?> FindBestCommunityAsync(
        EmbeddingVector queryEmbedding,
        IReadOnlyList<Community> communities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        ArgumentNullException.ThrowIfNull(communities);

        if (communities.Count == 0)
            return null;

        Community? bestCommunity = null;
        double bestSimilarity = double.MinValue;

        foreach (var community in communities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (community.Centroid == null)
                continue;

            var similarity = CalculateCosineSimilarity(queryEmbedding.Values, community.Centroid.Values);

            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestCommunity = community;
            }
        }

        _logger.LogDebug(
            "Found best community {CommunityId} with similarity {Similarity:F3}",
            bestCommunity?.CommunityId, bestSimilarity);

        return bestCommunity;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommunityMatch>> FindRelevantCommunitiesAsync(
        EmbeddingVector queryEmbedding,
        IReadOnlyList<Community> communities,
        int topK = 3,
        double minSimilarity = 0.5,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        ArgumentNullException.ThrowIfNull(communities);

        var matches = new List<CommunityMatch>();

        foreach (var community in communities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (community.Centroid == null)
                continue;

            var similarity = CalculateCosineSimilarity(queryEmbedding.Values, community.Centroid.Values);

            if (similarity >= minSimilarity)
            {
                matches.Add(new CommunityMatch
                {
                    Community = community,
                    Similarity = similarity
                });
            }
        }

        var topMatches = matches
            .OrderByDescending(m => m.Similarity)
            .Take(topK)
            .ToList();

        _logger.LogDebug("Found {Count} relevant communities", topMatches.Count);
        return topMatches.AsReadOnly();
    }

    #region Clustering Algorithms

    private Task<IReadOnlyList<Community>> DetectWithKMeansAsync(
        IReadOnlyList<ChunkWithEmbedding> chunks,
        CommunityDetectionOptions options,
        CancellationToken cancellationToken)
    {
        var k = options.NumClusters > 0
            ? options.NumClusters
            : EstimateOptimalK(chunks.Count);

        _logger.LogDebug("Running K-Means with k={K}", k);

        var embeddings = chunks.Select(c => c.Embedding.Values).ToList();
        var dimension = embeddings[0].Length;

        // Initialize centroids using K-Means++
        var centroids = InitializeCentroidsKMeansPlusPlus(embeddings, k);

        // Iterative assignment and update
        int[] assignments = new int[chunks.Count];
        bool converged = false;
        int iteration = 0;

        while (!converged && iteration < options.MaxIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Assign points to nearest centroid
            var newAssignments = AssignToCentroids(embeddings, centroids);

            // Check convergence
            converged = assignments.SequenceEqual(newAssignments);
            assignments = newAssignments;

            if (!converged)
            {
                // Update centroids
                centroids = UpdateCentroids(embeddings, assignments, k, dimension);
            }

            iteration++;
        }

        _logger.LogDebug("K-Means converged after {Iterations} iterations", iteration);

        // Build communities
        var communities = BuildCommunities(chunks, assignments, centroids, k);
        return Task.FromResult(communities);
    }

    private Task<IReadOnlyList<Community>> DetectWithDBSCANAsync(
        IReadOnlyList<ChunkWithEmbedding> chunks,
        CommunityDetectionOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Running DBSCAN with eps={Eps}, minPts={MinPts}",
            options.Epsilon, options.MinPoints);

        var embeddings = chunks.Select(c => c.Embedding.Values).ToList();
        var n = embeddings.Count;

        // Labels: -1 = noise, 0+ = cluster id
        var labels = new int[n];
        Array.Fill(labels, -1);

        int clusterId = 0;

        for (int i = 0; i < n; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (labels[i] != -1)
                continue;

            var neighbors = GetNeighbors(embeddings, i, options.Epsilon);

            if (neighbors.Count < options.MinPoints)
            {
                // Noise point
                continue;
            }

            // Start new cluster
            labels[i] = clusterId;
            var seeds = new HashSet<int>(neighbors);
            seeds.Remove(i);

            while (seeds.Count > 0)
            {
                var current = seeds.First();
                seeds.Remove(current);

                if (labels[current] == -1)
                {
                    // Was noise, now border point
                    labels[current] = clusterId;
                }

                if (labels[current] != -1)
                    continue;

                labels[current] = clusterId;

                var currentNeighbors = GetNeighbors(embeddings, current, options.Epsilon);
                if (currentNeighbors.Count >= options.MinPoints)
                {
                    foreach (var neighbor in currentNeighbors)
                    {
                        if (labels[neighbor] == -1)
                            seeds.Add(neighbor);
                    }
                }
            }

            clusterId++;
        }

        // Build communities (excluding noise)
        var communities = BuildCommunitiesFromLabels(chunks, labels, embeddings);
        return Task.FromResult(communities);
    }

    private Task<IReadOnlyList<Community>> DetectWithHierarchicalAsync(
        IReadOnlyList<ChunkWithEmbedding> chunks,
        CommunityDetectionOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Running Hierarchical clustering");

        var n = chunks.Count;
        if (n <= 1)
        {
            return Task.FromResult<IReadOnlyList<Community>>(
                new[] { CreateSingleCommunity(chunks, 0) });
        }

        var embeddings = chunks.Select(c => c.Embedding.Values).ToList();

        // Initialize each point as its own cluster
        var clusters = Enumerable.Range(0, n).Select(i => new List<int> { i }).ToList();

        // Distance matrix (upper triangular)
        var distances = ComputeDistanceMatrix(embeddings);

        var targetClusters = options.NumClusters > 0
            ? options.NumClusters
            : EstimateOptimalK(n);

        while (clusters.Count > targetClusters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Find closest pair of clusters
            (int i, int j, double minDist) = FindClosestClusters(clusters, distances, embeddings);

            if (i == -1 || j == -1)
                break;

            // Merge clusters
            clusters[i].AddRange(clusters[j]);
            clusters.RemoveAt(j);
        }

        // Build communities from final clusters
        var communities = new List<Community>();
        for (int i = 0; i < clusters.Count; i++)
        {
            var clusterChunks = clusters[i].Select(idx => chunks[idx]).ToList();
            var centroid = CalculateCentroid(clusterChunks.Select(c => c.Embedding.Values).ToList());

            communities.Add(new Community
            {
                CommunityId = i,
                ChunkIds = clusters[i].Select(idx => chunks[idx].ChunkId).ToList().AsReadOnly(),
                Centroid = new EmbeddingVector(centroid, chunks[0].Embedding.ModelName),
                Size = clusters[i].Count,
                Coherence = CalculateClusterCoherence(clusterChunks.Select(c => c.Embedding.Values).ToList(), centroid)
            });
        }

        return Task.FromResult<IReadOnlyList<Community>>(communities.AsReadOnly());
    }

    private async Task<IReadOnlyList<Community>> DetectWithLabelPropagationAsync(
        IReadOnlyList<ChunkWithEmbedding> chunks,
        CommunityDetectionOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Running Label Propagation");

        if (_graphService == null)
        {
            _logger.LogWarning("Graph service not available, falling back to K-Means");
            return await DetectWithKMeansAsync(chunks, options, cancellationToken);
        }

        var n = chunks.Count;
        var labels = Enumerable.Range(0, n).ToArray();
        var embeddings = chunks.Select(c => c.Embedding.Values).ToList();

        // Build similarity graph
        var adjacency = BuildSimilarityGraph(embeddings, options.SimilarityThreshold);

        bool changed = true;
        int iteration = 0;

        while (changed && iteration < options.MaxIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            changed = false;
            var order = Enumerable.Range(0, n).OrderBy(_ => Random.Shared.Next()).ToList();

            foreach (var i in order)
            {
                var neighbors = adjacency[i];
                if (neighbors.Count == 0)
                    continue;

                // Find most frequent label among neighbors
                var labelCounts = neighbors
                    .GroupBy(j => labels[j])
                    .ToDictionary(g => g.Key, g => g.Count());

                var maxCount = labelCounts.Values.Max();
                var bestLabels = labelCounts.Where(kv => kv.Value == maxCount).Select(kv => kv.Key).ToList();

                // Randomly select among ties
                var newLabel = bestLabels[Random.Shared.Next(bestLabels.Count)];

                if (newLabel != labels[i])
                {
                    labels[i] = newLabel;
                    changed = true;
                }
            }

            iteration++;
        }

        _logger.LogDebug("Label Propagation converged after {Iterations} iterations", iteration);

        // Build communities from labels
        var communities = BuildCommunitiesFromLabels(chunks, labels, embeddings);
        return communities;
    }

    #endregion

    #region Helper Methods

    private int EstimateOptimalK(int n)
    {
        // Rule of thumb: sqrt(n/2)
        return Math.Max(2, (int)Math.Sqrt(n / 2.0));
    }

    private float[][] InitializeCentroidsKMeansPlusPlus(IReadOnlyList<float[]> embeddings, int k)
    {
        var centroids = new List<float[]>();
        var random = Random.Shared;

        // Choose first centroid randomly
        var firstIdx = random.Next(embeddings.Count);
        centroids.Add((float[])embeddings[firstIdx].Clone());

        // Choose remaining centroids with probability proportional to distance squared
        while (centroids.Count < k)
        {
            var distances = new double[embeddings.Count];
            double totalDist = 0;

            for (int i = 0; i < embeddings.Count; i++)
            {
                var minDist = centroids.Min(c => CalculateEuclideanDistance(embeddings[i], c));
                distances[i] = minDist * minDist;
                totalDist += distances[i];
            }

            // Weighted random selection
            var threshold = random.NextDouble() * totalDist;
            double cumulative = 0;
            for (int i = 0; i < embeddings.Count; i++)
            {
                cumulative += distances[i];
                if (cumulative >= threshold)
                {
                    centroids.Add((float[])embeddings[i].Clone());
                    break;
                }
            }
        }

        return centroids.ToArray();
    }

    private int[] AssignToCentroids(IReadOnlyList<float[]> embeddings, float[][] centroids)
    {
        var assignments = new int[embeddings.Count];

        for (int i = 0; i < embeddings.Count; i++)
        {
            var minDist = double.MaxValue;
            var bestCentroid = 0;

            for (int j = 0; j < centroids.Length; j++)
            {
                var dist = CalculateEuclideanDistance(embeddings[i], centroids[j]);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestCentroid = j;
                }
            }

            assignments[i] = bestCentroid;
        }

        return assignments;
    }

    private float[][] UpdateCentroids(
        IReadOnlyList<float[]> embeddings,
        int[] assignments,
        int k,
        int dimension)
    {
        var newCentroids = new float[k][];
        var counts = new int[k];

        for (int i = 0; i < k; i++)
        {
            newCentroids[i] = new float[dimension];
        }

        for (int i = 0; i < embeddings.Count; i++)
        {
            var cluster = assignments[i];
            counts[cluster]++;
            for (int d = 0; d < dimension; d++)
            {
                newCentroids[cluster][d] += embeddings[i][d];
            }
        }

        for (int i = 0; i < k; i++)
        {
            if (counts[i] > 0)
            {
                for (int d = 0; d < dimension; d++)
                {
                    newCentroids[i][d] /= counts[i];
                }
            }
        }

        return newCentroids;
    }

    private IReadOnlyList<Community> BuildCommunities(
        IReadOnlyList<ChunkWithEmbedding> chunks,
        int[] assignments,
        float[][] centroids,
        int k)
    {
        var communities = new List<Community>();

        for (int clusterId = 0; clusterId < k; clusterId++)
        {
            var clusterChunkIds = new List<string>();
            var clusterEmbeddings = new List<float[]>();

            for (int i = 0; i < chunks.Count; i++)
            {
                if (assignments[i] == clusterId)
                {
                    clusterChunkIds.Add(chunks[i].ChunkId);
                    clusterEmbeddings.Add(chunks[i].Embedding.Values);
                }
            }

            if (clusterChunkIds.Count == 0)
                continue;

            var coherence = CalculateClusterCoherence(clusterEmbeddings, centroids[clusterId]);

            communities.Add(new Community
            {
                CommunityId = communities.Count,
                ChunkIds = clusterChunkIds.AsReadOnly(),
                Centroid = new EmbeddingVector(centroids[clusterId], chunks[0].Embedding.ModelName),
                Size = clusterChunkIds.Count,
                Coherence = coherence
            });
        }

        return communities.AsReadOnly();
    }

    private IReadOnlyList<Community> BuildCommunitiesFromLabels(
        IReadOnlyList<ChunkWithEmbedding> chunks,
        int[] labels,
        IReadOnlyList<float[]> embeddings)
    {
        var clusterGroups = new Dictionary<int, List<int>>();

        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] < 0) continue; // Skip noise

            if (!clusterGroups.ContainsKey(labels[i]))
                clusterGroups[labels[i]] = new List<int>();

            clusterGroups[labels[i]].Add(i);
        }

        var communities = new List<Community>();
        foreach (var (label, indices) in clusterGroups.OrderBy(kv => kv.Key))
        {
            var clusterChunkIds = indices.Select(i => chunks[i].ChunkId).ToList();
            var clusterEmbeddings = indices.Select(i => embeddings[i]).ToList();
            var centroid = CalculateCentroid(clusterEmbeddings);
            var coherence = CalculateClusterCoherence(clusterEmbeddings, centroid);

            communities.Add(new Community
            {
                CommunityId = communities.Count,
                ChunkIds = clusterChunkIds.AsReadOnly(),
                Centroid = new EmbeddingVector(centroid, chunks[0].Embedding.ModelName),
                Size = clusterChunkIds.Count,
                Coherence = coherence
            });
        }

        return communities.AsReadOnly();
    }

    private Community CreateSingleCommunity(IReadOnlyList<ChunkWithEmbedding> chunks, int id)
    {
        var embeddings = chunks.Select(c => c.Embedding.Values).ToList();
        var centroid = CalculateCentroid(embeddings);

        return new Community
        {
            CommunityId = id,
            ChunkIds = chunks.Select(c => c.ChunkId).ToList().AsReadOnly(),
            Centroid = new EmbeddingVector(centroid, chunks[0].Embedding.ModelName),
            Size = chunks.Count,
            Coherence = 1.0
        };
    }

    private List<int> GetNeighbors(IReadOnlyList<float[]> embeddings, int pointIndex, double epsilon)
    {
        var neighbors = new List<int>();

        for (int i = 0; i < embeddings.Count; i++)
        {
            if (i == pointIndex) continue;

            var dist = CalculateEuclideanDistance(embeddings[pointIndex], embeddings[i]);
            if (dist <= epsilon)
                neighbors.Add(i);
        }

        return neighbors;
    }

    private Dictionary<int, List<int>> BuildSimilarityGraph(
        IReadOnlyList<float[]> embeddings,
        double threshold)
    {
        var adjacency = new Dictionary<int, List<int>>();

        for (int i = 0; i < embeddings.Count; i++)
        {
            adjacency[i] = new List<int>();
        }

        for (int i = 0; i < embeddings.Count; i++)
        {
            for (int j = i + 1; j < embeddings.Count; j++)
            {
                var similarity = CalculateCosineSimilarity(embeddings[i], embeddings[j]);
                if (similarity >= threshold)
                {
                    adjacency[i].Add(j);
                    adjacency[j].Add(i);
                }
            }
        }

        return adjacency;
    }

    private double[,] ComputeDistanceMatrix(IReadOnlyList<float[]> embeddings)
    {
        var n = embeddings.Count;
        var distances = new double[n, n];

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                var dist = CalculateEuclideanDistance(embeddings[i], embeddings[j]);
                distances[i, j] = dist;
                distances[j, i] = dist;
            }
        }

        return distances;
    }

    private (int, int, double) FindClosestClusters(
        List<List<int>> clusters,
        double[,] distances,
        IReadOnlyList<float[]> embeddings)
    {
        int bestI = -1, bestJ = -1;
        double minDist = double.MaxValue;

        for (int i = 0; i < clusters.Count; i++)
        {
            for (int j = i + 1; j < clusters.Count; j++)
            {
                // Average linkage
                var dist = CalculateAverageLinkage(clusters[i], clusters[j], embeddings);

                if (dist < minDist)
                {
                    minDist = dist;
                    bestI = i;
                    bestJ = j;
                }
            }
        }

        return (bestI, bestJ, minDist);
    }

    private double CalculateAverageLinkage(
        List<int> cluster1,
        List<int> cluster2,
        IReadOnlyList<float[]> embeddings)
    {
        double totalDist = 0;
        int count = 0;

        foreach (var i in cluster1)
        {
            foreach (var j in cluster2)
            {
                totalDist += CalculateEuclideanDistance(embeddings[i], embeddings[j]);
                count++;
            }
        }

        return count > 0 ? totalDist / count : double.MaxValue;
    }

    private float[] CalculateCentroid(IReadOnlyList<float[]> embeddings)
    {
        if (embeddings.Count == 0)
            return Array.Empty<float>();

        var dimension = embeddings[0].Length;
        var centroid = new float[dimension];

        foreach (var embedding in embeddings)
        {
            for (int i = 0; i < dimension; i++)
            {
                centroid[i] += embedding[i];
            }
        }

        for (int i = 0; i < dimension; i++)
        {
            centroid[i] /= embeddings.Count;
        }

        return centroid;
    }

    private double CalculateClusterCoherence(IReadOnlyList<float[]> embeddings, float[] centroid)
    {
        if (embeddings.Count == 0)
            return 0;

        var avgSimilarity = embeddings
            .Average(e => CalculateCosineSimilarity(e, centroid));

        return avgSimilarity;
    }

    private double CalculateCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return 0;

        double dotProduct = 0;
        double normA = 0;
        double normB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denominator > 0 ? dotProduct / denominator : 0;
    }

    private double CalculateEuclideanDistance(float[] a, float[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            var diff = a[i] - b[i];
            sum += diff * diff;
        }
        return Math.Sqrt(sum);
    }

    private double CalculateCommunitySimilarity(Community a, Community b)
    {
        if (a.Centroid == null || b.Centroid == null)
            return 0;

        return CalculateCosineSimilarity(a.Centroid.Values, b.Centroid.Values);
    }

    private Community MergeTwoCommunities(Community a, Community b)
    {
        var mergedChunkIds = a.ChunkIds.Concat(b.ChunkIds).Distinct().ToList();

        // Calculate new centroid (weighted average)
        float[]? newCentroid = null;
        if (a.Centroid != null && b.Centroid != null)
        {
            var dimension = a.Centroid.Dimension;
            newCentroid = new float[dimension];

            for (int i = 0; i < dimension; i++)
            {
                newCentroid[i] = (a.Centroid.Values[i] * a.Size + b.Centroid.Values[i] * b.Size)
                    / (a.Size + b.Size);
            }
        }

        return new Community
        {
            CommunityId = a.CommunityId,
            ChunkIds = mergedChunkIds.AsReadOnly(),
            Centroid = newCentroid != null
                ? new EmbeddingVector(newCentroid, a.Centroid!.ModelName)
                : null,
            Size = mergedChunkIds.Count,
            Coherence = (a.Coherence * a.Size + b.Coherence * b.Size) / (a.Size + b.Size),
            Summary = a.Summary // Keep first community's summary
        };
    }

    private async Task<IReadOnlyList<Community>> GenerateCommunitySummariesAsync(
        IReadOnlyList<Community> communities,
        IReadOnlyList<ChunkWithEmbedding> chunks,
        CancellationToken cancellationToken)
    {
        if (_completionService == null)
            return communities;

        var chunkLookup = chunks.ToDictionary(c => c.ChunkId);
        var updatedCommunities = new List<Community>();

        foreach (var community in communities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Get sample content from community
            var sampleContent = community.ChunkIds
                .Take(5)
                .Select(id => chunkLookup.TryGetValue(id, out var chunk) ? chunk.Content : "")
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();

            if (sampleContent.Count == 0)
            {
                updatedCommunities.Add(community);
                continue;
            }

            try
            {
                var prompt = $"""
                    Summarize the main theme of these document chunks in 1-2 sentences:

                    {string.Join("\n---\n", sampleContent.Take(3))}

                    Summary:
                    """;

                var summary = await _completionService.GenerateCompletionAsync(
                    prompt,
                    100,
                    0.3f,
                    cancellationToken);

                updatedCommunities.Add(community with { Summary = summary.Trim() });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate summary for community {Id}", community.CommunityId);
                updatedCommunities.Add(community);
            }
        }

        return updatedCommunities.AsReadOnly();
    }

    private CommunityMetrics CalculateCommunityMetrics(
        IReadOnlyList<Community> communities,
        IReadOnlyList<ChunkWithEmbedding> chunks)
    {
        if (communities.Count == 0)
        {
            return new CommunityMetrics
            {
                TotalCommunities = 0,
                SilhouetteScore = 0,
                AverageCommunitySize = 0,
                AverageCoherence = 0
            };
        }

        var avgSize = communities.Average(c => c.Size);
        var avgCoherence = communities.Average(c => c.Coherence);
        var silhouette = CalculateSilhouetteScore(communities, chunks);

        return new CommunityMetrics
        {
            TotalCommunities = communities.Count,
            SilhouetteScore = silhouette,
            AverageCommunitySize = avgSize,
            AverageCoherence = avgCoherence,
            LargestCommunitySize = communities.Max(c => c.Size),
            SmallestCommunitySize = communities.Min(c => c.Size)
        };
    }

    private double CalculateSilhouetteScore(
        IReadOnlyList<Community> communities,
        IReadOnlyList<ChunkWithEmbedding> chunks)
    {
        if (communities.Count <= 1)
            return 1.0;

        var chunkToCommunity = new Dictionary<string, int>();
        foreach (var community in communities)
        {
            foreach (var chunkId in community.ChunkIds)
            {
                chunkToCommunity[chunkId] = community.CommunityId;
            }
        }

        var chunkLookup = chunks.ToDictionary(c => c.ChunkId);
        var silhouetteScores = new List<double>();

        foreach (var chunk in chunks)
        {
            if (!chunkToCommunity.TryGetValue(chunk.ChunkId, out var communityId))
                continue;

            var community = communities.FirstOrDefault(c => c.CommunityId == communityId);
            if (community == null || community.Size <= 1)
                continue;

            // Calculate a(i): average distance to same cluster
            var sameClusterChunks = community.ChunkIds
                .Where(id => id != chunk.ChunkId && chunkLookup.ContainsKey(id))
                .Select(id => chunkLookup[id])
                .ToList();

            if (sameClusterChunks.Count == 0)
                continue;

            var a = sameClusterChunks.Average(c =>
                1 - CalculateCosineSimilarity(chunk.Embedding.Values, c.Embedding.Values));

            // Calculate b(i): minimum average distance to other clusters
            var b = double.MaxValue;
            foreach (var otherCommunity in communities.Where(c => c.CommunityId != communityId))
            {
                var otherClusterChunks = otherCommunity.ChunkIds
                    .Where(id => chunkLookup.ContainsKey(id))
                    .Select(id => chunkLookup[id])
                    .ToList();

                if (otherClusterChunks.Count == 0)
                    continue;

                var avgDist = otherClusterChunks.Average(c =>
                    1 - CalculateCosineSimilarity(chunk.Embedding.Values, c.Embedding.Values));

                b = Math.Min(b, avgDist);
            }

            if (b == double.MaxValue)
                continue;

            var s = (b - a) / Math.Max(a, b);
            silhouetteScores.Add(s);
        }

        return silhouetteScores.Count > 0 ? silhouetteScores.Average() : 0;
    }

    #endregion
}

/// <summary>
/// Interface for community detection service
/// </summary>
public interface ICommunityDetectionService
{
    /// <summary>
    /// Detects communities in a set of chunks based on embedding similarity
    /// </summary>
    Task<CommunityDetectionResult> DetectCommunitiesAsync(
        IReadOnlyList<ChunkWithEmbedding> chunks,
        CommunityDetectionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges similar communities
    /// </summary>
    Task<IReadOnlyList<Community>> MergeCommunitiesAsync(
        IReadOnlyList<Community> communities,
        double similarityThreshold = 0.8,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the best matching community for a query
    /// </summary>
    Task<Community?> FindBestCommunityAsync(
        EmbeddingVector queryEmbedding,
        IReadOnlyList<Community> communities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds relevant communities for a query
    /// </summary>
    Task<IReadOnlyList<CommunityMatch>> FindRelevantCommunitiesAsync(
        EmbeddingVector queryEmbedding,
        IReadOnlyList<Community> communities,
        int topK = 3,
        double minSimilarity = 0.5,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for community detection
/// </summary>
public class CommunityDetectionOptions
{
    /// <summary>
    /// Clustering algorithm to use
    /// </summary>
    public ClusteringAlgorithm Algorithm { get; set; } = ClusteringAlgorithm.KMeans;

    /// <summary>
    /// Number of clusters (for K-Means and Hierarchical)
    /// </summary>
    public int NumClusters { get; set; } = 0; // 0 = auto-detect

    /// <summary>
    /// Maximum iterations for iterative algorithms
    /// </summary>
    public int MaxIterations { get; set; } = 100;

    /// <summary>
    /// Epsilon for DBSCAN
    /// </summary>
    public double Epsilon { get; set; } = 0.5;

    /// <summary>
    /// Minimum points for DBSCAN
    /// </summary>
    public int MinPoints { get; set; } = 3;

    /// <summary>
    /// Similarity threshold for Label Propagation
    /// </summary>
    public double SimilarityThreshold { get; set; } = 0.5;

    /// <summary>
    /// Whether to generate community summaries
    /// </summary>
    public bool GenerateSummaries { get; set; } = false;
}

/// <summary>
/// Clustering algorithm types
/// </summary>
public enum ClusteringAlgorithm
{
    /// <summary>K-Means clustering</summary>
    KMeans,

    /// <summary>DBSCAN density-based clustering</summary>
    DBSCAN,

    /// <summary>Hierarchical agglomerative clustering</summary>
    Hierarchical,

    /// <summary>Label Propagation for graph-based clustering</summary>
    LabelPropagation
}

/// <summary>
/// Chunk with its embedding
/// </summary>
public class ChunkWithEmbedding
{
    /// <summary>
    /// Chunk identifier
    /// </summary>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>
    /// Chunk content
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Embedding vector
    /// </summary>
    public EmbeddingVector Embedding { get; init; } = null!;
}

/// <summary>
/// Detected community
/// </summary>
public record Community
{
    /// <summary>
    /// Community identifier
    /// </summary>
    public int CommunityId { get; init; }

    /// <summary>
    /// Chunk IDs in this community
    /// </summary>
    public IReadOnlyList<string> ChunkIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Community centroid
    /// </summary>
    public EmbeddingVector? Centroid { get; init; }

    /// <summary>
    /// Community size
    /// </summary>
    public int Size { get; init; }

    /// <summary>
    /// Coherence score (0-1)
    /// </summary>
    public double Coherence { get; init; }

    /// <summary>
    /// Generated summary of community theme
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Keywords associated with this community
    /// </summary>
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Community match result
/// </summary>
public class CommunityMatch
{
    /// <summary>
    /// Matched community
    /// </summary>
    public Community Community { get; init; } = null!;

    /// <summary>
    /// Similarity score
    /// </summary>
    public double Similarity { get; init; }
}

/// <summary>
/// Community detection result
/// </summary>
public class CommunityDetectionResult
{
    /// <summary>
    /// Detected communities
    /// </summary>
    public IReadOnlyList<Community> Communities { get; init; } = Array.Empty<Community>();

    /// <summary>
    /// Detection metrics
    /// </summary>
    public CommunityMetrics Metrics { get; init; } = new();

    /// <summary>
    /// Algorithm used
    /// </summary>
    public ClusteringAlgorithm Algorithm { get; init; }

    /// <summary>
    /// Execution time in milliseconds
    /// </summary>
    public double ExecutionTimeMs { get; init; }

    /// <summary>
    /// Empty result
    /// </summary>
    public static CommunityDetectionResult Empty => new()
    {
        Communities = Array.Empty<Community>(),
        Metrics = new CommunityMetrics()
    };
}

/// <summary>
/// Community detection metrics
/// </summary>
public class CommunityMetrics
{
    /// <summary>
    /// Total number of communities
    /// </summary>
    public int TotalCommunities { get; init; }

    /// <summary>
    /// Silhouette score (-1 to 1)
    /// </summary>
    public double SilhouetteScore { get; init; }

    /// <summary>
    /// Average community size
    /// </summary>
    public double AverageCommunitySize { get; init; }

    /// <summary>
    /// Average coherence
    /// </summary>
    public double AverageCoherence { get; init; }

    /// <summary>
    /// Largest community size
    /// </summary>
    public int LargestCommunitySize { get; init; }

    /// <summary>
    /// Smallest community size
    /// </summary>
    public int SmallestCommunitySize { get; init; }
}
