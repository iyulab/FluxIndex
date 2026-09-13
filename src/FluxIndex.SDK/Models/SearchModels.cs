using System;
using System.Collections.Generic;
using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.SDK;

/// <summary>
/// 검색 요청 모델
/// </summary>
public class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public SearchType Type { get; set; } = SearchType.Hybrid;
    public int MaxResults { get; set; } = 10;
    public int Offset { get; set; }
    public Dictionary<string, string> Filters { get; set; } = new();
    public bool IncludeMetadata { get; set; } = true;
    public float MinScore { get; set; }

    /// <summary>
    /// GraphRAG 검색 요청. <see cref="SearchRequest"/> 를 소비하는 구현은 현재 없다(<c>ISearchService</c> 는
    /// 인터페이스만 있고 구현이 없다) — 실제 검색 옵션은 <see cref="SearchOptions"/> 를 본다.
    /// <see cref="Retriever.SearchAsync(string, SearchOptions?, CancellationToken)"/> 는 그래프 검색을 수행하지 않는다 —
    /// 그래프 질의는 조회 대상 청크로 만든 인덱스가 필요해 자유 텍스트 검색 한 번으로는 성립하지 않는다.
    /// - null / false (기본값): 그래프 검색 없음. 서비스가 등록돼 있어도 자동 활성화되지 않는다.
    /// - true: 예외 — 서비스 미등록이면 <see cref="InvalidOperationException"/>, 등록돼 있으면
    ///   <see cref="NotSupportedException"/>(<c>IGraphRAGService.BuildIndexAsync</c>/<c>LoadIndexAsync</c> 뒤
    ///   <c>QueryAsync</c> 를 직접 호출하라는 안내). 요청을 조용히 버리지 않기 위한 동작이다.
    /// </summary>
    public bool? UseGraphRAG { get; set; }

    /// <summary>
    /// GraphRAG 쿼리 옵션. 위 <see cref="UseGraphRAG"/> 와 같은 이유로 이 검색 경로에서는 읽히지 않는다 —
    /// <c>IGraphRAGService.QueryAsync</c> 에 직접 넘긴다.
    /// </summary>
    public GraphRAGQueryOptions? GraphRAGOptions { get; set; }

    /// <summary>
    /// 하이브리드 검색 활성화 (Vector + Keyword).
    /// - null (기본값): IHybridSearchService가 등록되어 있으면 자동 활성화
    /// - true: 강제 활성화 (서비스 미등록 시 오류)
    /// - false: 강제 비활성화
    /// </summary>
    public bool? UseHybridSearch { get; set; }
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
    public Dictionary<string, object> Metadata { get; set; } = new();
    public Dictionary<string, object> Highlights { get; set; } = new();
    public int ChunkIndex { get; set; }
}

/// <summary>
/// 검색 옵션
/// </summary>
public class SearchOptions
{
    public int TopK { get; set; } = 10;
    public float MinSimilarity { get; set; }
    /// <summary>
    /// 읽히지 않는다 — <see cref="SearchResult"/> 에는 임베딩을 실을 필드가 없어 이 스위치가 바꿀 출력이 없다.
    /// 결과에 벡터가 필요하면 저장소(<c>IVectorStore</c>)에서 청크를 직접 읽는다.
    /// </summary>
    public bool IncludeVectors { get; set; }
    public Dictionary<string, string> MetadataFilters { get; set; } = new();

    /// <summary>
    /// GraphRAG 검색 요청.
    /// <see cref="Retriever.SearchAsync(string, SearchOptions?, CancellationToken)"/> 는 그래프 검색을 수행하지 않는다 —
    /// 그래프 질의는 조회 대상 청크로 만든 인덱스가 필요해 자유 텍스트 검색 한 번으로는 성립하지 않는다.
    /// - null / false (기본값): 그래프 검색 없음. 서비스가 등록돼 있어도 자동 활성화되지 않는다.
    /// - true: 예외 — 서비스 미등록이면 <see cref="InvalidOperationException"/>, 등록돼 있으면
    ///   <see cref="NotSupportedException"/>(<c>IGraphRAGService.BuildIndexAsync</c>/<c>LoadIndexAsync</c> 뒤
    ///   <c>QueryAsync</c> 를 직접 호출하라는 안내). 요청을 조용히 버리지 않기 위한 동작이다.
    /// </summary>
    public bool? UseGraphRAG { get; set; }

    /// <summary>
    /// GraphRAG 쿼리 옵션. 위 <see cref="UseGraphRAG"/> 와 같은 이유로 이 검색 경로에서는 읽히지 않는다 —
    /// <c>IGraphRAGService.QueryAsync</c> 에 직접 넘긴다.
    /// </summary>
    public GraphRAGQueryOptions? GraphRAGOptions { get; set; }

    /// <summary>
    /// 하이브리드 검색 활성화 (Vector + Keyword).
    /// - null (기본값): IHybridSearchService가 등록되어 있으면 자동 활성화
    /// - true: 강제 활성화 (서비스 미등록 시 오류)
    /// - false: 강제 비활성화
    /// </summary>
    public bool? UseHybridSearch { get; set; }
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
    public bool CaseSensitive { get; set; }
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