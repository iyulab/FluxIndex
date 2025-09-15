using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Application.Interfaces;

/// <summary>
/// Interface for reranking search results using cross-encoder models or other techniques
/// </summary>
public interface IReranker
{
    /// <summary>
    /// Reranks a set of retrieval candidates based on their relevance to the query
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="candidates">Initial retrieval candidates to rerank</param>
    /// <param name="options">Reranking options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Reranked results ordered by relevance</returns>
    Task<IEnumerable<RerankResult>> RerankAsync(
        string query,
        IEnumerable<RetrievalCandidate> candidates,
        RerankOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about the reranker model
    /// </summary>
    RerankModelInfo GetModelInfo();
}

/// <summary>
/// Represents a candidate document for reranking
/// </summary>
public class RetrievalCandidate
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float InitialScore { get; set; }
    public int InitialRank { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Result after reranking
/// </summary>
public class RerankResult : RetrievalCandidate
{
    public float RerankScore { get; set; }
    public int NewRank { get; set; }
    public float ScoreChange => RerankScore - InitialScore;
    public int RankChange => InitialRank - NewRank;
    public string? Explanation { get; set; }
}

/// <summary>
/// Options for reranking
/// </summary>
public class RerankOptions
{
    /// <summary>
    /// Number of top results to return after reranking
    /// </summary>
    public int TopN { get; set; } = 10;

    /// <summary>
    /// Model to use for reranking
    /// </summary>
    public RerankModel Model { get; set; } = RerankModel.Local;

    /// <summary>
    /// Minimum score threshold for results
    /// </summary>
    public float ScoreThreshold { get; set; } = 0.0f;

    /// <summary>
    /// Whether to include explanations for reranking decisions
    /// </summary>
    public bool IncludeExplanation { get; set; } = false;

    /// <summary>
    /// Maximum length of content to consider (for performance)
    /// </summary>
    public int MaxContentLength { get; set; } = 512;

    /// <summary>
    /// Custom model parameters
    /// </summary>
    public Dictionary<string, object>? ModelParameters { get; set; }
}

/// <summary>
/// Available reranking models
/// </summary>
public enum RerankModel
{
    /// <summary>
    /// Local similarity-based reranking
    /// </summary>
    Local,
    
    /// <summary>
    /// ONNX cross-encoder model (ms-marco-MiniLM)
    /// </summary>
    OnnxCrossEncoder,
    
    /// <summary>
    /// Cohere Rerank API
    /// </summary>
    Cohere,
    
    /// <summary>
    /// Custom implementation
    /// </summary>
    Custom
}

/// <summary>
/// Information about the reranker model
/// </summary>
public class RerankModelInfo
{
    public string Name { get; set; } = string.Empty;
    public RerankModel Type { get; set; }
    public string Version { get; set; } = string.Empty;
    public bool SupportsMultilingual { get; set; }
    public int MaxInputLength { get; set; }
    public float EstimatedLatencyMs { get; set; }
    public bool RequiresApiKey { get; set; }
    public Dictionary<string, object>? Capabilities { get; set; }
}