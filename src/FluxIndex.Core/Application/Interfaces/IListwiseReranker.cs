using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Listwise reranking interface for advanced document ranking.
/// Unlike pointwise reranking which scores documents independently,
/// listwise reranking considers all candidates together for optimal ordering.
/// </summary>
public interface IListwiseReranker
{
    /// <summary>
    /// Reranks candidates using listwise approach considering relative document ordering.
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="candidates">Candidates to rerank</param>
    /// <param name="options">Listwise reranking options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Reranked results with listwise scores</returns>
    Task<IReadOnlyList<ListwiseRerankResult>> RerankAsync(
        string query,
        IEnumerable<RetrievalCandidate> candidates,
        ListwiseRerankOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes pairwise preferences for document pairs.
    /// Used as a building block for listwise ranking.
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="docA">First document content</param>
    /// <param name="docB">Second document content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Preference result (-1 = B preferred, 0 = tie, 1 = A preferred)</returns>
    Task<PairwisePreference> ComputePairwisePreferenceAsync(
        string query,
        string docA,
        string docB,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about the listwise reranker.
    /// </summary>
    ListwiseRerankModelInfo GetModelInfo();
}

/// <summary>
/// Options for listwise reranking
/// </summary>
public class ListwiseRerankOptions
{
    /// <summary>
    /// Reranking method to use
    /// </summary>
    public ListwiseMethod Method { get; set; } = ListwiseMethod.SlidingWindow;

    /// <summary>
    /// Number of top results to return
    /// </summary>
    public int TopN { get; set; } = 10;

    /// <summary>
    /// Minimum score threshold for inclusion
    /// </summary>
    public float ScoreThreshold { get; set; }

    /// <summary>
    /// Window size for sliding window method.
    /// Larger windows consider more context but may hit token limits.
    /// Default: 10
    /// </summary>
    public int WindowSize { get; set; } = 10;

    /// <summary>
    /// Step size for sliding window.
    /// Default: 5
    /// </summary>
    public int WindowStep { get; set; } = 5;

    /// <summary>
    /// Maximum number of pairwise comparisons for tournament method.
    /// Higher values give more accurate rankings but increase latency.
    /// Default: 50
    /// </summary>
    public int MaxPairwiseComparisons { get; set; } = 50;

    /// <summary>
    /// Whether to use LLM for ranking (more accurate but slower)
    /// </summary>
    public bool UseLlm { get; set; } = true;

    /// <summary>
    /// LLM temperature for ranking decisions
    /// </summary>
    public float LlmTemperature { get; set; }

    /// <summary>
    /// Whether to use attention-based scoring with embeddings
    /// </summary>
    public bool UseAttentionScoring { get; set; } = true;

    /// <summary>
    /// Weight for initial retrieval score (0-1).
    /// Higher values trust initial ranking more.
    /// Default: 0.3
    /// </summary>
    public float InitialScoreWeight { get; set; } = 0.3f;

    /// <summary>
    /// Whether to include detailed explanations
    /// </summary>
    public bool IncludeExplanation { get; set; }

    /// <summary>
    /// Maximum content length per document for LLM processing
    /// </summary>
    public int MaxContentLength { get; set; } = 512;

    /// <summary>
    /// Number of parallel comparison batches
    /// </summary>
    public int ParallelBatches { get; set; } = 3;
}

/// <summary>
/// Listwise reranking methods
/// </summary>
public enum ListwiseMethod
{
    /// <summary>
    /// Sliding window approach - ranks documents in overlapping windows
    /// and merges results. Balances accuracy and token efficiency.
    /// </summary>
    SlidingWindow,

    /// <summary>
    /// Tournament-style pairwise comparisons with Bradley-Terry aggregation.
    /// More comparisons yield better rankings.
    /// </summary>
    Tournament,

    /// <summary>
    /// Direct LLM ranking - present all documents and ask for ordering.
    /// Most accurate for small sets, limited by context window.
    /// </summary>
    DirectLlm,

    /// <summary>
    /// Attention-based scoring using query-document attention matrices.
    /// Fast and embedding-based, no LLM required.
    /// </summary>
    AttentionBased,

    /// <summary>
    /// Hybrid approach combining multiple methods with score fusion.
    /// </summary>
    Hybrid
}

/// <summary>
/// Result from listwise reranking
/// </summary>
public record ListwiseRerankResult
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Document ID
    /// </summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>
    /// Chunk ID
    /// </summary>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>
    /// Document content
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Initial retrieval score
    /// </summary>
    public float InitialScore { get; init; }

    /// <summary>
    /// Initial rank before reranking
    /// </summary>
    public int InitialRank { get; init; }

    /// <summary>
    /// Listwise reranking score (0-1)
    /// </summary>
    public float ListwiseScore { get; init; }

    /// <summary>
    /// New rank after listwise reranking
    /// </summary>
    public int NewRank { get; init; }

    /// <summary>
    /// Rank change (positive = improved, negative = dropped)
    /// </summary>
    public int RankChange => InitialRank - NewRank;

    /// <summary>
    /// Pairwise win rate in tournament comparisons
    /// </summary>
    public float WinRate { get; init; }

    /// <summary>
    /// Number of wins in pairwise comparisons
    /// </summary>
    public int Wins { get; init; }

    /// <summary>
    /// Number of comparisons participated in
    /// </summary>
    public int ComparisonCount { get; init; }

    /// <summary>
    /// Attention score from embedding-based attention
    /// </summary>
    public float AttentionScore { get; init; }

    /// <summary>
    /// Confidence in the ranking (0-1)
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Explanation of ranking decision
    /// </summary>
    public string? Explanation { get; init; }

    /// <summary>
    /// Component scores breakdown
    /// </summary>
    public ListwiseScoreComponents? Components { get; init; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Breakdown of listwise score components
/// </summary>
public class ListwiseScoreComponents
{
    /// <summary>
    /// Score from sliding window ranking
    /// </summary>
    public float SlidingWindowScore { get; init; }

    /// <summary>
    /// Score from pairwise tournament
    /// </summary>
    public float TournamentScore { get; init; }

    /// <summary>
    /// Score from attention mechanism
    /// </summary>
    public float AttentionScore { get; init; }

    /// <summary>
    /// Score from initial retrieval
    /// </summary>
    public float InitialScore { get; init; }

    /// <summary>
    /// Weights used for each component
    /// </summary>
    public Dictionary<string, float> Weights { get; init; } = new();
}

/// <summary>
/// Pairwise preference result
/// </summary>
public class PairwisePreference
{
    /// <summary>
    /// Preference value: -1 (B preferred), 0 (tie), 1 (A preferred)
    /// </summary>
    public int Preference { get; init; }

    /// <summary>
    /// Confidence in the preference (0-1)
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Reasoning for preference
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Information about the listwise reranker
/// </summary>
public class ListwiseRerankModelInfo
{
    /// <summary>
    /// Model name
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Version
    /// </summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>
    /// Supported methods
    /// </summary>
    public IReadOnlyList<ListwiseMethod> SupportedMethods { get; init; } = Array.Empty<ListwiseMethod>();

    /// <summary>
    /// Whether LLM is available
    /// </summary>
    public bool LlmAvailable { get; init; }

    /// <summary>
    /// Whether embedding service is available
    /// </summary>
    public bool EmbeddingsAvailable { get; init; }

    /// <summary>
    /// Maximum candidates that can be ranked in single pass
    /// </summary>
    public int MaxCandidatesPerPass { get; init; }

    /// <summary>
    /// Estimated latency per candidate (ms)
    /// </summary>
    public float EstimatedLatencyPerCandidateMs { get; init; }

    /// <summary>
    /// Additional capabilities
    /// </summary>
    public Dictionary<string, object>? Capabilities { get; init; }
}
