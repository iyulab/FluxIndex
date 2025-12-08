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
    public SearchMode Mode { get; set; } = SearchMode.Hybrid;
    public Dictionary<string, object>? Filters { get; set; }
    public bool IncludeContent { get; set; } = true;
    public bool IncludeMetadata { get; set; } = true;

    /// <summary>
    /// Enable cross-encoder reranking for higher quality results.
    /// </summary>
    public bool EnableReranking { get; set; } = false;
}

/// <summary>
/// Search mode enumeration.
/// </summary>
public enum SearchMode
{
    Vector,
    Keyword,
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
    public double Score { get; init; }
    public double? VectorScore { get; init; }
    public double? KeywordScore { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
    public List<string>? Highlights { get; init; }

    /// <summary>
    /// Rerank score from cross-encoder model (if reranking was applied).
    /// </summary>
    public double? RerankScore { get; init; }

    /// <summary>
    /// Explanation for reranking decision (if requested).
    /// </summary>
    public string? RerankExplanation { get; init; }
}

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
