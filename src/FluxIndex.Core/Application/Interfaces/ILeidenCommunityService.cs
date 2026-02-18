using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Domain.ValueObjects;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Leiden algorithm-based hierarchical community detection service.
/// Implements the Leiden algorithm for detecting well-connected communities
/// at multiple resolution levels, supporting GraphRAG global search.
/// </summary>
public interface ILeidenCommunityService
{
    /// <summary>
    /// Detects hierarchical communities using the Leiden algorithm.
    /// Returns a multi-level community structure with summaries at each level.
    /// </summary>
    /// <param name="chunks">Chunks with embeddings to cluster</param>
    /// <param name="options">Leiden algorithm options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hierarchical community structure</returns>
    Task<CommunityHierarchy> DetectHierarchicalCommunitiesAsync(
        IEnumerable<LeidenChunk> chunks,
        LeidenOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates summaries for communities at a specific level.
    /// Uses LLM to create coherent summaries of community themes.
    /// </summary>
    /// <param name="hierarchy">Community hierarchy</param>
    /// <param name="level">Hierarchy level (0 = finest, higher = coarser)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Community summaries for the level</returns>
    Task<IReadOnlyList<LeidenCommunitySummary>> GenerateSummariesAsync(
        CommunityHierarchy hierarchy,
        int level,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds communities relevant to a query at the specified level.
    /// </summary>
    /// <param name="queryEmbedding">Query embedding vector</param>
    /// <param name="hierarchy">Community hierarchy to search</param>
    /// <param name="level">Hierarchy level to search</param>
    /// <param name="topK">Number of communities to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Matched communities with similarity scores</returns>
    Task<IReadOnlyList<LeidenCommunityMatch>> FindRelevantCommunitiesAsync(
        EmbeddingVector queryEmbedding,
        CommunityHierarchy hierarchy,
        int level = 0,
        int topK = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the community structure incrementally with new chunks.
    /// </summary>
    /// <param name="hierarchy">Existing hierarchy</param>
    /// <param name="newChunks">New chunks to add</param>
    /// <param name="options">Options for update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated hierarchy</returns>
    Task<CommunityHierarchy> UpdateHierarchyAsync(
        CommunityHierarchy hierarchy,
        IEnumerable<LeidenChunk> newChunks,
        LeidenOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for Leiden algorithm execution
/// </summary>
public class LeidenOptions
{
    /// <summary>
    /// Resolution parameter for modularity optimization.
    /// Higher values produce more, smaller communities.
    /// Default: 1.0
    /// </summary>
    public double Resolution { get; set; } = 1.0;

    /// <summary>
    /// Maximum number of iterations per level.
    /// Default: 100
    /// </summary>
    public int MaxIterations { get; set; } = 100;

    /// <summary>
    /// Minimum improvement in modularity to continue.
    /// Default: 0.0001
    /// </summary>
    public double MinModularityGain { get; set; } = 0.0001;

    /// <summary>
    /// Maximum hierarchy levels to generate.
    /// Default: 3
    /// </summary>
    public int MaxHierarchyLevels { get; set; } = 3;

    /// <summary>
    /// Minimum community size at finest level.
    /// Communities smaller than this are merged or discarded.
    /// Default: 3
    /// </summary>
    public int MinCommunitySize { get; set; } = 3;

    /// <summary>
    /// Similarity threshold for building the neighborhood graph.
    /// Edges are created between nodes with similarity >= threshold.
    /// Default: 0.3
    /// </summary>
    public double SimilarityThreshold { get; set; } = 0.3;

    /// <summary>
    /// Maximum number of neighbors per node (k-NN graph).
    /// Default: 15
    /// </summary>
    public int MaxNeighbors { get; set; } = 15;

    /// <summary>
    /// Whether to generate summaries during detection.
    /// Default: false (generate on-demand)
    /// </summary>
    public bool GenerateSummariesOnDetection { get; set; }

    /// <summary>
    /// Random seed for reproducibility.
    /// Null = use random seed.
    /// </summary>
    public int? RandomSeed { get; set; }

    /// <summary>
    /// Whether to use the refinement phase.
    /// Refinement ensures well-connected communities.
    /// Default: true
    /// </summary>
    public bool UseRefinement { get; set; } = true;
}

/// <summary>
/// Chunk with embedding for Leiden clustering
/// </summary>
public class LeidenChunk
{
    /// <summary>
    /// Unique chunk identifier
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Chunk content text
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Document identifier this chunk belongs to
    /// </summary>
    public string? DocumentId { get; init; }

    /// <summary>
    /// Embedding vector
    /// </summary>
    public EmbeddingVector Embedding { get; init; } = null!;

    /// <summary>
    /// Additional metadata
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Hierarchical community structure from Leiden algorithm
/// </summary>
public class CommunityHierarchy
{
    /// <summary>
    /// Unique identifier for this hierarchy
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Communities at each level (index 0 = finest, higher = coarser)
    /// </summary>
    public IReadOnlyList<CommunityLevel> Levels { get; init; } = Array.Empty<CommunityLevel>();

    /// <summary>
    /// Total number of levels
    /// </summary>
    public int LevelCount => Levels.Count;

    /// <summary>
    /// Total chunks in the hierarchy
    /// </summary>
    public int TotalChunks { get; init; }

    /// <summary>
    /// When the hierarchy was created
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Detection statistics
    /// </summary>
    public LeidenStatistics Statistics { get; init; } = new();

    /// <summary>
    /// Options used for detection
    /// </summary>
    public LeidenOptions Options { get; init; } = new();
}

/// <summary>
/// A single level in the community hierarchy
/// </summary>
public class CommunityLevel
{
    /// <summary>
    /// Level index (0 = finest)
    /// </summary>
    public int LevelIndex { get; init; }

    /// <summary>
    /// Communities at this level
    /// </summary>
    public IReadOnlyList<LeidenCommunity> Communities { get; init; } = Array.Empty<LeidenCommunity>();

    /// <summary>
    /// Modularity score at this level
    /// </summary>
    public double Modularity { get; init; }

    /// <summary>
    /// Resolution used for this level
    /// </summary>
    public double Resolution { get; init; }

    /// <summary>
    /// Number of communities at this level
    /// </summary>
    public int CommunityCount => Communities.Count;
}

/// <summary>
/// A community detected by the Leiden algorithm
/// </summary>
public class LeidenCommunity
{
    /// <summary>
    /// Unique community identifier
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Community index within its level
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// IDs of chunks in this community
    /// </summary>
    public IReadOnlyList<string> ChunkIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Community size (number of chunks)
    /// </summary>
    public int Size => ChunkIds.Count;

    /// <summary>
    /// Community centroid embedding
    /// </summary>
    public EmbeddingVector? Centroid { get; init; }

    /// <summary>
    /// Internal density (edges within / possible edges within)
    /// </summary>
    public double InternalDensity { get; init; }

    /// <summary>
    /// Cohesion score (average similarity within community)
    /// </summary>
    public double Cohesion { get; init; }

    /// <summary>
    /// Parent community ID (in coarser level), null for top level
    /// </summary>
    public string? ParentCommunityId { get; init; }

    /// <summary>
    /// Child community IDs (in finer level)
    /// </summary>
    public IReadOnlyList<string> ChildCommunityIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Generated summary (if available)
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Keywords extracted from community content
    /// </summary>
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Representative chunk IDs for this community
    /// </summary>
    public IReadOnlyList<string> RepresentativeChunkIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Community summary generated by LLM
/// </summary>
public class LeidenCommunitySummary
{
    /// <summary>
    /// Community ID
    /// </summary>
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>
    /// Level index
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Generated summary text
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Extracted themes/topics
    /// </summary>
    public IReadOnlyList<string> Themes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Key entities mentioned in the community
    /// </summary>
    public IReadOnlyList<string> Entities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Confidence score for the summary (0-1)
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// When the summary was generated
    /// </summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Community match result
/// </summary>
public class LeidenCommunityMatch
{
    /// <summary>
    /// Matched community
    /// </summary>
    public LeidenCommunity Community { get; init; } = null!;

    /// <summary>
    /// Level in hierarchy
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Similarity score to query
    /// </summary>
    public double Similarity { get; init; }

    /// <summary>
    /// Summary if available
    /// </summary>
    public LeidenCommunitySummary? Summary { get; init; }
}

/// <summary>
/// Statistics from Leiden algorithm execution
/// </summary>
public class LeidenStatistics
{
    /// <summary>
    /// Total iterations across all levels
    /// </summary>
    public int TotalIterations { get; init; }

    /// <summary>
    /// Final modularity score
    /// </summary>
    public double FinalModularity { get; init; }

    /// <summary>
    /// Processing time in milliseconds
    /// </summary>
    public double ProcessingTimeMs { get; init; }

    /// <summary>
    /// Number of edges in the similarity graph
    /// </summary>
    public int GraphEdges { get; init; }

    /// <summary>
    /// Average community size at finest level
    /// </summary>
    public double AverageCommunitySize { get; init; }

    /// <summary>
    /// Modularity improvement per level
    /// </summary>
    public IReadOnlyList<double> ModularityByLevel { get; init; } = Array.Empty<double>();
}
