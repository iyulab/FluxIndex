using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Domain.Entities;
using MonitoringThresholds = FluxIndex.Core.Application.Interfaces.QualityThresholds;
using FluxIndex.SDK.Models;
using DocumentChunkEntity = FluxIndex.Core.Domain.Entities.DocumentChunk;
using DocumentChunkModel = FluxIndex.Core.Domain.Models.CacheDocumentChunk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
// Removed legacy SearchResult alias - using Core types only

namespace FluxIndex.SDK;

/// <summary>
/// FluxIndex 컨텍스트 - Retriever와 Indexer를 통한 간편한 진입점
/// 시맨틱 캐싱을 통한 성능 최적화 지원
/// </summary>
public partial class FluxIndexContext : IFluxIndexContext, IDisposable
{
    private readonly Retriever _retriever;
    private readonly Indexer _indexer;
    private readonly ISemanticCacheService? _cacheService;
    private readonly IHybridSearchService? _hybridSearchService;
    private readonly ISmallToBigRetriever? _smallToBigRetriever;
    private readonly IQualityMonitoringService? _qualityMonitor;
    private readonly IAdaptiveSearchService? _adaptiveSearchService;
    private readonly ILogger<FluxIndexContext> _logger;
    private bool _disposed;

    public FluxIndexContext(
        Retriever retriever,
        Indexer indexer,
        IServiceProvider serviceProvider,
        ILogger<FluxIndexContext>? logger = null,
        ISemanticCacheService? cacheService = null,
        IHybridSearchService? hybridSearchService = null,
        ISmallToBigRetriever? smallToBigRetriever = null,
        IQualityMonitoringService? qualityMonitor = null,
        IAdaptiveSearchService? adaptiveSearchService = null)
    {
        _retriever = retriever;
        _indexer = indexer;
        ServiceProvider = serviceProvider;
        _cacheService = cacheService;
        _hybridSearchService = hybridSearchService;
        _smallToBigRetriever = smallToBigRetriever;
        _qualityMonitor = qualityMonitor;
        _adaptiveSearchService = adaptiveSearchService;
        _logger = logger ?? NullLogger<FluxIndexContext>.Instance;

        if (_cacheService != null)
        {
            LogSemanticCachingEnabled(_logger);
        }

        if (_qualityMonitor != null)
        {
            LogQualityMonitoringEnabled(_logger);
            _ = _qualityMonitor.StartMonitoringAsync();
        }

        if (_hybridSearchService != null)
        {
            LogHybridSearchEnabled(_logger);
        }

        if (_smallToBigRetriever != null)
        {
            LogSmallToBigEnabled(_logger);
        }

        if (_adaptiveSearchService != null)
        {
            LogAdaptiveSearchEnabled(_logger);
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
    /// ServiceProvider 접근자
    /// </summary>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// FluxIndexContext 빌더 생성
    /// </summary>
    public static FluxIndexContextBuilder CreateBuilder()
    {
        return new FluxIndexContextBuilder();
    }

    // Convenience methods - delegate to Retriever

    /// <summary>
    /// 벡터 검색 - 시맨틱 캐싱 지원
    /// </summary>
    public async Task<IEnumerable<SearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        float minScore = 0.2f, // Lowered from 0.5f for better recall
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
                var cachedResult = await _cacheService.GetCachedResultAsync(query, 0.95f, cancellationToken);
                if (cachedResult != null)
                {
                    LogCacheHit(_logger, query, cachedResult.SimilarityScore);

                    var cachedResults = ConvertCachedToSDKSearchResults(cachedResult.Results);

                    // 품질 모니터링 (캐시 히트)
                    if (_qualityMonitor != null)
                    {
                        var responseTime = DateTime.UtcNow - startTime;
                        var metadata = new Dictionary<string, object>
                        {
                            ["cache_hit"] = true,
                            ["search_strategy"] = "cached"
                        };

                        var coreCachedResults = cachedResults.Select(ConvertToCore).ToList();
                        _ = _qualityMonitor.EvaluateSearchQualityAsync(query, coreCachedResults, responseTime, metadata, cancellationToken);
                    }

                    return cachedResults;
                }

                LogCacheMiss(_logger, query);
            }

            // 2. 실제 검색 수행
            var coreResults = await _retriever.SearchAsync(query, maxResults, minScore, filter, cancellationToken);
            var sdkResults = ConvertToSDKSearchResults(coreResults).ToList();

            // 3. 결과 캐싱 (검색 결과가 있는 경우만)
            if (_cacheService != null && sdkResults.Count != 0)
            {
                var searchResultsForCache = sdkResults.Select(r => new DocumentChunkModel
                {
                    Id = r.Id,
                    DocumentId = r.DocumentId,
                    Content = r.Content,
                    ChunkIndex = r.ChunkIndex,
                    TokenCount = 0,
                    Metadata = new Dictionary<string, object>(),
                    Score = r.Score
                }).ToList();

                await _cacheService.SetCachedResultAsync(
                    query,
                    searchResultsForCache,
                    null, // 기본 메타데이터
                    null, // 기본 만료 시간 사용
                    cancellationToken);

                LogCachedSearchResults(_logger, query, sdkResults.Count);
            }

            var duration = DateTime.UtcNow - startTime;

            // 품질 모니터링 (실제 검색)
            if (_qualityMonitor != null)
            {
                var metadata = new Dictionary<string, object>
                {
                    ["cache_hit"] = false,
                    ["search_strategy"] = "vector"
                };

                var coreSearchResults = sdkResults.Select(ConvertToCore).ToList();
                _ = _qualityMonitor.EvaluateSearchQualityAsync(query, coreSearchResults, duration, metadata, cancellationToken);
            }

            LogSearchCompleted(_logger, query, sdkResults.Count, duration.TotalMilliseconds);

            return sdkResults;
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;

            // 품질 모니터링 (검색 실패)
            if (_qualityMonitor != null)
            {
                var failedResults = new List<SearchResult>();
                var metadata = new Dictionary<string, object>
                {
                    ["cache_hit"] = false,
                    ["search_strategy"] = "vector",
                    ["error"] = ex.Message
                };

                var coreFailedResults = failedResults.Select(ConvertToCore).ToList();
                _ = _qualityMonitor.EvaluateSearchQualityAsync(query, coreFailedResults, duration, metadata, cancellationToken);
            }

            LogSearchFailed(_logger, ex, query, duration.TotalMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// 하이브리드 검색 (기존 Retriever 위임)
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
    /// 새로운 하이브리드 검색 (HybridSearchService 사용)
    /// </summary>
    public async Task<IReadOnlyList<HybridSearchResult>> HybridSearchV2Async(
        string query,
        FluxIndex.Core.Domain.Models.HybridSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_hybridSearchService == null)
        {
            throw new InvalidOperationException("HybridSearchService is not configured. Please configure it using the FluxIndexClientBuilder.");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query cannot be null or empty", nameof(query));
        }

        try
        {
            var startTime = DateTime.UtcNow;
            var results = await _hybridSearchService.SearchAsync(query, options, cancellationToken);
            var duration = DateTime.UtcNow - startTime;

            LogHybridSearchCompleted(_logger, query, results.Count, duration.TotalMilliseconds);

            return results;
        }
        catch (Exception ex)
        {
            LogHybridSearchFailed(_logger, ex, query);
            throw;
        }
    }

    #region Adaptive Search Methods (DAT-enabled)

    /// <summary>
    /// Adaptive Search with Dynamic Alpha Tuning (DAT).
    /// Automatically selects optimal search strategy and fusion weights based on query analysis.
    /// Research shows 6.6% improvement in retrieval quality with query-adaptive weights.
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="options">Adaptive search options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Adaptive search result with strategy information</returns>
    public async Task<AdaptiveSearchResult> AdaptiveSearchAsync(
        string query,
        AdaptiveSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_adaptiveSearchService == null)
        {
            throw new InvalidOperationException(
                "AdaptiveSearchService is not configured. " +
                "Use FluxIndexContextBuilder to enable adaptive search.");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query cannot be null or empty", nameof(query));
        }

        try
        {
            var startTime = DateTime.UtcNow;
            var result = await _adaptiveSearchService.SearchAsync(query, options, cancellationToken);
            var duration = DateTime.UtcNow - startTime;

            var resultCount = result.Documents.Count();
            var strategyName = result.UsedStrategy.ToString();
            LogAdaptiveSearchCompleted(_logger, query, strategyName, resultCount, duration.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            LogAdaptiveSearchFailed(_logger, ex, query);
            throw;
        }
    }

    /// <summary>
    /// Adaptive search with forced strategy.
    /// Useful for testing or when a specific strategy is required.
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="strategy">Forced search strategy</param>
    /// <param name="options">Adaptive search options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Adaptive search result with strategy information</returns>
    public async Task<AdaptiveSearchResult> AdaptiveSearchWithStrategyAsync(
        string query,
        FluxIndex.Core.Application.Interfaces.SearchStrategy strategy,
        AdaptiveSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_adaptiveSearchService == null)
        {
            throw new InvalidOperationException(
                "AdaptiveSearchService is not configured. " +
                "Use FluxIndexContextBuilder to enable adaptive search.");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query cannot be null or empty", nameof(query));
        }

        return await _adaptiveSearchService.SearchWithStrategyAsync(query, strategy, options, cancellationToken);
    }

    /// <summary>
    /// Indicates whether adaptive search (DAT) is available.
    /// </summary>
    public bool SupportsAdaptiveSearch => _adaptiveSearchService != null;

    /// <summary>
    /// Gets performance report for all search strategies.
    /// </summary>
    public async Task<StrategyPerformanceReport?> GetStrategyPerformanceReportAsync(
        CancellationToken cancellationToken = default)
    {
        if (_adaptiveSearchService == null)
        {
            return null;
        }

        return await _adaptiveSearchService.GetPerformanceReportAsync(cancellationToken);
    }

    /// <summary>
    /// Provides feedback on search results for adaptive learning.
    /// </summary>
    /// <param name="query">Original search query</param>
    /// <param name="result">Search result to provide feedback on</param>
    /// <param name="feedback">User feedback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task ProvideSearchFeedbackAsync(
        string query,
        AdaptiveSearchResult result,
        UserFeedback feedback,
        CancellationToken cancellationToken = default)
    {
        if (_adaptiveSearchService == null)
        {
            LogFeedbackIgnored(_logger);
            return;
        }

        await _adaptiveSearchService.UpdateFeedbackAsync(query, result, feedback, cancellationToken);
        LogSearchFeedbackRecorded(_logger, query);
    }

    #endregion

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

    #region Quantized Search Methods

    /// <summary>
    /// 양자화 지원 여부 확인
    /// </summary>
    public bool SupportsQuantization => _retriever.SupportsQuantization;

    /// <summary>
    /// 양자화기 인스턴스
    /// </summary>
    public IVectorQuantizer? Quantizer => _retriever.Quantizer;

    /// <summary>
    /// 양자화 벡터를 사용한 빠른 근사 검색
    /// </summary>
    /// <param name="query">검색 쿼리</param>
    /// <param name="maxResults">최대 결과 수</param>
    /// <param name="minScore">최소 유사도 점수</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>검색 결과 목록</returns>
    public async Task<IEnumerable<SearchResult>> SearchQuantizedAsync(
        string query,
        int maxResults = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be null or empty", nameof(query));

        var coreResults = await _retriever.SearchQuantizedAsync(query, maxResults, minScore, cancellationToken);
        return ConvertToSDKSearchResults(coreResults);
    }

    /// <summary>
    /// 양자화 후보 선택 + 원본 벡터 리랭킹 검색
    /// 양자화로 빠르게 후보군을 선택한 후, 원본 벡터로 정확하게 리랭킹
    /// </summary>
    /// <param name="query">검색 쿼리</param>
    /// <param name="maxResults">최대 결과 수</param>
    /// <param name="candidateMultiplier">후보군 배수 (기본 3배)</param>
    /// <param name="minScore">최소 유사도 점수</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>리랭킹된 검색 결과</returns>
    public async Task<IEnumerable<SearchResult>> SearchWithRerankAsync(
        string query,
        int maxResults = 10,
        int candidateMultiplier = 3,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be null or empty", nameof(query));

        var coreResults = await _retriever.SearchWithRerankAsync(query, maxResults, candidateMultiplier, minScore, cancellationToken);
        return ConvertToSDKSearchResults(coreResults);
    }

    /// <summary>
    /// 양자화 저장소 통계 조회
    /// </summary>
    public async Task<QuantizedStorageStats?> GetQuantizedStatsAsync(CancellationToken cancellationToken = default)
    {
        return await _retriever.GetQuantizedStatsAsync(cancellationToken);
    }

    #endregion

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
        IEnumerable<DocumentChunkModel> chunks,
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
        return await _indexer.DeleteByDocumentIdAsync(documentId, cancellationToken);
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

        // 시맨틱 캐시 통계 수집 (임시 비활성화)
        // Core.Application.Interfaces.CacheStatistics? cacheStats = null;
        // if (_cacheService != null)
        // {
        //     try
        //     {
        //         cacheStats = await _cacheService.GetCacheStatisticsAsync(cancellationToken);
        //     }
        //     catch (Exception ex)
        //     {
        //         _logger.LogWarning(ex, "Failed to get cache statistics");
        //     }
        // }

        return new ClientStatistics
        {
            TotalDocuments = retrieverStats.TotalDocuments,
            TotalChunks = retrieverStats.TotalChunks,
            AverageChunksPerDocument = retrieverStats.AverageChunksPerDocument,
            CacheEnabled = retrieverStats.CacheEnabled || _cacheService != null,
            VectorStoreProvider = retrieverStats.VectorStoreProvider,
            ResolvedStoreName = retrieverStats.ResolvedStoreName,
            DetectedDimension = retrieverStats.DetectedDimension,
            DefaultChunkSize = indexerStats.DefaultChunkSize,
            DefaultChunkOverlap = indexerStats.DefaultChunkOverlap,
            EmbeddingModel = indexerStats.EmbeddingModel,

            // 시맨틱 캐시 통계 추가 (임시 비활성화)
            SemanticCacheEnabled = _cacheService != null,
            CacheHitRate = 0f, // cacheStats?.HitRate ?? 0f,
            CacheResponseTime = 0, // cacheStats?.AverageResponseTimeMs ?? 0,
            CachedItemsCount = 0, // cacheStats?.TotalEntries ?? 0,
            CacheMemoryUsageMB = 0 // cacheStats != null ? cacheStats.CacheSizeBytes / 1024.0 / 1024.0 : 0
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
            var stats = await _cacheService.GetCacheStatisticsAsync(cancellationToken);
            return new FluxIndex.Core.Domain.ValueObjects.CacheStatistics
            {
                TotalQueries = stats.TotalEntries,
                CacheHits = stats.CacheHits,
                CacheMisses = stats.CacheMisses,
                CachedItemsCount = stats.TotalEntries,
                MemoryUsageBytes = 0, // Application interface doesn't expose this
                AverageResponseTime = TimeSpan.Zero, // Application interface doesn't expose this
                CacheResponseTime = TimeSpan.Zero, // Application interface doesn't expose this
                AverageSimilarityScore = 0.0f // Application interface doesn't expose this
            };
        }
        catch (Exception ex)
        {
            LogFailedToGetCacheStatistics(_logger, ex);
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
            LogCacheNotAvailableForWarmup(_logger);
            return false;
        }

        try
        {
            await _cacheService.WarmupCacheAsync(commonQueries.ToList(), cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            LogCacheWarmupFailed(_logger, ex);
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
            LogCacheNotAvailableForOptimization(_logger);
            return;
        }

        try
        {
            await _cacheService.CompactCacheAsync(cancellationToken);
            LogCacheOptimizationCompleted(_logger);
        }
        catch (Exception ex)
        {
            LogCacheOptimizationFailed(_logger, ex);
        }
    }

    /// <summary>
    /// Convert VectorSearchResult to SDK SearchResult
    /// </summary>
    private static IEnumerable<SearchResult> ConvertToSDKSearchResults(IEnumerable<VectorSearchResult> vectorResults)
    {
        return vectorResults.Select(vr => new SearchResult
        {
            Id = vr.DocumentChunk.Id,
            DocumentId = vr.DocumentChunk.DocumentId,
            Content = vr.DocumentChunk.Content,
            Score = (float)vr.Score,
            ChunkIndex = vr.DocumentChunk.ChunkIndex,
            Metadata = vr.DocumentChunk.Metadata ?? new Dictionary<string, object>()
        });
    }

    /// <summary>
    /// Small-to-Big 검색 - 정밀 검색 후 컨텍스트 확장
    /// </summary>
    public async Task<IEnumerable<SmallToBigSearchResult>> SmallToBigSearchAsync(
        string query,
        SmallToBigSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_smallToBigRetriever == null)
        {
            LogSmallToBigNotConfigured(_logger);
            return Enumerable.Empty<SmallToBigSearchResult>();
        }

        try
        {
            var coreOptions = options?.ToCoreOptions() ?? new FluxIndex.Core.Domain.Models.SmallToBigOptions();
            var results = await _smallToBigRetriever.SearchAsync(query, coreOptions, cancellationToken);

            return results.Select(r => new SmallToBigSearchResult
            {
                PrimaryChunk = ConvertEntityToCache(r.PrimaryChunk),
                ContextChunks = r.ContextChunks.Select(ConvertEntityToCache).ToList(),
                RelevanceScore = r.RelevanceScore,
                WindowSize = r.WindowSize,
                ExpansionReason = r.ExpansionReason,
                ContextQuality = r.ContextQuality,
                CombinedText = r.CombinedText
            });
        }
        catch (Exception ex)
        {
            LogSmallToBigSearchFailed(_logger, ex, query);
            throw;
        }
    }

    /// <summary>
    /// 쿼리 복잡도 분석
    /// </summary>
    public async Task<QueryComplexityResult?> AnalyzeQueryComplexityAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (_smallToBigRetriever == null)
        {
            LogSmallToBigNotConfigured(_logger);
            return null;
        }

        try
        {
            var analysis = await _smallToBigRetriever.AnalyzeQueryComplexityAsync(query, cancellationToken);

            return new QueryComplexityResult
            {
                OverallComplexity = analysis.OverallComplexity,
                LexicalComplexity = analysis.LexicalComplexity,
                SemanticComplexity = analysis.SemanticComplexity,
                RecommendedWindowSize = analysis.RecommendedWindowSize,
                AnalysisConfidence = analysis.AnalysisConfidence
            };
        }
        catch (Exception ex)
        {
            LogQueryComplexityAnalysisFailed(_logger, ex, query);
            return null;
        }
    }

    /// <summary>
    /// Convert cached SearchResult to SDK SearchResult
    /// </summary>
    private static IEnumerable<SearchResult> ConvertCachedToSDKSearchResults(IReadOnlyList<DocumentChunkModel> cachedResults)
    {
        return cachedResults.Select(cr => new SearchResult
        {
            Id = cr.Id,
            DocumentId = cr.DocumentId,
            Content = cr.Content,
            Score = cr.Score,
            ChunkIndex = cr.ChunkIndex,
            Metadata = cr.Metadata ?? new Dictionary<string, object>()
        });
    }

    /// <summary>
    /// Core DocumentChunk를 SDK DocumentChunk로 변환
    /// </summary>
    private static DocumentChunkModel ConvertToSDKChunk(DocumentChunkEntity coreChunk)
    {
        var sdkChunk = DocumentChunkModel.Create(
            coreChunk.DocumentId,
            coreChunk.Content,
            coreChunk.ChunkIndex,
            1, // totalChunks - default value
            coreChunk.Embedding,
            0f, // score - default value
            coreChunk.TokenCount,
            coreChunk.Metadata);

        // 새 인스턴스를 생성하여 ID 설정
        return new DocumentChunkModel
        {
            Id = coreChunk.Id,
            DocumentId = sdkChunk.DocumentId,
            Content = sdkChunk.Content,
            ChunkIndex = sdkChunk.ChunkIndex,
            TotalChunks = sdkChunk.TotalChunks,
            Embedding = sdkChunk.Embedding,
            Score = sdkChunk.Score,
            TokenCount = sdkChunk.TokenCount,
            Metadata = sdkChunk.Metadata,
            CreatedAt = sdkChunk.CreatedAt
        };
    }

    private static DocumentChunkEntity ConvertToEntityChunk(DocumentChunkModel modelChunk)
    {
        return DocumentChunkEntity.Create(
            modelChunk.DocumentId,
            modelChunk.Content,
            modelChunk.ChunkIndex,
            modelChunk.TotalChunks
        );
    }

    /// <summary>
    /// Entity DocumentChunk를 Model CacheDocumentChunk로 변환
    /// </summary>
    private DocumentChunkModel ConvertEntityToCache(DocumentChunkEntity entityChunk)
    {
        return new DocumentChunkModel
        {
            Id = entityChunk.Id,
            DocumentId = entityChunk.DocumentId,
            Content = entityChunk.Content,
            ChunkIndex = entityChunk.ChunkIndex,
            Score = 0f,
            Metadata = entityChunk.Metadata ?? new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// 실시간 품질 대시보드 조회
    /// </summary>
    public async Task<QualityDashboard?> GetQualityDashboardAsync(
        TimeSpan? timeWindow = null,
        CancellationToken cancellationToken = default)
    {
        if (_qualityMonitor == null)
        {
            LogQualityMonitoringNotEnabled(_logger);
            return null;
        }

        var window = timeWindow ?? TimeSpan.FromHours(1);
        return await _qualityMonitor.GetRealTimeMetricsAsync(window, cancellationToken);
    }

    /// <summary>
    /// 품질 경고 조회
    /// </summary>
    public async Task<IReadOnlyList<QualityAlert>?> GetQualityAlertsAsync(
        AlertSeverity? severity = null,
        TimeSpan? timeWindow = null,
        CancellationToken cancellationToken = default)
    {
        if (_qualityMonitor == null)
        {
            LogQualityMonitoringNotEnabled(_logger);
            return null;
        }

        return await _qualityMonitor.GetQualityAlertsAsync(severity, timeWindow, cancellationToken);
    }

    /// <summary>
    /// 품질 트렌드 분석
    /// </summary>
    public async Task<QualityTrends?> GetQualityTrendsAsync(
        TimeSpan? period = null,
        TimeSpan? granularity = null,
        CancellationToken cancellationToken = default)
    {
        if (_qualityMonitor == null)
        {
            LogQualityMonitoringNotEnabled(_logger);
            return null;
        }

        var analysisWindow = period ?? TimeSpan.FromHours(6);
        var dataGranularity = granularity ?? TimeSpan.FromMinutes(30);

        return await _qualityMonitor.AnalyzeQualityTrendsAsync(analysisWindow, dataGranularity, cancellationToken);
    }

    /// <summary>
    /// 품질 임계값 설정
    /// </summary>
    public async Task SetQualityThresholdsAsync(
        MonitoringThresholds thresholds,
        CancellationToken cancellationToken = default)
    {
        if (_qualityMonitor == null)
        {
            LogQualityMonitoringNotEnabled(_logger);
            return;
        }

        await _qualityMonitor.SetQualityThresholdsAsync(thresholds, cancellationToken);
        LogQualityThresholdsUpdated(_logger);
    }

    /// <summary>
    /// SDK HybridSearchOptions를 Core HybridSearchOptions로 변환.
    /// 매핑 정본은 <see cref="HybridSearchOptionsMapper"/> — Retriever 경로와 갈라지지 않게 공유한다.
    /// </summary>
    private static FluxIndex.Core.Domain.Models.HybridSearchOptions ConvertToCore(HybridSearchOptions sdkOptions)
        => HybridSearchOptionsMapper.ToCore(sdkOptions);
    /// <summary>
    /// SDK SearchResult를 Core SearchResult로 변환
    /// </summary>
    private static Core.Application.Interfaces.SearchResult ConvertToCore(SearchResult sdkResult)
    {
        // SDK SearchResult lacks the DocumentChunk object needed for Core SearchResult
        // For now, create a basic conversion that maintains compatibility
        var documentChunk = new DocumentChunkEntity
        {
            Id = sdkResult.Id,
            DocumentId = sdkResult.DocumentId,
            Content = sdkResult.Content,
            ChunkIndex = sdkResult.ChunkIndex,
            TotalChunks = 1 // Default value since SDK doesn't track this
        };

        return new Core.Application.Interfaces.SearchResult
        {
            Chunk = documentChunk,
            Score = sdkResult.Score,
            FileName = string.Empty, // Default value since SDK doesn't track this
            Metadata = sdkResult.Metadata
        };
    }

    /// <summary>
    /// Dispose pattern implementation for proper resource cleanup
    /// ✅ Issue #3 fix: Ensures SQLite connections are properly closed
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            try
            {
                // 1. Stop quality monitoring if enabled
                if (_qualityMonitor != null)
                {
                    try
                    {
                        _qualityMonitor.StopMonitoringAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        LogFailedToStopQualityMonitoring(_logger, ex);
                    }
                }

                // 2. Dispose VectorStore (DbContext) to close DB connections
                if (ServiceProvider.GetService(typeof(IVectorStore)) is IDisposable vectorStore)
                {
                    vectorStore.Dispose();
                }

                // 3. Dispose SQLite DbContext explicitly (via reflection to avoid storage dependency)
                var dbContextType = Type.GetType("FluxIndex.Storage.SQLite.SQLiteDbContext, FluxIndex.Storage.SQLite");
                if (dbContextType != null)
                {
                    var dbContextObj = ServiceProvider.GetService(dbContextType);
                    if (dbContextObj != null)
                    {
                        // Close connection explicitly before disposing
                        // DbContext.Database.CloseConnection() via reflection
                        var databaseProp = dbContextObj.GetType().GetProperty("Database");
                        var database = databaseProp?.GetValue(dbContextObj);
                        if (database != null)
                        {
                            var closeMethod = database.GetType().GetMethod("CloseConnection", BindingFlags.Public | BindingFlags.Instance);
                            closeMethod?.Invoke(database, null);
                        }

                        (dbContextObj as IDisposable)?.Dispose();
                    }
                }

                // 4. Dispose ServiceProvider
                if (ServiceProvider is IDisposable disposableProvider)
                {
                    disposableProvider.Dispose();
                }

                LogDisposedSuccessfully(_logger);
            }
            catch (Exception ex)
            {
                LogDisposeError(_logger, ex);
            }
        }

        _disposed = true;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "FluxIndexContext initialized with semantic caching enabled")]
    private static partial void LogSemanticCachingEnabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "FluxIndexContext initialized with quality monitoring enabled")]
    private static partial void LogQualityMonitoringEnabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "FluxIndexClient initialized with hybrid search enabled")]
    private static partial void LogHybridSearchEnabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "FluxIndexClient initialized with Small-to-Big retrieval enabled")]
    private static partial void LogSmallToBigEnabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "FluxIndexClient initialized with adaptive search (DAT) enabled")]
    private static partial void LogAdaptiveSearchEnabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache hit for query: '{Query}' (similarity: {Similarity:F3})")]
    private static partial void LogCacheHit(ILogger logger, string query, float similarity);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache miss for query: '{Query}' - proceeding with retrieval")]
    private static partial void LogCacheMiss(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cached search results for query: '{Query}' ({Count} results)")]
    private static partial void LogCachedSearchResults(ILogger logger, string query, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Search completed: query='{Query}', results={Count}, duration={Duration}ms")]
    private static partial void LogSearchCompleted(ILogger logger, string query, int count, double duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "Search failed: query='{Query}', duration={Duration}ms")]
    private static partial void LogSearchFailed(ILogger logger, Exception exception, string query, double duration);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hybrid search completed: query='{Query}', results={Count}, duration={Duration}ms")]
    private static partial void LogHybridSearchCompleted(ILogger logger, string query, int count, double duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "Hybrid search failed: query='{Query}'")]
    private static partial void LogHybridSearchFailed(ILogger logger, Exception exception, string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "Adaptive search completed: query='{Query}', strategy={Strategy}, results={Count}, duration={Duration}ms")]
    private static partial void LogAdaptiveSearchCompleted(ILogger logger, string query, string strategy, int count, double duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "Adaptive search failed: query='{Query}'")]
    private static partial void LogAdaptiveSearchFailed(ILogger logger, Exception exception, string query);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Feedback ignored: AdaptiveSearchService is not configured")]
    private static partial void LogFeedbackIgnored(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Search feedback recorded for query: '{Query}'")]
    private static partial void LogSearchFeedbackRecorded(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get cache statistics")]
    private static partial void LogFailedToGetCacheStatistics(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cache service is not available for warmup")]
    private static partial void LogCacheNotAvailableForWarmup(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Cache warmup failed")]
    private static partial void LogCacheWarmupFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cache service is not available for optimization")]
    private static partial void LogCacheNotAvailableForOptimization(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache optimization completed successfully")]
    private static partial void LogCacheOptimizationCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Cache optimization failed")]
    private static partial void LogCacheOptimizationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Small-to-Big retriever is not configured")]
    private static partial void LogSmallToBigNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Small-to-Big search failed for query: {Query}")]
    private static partial void LogSmallToBigSearchFailed(ILogger logger, Exception exception, string query);

    [LoggerMessage(Level = LogLevel.Error, Message = "Query complexity analysis failed for: {Query}")]
    private static partial void LogQueryComplexityAnalysisFailed(ILogger logger, Exception exception, string query);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Quality monitoring is not enabled")]
    private static partial void LogQualityMonitoringNotEnabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Quality thresholds updated successfully")]
    private static partial void LogQualityThresholdsUpdated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to stop quality monitoring")]
    private static partial void LogFailedToStopQualityMonitoring(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "FluxIndexContext disposed successfully")]
    private static partial void LogDisposedSuccessfully(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error disposing FluxIndexContext")]
    private static partial void LogDisposeError(ILogger logger, Exception exception);

    #endregion
}

/// <summary>
/// FluxIndex 컨텍스트 인터페이스
/// </summary>
public interface IFluxIndexContext
{
    Retriever Retriever { get; }
    Indexer Indexer { get; }
    IServiceProvider ServiceProvider { get; }

    // Convenience methods
    Task<IEnumerable<SearchResult>> SearchAsync(string query, int maxResults = 10, float minScore = 0.5f, Dictionary<string, object>? filter = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<SearchResult>> HybridSearchAsync(string keyword, string query, int maxResults = 10, float vectorWeight = 0.7f, Dictionary<string, object>? filter = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HybridSearchResult>> HybridSearchV2Async(string query, FluxIndex.Core.Domain.Models.HybridSearchOptions? options = null, CancellationToken cancellationToken = default);
    Task<Document?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task<string> IndexAsync(Document document, CancellationToken cancellationToken = default);
    Task<string> IndexChunksAsync(IEnumerable<DocumentChunkModel> chunks, string? documentId = null, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task UpdateDocumentAsync(string documentId, Document updatedDocument, CancellationToken cancellationToken = default);
    Task<ClientStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    Task<int> GetDocumentCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetChunkCountAsync(CancellationToken cancellationToken = default);

    // Quantized Search APIs
    /// <summary>양자화 지원 여부</summary>
    bool SupportsQuantization { get; }
    /// <summary>양자화기 인스턴스</summary>
    IVectorQuantizer? Quantizer { get; }
    /// <summary>양자화 벡터를 사용한 빠른 근사 검색</summary>
    Task<IEnumerable<SearchResult>> SearchQuantizedAsync(string query, int maxResults = 10, float minScore = 0.0f, CancellationToken cancellationToken = default);
    /// <summary>양자화 후보 선택 + 원본 벡터 리랭킹 검색</summary>
    Task<IEnumerable<SearchResult>> SearchWithRerankAsync(string query, int maxResults = 10, int candidateMultiplier = 3, float minScore = 0.0f, CancellationToken cancellationToken = default);
    /// <summary>양자화 저장소 통계 조회</summary>
    Task<QuantizedStorageStats?> GetQuantizedStatsAsync(CancellationToken cancellationToken = default);

    // Quality Monitoring APIs
    Task<QualityDashboard?> GetQualityDashboardAsync(TimeSpan? timeWindow = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QualityAlert>?> GetQualityAlertsAsync(AlertSeverity? severity = null, TimeSpan? timeWindow = null, CancellationToken cancellationToken = default);
    Task<QualityTrends?> GetQualityTrendsAsync(TimeSpan? period = null, TimeSpan? granularity = null, CancellationToken cancellationToken = default);
    Task SetQualityThresholdsAsync(MonitoringThresholds thresholds, CancellationToken cancellationToken = default);
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
    public string? ResolvedStoreName { get; set; }
    public int? DetectedDimension { get; set; }
    public int DefaultChunkSize { get; set; }
    public int DefaultChunkOverlap { get; set; }
    public string EmbeddingModel { get; set; } = string.Empty;

    // 시맨틱 캐시 관련 속성
    public bool SemanticCacheEnabled { get; set; }
    public float CacheHitRate { get; set; }
    public double CacheResponseTime { get; set; }
    public long CachedItemsCount { get; set; }
    public double CacheMemoryUsageMB { get; set; }
}