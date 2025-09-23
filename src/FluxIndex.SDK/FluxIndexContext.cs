using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.ValueObjects;
using FluxIndex.Domain.Models;
using FluxIndex.Domain.Entities;
using MonitoringThresholds = FluxIndex.Core.Application.Interfaces.QualityThresholds;
using FluxIndex.SDK.Models;
using DocumentChunkEntity = FluxIndex.Domain.Entities.DocumentChunk;
using DocumentChunkModel = FluxIndex.Domain.Models.DocumentChunk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
public class FluxIndexContext : IFluxIndexContext
{
    private readonly Retriever _retriever;
    private readonly Indexer _indexer;
    private readonly ISemanticCacheService? _cacheService;
    private readonly IHybridSearchService? _hybridSearchService;
    private readonly ISmallToBigRetriever? _smallToBigRetriever;
    private readonly IQualityMonitoringService? _qualityMonitor;
    private readonly ILogger<FluxIndexContext> _logger;

    public FluxIndexContext(
        Retriever retriever,
        Indexer indexer,
        IServiceProvider serviceProvider,
        ILogger<FluxIndexContext>? logger = null,
        ISemanticCacheService? cacheService = null,
        IHybridSearchService? hybridSearchService = null,
        ISmallToBigRetriever? smallToBigRetriever = null,
        IQualityMonitoringService? qualityMonitor = null)
    {
        _retriever = retriever;
        _indexer = indexer;
        ServiceProvider = serviceProvider;
        _cacheService = cacheService;
        _hybridSearchService = hybridSearchService;
        _smallToBigRetriever = smallToBigRetriever;
        _qualityMonitor = qualityMonitor;
        _logger = logger ?? new NullLogger<FluxIndexContext>();

        if (_cacheService != null)
        {
            _logger.LogInformation("FluxIndexContext initialized with semantic caching enabled");
        }

        if (_qualityMonitor != null)
        {
            _logger.LogInformation("FluxIndexContext initialized with quality monitoring enabled");
            _ = _qualityMonitor.StartMonitoringAsync();
        }

        if (_hybridSearchService != null)
        {
            _logger.LogInformation("FluxIndexClient initialized with hybrid search enabled");
        }

        if (_smallToBigRetriever != null)
        {
            _logger.LogInformation("FluxIndexClient initialized with Small-to-Big retrieval enabled");
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
                    _logger.LogInformation("Cache hit for query: '{Query}' (similarity: {Similarity:F3})",
                        query, cachedResult.SimilarityScore);

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

                _logger.LogDebug("Cache miss for query: '{Query}' - proceeding with retrieval", query);
            }

            // 2. 실제 검색 수행
            var coreResults = await _retriever.SearchAsync(query, maxResults, minScore, filter, cancellationToken);
            var sdkResults = ConvertToSDKSearchResults(coreResults).ToList();

            // 3. 결과 캐싱 (검색 결과가 있는 경우만)
            if (_cacheService != null && sdkResults.Any())
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

                _logger.LogDebug("Cached search results for query: '{Query}' ({Count} results)", query, sdkResults.Count);
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

            _logger.LogInformation("Search completed: query='{Query}', results={Count}, duration={Duration}ms",
                query, sdkResults.Count, duration.TotalMilliseconds);

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

            _logger.LogError(ex, "Search failed: query='{Query}', duration={Duration}ms", query, duration.TotalMilliseconds);
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
        FluxIndex.Domain.Models.HybridSearchOptions? options = null,
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

            _logger.LogInformation("Hybrid search completed: query='{Query}', results={Count}, duration={Duration}ms",
                query, results.Count, duration.TotalMilliseconds);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hybrid search failed: query='{Query}'", query);
            throw;
        }
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
    public async Task<FluxIndex.Domain.ValueObjects.CacheStatistics?> GetCacheStatisticsAsync(CancellationToken cancellationToken = default)
    {
        if (_cacheService == null)
            return null;

        try
        {
            var stats = await _cacheService.GetCacheStatisticsAsync(cancellationToken);
            return new FluxIndex.Domain.ValueObjects.CacheStatistics
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
            await _cacheService.WarmupCacheAsync(commonQueries.ToList(), cancellationToken);
            return true;
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
            await _cacheService.CompactCacheAsync(cancellationToken);
            _logger.LogInformation("Cache optimization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache optimization failed");
        }
    }

    /// <summary>
    /// Convert VectorSearchResult to SDK SearchResult
    /// </summary>
    private IEnumerable<SearchResult> ConvertToSDKSearchResults(IEnumerable<VectorSearchResult> vectorResults)
    {
        return vectorResults.Select(vr => new SearchResult
        {
            Id = vr.DocumentChunk.Id,
            DocumentId = vr.DocumentChunk.DocumentId,
            Content = vr.DocumentChunk.Content,
            Score = (float)vr.Score,
            ChunkIndex = vr.DocumentChunk.ChunkIndex
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
            _logger.LogWarning("Small-to-Big retriever is not configured");
            return Enumerable.Empty<SmallToBigSearchResult>();
        }

        try
        {
            var coreOptions = options?.ToCoreOptions() ?? new Domain.Models.SmallToBigOptions();
            var results = await _smallToBigRetriever.SearchAsync(query, coreOptions, cancellationToken);

            return results.Select(r => new SmallToBigSearchResult
            {
                PrimaryChunk = r.PrimaryChunk,
                ContextChunks = r.ContextChunks.ToList(),
                RelevanceScore = r.RelevanceScore,
                WindowSize = r.WindowSize,
                ExpansionReason = r.ExpansionReason,
                ContextQuality = r.ContextQuality,
                CombinedText = r.CombinedText
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Small-to-Big search failed for query: {Query}", query);
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
            _logger.LogWarning("Small-to-Big retriever is not configured");
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
            _logger.LogError(ex, "Query complexity analysis failed for: {Query}", query);
            return null;
        }
    }

    /// <summary>
    /// Convert cached SearchResult to SDK SearchResult
    /// </summary>
    private IEnumerable<SearchResult> ConvertCachedToSDKSearchResults(IReadOnlyList<DocumentChunkModel> cachedResults)
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
    private DocumentChunkModel ConvertToSDKChunk(DocumentChunkEntity coreChunk)
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

    private DocumentChunkEntity ConvertToEntityChunk(DocumentChunkModel modelChunk)
    {
        return DocumentChunkEntity.Create(
            modelChunk.DocumentId,
            modelChunk.Content,
            modelChunk.ChunkIndex,
            modelChunk.TotalChunks
        );
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
            _logger.LogWarning("Quality monitoring is not enabled");
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
            _logger.LogWarning("Quality monitoring is not enabled");
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
            _logger.LogWarning("Quality monitoring is not enabled");
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
            _logger.LogWarning("Quality monitoring is not enabled");
            return;
        }

        await _qualityMonitor.SetQualityThresholdsAsync(thresholds, cancellationToken);
        _logger.LogInformation("Quality thresholds updated successfully");
    }

    /// <summary>
    /// SDK HybridSearchOptions를 Core HybridSearchOptions로 변환
    /// </summary>
    private Domain.Models.HybridSearchOptions ConvertToCore(HybridSearchOptions sdkOptions)
    {
        return new Domain.Models.HybridSearchOptions
        {
            MaxResults = sdkOptions.TopK,
            VectorWeight = (double)sdkOptions.VectorWeight,
            SparseWeight = (double)sdkOptions.KeywordWeight,
            FusionMethod = sdkOptions.RerankingStrategy switch
            {
                RerankingStrategy.WeightedAverage => Domain.Models.FusionMethod.WeightedSum,
                RerankingStrategy.ReciprocalRankFusion => Domain.Models.FusionMethod.RRF,
                _ => Domain.Models.FusionMethod.RRF
            }
        };
    }
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
    Task<IReadOnlyList<HybridSearchResult>> HybridSearchV2Async(string query, FluxIndex.Domain.Models.HybridSearchOptions? options = null, CancellationToken cancellationToken = default);
    Task<Document?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task<string> IndexAsync(Document document, CancellationToken cancellationToken = default);
    Task<string> IndexChunksAsync(IEnumerable<DocumentChunkModel> chunks, string? documentId = null, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task UpdateDocumentAsync(string documentId, Document updatedDocument, CancellationToken cancellationToken = default);
    Task<ClientStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    Task<int> GetDocumentCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetChunkCountAsync(CancellationToken cancellationToken = default);

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