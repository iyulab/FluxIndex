using System;
using System.Collections.Generic;
using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.SDK;

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

    /// <summary>
    /// The score retrieval gave this result before a reranker replaced <see cref="Score"/>. Null when the
    /// search was not reranked.
    /// </summary>
    public float? RetrievalScore { get; set; }
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

    /// <summary>
    /// Reorders the retrieved candidates with the registered <c>IReranker</c> and returns its top
    /// <see cref="TopK"/>. Off by default: a registered reranker does nothing until a search asks for it.
    /// True with no <c>IReranker</c> registered throws <see cref="InvalidOperationException"/> rather than
    /// returning results that were silently not reranked.
    /// </summary>
    public bool UseReranker { get; set; }

    /// <summary>
    /// How many candidates retrieval fetches for the reranker to order. Null uses three times
    /// <see cref="TopK"/>. A reranker only reorders what it is given, so fetching <see cref="TopK"/>
    /// would leave it nothing to promote. Read only when <see cref="UseReranker"/> is set.
    /// </summary>
    public int? RerankCandidateCount { get; set; }
}

/// <summary>
/// 하이브리드 검색 옵션
/// </summary>
public class HybridSearchOptions : SearchOptions
{
    public float VectorWeight { get; set; } = 0.7f;
    public float KeywordWeight { get; set; } = 0.3f;
    public RerankingStrategy RerankingStrategy { get; set; } = RerankingStrategy.WeightedAverage;

    /// <summary>
    /// The fusion method the hybrid search service uses to merge the vector and keyword legs. Null
    /// (the default) derives it from <see cref="RerankingStrategy"/>: <c>WeightedAverage</c> maps to
    /// <c>WeightedSum</c>, <c>ReciprocalRankFusion</c> to <c>RRF</c>. Set it to reach the methods the
    /// two-value strategy cannot name (relative-score fusion, product, maximum, harmonic mean).
    /// </summary>
    public Core.Domain.Models.FusionMethod? FusionMethod { get; set; }

    /// <summary>
    /// The <c>k</c> constant of reciprocal rank fusion. Null keeps the service default (60).
    /// </summary>
    public double? RrfK { get; set; }
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