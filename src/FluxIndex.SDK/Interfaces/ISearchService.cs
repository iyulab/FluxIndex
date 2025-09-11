using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.SDK.Interfaces;

/// <summary>
/// 검색 서비스 인터페이스 - 하이브리드 검색 및 리랭킹
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// 기본 검색
    /// </summary>
    Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 의미 기반 검색 (벡터 유사도)
    /// </summary>
    Task<SearchResponse> SemanticSearchAsync(string query, SemanticSearchOptions options, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 키워드 기반 검색
    /// </summary>
    Task<SearchResponse> KeywordSearchAsync(string query, KeywordSearchOptions options, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 하이브리드 검색 (의미 + 키워드)
    /// </summary>
    Task<SearchResponse> HybridSearchAsync(string query, HybridSearchOptions options, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 패싯 검색 (카테고리별 필터링)
    /// </summary>
    Task<FacetedSearchResponse> FacetedSearchAsync(string query, FacetSearchOptions options, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 유사 문서 검색
    /// </summary>
    Task<SearchResponse> FindSimilarAsync(string documentId, SimilarityOptions options, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 검색 결과 리랭킹
    /// </summary>
    Task<SearchResponse> RerankAsync(SearchResponse results, RerankingOptions options, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 검색 제안 (자동완성)
    /// </summary>
    Task<IEnumerable<string>> GetSuggestionsAsync(string prefix, int maxSuggestions = 10, CancellationToken cancellationToken = default);
}