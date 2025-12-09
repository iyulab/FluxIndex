namespace FluxIndex.Stack.Shared.DTOs.Search;

/// <summary>
/// Search request DTO.
/// </summary>
public class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public Guid? CollectionId { get; set; }
    public int TopK { get; set; } = 10;
    public double MinScore { get; set; } = 0.0;

    /// <summary>
    /// Search mode. Default is Auto which automatically selects the best strategy.
    /// </summary>
    public SearchMode Mode { get; set; } = SearchMode.Auto;

    public Dictionary<string, object>? Filters { get; set; }
    public bool IncludeContent { get; set; } = true;
    public bool IncludeMetadata { get; set; } = true;

    /// <summary>
    /// Enable cross-encoder reranking for higher quality results.
    /// Only used when Mode is not Auto. In Auto mode, reranking is applied based on QualityPreference.
    /// </summary>
    public bool EnableReranking { get; set; } = false;

    /// <summary>
    /// Quality preference for Auto mode. Ignored when Mode is explicitly set.
    /// - Speed: Fastest results with acceptable quality
    /// - Balanced: Good balance between speed and quality (default)
    /// - Quality: Maximum quality, may take longer
    /// </summary>
    public QualityPreference QualityPreference { get; set; } = QualityPreference.Balanced;

    /// <summary>
    /// Include detailed explanation of search strategy (only in Auto mode).
    /// </summary>
    public bool IncludeExplanation { get; set; } = false;
}

/// <summary>
/// Search mode enumeration.
/// </summary>
public enum SearchMode
{
    /// <summary>
    /// Automatic mode - intelligently selects the best search strategy
    /// based on query characteristics and available resources.
    /// This is the recommended default for best quality results.
    /// </summary>
    Auto,

    /// <summary>
    /// Pure vector similarity search using embeddings.
    /// </summary>
    Vector,

    /// <summary>
    /// Keyword-based search using BM25/TF-IDF.
    /// </summary>
    Keyword,

    /// <summary>
    /// Hybrid search combining vector and keyword with RRF fusion.
    /// </summary>
    Hybrid
}

/// <summary>
/// Search response DTO.
/// </summary>
public class SearchResponse
{
    public string Query { get; init; } = string.Empty;
    public List<SearchResultDto> Results { get; init; } = new();
    public int TotalResults { get; init; }
    public double ExecutionTimeMs { get; init; }
    public SearchMode Mode { get; init; }

    /// <summary>
    /// Whether results were served from semantic cache.
    /// </summary>
    public bool FromCache { get; init; }

    /// <summary>
    /// Search strategy information (populated in Auto mode).
    /// </summary>
    public SearchStrategyInfo? Strategy { get; init; }

    /// <summary>
    /// Quality information (populated in Auto mode).
    /// </summary>
    public SearchQualityInfo? Quality { get; init; }

    /// <summary>
    /// Detailed explanation of search process (if requested).
    /// </summary>
    public SearchExplanation? Explanation { get; init; }
}

/// <summary>
/// Information about the search strategy used.
/// </summary>
public class SearchStrategyInfo
{
    /// <summary>
    /// Primary strategy: Vector, Keyword, Hybrid, ColBERT
    /// </summary>
    public string PrimaryStrategy { get; init; } = "Hybrid";

    /// <summary>
    /// Reranking method used: None, CrossEncoder, Listwise, ColBERT
    /// </summary>
    public string RerankingMethod { get; init; } = "None";

    /// <summary>
    /// Fusion method used: RRF, WeightedSum, DynamicAlpha
    /// </summary>
    public string FusionMethod { get; init; } = "RRF";

    /// <summary>
    /// Backends used: PostgreSQL, Qdrant, Neo4j, Redis
    /// </summary>
    public List<string> BackendsUsed { get; init; } = new();

    /// <summary>
    /// Dynamic alpha value if dynamic fusion was used.
    /// </summary>
    public double? DynamicAlpha { get; init; }
}

/// <summary>
/// Individual search result DTO.
/// </summary>
public record SearchResultDto
{
    public Guid ChunkId { get; init; }
    public Guid DocumentId { get; init; }
    public string DocumentTitle { get; init; } = string.Empty;
    public int ChunkIndex { get; init; }
    public string? Content { get; init; }

    /// <summary>
    /// Final combined score after all optimizations.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Confidence level: High, Medium, Low.
    /// </summary>
    public string Confidence { get; init; } = "Medium";

    public double? VectorScore { get; init; }
    public double? KeywordScore { get; init; }
    public double? ColBERTScore { get; init; }
    public double? GraphScore { get; init; }
    public double? RerankScore { get; init; }

    public Dictionary<string, object>? Metadata { get; init; }
    public List<string>? Highlights { get; init; }

    /// <summary>
    /// Related entities found in this result (in Auto mode with Neo4j).
    /// </summary>
    public List<string>? RelatedEntities { get; init; }
}

/// <summary>
/// Quality preference for Auto mode search optimization.
/// </summary>
public enum QualityPreference
{
    /// <summary>
    /// Fastest results with acceptable quality.
    /// Uses basic hybrid search without reranking.
    /// </summary>
    Speed,

    /// <summary>
    /// Good balance between speed and quality (default).
    /// Uses hybrid search with cross-encoder reranking.
    /// </summary>
    Balanced,

    /// <summary>
    /// Maximum quality, may take longer.
    /// Uses advanced features like ColBERT, listwise reranking, and graph expansion.
    /// </summary>
    Quality
}

/// <summary>
/// Quality information for search results.
/// </summary>
public class SearchQualityInfo
{
    /// <summary>
    /// Estimated quality score (0.0 - 1.0).
    /// </summary>
    public double EstimatedQuality { get; init; }

    /// <summary>
    /// Quality tier: Low, Medium, High.
    /// </summary>
    public string QualityTier { get; init; } = "Medium";

    /// <summary>
    /// Factors contributing to quality.
    /// </summary>
    public List<string> QualityFactors { get; init; } = new();

    /// <summary>
    /// Suggestions for improving results.
    /// </summary>
    public List<string>? ImprovementSuggestions { get; init; }
}

/// <summary>
/// Detailed explanation of search process.
/// </summary>
public class SearchExplanation
{
    /// <summary>
    /// Query analysis results.
    /// </summary>
    public QueryAnalysisDto? QueryAnalysis { get; init; }

    /// <summary>
    /// Strategy selection reasoning.
    /// </summary>
    public string? StrategyReason { get; init; }

    /// <summary>
    /// Step-by-step execution details.
    /// </summary>
    public List<ExecutionStep> ExecutionSteps { get; init; } = new();

    /// <summary>
    /// Performance breakdown by stage.
    /// </summary>
    public Dictionary<string, double>? PerformanceBreakdown { get; init; }
}

/// <summary>
/// Individual execution step in search process.
/// </summary>
public class ExecutionStep
{
    /// <summary>
    /// Step name (e.g., "SemanticCache", "QueryAnalysis", "VectorSearch").
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Duration in milliseconds.
    /// </summary>
    public double DurationMs { get; init; }

    /// <summary>
    /// Number of results at this step.
    /// </summary>
    public int ResultCount { get; init; }

    /// <summary>
    /// Additional details about this step.
    /// </summary>
    public string? Details { get; init; }
}

// Note: QueryAnalysisDto is defined in AdvancedSearchDto.cs

/// <summary>
/// Semantic cache entry DTO.
/// </summary>
public class SemanticCacheEntryDto
{
    public string Query { get; init; } = string.Empty;
    public string Response { get; init; } = string.Empty;
    public double Similarity { get; init; }
    public DateTime CachedAt { get; init; }
}
