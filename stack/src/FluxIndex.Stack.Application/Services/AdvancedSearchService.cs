using System.Diagnostics;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Reranking;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Shared.DTOs.Search;
using Microsoft.Extensions.Logging;

// Type aliases to avoid ambiguity with Core interfaces
using StackIDocumentRepository = FluxIndex.Stack.Application.Interfaces.Repositories.IDocumentRepository;
using CoreQueryType = FluxIndex.Core.Application.Interfaces.QueryType;
using CoreQueryAnalysis = FluxIndex.Core.Application.Interfaces.QueryAnalysis;

namespace FluxIndex.Stack.Application.Services;

/// <summary>
/// Advanced search service with dynamic fusion, listwise reranking,
/// entity extraction, and community-based search capabilities.
/// </summary>
public class AdvancedSearchService : IAdvancedSearchService
{
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly StackIDocumentRepository _documentRepository;
    private readonly ISearchHistoryRepository _searchHistoryRepository;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IDynamicFusionService? _fusionService;
    private readonly IListwiseReranker? _listwiseReranker;
    private readonly IAdvancedEntityExtractionService? _entityService;
    private readonly ILeidenCommunityService? _communityService;
    private readonly IQueryComplexityAnalyzer? _queryAnalyzer;
    private readonly ILogger<AdvancedSearchService> _logger;

    public AdvancedSearchService(
        IDocumentChunkRepository chunkRepository,
        StackIDocumentRepository documentRepository,
        ISearchHistoryRepository searchHistoryRepository,
        IEmbeddingProvider embeddingProvider,
        ILogger<AdvancedSearchService> logger,
        IDynamicFusionService? fusionService = null,
        IListwiseReranker? listwiseReranker = null,
        IAdvancedEntityExtractionService? entityService = null,
        ILeidenCommunityService? communityService = null,
        IQueryComplexityAnalyzer? queryAnalyzer = null)
    {
        _chunkRepository = chunkRepository;
        _documentRepository = documentRepository;
        _searchHistoryRepository = searchHistoryRepository;
        _embeddingProvider = embeddingProvider;
        _fusionService = fusionService;
        _listwiseReranker = listwiseReranker;
        _entityService = entityService;
        _communityService = communityService;
        _queryAnalyzer = queryAnalyzer;
        _logger = logger;
    }

    public async Task<AdvancedSearchResponse> SearchAsync(
        AdvancedSearchRequest request,
        string? apiKeyPrefix = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Advanced search for: {Query} in collection: {CollectionId}",
            request.Query, request.CollectionId);

        var results = new List<AdvancedSearchResultDto>();
        QueryAnalysisDto? queryAnalysis = null;
        FusionDetailsDto? fusionDetails = null;
        List<ExtractedEntityDto>? entities = null;
        CommunitySearchInfoDto? communityInfo = null;

        try
        {
            // Step 1: Get documents
            var (documents, _) = await _documentRepository.GetPagedAsync(
                1, 1000,
                request.CollectionId,
                DocumentStatus.Indexed,
                cancellationToken);

            if (!documents.Any())
            {
                _logger.LogInformation("No indexed documents found for search");
                return CreateEmptyResponse(request, stopwatch);
            }

            var documentIds = documents.Select(d => d.Id).ToList();

            // Step 2: Get all chunks
            var allChunks = new List<DocumentChunk>();
            foreach (var docId in documentIds)
            {
                var chunks = await _chunkRepository.GetByDocumentIdAsync(docId, cancellationToken);
                allChunks.AddRange(chunks);
            }

            // Step 3: Analyze query (if requested or needed for dynamic fusion)
            CoreQueryAnalysis? coreQueryAnalysis = null;
            DynamicFusionConfiguration? fusionConfig = null;
            if (request.IncludeQueryAnalysis || request.EnableDynamicFusion)
            {
                coreQueryAnalysis = await AnalyzeQueryInternalAsync(request.Query, cancellationToken);
                if (request.IncludeQueryAnalysis)
                {
                    queryAnalysis = MapToDto(coreQueryAnalysis);
                }

                // Calculate dynamic fusion weights
                if (request.EnableDynamicFusion && _fusionService != null)
                {
                    fusionConfig = _fusionService.CalculateDynamicWeights(coreQueryAnalysis);
                }
            }

            // Step 4: Perform keyword search
            var keywordResults = await PerformKeywordSearchAsync(request, allChunks, documents, cancellationToken);

            // Step 5: Perform vector search
            var vectorResults = await PerformVectorSearchAsync(request, allChunks, documents, cancellationToken);

            // Step 6: Fuse results
            if (fusionConfig != null)
            {
                results = FuseWithDynamicAlpha(fusionConfig, keywordResults, vectorResults);

                if (request.IncludeFusionDetails)
                {
                    fusionDetails = new FusionDetailsDto
                    {
                        FusionMethod = fusionConfig.RecommendedFusion.ToString(),
                        KeywordWeight = fusionConfig.SparseWeight,
                        VectorWeight = fusionConfig.VectorWeight,
                        WasDynamicallyTuned = true,
                        TuningReason = fusionConfig.Reasoning
                    };
                }
            }
            else
            {
                // Use static fusion
                results = StaticFuse(keywordResults, vectorResults, request.FusionMethod);

                if (request.IncludeFusionDetails)
                {
                    fusionDetails = new FusionDetailsDto
                    {
                        FusionMethod = request.FusionMethod.ToString(),
                        KeywordWeight = 0.5,
                        VectorWeight = 0.5,
                        WasDynamicallyTuned = false
                    };
                }
            }

            // Step 7: Apply filters
            if (request.Filters != null && request.Filters.Count > 0)
            {
                results = ApplyFilters(results, request.Filters);
            }

            // Step 8: Apply minimum score filter
            if (request.MinScore > 0)
            {
                results = results.Where(r => r.Score >= request.MinScore).ToList();
            }

            // Step 9: Apply listwise reranking (if enabled)
            if (request.EnableListwiseReranking && _listwiseReranker != null && results.Count > 0)
            {
                results = await ApplyListwiseRerankingAsync(
                    request.Query, results, request.ListwiseMethod, request.TopK, cancellationToken);
            }

            // Step 10: Apply TopK limit
            results = results.Take(request.TopK).ToList();

            // Step 11: Extract entities (if enabled)
            if (request.EnableEntityExtraction && _entityService != null)
            {
                entities = await ExtractEntitiesFromResultsAsync(results, cancellationToken);
            }

            // Step 12: Get community info (if enabled)
            if (request.EnableCommunitySearch && _communityService != null && request.CollectionId.HasValue)
            {
                communityInfo = await GetCommunityInfoAsync(request.CollectionId.Value, results, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during advanced search for query: {Query}", request.Query);
            throw;
        }

        stopwatch.Stop();
        var executionTime = stopwatch.Elapsed.TotalMilliseconds;

        // Record search history
        await RecordSearchHistoryAsync(request, results.Count, executionTime, apiKeyPrefix, cancellationToken);

        return new AdvancedSearchResponse
        {
            Query = request.Query,
            Results = results,
            TotalResults = results.Count,
            ExecutionTimeMs = executionTime,
            QueryAnalysis = queryAnalysis,
            FusionDetails = fusionDetails,
            Entities = entities,
            CommunityInfo = communityInfo
        };
    }

    public async Task<QueryAnalysisDto> AnalyzeQueryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var analysis = await AnalyzeQueryInternalAsync(query, cancellationToken);
        return MapToDto(analysis);
    }

    public async Task<List<ExtractedEntityDto>> ExtractEntitiesAsync(
        Guid collectionId,
        int maxEntities = 100,
        CancellationToken cancellationToken = default)
    {
        if (_entityService == null)
        {
            _logger.LogWarning("Entity extraction service not available");
            return new List<ExtractedEntityDto>();
        }

        var (documents, _) = await _documentRepository.GetPagedAsync(
            1, 1000, collectionId, DocumentStatus.Indexed, cancellationToken);

        var allChunks = new List<DocumentChunk>();
        foreach (var doc in documents)
        {
            var chunks = await _chunkRepository.GetByDocumentIdAsync(doc.Id, cancellationToken);
            allChunks.AddRange(chunks);
        }

        var combinedText = string.Join("\n", allChunks.Select(c => c.Content));
        var extractedEntities = await _entityService.ExtractEntitiesAsync(combinedText, null, cancellationToken);

        return extractedEntities
            .Take(maxEntities)
            .Select(e => new ExtractedEntityDto
            {
                Name = e.Text,
                Type = e.Type.ToString(),
                Confidence = e.Confidence,
                MentionCount = e.OccurrenceCount
            })
            .ToList();
    }

    public async Task<CommunitySearchInfoDto> BuildCommunitiesAsync(
        Guid collectionId,
        int maxLevels = 3,
        CancellationToken cancellationToken = default)
    {
        if (_communityService == null)
        {
            _logger.LogWarning("Community service not available");
            return new CommunitySearchInfoDto { TotalCommunities = 0, CommunitiesSearched = 0 };
        }

        var (documents, _) = await _documentRepository.GetPagedAsync(
            1, 1000, collectionId, DocumentStatus.Indexed, cancellationToken);

        var allChunks = new List<DocumentChunk>();
        foreach (var doc in documents)
        {
            var chunks = await _chunkRepository.GetByDocumentIdAsync(doc.Id, cancellationToken);
            allChunks.AddRange(chunks);
        }

        // Build LeidenChunks for community detection
        var leidenChunks = new List<LeidenChunk>();
        foreach (var chunk in allChunks)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (chunk.Embedding != null && chunk.Embedding.ToArray().Length > 0)
            {
                leidenChunks.Add(new LeidenChunk
                {
                    Id = chunk.Id.ToString(),
                    Content = chunk.Content,
                    Embedding = new EmbeddingVector(chunk.Embedding.ToArray(), "default"),
                    DocumentId = chunk.DocumentId.ToString(),
                    Metadata = chunk.Metadata
                });
            }
#pragma warning restore CS0618
        }

        if (leidenChunks.Count == 0)
        {
            return new CommunitySearchInfoDto { TotalCommunities = 0, CommunitiesSearched = 0 };
        }

        var options = new LeidenOptions { MaxHierarchyLevels = maxLevels };
        var hierarchy = await _communityService.DetectHierarchicalCommunitiesAsync(
            leidenChunks, options, cancellationToken);

        var communities = new List<CommunityDto>();
        var communityIndex = 0;
        foreach (var level in hierarchy.Levels)
        {
            foreach (var community in level.Communities)
            {
                communities.Add(new CommunityDto
                {
                    CommunityId = communityIndex++,
                    Level = level.LevelIndex,
                    MemberCount = community.ChunkIds.Count,
                    RelevanceScore = 0,
                    Summary = community.Summary
                });
            }
        }

        return new CommunitySearchInfoDto
        {
            TotalCommunities = communities.Count,
            CommunitiesSearched = 0,
            RelevantCommunities = communities.Take(10).ToList()
        };
    }

    public async Task<List<CommunityDto>> GetCommunitiesAsync(
        Guid collectionId,
        int? level = null,
        CancellationToken cancellationToken = default)
    {
        // This would retrieve cached community structure
        // For now, rebuild communities
        var info = await BuildCommunitiesAsync(collectionId, 3, cancellationToken);

        if (level.HasValue)
        {
            return info.RelevantCommunities.Where(c => c.Level == level.Value).ToList();
        }

        return info.RelevantCommunities;
    }

    #region Private Methods

    private async Task<CoreQueryAnalysis> AnalyzeQueryInternalAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (_queryAnalyzer != null)
        {
            return await _queryAnalyzer.AnalyzeAsync(query, cancellationToken);
        }

        // Fallback to simple analysis
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new CoreQueryAnalysis
        {
            Type = words.Length > 5 ? CoreQueryType.NaturalQuestion : CoreQueryType.SimpleKeyword,
            Complexity = words.Length switch
            {
                <= 2 => ComplexityLevel.Simple,
                <= 5 => ComplexityLevel.Moderate,
                <= 10 => ComplexityLevel.Complex,
                _ => ComplexityLevel.VeryComplex
            },
            Keywords = new List<string>(words.Take(10)),
            Entities = new List<string>()
        };
    }

    private async Task<List<AdvancedSearchResultDto>> PerformKeywordSearchAsync(
        AdvancedSearchRequest request,
        List<DocumentChunk> chunks,
        List<Document> documents,
        CancellationToken cancellationToken)
    {
        var queryTerms = request.Query.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (queryTerms.Length == 0)
            return new List<AdvancedSearchResultDto>();

        var docLookup = documents.ToDictionary(d => d.Id);
        var results = new List<AdvancedSearchResultDto>();

        foreach (var chunk in chunks)
        {
            var content = chunk.Content.ToLowerInvariant();
            var matchCount = queryTerms.Count(term => content.Contains(term));

            if (matchCount > 0)
            {
                var score = (double)matchCount / queryTerms.Length;
                results.Add(new AdvancedSearchResultDto
                {
                    ChunkId = chunk.Id,
                    DocumentId = chunk.DocumentId,
                    DocumentTitle = docLookup.TryGetValue(chunk.DocumentId, out var doc) ? doc.Title : "Unknown",
                    ChunkIndex = chunk.ChunkIndex,
                    Content = request.IncludeContent ? chunk.Content : null,
                    Score = score,
                    KeywordScore = score,
                    Metadata = request.IncludeMetadata ? chunk.Metadata : null,
                    Highlights = ExtractHighlights(chunk.Content, request.Query)
                });
            }
        }

        return results.OrderByDescending(r => r.Score).ToList();
    }

    private async Task<List<AdvancedSearchResultDto>> PerformVectorSearchAsync(
        AdvancedSearchRequest request,
        List<DocumentChunk> chunks,
        List<Document> documents,
        CancellationToken cancellationToken)
    {
        try
        {
            var queryEmbedding = await _embeddingProvider.GetEmbeddingAsync(request.Query, cancellationToken);
            var documentIds = documents.Select(d => d.Id).ToList();

            var vectorResults = await _chunkRepository.SearchByVectorAsync(
                queryEmbedding,
                limit: request.TopK * 3,
                documentIds: documentIds,
                minScore: request.MinScore,
                cancellationToken: cancellationToken);

            var docLookup = documents.ToDictionary(d => d.Id);

            return vectorResults.Select(r => new AdvancedSearchResultDto
            {
                ChunkId = r.Chunk.Id,
                DocumentId = r.Chunk.DocumentId,
                DocumentTitle = docLookup.TryGetValue(r.Chunk.DocumentId, out var doc) ? doc.Title : "Unknown",
                ChunkIndex = r.Chunk.ChunkIndex,
                Content = request.IncludeContent ? r.Chunk.Content : null,
                Score = r.Score,
                VectorScore = r.Score,
                Metadata = request.IncludeMetadata ? r.Chunk.Metadata : null,
                Highlights = ExtractHighlights(r.Chunk.Content, request.Query)
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector search failed, returning empty results");
            return new List<AdvancedSearchResultDto>();
        }
    }

    private List<AdvancedSearchResultDto> FuseWithDynamicAlpha(
        DynamicFusionConfiguration config,
        List<AdvancedSearchResultDto> keywordResults,
        List<AdvancedSearchResultDto> vectorResults)
    {
        // Use weighted sum based on dynamic configuration
        return MergeWithWeightedSum(keywordResults, vectorResults, config.SparseWeight);
    }

    private List<AdvancedSearchResultDto> StaticFuse(
        List<AdvancedSearchResultDto> keywordResults,
        List<AdvancedSearchResultDto> vectorResults,
        FusionMethodDto method)
    {
        return method switch
        {
            FusionMethodDto.RRF => MergeWithRRF(keywordResults, vectorResults),
            FusionMethodDto.WeightedSum => MergeWithWeightedSum(keywordResults, vectorResults, 0.5),
            _ => MergeWithRRF(keywordResults, vectorResults)
        };
    }

    private List<AdvancedSearchResultDto> MergeWithRRF(
        List<AdvancedSearchResultDto> keywordResults,
        List<AdvancedSearchResultDto> vectorResults,
        int k = 60)
    {
        var merged = new Dictionary<Guid, AdvancedSearchResultDto>();
        var scores = new Dictionary<Guid, double>();

        for (int i = 0; i < keywordResults.Count; i++)
        {
            var result = keywordResults[i];
            scores[result.ChunkId] = 1.0 / (k + i + 1);
            merged[result.ChunkId] = result;
        }

        for (int i = 0; i < vectorResults.Count; i++)
        {
            var result = vectorResults[i];
            var rrfScore = 1.0 / (k + i + 1);

            if (scores.TryGetValue(result.ChunkId, out var existing))
            {
                scores[result.ChunkId] = existing + rrfScore;
                var existingResult = merged[result.ChunkId];
                merged[result.ChunkId] = existingResult with { VectorScore = result.VectorScore };
            }
            else
            {
                scores[result.ChunkId] = rrfScore;
                merged[result.ChunkId] = result;
            }
        }

        return merged.Values
            .Select(r => r with { Score = scores[r.ChunkId], FusionScore = scores[r.ChunkId] })
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    private List<AdvancedSearchResultDto> MergeWithWeightedSum(
        List<AdvancedSearchResultDto> keywordResults,
        List<AdvancedSearchResultDto> vectorResults,
        double alpha)
    {
        var merged = new Dictionary<Guid, AdvancedSearchResultDto>();
        var keywordScores = keywordResults.ToDictionary(r => r.ChunkId, r => r.KeywordScore ?? 0);
        var vectorScores = vectorResults.ToDictionary(r => r.ChunkId, r => r.VectorScore ?? 0);

        var allIds = keywordScores.Keys.Union(vectorScores.Keys).ToList();

        foreach (var id in allIds)
        {
            var keyScore = keywordScores.GetValueOrDefault(id, 0);
            var vecScore = vectorScores.GetValueOrDefault(id, 0);
            var combinedScore = alpha * keyScore + (1 - alpha) * vecScore;

            var keyword = keywordResults.FirstOrDefault(r => r.ChunkId == id);
            var vector = vectorResults.FirstOrDefault(r => r.ChunkId == id);
            var source = keyword ?? vector!;

            merged[id] = source with
            {
                Score = combinedScore,
                FusionScore = combinedScore,
                KeywordScore = keyScore,
                VectorScore = vecScore
            };
        }

        return merged.Values.OrderByDescending(r => r.Score).ToList();
    }

    private async Task<List<AdvancedSearchResultDto>> ApplyListwiseRerankingAsync(
        string query,
        List<AdvancedSearchResultDto> results,
        ListwiseMethodDto method,
        int topK,
        CancellationToken cancellationToken)
    {
        try
        {
            var candidates = results.Select((r, idx) => new RetrievalCandidate
            {
                Id = r.ChunkId.ToString(),
                DocumentId = r.DocumentId.ToString(),
                ChunkId = r.ChunkId.ToString(),
                Content = r.Content ?? string.Empty,
                InitialScore = (float)r.Score,
                InitialRank = idx + 1,
                Metadata = r.Metadata
            }).ToList();

            var options = new ListwiseRerankOptions
            {
                TopN = topK,
                Method = method switch
                {
                    ListwiseMethodDto.AttentionBased => ListwiseMethod.AttentionBased,
                    ListwiseMethodDto.SlidingWindow => ListwiseMethod.SlidingWindow,
                    ListwiseMethodDto.DirectLlm => ListwiseMethod.DirectLlm,
                    ListwiseMethodDto.Tournament => ListwiseMethod.Tournament,
                    ListwiseMethodDto.Hybrid => ListwiseMethod.Hybrid,
                    _ => ListwiseMethod.AttentionBased
                }
            };

            var reranked = await _listwiseReranker!.RerankAsync(query, candidates, options, cancellationToken);
            var resultLookup = results.ToDictionary(r => r.ChunkId);

            return reranked.Select((rr, newRank) =>
            {
                var chunkId = Guid.Parse(rr.ChunkId);
                var original = resultLookup[chunkId];

                return original with
                {
                    Score = rr.ListwiseScore,
                    RerankScore = rr.ListwiseScore,
                    ListwiseDetails = new ListwiseResultDetailsDto
                    {
                        OriginalRank = rr.InitialRank,
                        NewRank = newRank + 1,
                        ListwiseScore = rr.ListwiseScore,
                        Confidence = rr.Confidence,
                        ComponentWeights = rr.Components?.Weights
                    }
                };
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Listwise reranking failed, returning original results");
            return results;
        }
    }

    private async Task<List<ExtractedEntityDto>> ExtractEntitiesFromResultsAsync(
        List<AdvancedSearchResultDto> results,
        CancellationToken cancellationToken)
    {
        var combinedText = string.Join("\n", results.Where(r => r.Content != null).Select(r => r.Content));
        if (string.IsNullOrWhiteSpace(combinedText))
            return new List<ExtractedEntityDto>();

        var entities = await _entityService!.ExtractEntitiesAsync(combinedText, null, cancellationToken);
        return entities.Take(20).Select(e => new ExtractedEntityDto
        {
            Name = e.Text,
            Type = e.Type.ToString(),
            Confidence = e.Confidence,
            MentionCount = e.OccurrenceCount
        }).ToList();
    }

    private async Task<CommunitySearchInfoDto> GetCommunityInfoAsync(
        Guid collectionId,
        List<AdvancedSearchResultDto> results,
        CancellationToken cancellationToken)
    {
        // For now, return a simplified community info
        // In a full implementation, this would query cached community structures
        return new CommunitySearchInfoDto
        {
            TotalCommunities = 0,
            CommunitiesSearched = 0,
            RelevantCommunities = new List<CommunityDto>()
        };
    }

    private static List<AdvancedSearchResultDto> ApplyFilters(
        List<AdvancedSearchResultDto> results,
        Dictionary<string, object> filters)
    {
        return results.Where(r =>
        {
            if (r.Metadata == null) return false;
            return filters.All(f => r.Metadata.TryGetValue(f.Key, out var value) && value.Equals(f.Value));
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

    private static AdvancedSearchResponse CreateEmptyResponse(AdvancedSearchRequest request, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new AdvancedSearchResponse
        {
            Query = request.Query,
            Results = new List<AdvancedSearchResultDto>(),
            TotalResults = 0,
            ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
        };
    }

    private async Task RecordSearchHistoryAsync(
        AdvancedSearchRequest request,
        int resultCount,
        double executionTime,
        string? apiKeyPrefix,
        CancellationToken cancellationToken)
    {
        try
        {
            var history = SearchHistory.Create(
                request.Query,
                request.CollectionId,
                resultCount,
                executionTime,
                SearchType.Hybrid,
                apiKeyPrefix);

            await _searchHistoryRepository.AddAsync(history, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record search history");
        }
    }

    private static QueryAnalysisDto MapToDto(CoreQueryAnalysis analysis)
    {
        return new QueryAnalysisDto
        {
            QueryType = analysis.Type.ToString(),
            ComplexityLevel = analysis.Complexity.ToString(),
            Entities = analysis.Entities.ToList(),
            Keywords = analysis.Keywords.ToList(),
            ContainsTechnicalTerms = analysis.ContainsTechnicalTerms,
            TokenCount = analysis.Keywords.Count // Approximate token count
        };
    }

    #endregion
}
