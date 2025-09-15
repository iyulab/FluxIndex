using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// 검색 서비스 - 고급 재순위화 및 메타데이터 활용 포함
/// </summary>
public class SearchService
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IDocumentRepository _documentRepository;
    private readonly IAdvancedRerankingService _rerankingService;
    private readonly IMetadataEnrichmentService _metadataEnrichmentService;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        IDocumentRepository documentRepository,
        IAdvancedRerankingService rerankingService,
        IMetadataEnrichmentService metadataEnrichmentService,
        ILogger<SearchService> logger)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _rerankingService = rerankingService ?? throw new ArgumentNullException(nameof(rerankingService));
        _metadataEnrichmentService = metadataEnrichmentService ?? throw new ArgumentNullException(nameof(metadataEnrichmentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IEnumerable<SearchResult>> SearchAsync(
        string query,
        int topK = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Searching for query: {Query}", query);

        // Generate embedding for query
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        // Search in vector store
        var searchResults = await _vectorStore.SearchAsync(queryEmbedding, topK, minScore, cancellationToken);

        // Convert to search results
        var results = new List<SearchResult>();
        foreach (var chunk in searchResults)
        {
            var document = await _documentRepository.GetByIdAsync(chunk.DocumentId, cancellationToken);
            if (document != null)
            {
                results.Add(new SearchResult
                {
                    Chunk = chunk,
                    Score = chunk.Score ?? 0,
                    FileName = document.FileName,
                    Metadata = document.Metadata
                });
            }
        }

        _logger.LogInformation("Found {ResultCount} results for query", results.Count);
        return results;
    }

    public async Task<IEnumerable<SearchResult>> FindSimilarAsync(
        string documentId,
        int topK = 10,
        float minScore = 0.8f,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Finding similar documents to {DocumentId}", documentId);

        // Get document chunks
        var chunks = await _vectorStore.GetByDocumentIdAsync(documentId, cancellationToken);
        var firstChunk = chunks.FirstOrDefault();
        
        if (firstChunk?.Embedding == null)
        {
            _logger.LogWarning("No chunks found for document {DocumentId}", documentId);
            return Enumerable.Empty<SearchResult>();
        }

        // Search for similar documents
        var searchResults = await _vectorStore.SearchAsync(
            firstChunk.Embedding, 
            topK + 1, // +1 to exclude self
            minScore, 
            cancellationToken
        );

        // Convert to search results, excluding self
        var results = new List<SearchResult>();
        foreach (var chunk in searchResults)
        {
            if (chunk.DocumentId == documentId) continue; // Skip self

            var document = await _documentRepository.GetByIdAsync(chunk.DocumentId, cancellationToken);
            if (document != null)
            {
                results.Add(new SearchResult
                {
                    Chunk = chunk,
                    Score = chunk.Score ?? 0,
                    FileName = document.FileName,
                    Metadata = document.Metadata
                });
            }

            if (results.Count >= topK) break;
        }

        _logger.LogInformation("Found {ResultCount} similar documents", results.Count);
        return results;
    }

    /// <summary>
    /// 고급 검색 - 메타데이터 활용 및 재순위화 포함
    /// </summary>
    public async Task<IEnumerable<EnhancedSearchResult>> AdvancedSearchAsync(
        string query,
        int topK = 10,
        float minScore = 0.0f,
        RerankingStrategy rerankingStrategy = RerankingStrategy.Adaptive,
        Dictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Advanced search for query: {Query} with strategy: {Strategy}", 
            query, rerankingStrategy);

        // 1. 기본 벡터 검색 (더 많은 결과 검색 후 재순위화)
        var expandedTopK = Math.Min(topK * 3, 50); // 3배 확장 검색
        var basicResults = await SearchAsync(query, expandedTopK, minScore * 0.7f, cancellationToken);

        if (!basicResults.Any())
        {
            return Enumerable.Empty<EnhancedSearchResult>();
        }

        // 2. 메타데이터 기반 필터링
        var filteredResults = ApplyMetadataFilters(basicResults, filters);

        // 3. 고급 재순위화
        var rerankedResults = await _rerankingService.RerankAsync(
            query, filteredResults, rerankingStrategy, cancellationToken);

        // 4. 최종 결과 반환
        var finalResults = rerankedResults.Take(topK).ToList();

        _logger.LogInformation("Advanced search completed: {ResultCount} results after reranking", 
            finalResults.Count);

        return finalResults;
    }

    /// <summary>
    /// 맥락적 검색 - 관련 청크 확장 포함
    /// </summary>
    public async Task<IEnumerable<EnhancedSearchResult>> ContextualSearchAsync(
        string query,
        int topK = 10,
        bool includeRelatedChunks = true,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Contextual search for query: {Query}", query);

        // 1. 고급 검색 수행
        var baseResults = await AdvancedSearchAsync(
            query, topK, 0.0f, RerankingStrategy.Contextual, cancellationToken: cancellationToken);

        if (!includeRelatedChunks)
        {
            return baseResults;
        }

        // 2. 관련 청크 확장
        var expandedResults = new List<EnhancedSearchResult>();
        
        foreach (var result in baseResults)
        {
            expandedResults.Add(result);

            // 순차적 청크 추가 (이전/다음)
            await AddSequentialChunksAsync(result, expandedResults, cancellationToken);

            // 의미적으로 관련된 청크 추가
            await AddRelatedChunksAsync(result, expandedResults, query, cancellationToken);
        }

        // 3. 중복 제거 및 점수 재조정
        var uniqueResults = RemoveDuplicatesAndRebalanceScores(expandedResults, topK);

        _logger.LogInformation("Contextual search completed: {ResultCount} results with context", 
            uniqueResults.Count);

        return uniqueResults;
    }

    #region Private Helper Methods

    private IEnumerable<SearchResult> ApplyMetadataFilters(
        IEnumerable<SearchResult> results,
        Dictionary<string, object>? filters)
    {
        if (filters == null || !filters.Any())
            return results;

        return results.Where(result =>
        {
            // 기본 메타데이터 필터링 구현
            foreach (var (key, value) in filters)
            {
                if (!result.Metadata.Properties.TryGetValue(key, out var propValue))
                    return false;

                if (!propValue.Equals(value))
                    return false;
            }
            return true;
        });
    }

    private async Task AddSequentialChunksAsync(
        EnhancedSearchResult baseResult,
        List<EnhancedSearchResult> expandedResults,
        CancellationToken cancellationToken)
    {
        var baseChunk = baseResult.Chunk;
        if (baseChunk == null) return;

        // 이전 청크
        if (baseChunk.ChunkIndex > 0)
        {
            var prevChunk = await FindChunkByIndexAsync(
                baseChunk.DocumentId, baseChunk.ChunkIndex - 1, cancellationToken);
            
            if (prevChunk != null)
            {
                var prevResult = CreateEnhancedResult(prevChunk, baseResult.HybridScore * 0.8, "sequential_prev");
                expandedResults.Add(prevResult);
            }
        }

        // 다음 청크
        if (baseChunk.ChunkIndex < baseChunk.TotalChunks - 1)
        {
            var nextChunk = await FindChunkByIndexAsync(
                baseChunk.DocumentId, baseChunk.ChunkIndex + 1, cancellationToken);
            
            if (nextChunk != null)
            {
                var nextResult = CreateEnhancedResult(nextChunk, baseResult.HybridScore * 0.8, "sequential_next");
                expandedResults.Add(nextResult);
            }
        }
    }

    private async Task AddRelatedChunksAsync(
        EnhancedSearchResult baseResult,
        List<EnhancedSearchResult> expandedResults,
        string query,
        CancellationToken cancellationToken)
    {
        var relationships = baseResult.Chunk?.Relationships?
            .Where(r => r.Type == RelationshipType.Semantic || r.Type == RelationshipType.Reference)
            .OrderByDescending(r => r.Strength)
            .Take(3) ?? Enumerable.Empty<ChunkRelationship>(); // 최대 3개 관련 청크

        foreach (var relationship in relationships)
        {
            var relatedChunk = await _vectorStore.GetByIdAsync(relationship.TargetChunkId, cancellationToken);
            
            if (relatedChunk != null)
            {
                var relatedScore = baseResult.HybridScore * relationship.Strength * 0.7;
                var relatedResult = CreateEnhancedResult(relatedChunk, relatedScore, $"related_{relationship.Type}");
                expandedResults.Add(relatedResult);
            }
        }
    }

    private async Task<DocumentChunk?> FindChunkByIndexAsync(
        string documentId, 
        int chunkIndex, 
        CancellationToken cancellationToken)
    {
        var chunks = await _vectorStore.GetByDocumentIdAsync(documentId, cancellationToken);
        return chunks.FirstOrDefault(c => c.ChunkIndex == chunkIndex);
    }

    private EnhancedSearchResult CreateEnhancedResult(
        DocumentChunk chunk, 
        double score, 
        string contextType)
    {
        return new EnhancedSearchResult
        {
            Chunk = chunk,
            SimilarityScore = score,
            HybridScore = score,
            RerankedScore = score,
            ExplanationMetadata = new Dictionary<string, object>
            {
                ["context_type"] = contextType,
                ["base_score"] = score
            }
        };
    }

    private List<EnhancedSearchResult> RemoveDuplicatesAndRebalanceScores(
        List<EnhancedSearchResult> results, 
        int maxResults)
    {
        // 중복 제거 (청크 ID 기준)
        var unique = results
            .Where(r => r.Chunk?.Id != null)
            .GroupBy(r => r.Chunk!.Id)
            .Select(g => g.OrderByDescending(r => r.RerankedScore).First())
            .OrderByDescending(r => r.RerankedScore)
            .Take(maxResults)
            .ToList();

        // 점수 정규화 (0.0-1.0 범위)
        if (unique.Any())
        {
            var maxScore = unique.Max(r => r.RerankedScore);
            var minScore = unique.Min(r => r.RerankedScore);
            var range = maxScore - minScore;

            if (range > 0)
            {
                foreach (var result in unique)
                {
                    result.RerankedScore = (result.RerankedScore - minScore) / range;
                }
            }
        }

        return unique;
    }

    #endregion
}