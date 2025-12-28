using System.Diagnostics;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Shared.DTOs.Search;
using Microsoft.Extensions.Logging;

// Type aliases
using StackIDocumentRepository = FluxIndex.Stack.Application.Interfaces.Repositories.IDocumentRepository;
using IReranker = FluxIndex.Core.Application.Interfaces.IReranker;
using RetrievalCandidate = FluxIndex.Core.Application.Interfaces.RetrievalCandidate;
using RerankOptions = FluxIndex.Core.Application.Interfaces.RerankOptions;
using ISemanticCacheService = FluxIndex.Core.Application.Interfaces.ISemanticCacheService;
using CachedSearchResult = FluxIndex.Core.Application.Interfaces.CachedSearchResult;
using SearchMetadata = FluxIndex.Core.Application.Interfaces.SearchMetadata;
using CoreSearchStrategy = FluxIndex.Core.Application.Interfaces.SearchStrategy;
using CoreAdaptiveSearchOptions = FluxIndex.Core.Application.Interfaces.AdaptiveSearchOptions;
using CoreAdaptiveSearchResult = FluxIndex.Core.Application.Interfaces.AdaptiveSearchResult;
using IQueryTransformationService = FluxIndex.Core.Application.Interfaces.IQueryTransformationService;
using HyDEOptions = FluxIndex.Core.Domain.Models.HyDEOptions;

namespace FluxIndex.Stack.Application.Services;

/// <summary>
/// Unified search service - thin wrapper around Core's search services.
///
/// This service delegates core search algorithms to FluxIndex.Core:
/// - Query analysis → IQueryComplexityAnalyzer
/// - Strategy selection → IAdaptiveSearchService
/// - Hybrid search → IHybridSearchService
/// - Dynamic fusion → IDynamicFusionService
/// - Reranking → IReranker, IListwiseReranker
///
/// Stack-specific responsibilities:
/// - DTO conversion (SearchRequest ↔ Core options)
/// - Stack entity access (Document, DocumentChunk)
/// - Search history recording
/// - Optional backend integration (Qdrant, Neo4j)
/// </summary>
public class SearchService : ISearchService
{
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly StackIDocumentRepository _documentRepository;
    private readonly ISearchHistoryRepository _searchHistoryRepository;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<SearchService> _logger;

    // Core search services (primary delegation targets)
    private readonly IAdaptiveSearchService? _adaptiveSearchService;
    private readonly IHybridSearchService? _hybridSearchService;
    private readonly IQueryComplexityAnalyzer? _queryAnalyzer;
    private readonly IDynamicFusionService? _fusionService;
    private readonly IReranker? _reranker;
    private readonly ISemanticCacheService? _semanticCache;
    private readonly IRankFusionService? _rankFusionService;

    // Optional Stack-specific backends
    private readonly IQdrantSearchService? _qdrantService;
    private readonly INeo4jGraphService? _neo4jService;
    private readonly IAdvancedEntityExtractionService? _entityService;

    // Query transformation service for HyDE (Hypothetical Document Embeddings)
    private readonly IQueryTransformationService? _queryTransformationService;

    public SearchService(
        IDocumentChunkRepository chunkRepository,
        StackIDocumentRepository documentRepository,
        ISearchHistoryRepository searchHistoryRepository,
        IEmbeddingProvider embeddingProvider,
        ILogger<SearchService> logger,
        IAdaptiveSearchService? adaptiveSearchService = null,
        IHybridSearchService? hybridSearchService = null,
        IQueryComplexityAnalyzer? queryAnalyzer = null,
        IDynamicFusionService? fusionService = null,
        IReranker? reranker = null,
        ISemanticCacheService? semanticCache = null,
        IRankFusionService? rankFusionService = null,
        IQdrantSearchService? qdrantService = null,
        INeo4jGraphService? neo4jService = null,
        IAdvancedEntityExtractionService? entityService = null,
        IQueryTransformationService? queryTransformationService = null)
    {
        _chunkRepository = chunkRepository;
        _documentRepository = documentRepository;
        _searchHistoryRepository = searchHistoryRepository;
        _embeddingProvider = embeddingProvider;
        _logger = logger;
        _adaptiveSearchService = adaptiveSearchService;
        _hybridSearchService = hybridSearchService;
        _queryAnalyzer = queryAnalyzer;
        _fusionService = fusionService;
        _reranker = reranker;
        _semanticCache = semanticCache;
        _rankFusionService = rankFusionService;
        _qdrantService = qdrantService;
        _neo4jService = neo4jService;
        _entityService = entityService;
        _queryTransformationService = queryTransformationService;
    }

    public async Task<SearchResponse> SearchAsync(
        SearchRequest request,
        string? apiKeyPrefix = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Search request: {Query}, Mode: {Mode}, Preference: {Preference}",
            request.Query, request.Mode, request.QualityPreference);

        // Route to appropriate search method
        var response = request.Mode == SearchMode.Auto
            ? await AutoSearchAsync(request, stopwatch, cancellationToken)
            : await ManualSearchAsync(request, stopwatch, cancellationToken);

        // Record search history (Stack-specific)
        await RecordSearchHistoryAsync(request, response, apiKeyPrefix, cancellationToken);

        return response;
    }

    #region Auto Mode - Delegates to Core's AdaptiveSearchService

    private async Task<SearchResponse> AutoSearchAsync(
        SearchRequest request,
        Stopwatch totalStopwatch,
        CancellationToken cancellationToken)
    {
        // If Core's AdaptiveSearchService is available, delegate to it
        if (_adaptiveSearchService != null)
        {
            return await DelegateToAdaptiveSearchAsync(request, totalStopwatch, cancellationToken);
        }

        // Fallback: use HybridSearchService directly
        if (_hybridSearchService != null)
        {
            return await DelegateToHybridSearchAsync(request, totalStopwatch, cancellationToken);
        }

        // Final fallback: basic Stack-level search
        return await FallbackSearchAsync(request, totalStopwatch, cancellationToken);
    }

    /// <summary>
    /// Delegates search to Core's AdaptiveSearchService (recommended path).
    /// Core handles: query analysis, strategy selection, fusion, caching.
    /// Stack handles: DTO conversion, entity enrichment, HyDE expansion.
    /// </summary>
    private async Task<SearchResponse> DelegateToAdaptiveSearchAsync(
        SearchRequest request,
        Stopwatch totalStopwatch,
        CancellationToken cancellationToken)
    {
        // 0. Apply Multi-Hypothetical HyDE if enabled
        var searchQuery = request.Query;
        string[]? hydeDocuments = null;

        if (request.EnableHyDE && _queryTransformationService != null)
        {
            try
            {
                var hydeOptions = HyDEOptions.CreateMultiHypothetical(request.HyDEDocumentCount);
                var hydeResult = await _queryTransformationService.GenerateHypotheticalDocumentAsync(
                    request.Query, hydeOptions, cancellationToken);

                if (hydeResult.IsSuccessful && hydeResult.HypotheticalDocuments.Count > 0)
                {
                    hydeDocuments = hydeResult.HypotheticalDocuments.ToArray();
                    _logger.LogInformation("Generated {Count} hypothetical documents for HyDE",
                        hydeDocuments.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HyDE generation failed, falling back to original query");
            }
        }

        // 1. Convert Stack options to Core options
        var coreOptions = MapToAdaptiveSearchOptions(request);

        // 2. Delegate to Core's AdaptiveSearchService
        // If HyDE generated documents, search with each and merge results
        CoreAdaptiveSearchResult coreResult;
        if (hydeDocuments != null && hydeDocuments.Length > 0)
        {
            coreResult = await SearchWithHyDEDocumentsAsync(
                request.Query, hydeDocuments, coreOptions, cancellationToken);
        }
        else
        {
            coreResult = await _adaptiveSearchService!.SearchAsync(
                searchQuery, coreOptions, cancellationToken);
        }

        // 3. Enrich with Stack-specific data (entity extraction, graph expansion)
        var enrichedResults = await EnrichCoreResultsAsync(
            coreResult, request, cancellationToken);

        // 4. Convert Core result to Stack DTO
        totalStopwatch.Stop();
        return MapToSearchResponse(coreResult, enrichedResults, request, totalStopwatch.Elapsed);
    }

    /// <summary>
    /// Executes search with multiple HyDE-generated hypothetical documents.
    /// Merges results using reciprocal rank fusion for robust retrieval.
    /// </summary>
    private async Task<CoreAdaptiveSearchResult> SearchWithHyDEDocumentsAsync(
        string originalQuery,
        string[] hydeDocuments,
        CoreAdaptiveSearchOptions options,
        CancellationToken cancellationToken)
    {
        var allResults = new List<CoreAdaptiveSearchResult>();

        // Search with original query
        var originalResult = await _adaptiveSearchService!.SearchAsync(
            originalQuery, options, cancellationToken);
        allResults.Add(originalResult);

        // Search with each HyDE document (in parallel for performance)
        var hydeTasks = hydeDocuments.Select(async doc =>
        {
            try
            {
                return await _adaptiveSearchService.SearchAsync(doc, options, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HyDE document search failed");
                return null;
            }
        });

        var hydeResults = await Task.WhenAll(hydeTasks);
        allResults.AddRange(hydeResults.Where(r => r != null)!);

        // Merge results using Reciprocal Rank Fusion
        return MergeHyDEResults(originalResult, allResults);
    }

    /// <summary>
    /// Merges multiple search results using Reciprocal Rank Fusion (RRF).
    /// RRF provides robust result merging without score normalization.
    /// </summary>
    private CoreAdaptiveSearchResult MergeHyDEResults(
        CoreAdaptiveSearchResult original,
        List<CoreAdaptiveSearchResult> allResults)
    {
        const int k = 60; // RRF constant
        var rrfScores = new Dictionary<string, double>();
        var docMap = new Dictionary<string, FluxIndex.Core.Domain.Entities.Document>();

        foreach (var result in allResults)
        {
            var rank = 1;
            foreach (var doc in result.Documents)
            {
                var key = doc.Id.ToString();
                if (!rrfScores.ContainsKey(key))
                {
                    rrfScores[key] = 0;
                    docMap[key] = doc;
                }
                rrfScores[key] += 1.0 / (k + rank);
                rank++;
            }
        }

        // Sort by RRF score and return top results
        var mergedDocs = rrfScores
            .OrderByDescending(kv => kv.Value)
            .Take(original.Documents.Count() > 0 ? original.Documents.Count() * 2 : 20)
            .Select(kv => docMap[kv.Key])
            .ToList();

        return new CoreAdaptiveSearchResult
        {
            Documents = mergedDocs,
            UsedStrategy = original.UsedStrategy,
            Performance = original.Performance,
            QueryAnalysis = original.QueryAnalysis,
            StrategyReasons = new List<string>(original.StrategyReasons)
            {
                $"Multi-HyDE with {allResults.Count} searches (RRF merged)"
            },
            ConfidenceScore = original.ConfidenceScore
        };
    }

    /// <summary>
    /// Delegates search to Core's HybridSearchService.
    /// </summary>
    private async Task<SearchResponse> DelegateToHybridSearchAsync(
        SearchRequest request,
        Stopwatch totalStopwatch,
        CancellationToken cancellationToken)
    {
        // 1. Convert to Core's HybridSearchOptions
        var hybridOptions = new HybridSearchOptions
        {
            MaxResults = request.TopK * 2, // Over-fetch for filtering
            MinFusedScore = request.MinScore,
            EnableDynamicAlphaTuning = request.QualityPreference != QualityPreference.Speed,
            EnableAutoStrategy = true
        };

        // 2. Apply Dynamic Alpha if available
        if (_fusionService != null)
        {
            var fusionConfig = await _fusionService.CalculateDynamicWeightsAsync(
                request.Query, cancellationToken);
            hybridOptions.VectorWeight = fusionConfig.VectorWeight;
            hybridOptions.SparseWeight = fusionConfig.SparseWeight;
            hybridOptions.FusionMethod = fusionConfig.RecommendedFusion;
        }

        // 3. Execute Core hybrid search
        var coreResults = await _hybridSearchService!.SearchAsync(
            request.Query, hybridOptions, cancellationToken);

        // 4. Apply reranking if requested
        var rerankedResults = coreResults.ToList();
        if (request.QualityPreference != QualityPreference.Speed && _reranker != null)
        {
            rerankedResults = await ApplyRerankingAsync(
                request.Query, coreResults, request.TopK, cancellationToken);
        }

        // 5. Convert to Stack DTOs
        totalStopwatch.Stop();
        var results = rerankedResults
            .Take(request.TopK)
            .Select((r, idx) => MapHybridResultToDto(r, request, idx))
            .ToList();

        return new SearchResponse
        {
            Query = request.Query,
            Results = results,
            TotalResults = results.Count,
            ExecutionTimeMs = totalStopwatch.Elapsed.TotalMilliseconds,
            Mode = SearchMode.Auto,
            FromCache = false,
            Strategy = new SearchStrategyInfo
            {
                PrimaryStrategy = "Hybrid",
                FusionMethod = hybridOptions.FusionMethod.ToString(),
                DynamicAlpha = hybridOptions.VectorWeight,
                BackendsUsed = new List<string> { "PostgreSQL" }
            },
            Quality = CalculateQuality(results)
        };
    }

    #endregion

    #region Manual Modes - Vector, Keyword, Hybrid

    private async Task<SearchResponse> ManualSearchAsync(
        SearchRequest request,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResultDto>();

        try
        {
            var (documents, _) = await _documentRepository.GetPagedAsync(
                1, 1000, request.CollectionId, DocumentStatus.Indexed, cancellationToken);

            if (!documents.Any())
            {
                return CreateEmptyResponse(request, stopwatch);
            }

            var documentIds = documents.Select(d => d.Id).ToList();
            var docLookup = documents.ToDictionary(d => d.Id);

            var allChunks = new List<DocumentChunk>();
            foreach (var docId in documentIds)
            {
                var chunks = await _chunkRepository.GetByDocumentIdAsync(docId, cancellationToken);
                allChunks.AddRange(chunks);
            }

            results = request.Mode switch
            {
                SearchMode.Vector => await ExecuteVectorSearchAsync(request, documentIds, docLookup, cancellationToken),
                SearchMode.Keyword => ExecuteKeywordSearch(request, allChunks, docLookup),
                SearchMode.Hybrid => await ExecuteHybridSearchAsync(request, allChunks, documentIds, docLookup, cancellationToken),
                _ => ExecuteKeywordSearch(request, allChunks, docLookup)
            };

            // Apply filters
            if (request.Filters != null && request.Filters.Count > 0)
            {
                results = ApplyFilters(results, request.Filters);
            }

            if (request.MinScore > 0)
            {
                results = results.Where(r => r.Score >= request.MinScore).ToList();
            }

            // Apply reranking if enabled
            if (request.EnableReranking && _reranker != null && results.Count > 0)
            {
                results = await ApplyStackRerankingAsync(request.Query, results, request.TopK, cancellationToken);
            }

            results = results.Take(request.TopK).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual search for query: {Query}", request.Query);
            throw;
        }

        stopwatch.Stop();

        return new SearchResponse
        {
            Query = request.Query,
            Results = results,
            TotalResults = results.Count,
            ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds,
            Mode = request.Mode
        };
    }

    private async Task<List<SearchResultDto>> ExecuteVectorSearchAsync(
        SearchRequest request,
        List<Guid> documentIds,
        Dictionary<Guid, Document> docLookup,
        CancellationToken cancellationToken)
    {
        var queryEmbedding = await _embeddingProvider.GetEmbeddingAsync(request.Query, cancellationToken);

        var vectorResults = await _chunkRepository.SearchByVectorAsync(
            queryEmbedding,
            limit: request.TopK * 2,
            documentIds: documentIds,
            minScore: request.MinScore,
            cancellationToken: cancellationToken);

        return vectorResults
            .Select(r => new SearchResultDto
            {
                ChunkId = r.Chunk.Id,
                DocumentId = r.Chunk.DocumentId,
                DocumentTitle = docLookup.TryGetValue(r.Chunk.DocumentId, out var doc) ? doc.Title : "Unknown",
                ChunkIndex = r.Chunk.ChunkIndex,
                Content = request.IncludeContent ? r.Chunk.Content : null,
                Score = r.Score,
                Confidence = r.Score > 0.8 ? "High" : r.Score > 0.5 ? "Medium" : "Low",
                VectorScore = r.Score,
                Metadata = request.IncludeMetadata ? r.Chunk.Metadata : null,
                Highlights = ExtractHighlights(r.Chunk.Content, request.Query)
            })
            .ToList();
    }

    private List<SearchResultDto> ExecuteKeywordSearch(
        SearchRequest request,
        List<DocumentChunk> chunks,
        Dictionary<Guid, Document> docLookup)
    {
        var queryTerms = request.Query.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (queryTerms.Length == 0) return new List<SearchResultDto>();

        var results = new List<(DocumentChunk Chunk, double Score, List<string> Highlights)>();

        foreach (var chunk in chunks)
        {
            var content = chunk.Content.ToLowerInvariant();
            var matchCount = queryTerms.Count(term => content.Contains(term));

            if (matchCount > 0)
            {
                var score = (double)matchCount / queryTerms.Length;
                var highlights = ExtractHighlights(chunk.Content, request.Query);
                results.Add((chunk, score, highlights));
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(request.TopK * 2)
            .Select(r => new SearchResultDto
            {
                ChunkId = r.Chunk.Id,
                DocumentId = r.Chunk.DocumentId,
                DocumentTitle = docLookup.TryGetValue(r.Chunk.DocumentId, out var doc) ? doc.Title : "Unknown",
                ChunkIndex = r.Chunk.ChunkIndex,
                Content = request.IncludeContent ? r.Chunk.Content : null,
                Score = r.Score,
                Confidence = r.Score > 0.7 ? "High" : r.Score > 0.4 ? "Medium" : "Low",
                KeywordScore = r.Score,
                Metadata = request.IncludeMetadata ? r.Chunk.Metadata : null,
                Highlights = r.Highlights
            })
            .ToList();
    }

    private async Task<List<SearchResultDto>> ExecuteHybridSearchAsync(
        SearchRequest request,
        List<DocumentChunk> allChunks,
        List<Guid> documentIds,
        Dictionary<Guid, Document> docLookup,
        CancellationToken cancellationToken)
    {
        // Use Core's HybridSearchService if available
        if (_hybridSearchService != null)
        {
            var hybridOptions = new HybridSearchOptions
            {
                MaxResults = request.TopK * 2,
                MinFusedScore = request.MinScore
            };

            var coreResults = await _hybridSearchService.SearchAsync(
                request.Query, hybridOptions, cancellationToken);

            return coreResults
                .Take(request.TopK)
                .Select((r, idx) => MapHybridResultToDto(r, request, idx))
                .ToList();
        }

        // Fallback: simple RRF fusion
        var keywordResults = ExecuteKeywordSearch(request, allChunks, docLookup);
        var vectorResults = await ExecuteVectorSearchAsync(request, documentIds, docLookup, cancellationToken);
        return MergeWithRRF(keywordResults, vectorResults);
    }

    #endregion

    #region Fallback Search (when Core services unavailable)

    private async Task<SearchResponse> FallbackSearchAsync(
        SearchRequest request,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("Core search services unavailable, using fallback search");

        var (documents, _) = await _documentRepository.GetPagedAsync(
            1, 1000, request.CollectionId, DocumentStatus.Indexed, cancellationToken);

        if (!documents.Any())
        {
            return CreateEmptyResponse(request, stopwatch);
        }

        var documentIds = documents.Select(d => d.Id).ToList();
        var docLookup = documents.ToDictionary(d => d.Id);

        var allChunks = new List<DocumentChunk>();
        foreach (var docId in documentIds)
        {
            var chunks = await _chunkRepository.GetByDocumentIdAsync(docId, cancellationToken);
            allChunks.AddRange(chunks);
        }

        var results = await ExecuteHybridSearchAsync(request, allChunks, documentIds, docLookup, cancellationToken);

        if (request.MinScore > 0)
        {
            results = results.Where(r => r.Score >= request.MinScore).ToList();
        }

        results = results.Take(request.TopK).ToList();

        stopwatch.Stop();
        return new SearchResponse
        {
            Query = request.Query,
            Results = results,
            TotalResults = results.Count,
            ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds,
            Mode = SearchMode.Auto,
            FromCache = false,
            Strategy = new SearchStrategyInfo
            {
                PrimaryStrategy = "Hybrid",
                FusionMethod = "RRF",
                BackendsUsed = new List<string> { "PostgreSQL" }
            },
            Quality = CalculateQuality(results)
        };
    }

    #endregion

    #region DTO Mapping

    private static CoreAdaptiveSearchOptions MapToAdaptiveSearchOptions(SearchRequest request)
    {
        var strategy = request.QualityPreference switch
        {
            QualityPreference.Speed => CoreSearchStrategy.DirectVector,
            QualityPreference.Quality => CoreSearchStrategy.Adaptive,
            _ => CoreSearchStrategy.Hybrid
        };

        return new CoreAdaptiveSearchOptions
        {
            MaxResults = request.TopK * 2, // Over-fetch for filtering
            MinScore = request.MinScore,
            UseCache = true,
            ForceStrategy = request.Mode != SearchMode.Auto ? strategy : null
        };
    }

    private SearchResponse MapToSearchResponse(
        CoreAdaptiveSearchResult coreResult,
        List<SearchResultDto> enrichedResults,
        SearchRequest request,
        TimeSpan elapsed)
    {
        var strategyInfo = new SearchStrategyInfo
        {
            PrimaryStrategy = coreResult.UsedStrategy.ToString(),
            FusionMethod = "DynamicAlpha",
            BackendsUsed = new List<string> { "PostgreSQL" }
        };

        if (_qdrantService?.IsAvailable == true)
            strategyInfo.BackendsUsed.Add("Qdrant");
        if (_neo4jService?.IsAvailable == true)
            strategyInfo.BackendsUsed.Add("Neo4j");
        if (_semanticCache != null)
            strategyInfo.BackendsUsed.Add("Redis");

        var quality = new SearchQualityInfo
        {
            EstimatedQuality = coreResult.ConfidenceScore,
            QualityTier = coreResult.ConfidenceScore > 0.8 ? "High" :
                         coreResult.ConfidenceScore > 0.5 ? "Medium" : "Low",
            QualityFactors = coreResult.StrategyReasons
        };

        return new SearchResponse
        {
            Query = request.Query,
            Results = enrichedResults.Take(request.TopK).ToList(),
            TotalResults = enrichedResults.Count,
            ExecutionTimeMs = elapsed.TotalMilliseconds,
            Mode = SearchMode.Auto,
            FromCache = coreResult.Performance.CacheHit,
            Strategy = strategyInfo,
            Quality = quality,
            Explanation = request.IncludeExplanation ? new SearchExplanation
            {
                QueryAnalysis = new QueryAnalysisDto
                {
                    QueryType = coreResult.QueryAnalysis.Type.ToString(),
                    ComplexityLevel = coreResult.QueryAnalysis.Complexity.ToString(),
                    Keywords = coreResult.QueryAnalysis.Keywords.ToList(),
                    Entities = coreResult.QueryAnalysis.Entities.ToList(),
                    ContainsTechnicalTerms = coreResult.QueryAnalysis.ContainsTechnicalTerms
                },
                StrategyReason = string.Join(". ", coreResult.StrategyReasons),
                PerformanceBreakdown = new Dictionary<string, double>
                {
                    ["AnalysisMs"] = coreResult.Performance.AnalysisTime.TotalMilliseconds,
                    ["SearchMs"] = coreResult.Performance.SearchTime.TotalMilliseconds,
                    ["PostProcessMs"] = coreResult.Performance.PostProcessingTime.TotalMilliseconds
                }
            } : null
        };
    }

    private SearchResultDto MapHybridResultToDto(HybridSearchResult result, SearchRequest request, int rank)
    {
        return new SearchResultDto
        {
            ChunkId = Guid.TryParse(result.Chunk.Id, out var cid) ? cid : Guid.Empty,
            DocumentId = Guid.TryParse(result.Chunk.DocumentId, out var did) ? did : Guid.Empty,
            DocumentTitle = result.Chunk.Metadata?.GetValueOrDefault("title")?.ToString() ?? "Unknown",
            ChunkIndex = result.Chunk.ChunkIndex,
            Content = request.IncludeContent ? result.Chunk.Content : null,
            Score = result.FusedScore,
            Confidence = result.Confidence > 0.8 ? "High" : result.Confidence > 0.5 ? "Medium" : "Low",
            VectorScore = result.VectorScore,
            KeywordScore = result.SparseScore,
            Metadata = request.IncludeMetadata ? result.Chunk.Metadata : null,
            Highlights = result.MatchedTerms.Take(3).ToList()
        };
    }

    #endregion

    #region Enrichment (Stack-specific)

    private async Task<List<SearchResultDto>> EnrichCoreResultsAsync(
        CoreAdaptiveSearchResult coreResult,
        SearchRequest request,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResultDto>();

        foreach (var doc in coreResult.Documents)
        {
            var chunkId = doc.Metadata.TryGetValue("chunk_id", out var cid) ? cid?.ToString() : doc.Id;
            var content = doc.Metadata.TryGetValue("chunk_content", out var cc) ? cc?.ToString() : "";
            var score = doc.Metadata.TryGetValue("relevance_score", out var rs) ? Convert.ToDouble(rs) : 0.5;

            var result = new SearchResultDto
            {
                ChunkId = Guid.TryParse(chunkId, out var parsedChunkId) ? parsedChunkId : Guid.Empty,
                DocumentId = Guid.TryParse(doc.Id, out var parsedDocId) ? parsedDocId : Guid.Empty,
                DocumentTitle = doc.Metadata.TryGetValue("title", out var title) ? title?.ToString() ?? "Unknown" : "Unknown",
                Content = request.IncludeContent ? content : null,
                Score = score,
                Confidence = score > 0.8 ? "High" : score > 0.5 ? "Medium" : "Low",
                Metadata = request.IncludeMetadata ? doc.Metadata : null,
                Highlights = ExtractHighlights(content ?? "", request.Query)
            };

            results.Add(result);
        }

        // Entity enrichment for Quality preference
        if (request.QualityPreference == QualityPreference.Quality && _entityService != null)
        {
            results = await EnrichWithEntitiesAsync(results.Take(5).ToList(), cancellationToken);
        }

        return results;
    }

    private async Task<List<SearchResultDto>> EnrichWithEntitiesAsync(
        List<SearchResultDto> results,
        CancellationToken cancellationToken)
    {
        try
        {
            var enrichedResults = new List<SearchResultDto>();
            foreach (var result in results)
            {
                var entities = await _entityService!.ExtractEntitiesAsync(
                    result.Content ?? string.Empty, null, cancellationToken);
                enrichedResults.Add(result with
                {
                    RelatedEntities = entities.Take(5).Select(e => e.Text).ToList()
                });
            }
            return enrichedResults;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Entity extraction failed");
            return results;
        }
    }

    #endregion

    #region Reranking

    private async Task<List<HybridSearchResult>> ApplyRerankingAsync(
        string query,
        IReadOnlyList<HybridSearchResult> results,
        int topK,
        CancellationToken cancellationToken)
    {
        var candidates = results.Select((r, idx) => new RetrievalCandidate
        {
            Id = r.Chunk.Id,
            ChunkId = r.Chunk.Id,
            Content = r.Chunk.Content,
            InitialScore = (float)r.FusedScore,
            InitialRank = idx + 1
        }).ToList();

        var rerankOptions = new RerankOptions
        {
            TopN = topK,
            IncludeExplanation = false,
            MaxContentLength = 512
        };

        var reranked = await _reranker!.RerankAsync(query, candidates, rerankOptions, cancellationToken);
        var resultLookup = results.ToDictionary(r => r.Chunk.Id);

        return reranked
            .Where(rr => resultLookup.ContainsKey(rr.ChunkId))
            .Select(rr =>
            {
                var original = resultLookup[rr.ChunkId];
                return original with
                {
                    FusedScore = rr.RerankScore,
                    Confidence = rr.RerankScore > 0.8 ? 0.9 : rr.RerankScore > 0.5 ? 0.7 : 0.4
                };
            })
            .ToList();
    }

    private async Task<List<SearchResultDto>> ApplyStackRerankingAsync(
        string query,
        List<SearchResultDto> results,
        int topK,
        CancellationToken cancellationToken)
    {
        var candidates = results.Select((r, idx) => new RetrievalCandidate
        {
            Id = r.ChunkId.ToString(),
            ChunkId = r.ChunkId.ToString(),
            Content = r.Content ?? string.Empty,
            InitialScore = (float)r.Score,
            InitialRank = idx + 1
        }).ToList();

        var rerankOptions = new RerankOptions { TopN = topK };
        var reranked = await _reranker!.RerankAsync(query, candidates, rerankOptions, cancellationToken);
        var resultLookup = results.ToDictionary(r => r.ChunkId.ToString());

        return reranked
            .Where(rr => resultLookup.ContainsKey(rr.ChunkId))
            .Select(rr =>
            {
                var original = resultLookup[rr.ChunkId];
                return original with
                {
                    Score = rr.RerankScore,
                    RerankScore = rr.RerankScore,
                    Confidence = rr.RerankScore > 0.8 ? "High" : rr.RerankScore > 0.5 ? "Medium" : "Low"
                };
            })
            .ToList();
    }

    #endregion

    #region Caching Interface

    public async Task<SemanticCacheEntryDto?> GetCachedResponseAsync(
        string query,
        double similarityThreshold = 0.95,
        CancellationToken cancellationToken = default)
    {
        if (_semanticCache == null) return null;

        try
        {
            var cached = await _semanticCache.GetCachedResultAsync(
                query, (float)similarityThreshold, cancellationToken);

            if (cached != null)
            {
                return new SemanticCacheEntryDto
                {
                    Query = query,
                    Response = System.Text.Json.JsonSerializer.Serialize(cached.Results),
                    Similarity = cached.SimilarityScore,
                    CachedAt = cached.CachedAt
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache lookup failed");
        }

        return null;
    }

    public async Task CacheResponseAsync(string query, string response, CancellationToken cancellationToken = default)
    {
        if (_semanticCache == null) return;

        try
        {
            var chunks = new List<CacheDocumentChunk>
            {
                CacheDocumentChunk.Create("response", response, 0)
            };

            await _semanticCache.SetCachedResultAsync(
                query, chunks,
                metadata: new SearchMetadata { SearchAlgorithm = "semantic_search" },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache response");
        }
    }

    public async Task ClearCacheAsync(Guid? collectionId = null, CancellationToken cancellationToken = default)
    {
        if (_semanticCache == null) return;

        try
        {
            var pattern = collectionId.HasValue ? $"*{collectionId}*" : "*";
            await _semanticCache.InvalidateCacheAsync(pattern, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear cache");
        }
    }

    #endregion

    #region Helpers

    private static List<SearchResultDto> MergeWithRRF(
        List<SearchResultDto> keywordResults,
        List<SearchResultDto> vectorResults,
        int k = 60)
    {
        var merged = new Dictionary<Guid, SearchResultDto>();
        var scores = new Dictionary<Guid, double>();

        for (int i = 0; i < keywordResults.Count; i++)
        {
            var result = keywordResults[i];
            scores[result.ChunkId] = 1.0 / (k + i + 1);
            merged[result.ChunkId] = result with { KeywordScore = result.Score };
        }

        for (int i = 0; i < vectorResults.Count; i++)
        {
            var result = vectorResults[i];
            var rrfScore = 1.0 / (k + i + 1);

            if (scores.TryGetValue(result.ChunkId, out var existing))
            {
                scores[result.ChunkId] = existing + rrfScore;
                merged[result.ChunkId] = merged[result.ChunkId] with { VectorScore = result.Score };
            }
            else
            {
                scores[result.ChunkId] = rrfScore;
                merged[result.ChunkId] = result with { VectorScore = result.Score };
            }
        }

        return merged.Values
            .Select(r => r with
            {
                Score = scores[r.ChunkId],
                Confidence = scores[r.ChunkId] > 0.03 ? "High" : scores[r.ChunkId] > 0.015 ? "Medium" : "Low"
            })
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    private static List<SearchResultDto> ApplyFilters(List<SearchResultDto> results, Dictionary<string, object> filters)
    {
        return results.Where(r =>
        {
            if (r.Metadata == null) return false;
            return filters.All(f => r.Metadata.TryGetValue(f.Key, out var value) && value?.Equals(f.Value) == true);
        }).ToList();
    }

    private static List<string> ExtractHighlights(string content, string query, int contextSize = 50)
    {
        var highlights = new List<string>();
        var queryTerms = query.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var contentLower = content.ToLowerInvariant();

        foreach (var term in queryTerms.Take(3))
        {
            var idx = contentLower.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var start = Math.Max(0, idx - contextSize);
                var end = Math.Min(content.Length, idx + term.Length + contextSize);
                highlights.Add(content.Substring(start, end - start));
            }
        }

        return highlights.Distinct().Take(3).ToList();
    }

    private static SearchQualityInfo CalculateQuality(List<SearchResultDto> results)
    {
        if (!results.Any())
        {
            return new SearchQualityInfo
            {
                EstimatedQuality = 0,
                QualityTier = "Low",
                QualityFactors = new List<string> { "No results found" },
                ImprovementSuggestions = new List<string> { "Try broadening your search terms" }
            };
        }

        var avgScore = results.Average(r => r.Score);
        var quality = avgScore > 0.7 ? 0.85 : avgScore > 0.5 ? 0.65 : 0.45;

        if (results.Count >= 5) quality += 0.05;
        if (results.Any(r => r.RerankScore.HasValue)) quality += 0.1;

        quality = Math.Min(quality, 1.0);

        return new SearchQualityInfo
        {
            EstimatedQuality = quality,
            QualityTier = quality > 0.8 ? "Excellent" : quality > 0.6 ? "Good" : quality > 0.4 ? "Acceptable" : "Low",
            QualityFactors = new List<string>
            {
                $"{results.Count} results found",
                $"Average score: {avgScore:F2}"
            }
        };
    }

    private static SearchResponse CreateEmptyResponse(SearchRequest request, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new SearchResponse
        {
            Query = request.Query,
            Results = new List<SearchResultDto>(),
            TotalResults = 0,
            ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds,
            Mode = request.Mode,
            Quality = new SearchQualityInfo
            {
                EstimatedQuality = 0,
                QualityTier = "Low",
                QualityFactors = new List<string> { "No indexed documents found" },
                ImprovementSuggestions = new List<string> { "Index documents before searching" }
            }
        };
    }

    private async Task RecordSearchHistoryAsync(
        SearchRequest request,
        SearchResponse response,
        string? apiKeyPrefix,
        CancellationToken cancellationToken)
    {
        try
        {
            var searchType = request.Mode switch
            {
                SearchMode.Auto => SearchType.Hybrid,
                SearchMode.Vector => SearchType.Vector,
                SearchMode.Keyword => SearchType.Keyword,
                SearchMode.Hybrid => SearchType.Hybrid,
                _ => SearchType.Keyword
            };

            var history = SearchHistory.Create(
                request.Query,
                request.CollectionId,
                response.TotalResults,
                response.ExecutionTimeMs,
                searchType,
                apiKeyPrefix);

            await _searchHistoryRepository.AddAsync(history, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record search history");
        }
    }

    #endregion
}
