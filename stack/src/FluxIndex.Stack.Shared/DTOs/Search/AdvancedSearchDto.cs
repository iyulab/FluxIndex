namespace FluxIndex.Stack.Shared.DTOs.Search;

/// <summary>
/// Advanced search request with support for dynamic fusion and enhanced reranking.
/// </summary>
public class AdvancedSearchRequest
{
    public string Query { get; set; } = string.Empty;
    public Guid? CollectionId { get; set; }
    public int TopK { get; set; } = 10;
    public double MinScore { get; set; } = 0.0;
    public Dictionary<string, object>? Filters { get; set; }
    public bool IncludeContent { get; set; } = true;
    public bool IncludeMetadata { get; set; } = true;

    /// <summary>
    /// Enable Dynamic Alpha Tuning for query-adaptive fusion weights.
    /// </summary>
    public bool EnableDynamicFusion { get; set; } = true;

    /// <summary>
    /// Fusion method to use when dynamic fusion is disabled.
    /// </summary>
    public FusionMethodDto FusionMethod { get; set; } = FusionMethodDto.RRF;

    /// <summary>
    /// Enable listwise reranking for improved result quality.
    /// </summary>
    public bool EnableListwiseReranking { get; set; } = false;

    /// <summary>
    /// Listwise reranking method to use.
    /// </summary>
    public ListwiseMethodDto ListwiseMethod { get; set; } = ListwiseMethodDto.AttentionBased;

    /// <summary>
    /// Enable entity extraction and linking.
    /// </summary>
    public bool EnableEntityExtraction { get; set; } = false;

    /// <summary>
    /// Enable community-based search for hierarchical document organization.
    /// </summary>
    public bool EnableCommunitySearch { get; set; } = false;

    /// <summary>
    /// Include query analysis details in the response.
    /// </summary>
    public bool IncludeQueryAnalysis { get; set; } = false;

    /// <summary>
    /// Include fusion details in the response.
    /// </summary>
    public bool IncludeFusionDetails { get; set; } = false;
}

/// <summary>
/// Fusion method for combining keyword and vector search results.
/// </summary>
public enum FusionMethodDto
{
    RRF,
    WeightedSum,
    RelativeScoreFusion,
    Product
}

/// <summary>
/// Listwise reranking method.
/// </summary>
public enum ListwiseMethodDto
{
    AttentionBased,
    SlidingWindow,
    DirectLlm,
    Tournament,
    Hybrid
}

/// <summary>
/// Advanced search response with detailed fusion and analysis information.
/// </summary>
public class AdvancedSearchResponse
{
    public string Query { get; init; } = string.Empty;
    public List<AdvancedSearchResultDto> Results { get; init; } = new();
    public int TotalResults { get; init; }
    public double ExecutionTimeMs { get; init; }

    /// <summary>
    /// Query analysis details (if requested).
    /// </summary>
    public QueryAnalysisDto? QueryAnalysis { get; init; }

    /// <summary>
    /// Fusion details (if requested).
    /// </summary>
    public FusionDetailsDto? FusionDetails { get; init; }

    /// <summary>
    /// Extracted entities (if entity extraction was enabled).
    /// </summary>
    public List<ExtractedEntityDto>? Entities { get; init; }

    /// <summary>
    /// Community information (if community search was enabled).
    /// </summary>
    public CommunitySearchInfoDto? CommunityInfo { get; init; }
}

/// <summary>
/// Individual advanced search result.
/// </summary>
public record AdvancedSearchResultDto
{
    public Guid ChunkId { get; init; }
    public Guid DocumentId { get; init; }
    public string DocumentTitle { get; init; } = string.Empty;
    public int ChunkIndex { get; init; }
    public string? Content { get; init; }
    public double Score { get; init; }
    public double? VectorScore { get; init; }
    public double? KeywordScore { get; init; }
    public double? FusionScore { get; init; }
    public double? RerankScore { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
    public List<string>? Highlights { get; init; }

    /// <summary>
    /// Listwise reranking details.
    /// </summary>
    public ListwiseResultDetailsDto? ListwiseDetails { get; init; }

    /// <summary>
    /// Community membership information.
    /// </summary>
    public CommunityMembershipDto? CommunityMembership { get; init; }
}

/// <summary>
/// Query analysis information.
/// </summary>
public class QueryAnalysisDto
{
    public string QueryType { get; init; } = string.Empty;
    public string ComplexityLevel { get; init; } = string.Empty;
    public List<string> Entities { get; init; } = new();
    public List<string> Keywords { get; init; } = new();
    public bool ContainsTechnicalTerms { get; init; }
    public int TokenCount { get; init; }
}

/// <summary>
/// Fusion operation details.
/// </summary>
public class FusionDetailsDto
{
    public string FusionMethod { get; init; } = string.Empty;
    public double KeywordWeight { get; init; }
    public double VectorWeight { get; init; }
    public bool WasDynamicallyTuned { get; init; }
    public string? TuningReason { get; init; }
}

/// <summary>
/// Listwise reranking result details.
/// </summary>
public class ListwiseResultDetailsDto
{
    public int OriginalRank { get; init; }
    public int NewRank { get; init; }
    public double ListwiseScore { get; init; }
    public double Confidence { get; init; }
    public Dictionary<string, float>? ComponentWeights { get; init; }
}

/// <summary>
/// Extracted entity information.
/// </summary>
public class ExtractedEntityDto
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public int MentionCount { get; init; }
    public string? LinkedEntityId { get; init; }
}

/// <summary>
/// Community search information.
/// </summary>
public class CommunitySearchInfoDto
{
    public int TotalCommunities { get; init; }
    public int CommunitiesSearched { get; init; }
    public List<CommunityDto> RelevantCommunities { get; init; } = new();
}

/// <summary>
/// Community information.
/// </summary>
public class CommunityDto
{
    public int CommunityId { get; init; }
    public int Level { get; init; }
    public int MemberCount { get; init; }
    public double RelevanceScore { get; init; }
    public string? Summary { get; init; }
}

/// <summary>
/// Community membership for a search result.
/// </summary>
public class CommunityMembershipDto
{
    public int CommunityId { get; init; }
    public int Level { get; init; }
    public string? CommunityName { get; init; }
}
