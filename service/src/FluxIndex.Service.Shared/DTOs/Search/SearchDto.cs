namespace FluxIndex.Service.Shared.DTOs.Search;

/// <summary>
/// Search request DTO.
/// </summary>
public class SearchRequest
{
    public required string Query { get; init; }
    public Guid? CollectionId { get; init; }
    public int TopK { get; init; } = 10;
    public double MinScore { get; init; } = 0.0;
    public SearchMode Mode { get; init; } = SearchMode.Hybrid;
    public Dictionary<string, object>? Filters { get; init; }
    public bool IncludeContent { get; init; } = true;
    public bool IncludeMetadata { get; init; } = true;
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
public class SearchResultDto
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
