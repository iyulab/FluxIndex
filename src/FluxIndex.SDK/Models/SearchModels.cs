using System;
using System.Collections.Generic;

namespace FluxIndex.SDK;

/// <summary>
/// 검색 요청 모델
/// </summary>
public class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public SearchType Type { get; set; } = SearchType.Hybrid;
    public int MaxResults { get; set; } = 10;
    public int Offset { get; set; } = 0;
    public Dictionary<string, string> Filters { get; set; } = new();
    public bool IncludeMetadata { get; set; } = true;
    public float MinScore { get; set; } = 0.0f;
}

/// <summary>
/// 검색 응답 모델
/// </summary>
public class SearchResponse
{
    public string Query { get; set; } = string.Empty;
    public List<SearchResult> Results { get; set; } = new();
    public int TotalResults { get; set; }
    public TimeSpan SearchTime { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 검색 결과 항목
/// </summary>
public class SearchResult
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float Score { get; set; }
    public float? VectorScore { get; set; }
    public float? KeywordScore { get; set; }
    public DocumentMetadata Metadata { get; set; } = new();
    public Dictionary<string, object> Highlights { get; set; } = new();
    public int ChunkIndex { get; set; }
}

/// <summary>
/// 검색 옵션
/// </summary>
public class SearchOptions
{
    public int TopK { get; set; } = 10;
    public float MinSimilarity { get; set; } = 0.0f;
    public bool IncludeVectors { get; set; } = false;
    public Dictionary<string, string> MetadataFilters { get; set; } = new();
}

/// <summary>
/// 의미 검색 옵션
/// </summary>
public class SemanticSearchOptions : SearchOptions
{
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public bool UseCache { get; set; } = true;
}

/// <summary>
/// 키워드 검색 옵션
/// </summary>
public class KeywordSearchOptions : SearchOptions
{
    public bool UseFullTextSearch { get; set; } = true;
    public bool CaseSensitive { get; set; } = false;
    public string[] SearchFields { get; set; } = Array.Empty<string>();
}

/// <summary>
/// 하이브리드 검색 옵션
/// </summary>
public class HybridSearchOptions : SearchOptions
{
    public float VectorWeight { get; set; } = 0.7f;
    public float KeywordWeight { get; set; } = 0.3f;
    public RerankingStrategy RerankingStrategy { get; set; } = RerankingStrategy.WeightedAverage;
}

/// <summary>
/// 패싯 검색 옵션
/// </summary>
public class FacetSearchOptions : SearchOptions
{
    public string[] FacetFields { get; set; } = Array.Empty<string>();
    public int MaxFacetValues { get; set; } = 10;
}

/// <summary>
/// 패싯 검색 응답
/// </summary>
public class FacetedSearchResponse : SearchResponse
{
    public Dictionary<string, List<FacetValue>> Facets { get; set; } = new();
}

/// <summary>
/// 패싯 값
/// </summary>
public class FacetValue
{
    public string Value { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// 유사도 검색 옵션
/// </summary>
public class SimilarityOptions : SearchOptions
{
    public float SimilarityThreshold { get; set; } = 0.8f;
    public bool ExcludeSelf { get; set; } = true;
}

/// <summary>
/// 리랭킹 옵션
/// </summary>
public class RerankingOptions
{
    public RerankingStrategy Strategy { get; set; } = RerankingStrategy.CrossEncoder;
    public string RerankingModel { get; set; } = string.Empty;
    public int TopK { get; set; } = 10;
}

/// <summary>
/// 검색 타입
/// </summary>
public enum SearchType
{
    Semantic,
    Keyword,
    Hybrid
}

/// <summary>
/// 리랭킹 전략
/// </summary>
public enum RerankingStrategy
{
    WeightedAverage,
    CrossEncoder,
    ReciprocalRankFusion,
    Custom
}