using FluxIndex.Application.Interfaces;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Domain.Entities;
using FluxIndex.SDK.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreSearchResult = FluxIndex.Application.Interfaces.SearchResult;

namespace FluxIndex.SDK;

/// <summary>
/// FluxIndex 클라이언트 - Retriever와 Indexer를 통한 간편한 진입점
/// 시맨틱 캐싱을 통한 성능 최적화 지원
/// </summary>
public class FluxIndexClient : IFluxIndexClient
{
    private readonly Retriever _retriever;
    private readonly Indexer _indexer;
    private readonly ISemanticCacheService? _cacheService;
    private readonly ILogger<FluxIndexClient> _logger;

    public FluxIndexClient(
        Retriever retriever,
        Indexer indexer,
        ILogger<FluxIndexClient>? logger = null,
        ISemanticCacheService? cacheService = null)
    {
        _retriever = retriever;
        _indexer = indexer;
        _cacheService = cacheService;
        _logger = logger ?? new NullLogger<FluxIndexClient>();

        if (_cacheService != null)
        {
            _logger.LogInformation("FluxIndexClient initialized with semantic caching enabled");
        }
    }

    /// <summary>
    /// Retriever 접근자
    /// </summary>
    public Retriever Retriever => _retriever;

    /// <summary>
    /// Indexer 접근자
    /// </summary>
    public Indexer Indexer => _indexer;

    /// <summary>
    /// FluxIndexClient 빌더 생성
    /// </summary>
    public static FluxIndexClientBuilder CreateBuilder()
    {
        return new FluxIndexClientBuilder();
    }

    // Convenience methods - delegate to Retriever

    /// <summary>
    /// 벡터 검색 - 시맨틱 캐싱 지원
    /// </summary>
    public async Task<IEnumerable<SearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        float minScore = 0.5f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be null or empty", nameof(query));

        var startTime = DateTime.UtcNow;

        try
        {
            // 1. 시맨틱 캐시 확인
            if (_cacheService != null)
            {
                var cachedResult = await _cacheService.GetCachedResponseAsync(query, 0.95f, cancellationToken);
                if (cachedResult != null)
                {
                    _logger.LogInformation("Cache hit for query: '{Query}' (similarity: {Similarity:F3}, hit_type: {HitType})",
                        query, cachedResult.SimilarityScore, cachedResult.HitType);

                    // 캐시된 검색 결과를 SDK 형식으로 변환
                    return ConvertCachedToSDKSearchResults(cachedResult.SearchResults);
                }

                _logger.LogDebug("Cache miss for query: '{Query}' - proceeding with retrieval", query);
            }

            // 2. 실제 검색 수행
            var coreResults = await _retriever.SearchAsync(query, maxResults, minScore, filter, cancellationToken);
            var sdkResults = ConvertToSDKSearchResults(coreResults).ToList();

            // 3. 결과 캐싱 (검색 결과가 있는 경우만)
            if (_cacheService != null && sdkResults.Any())
            {
                var searchResultsForCache = sdkResults.Select(r => new Core.Domain.Entities.SearchResult
                {
                    Id = r.Id,
                    DocumentId = r.DocumentId,
                    Content = r.Content,
                    Score = r.Score,
                    ChunkIndex = r.ChunkIndex
                }).ToList();

                await _cacheService.CacheResponseAsync(
                    query,
                    $"Found {sdkResults.Count} results",
                    searchResultsForCache,
                    null, // 기본 만료 시간 사용
                    cancellationToken);

                _logger.LogDebug("Cached search results for query: '{Query}' ({Count} results)", query, sdkResults.Count);
            }

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Search completed: query='{Query}', results={Count}, duration={Duration}ms",
                query, sdkResults.Count, duration.TotalMilliseconds);

            return sdkResults;
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Search failed: query='{Query}', duration={Duration}ms", query, duration.TotalMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// 하이브리드 검색 (Retriever 위임)
    /// </summary>
    public async Task<IEnumerable<SearchResult>> HybridSearchAsync(
        string keyword,
        string query,
        int maxResults = 10,
        float vectorWeight = 0.7f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        var coreResults = await _retriever.HybridSearchAsync(keyword, query, maxResults, vectorWeight, filter, cancellationToken);
        return ConvertToSDKSearchResults(coreResults);
    }

    /// <summary>
    /// 문서 조회 (Retriever 위임)
    /// </summary>
    public async Task<Document?> GetDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        return await _retriever.GetDocumentAsync(documentId, cancellationToken);
    }

    /// <summary>
    /// 문서 수 조회 (Retriever 위임)
    /// </summary>
    public async Task<int> GetDocumentCountAsync(CancellationToken cancellationToken = default)
    {
        var stats = await _retriever.GetStatisticsAsync(cancellationToken);
        return stats.TotalDocuments;
    }

    /// <summary>
    /// 청크 수 조회 (Retriever 위임)
    /// </summary>
    public async Task<int> GetChunkCountAsync(CancellationToken cancellationToken = default)
    {
        var stats = await _retriever.GetStatisticsAsync(cancellationToken);
        return stats.TotalChunks;
    }

    // Convenience methods - delegate to Indexer

    /// <summary>
    /// 문서 인덱싱 (Indexer 위임)
    /// </summary>
    public async Task<string> IndexAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        return await _indexer.IndexDocumentAsync(document, cancellationToken);
    }

    /// <summary>
    /// 청크 인덱싱 (Indexer 위임)
    /// </summary>
    public async Task<string> IndexChunksAsync(
        IEnumerable<DocumentChunk> chunks,
        string? documentId = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        return await _indexer.IndexChunksAsync(chunks, documentId, metadata, cancellationToken);
    }

    /// <summary>
    /// 문서 삭제 (Indexer 위임)
    /// </summary>
    public async Task<bool> DeleteDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        return await _indexer.DeleteDocumentAsync(documentId, cancellationToken);
    }

    /// <summary>
    /// 문서 업데이트 (Indexer 위임)
    /// </summary>
    public async Task UpdateDocumentAsync(
        string documentId,
        Document updatedDocument,
        CancellationToken cancellationToken = default)
    {
        await _indexer.UpdateDocumentAsync(documentId, updatedDocument, cancellationToken);
    }

    /// <summary>
    /// 통계 조회 - 시맨틱 캐시 통계 포함
    /// </summary>
    public async Task<ClientStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var retrieverStats = await _retriever.GetStatisticsAsync(cancellationToken);
        var indexerStats = await _indexer.GetStatisticsAsync(cancellationToken);

        // 시맨틱 캐시 통계 수집
        CacheStatistics? cacheStats = null;
        if (_cacheService != null)
        {
            try
            {
                cacheStats = await _cacheService.GetStatisticsAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get cache statistics");
            }
        }

        return new ClientStatistics
        {
            TotalDocuments = retrieverStats.TotalDocuments,
            TotalChunks = retrieverStats.TotalChunks,
            AverageChunksPerDocument = retrieverStats.AverageChunksPerDocument,
            CacheEnabled = retrieverStats.CacheEnabled || _cacheService != null,
            VectorStoreProvider = retrieverStats.VectorStoreProvider,
            DefaultChunkSize = indexerStats.DefaultChunkSize,
            DefaultChunkOverlap = indexerStats.DefaultChunkOverlap,
            EmbeddingModel = indexerStats.EmbeddingModel,

            // 시맨틱 캐시 통계 추가
            SemanticCacheEnabled = _cacheService != null,
            CacheHitRate = cacheStats?.HitRate ?? 0f,
            CacheResponseTime = cacheStats?.CacheResponseTime ?? TimeSpan.Zero,
            CachedItemsCount = cacheStats?.CachedItemsCount ?? 0,
            CacheMemoryUsageMB = cacheStats != null ? cacheStats.MemoryUsageBytes / 1024 / 1024 : 0
        };
    }

    /// <summary>
    /// 시맨틱 캐시 통계 조회
    /// </summary>
    public async Task<FluxIndex.Core.Domain.ValueObjects.CacheStatistics?> GetCacheStatisticsAsync(CancellationToken cancellationToken = default)
    {
        if (_cacheService == null)
            return null;

        try
        {
            return await _cacheService.GetStatisticsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cache statistics");
            return null;
        }
    }

    /// <summary>
    /// 캐시 워밍업 - 자주 사용되는 쿼리들로 캐시 사전 로드
    /// </summary>
    public async Task<bool> WarmupCacheAsync(
        IEnumerable<string> commonQueries,
        CancellationToken cancellationToken = default)
    {
        if (_cacheService == null)
        {
            _logger.LogWarning("Cache service is not available for warmup");
            return false;
        }

        try
        {
            return await _cacheService.WarmupCacheAsync(commonQueries, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache warmup failed");
            return false;
        }
    }

    /// <summary>
    /// 캐시 최적화 실행
    /// </summary>
    public async Task OptimizeCacheAsync(CancellationToken cancellationToken = default)
    {
        if (_cacheService == null)
        {
            _logger.LogWarning("Cache service is not available for optimization");
            return;
        }

        try
        {
            await _cacheService.OptimizeCacheAsync(cancellationToken);
            _logger.LogInformation("Cache optimization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache optimization failed");
        }
    }

    /// <summary>
    /// Convert Core SearchResult to SDK SearchResult
    /// </summary>
    private IEnumerable<SearchResult> ConvertToSDKSearchResults(IEnumerable<CoreSearchResult> coreResults)
    {
        return coreResults.Select(cr => new SearchResult
        {
            Id = cr.ChunkId,
            DocumentId = cr.DocumentId,
            Content = cr.Content,
            Score = cr.Score,
            ChunkIndex = cr.ChunkIndex,
            Metadata = new DocumentMetadata
            {
                FileName = cr.FileName
            }
        });
    }

    /// <summary>
    /// Convert cached SearchResult to SDK SearchResult
    /// </summary>
    private IEnumerable<SearchResult> ConvertCachedToSDKSearchResults(List<Core.Domain.Entities.SearchResult> cachedResults)
    {
        return cachedResults.Select(cr => new SearchResult
        {
            Id = cr.Id,
            DocumentId = cr.DocumentId,
            Content = cr.Content,
            Score = cr.Score,
            ChunkIndex = cr.ChunkIndex,
            Metadata = new DocumentMetadata()
        });
    }
}

/// <summary>
/// FluxIndex 클라이언트 인터페이스
/// </summary>
public interface IFluxIndexClient
{
    Retriever Retriever { get; }
    Indexer Indexer { get; }
    
    // Convenience methods
    Task<IEnumerable<SearchResult>> SearchAsync(string query, int maxResults = 10, float minScore = 0.5f, Dictionary<string, object>? filter = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<SearchResult>> HybridSearchAsync(string keyword, string query, int maxResults = 10, float vectorWeight = 0.7f, Dictionary<string, object>? filter = null, CancellationToken cancellationToken = default);
    Task<Document?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task<string> IndexAsync(Document document, CancellationToken cancellationToken = default);
    Task<string> IndexChunksAsync(IEnumerable<DocumentChunk> chunks, string? documentId = null, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task UpdateDocumentAsync(string documentId, Document updatedDocument, CancellationToken cancellationToken = default);
    Task<ClientStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    Task<int> GetDocumentCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetChunkCountAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 클라이언트 통계
/// </summary>
public class ClientStatistics
{
    public int TotalDocuments { get; set; }
    public int TotalChunks { get; set; }
    public double AverageChunksPerDocument { get; set; }
    public bool CacheEnabled { get; set; }
    public string VectorStoreProvider { get; set; } = string.Empty;
    public int DefaultChunkSize { get; set; }
    public int DefaultChunkOverlap { get; set; }
    public string EmbeddingModel { get; set; } = string.Empty;
}