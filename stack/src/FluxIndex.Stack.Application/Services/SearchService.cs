using System.Diagnostics;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Shared.DTOs.Search;
using Microsoft.Extensions.Logging;
// Import Core types to avoid namespace collisions
using IReranker = FluxIndex.Core.Application.Interfaces.IReranker;
using RetrievalCandidate = FluxIndex.Core.Application.Interfaces.RetrievalCandidate;
using RerankOptions = FluxIndex.Core.Application.Interfaces.RerankOptions;
using ISemanticCacheService = FluxIndex.Core.Application.Interfaces.ISemanticCacheService;
using CachedSearchResult = FluxIndex.Core.Application.Interfaces.CachedSearchResult;
using SearchMetadata = FluxIndex.Core.Application.Interfaces.SearchMetadata;

namespace FluxIndex.Stack.Application.Services;

/// <summary>
/// Service implementation for search operations.
/// </summary>
public class SearchService : ISearchService
{
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly ISearchHistoryRepository _searchHistoryRepository;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IReranker? _reranker;
    private readonly ISemanticCacheService? _semanticCache;
    private readonly ILogger<SearchService> _logger;

    // Fallback in-memory cache when Redis is not available
    private static readonly Dictionary<string, (string Response, DateTime CachedAt)> _fallbackCache = new();
    private static readonly object _cacheLock = new();

    public SearchService(
        IDocumentChunkRepository chunkRepository,
        IDocumentRepository documentRepository,
        ISearchHistoryRepository searchHistoryRepository,
        IEmbeddingProvider embeddingProvider,
        ILogger<SearchService> logger,
        IReranker? reranker = null,
        ISemanticCacheService? semanticCache = null)
    {
        _chunkRepository = chunkRepository;
        _documentRepository = documentRepository;
        _searchHistoryRepository = searchHistoryRepository;
        _embeddingProvider = embeddingProvider;
        _reranker = reranker;
        _semanticCache = semanticCache;
        _logger = logger;
    }

    public async Task<SearchResponse> SearchAsync(
        SearchRequest request,
        string? apiKeyPrefix = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Searching for: {Query} in collection: {CollectionId}",
            request.Query, request.CollectionId);

        var results = new List<SearchResultDto>();

        try
        {
            // Get documents for the collection (or all if no collection specified)
            var (documents, _) = await _documentRepository.GetPagedAsync(
                1, 1000, // Get up to 1000 documents
                request.CollectionId,
                DocumentStatus.Indexed,
                cancellationToken);

            if (!documents.Any())
            {
                _logger.LogInformation("No indexed documents found for search");
                return CreateEmptyResponse(request, stopwatch);
            }

            var documentIds = documents.Select(d => d.Id).ToList();

            // Get all chunks for these documents
            var allChunks = new List<DocumentChunk>();
            foreach (var docId in documentIds)
            {
                var chunks = await _chunkRepository.GetByDocumentIdAsync(docId, cancellationToken);
                allChunks.AddRange(chunks);
            }

            // Perform search based on mode
            results = request.Mode switch
            {
                SearchMode.Vector => await VectorSearchAsync(request, allChunks, documents, cancellationToken),
                SearchMode.Keyword => KeywordSearch(request, allChunks, documents),
                SearchMode.Hybrid => await HybridSearchAsync(request, allChunks, documents, cancellationToken),
                _ => KeywordSearch(request, allChunks, documents)
            };

            // Apply filters if provided
            if (request.Filters != null && request.Filters.Count > 0)
            {
                results = ApplyFilters(results, request.Filters);
            }

            // Apply minimum score filter
            if (request.MinScore > 0)
            {
                results = results.Where(r => r.Score >= request.MinScore).ToList();
            }

            // Apply reranking if enabled and reranker is available
            if (request.EnableReranking && _reranker != null && results.Count > 0)
            {
                results = await ApplyRerankingAsync(request.Query, results, request.TopK, cancellationToken);
            }

            // Apply TopK limit
            results = results.Take(request.TopK).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during search for query: {Query}", request.Query);
            throw;
        }

        stopwatch.Stop();
        var executionTime = stopwatch.Elapsed.TotalMilliseconds;

        // Record search history
        try
        {
            var searchType = request.Mode switch
            {
                SearchMode.Vector => SearchType.Vector,
                SearchMode.Keyword => SearchType.Keyword,
                SearchMode.Hybrid => SearchType.Hybrid,
                _ => SearchType.Keyword
            };

            var history = SearchHistory.Create(
                request.Query,
                request.CollectionId,
                results.Count,
                executionTime,
                searchType,
                apiKeyPrefix);

            await _searchHistoryRepository.AddAsync(history, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record search history");
        }

        return new SearchResponse
        {
            Query = request.Query,
            Results = results,
            TotalResults = results.Count,
            ExecutionTimeMs = executionTime,
            Mode = request.Mode
        };
    }

    private async Task<List<SearchResultDto>> VectorSearchAsync(
        SearchRequest request,
        List<DocumentChunk> chunks,
        List<Document> documents,
        CancellationToken cancellationToken)
    {
        try
        {
            // Generate embedding for the query
            _logger.LogDebug("Generating embedding for query: {Query}", request.Query);
            var queryEmbedding = await _embeddingProvider.GetEmbeddingAsync(request.Query, cancellationToken);

            // Get document IDs for filtering
            var documentIds = documents.Select(d => d.Id).ToList();

            // Perform vector similarity search using pgvector
            _logger.LogDebug("Performing vector search with {ChunkCount} potential chunks", chunks.Count);
            var vectorResults = await _chunkRepository.SearchByVectorAsync(
                queryEmbedding,
                limit: request.TopK * 2, // Get more results for potential filtering
                documentIds: documentIds,
                minScore: request.MinScore,
                cancellationToken: cancellationToken);

            var docLookup = documents.ToDictionary(d => d.Id);

            return vectorResults
                .Take(request.TopK)
                .Select(r => new SearchResultDto
                {
                    ChunkId = r.Chunk.Id,
                    DocumentId = r.Chunk.DocumentId,
                    DocumentTitle = docLookup.TryGetValue(r.Chunk.DocumentId, out var doc) ? doc.Title : "Unknown",
                    ChunkIndex = r.Chunk.ChunkIndex,
                    Content = request.IncludeContent ? r.Chunk.Content : null,
                    Score = r.Score,
                    VectorScore = r.Score,
                    KeywordScore = null,
                    Metadata = request.IncludeMetadata ? r.Chunk.Metadata : null,
                    Highlights = ExtractHighlights(r.Chunk.Content, request.Query)
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector search failed, falling back to keyword search");
            return KeywordSearch(request, chunks, documents);
        }
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

    private async Task<List<SearchResultDto>> HybridSearchAsync(
        SearchRequest request,
        List<DocumentChunk> chunks,
        List<Document> documents,
        CancellationToken cancellationToken)
    {
        // Hybrid combines vector and keyword search
        // For now, use keyword search with boosted relevance
        var keywordResults = KeywordSearch(request, chunks, documents);
        var vectorResults = await VectorSearchAsync(request, chunks, documents, cancellationToken);

        // Merge results with RRF (Reciprocal Rank Fusion)
        var merged = MergeWithRRF(keywordResults, vectorResults);
        return merged.Take(request.TopK).ToList();
    }

    private List<SearchResultDto> KeywordSearch(
        SearchRequest request,
        List<DocumentChunk> chunks,
        List<Document> documents)
    {
        var queryTerms = request.Query.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (queryTerms.Length == 0)
        {
            return new List<SearchResultDto>();
        }

        var docLookup = documents.ToDictionary(d => d.Id);
        var results = new List<(DocumentChunk Chunk, double Score, List<string> Highlights)>();

        foreach (var chunk in chunks)
        {
            var content = chunk.Content.ToLowerInvariant();
            var matchCount = 0;
            var highlights = new List<string>();

            foreach (var term in queryTerms)
            {
                if (content.Contains(term))
                {
                    matchCount++;

                    // Find highlight context
                    var idx = content.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var start = Math.Max(0, idx - 50);
                        var end = Math.Min(content.Length, idx + term.Length + 50);
                        highlights.Add(chunk.Content.Substring(start, end - start));
                    }
                }
            }

            if (matchCount > 0)
            {
                // Simple TF scoring
                var score = (double)matchCount / queryTerms.Length;
                results.Add((chunk, score, highlights));
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .Select(r => new SearchResultDto
            {
                ChunkId = r.Chunk.Id,
                DocumentId = r.Chunk.DocumentId,
                DocumentTitle = docLookup.TryGetValue(r.Chunk.DocumentId, out var doc) ? doc.Title : "Unknown",
                ChunkIndex = r.Chunk.ChunkIndex,
                Content = request.IncludeContent ? r.Chunk.Content : null,
                Score = r.Score,
                KeywordScore = r.Score,
                VectorScore = null,
                Metadata = request.IncludeMetadata ? r.Chunk.Metadata : null,
                Highlights = r.Highlights.Take(3).ToList()
            })
            .ToList();
    }

    private static List<SearchResultDto> MergeWithRRF(
        List<SearchResultDto> keywordResults,
        List<SearchResultDto> vectorResults,
        int k = 60)
    {
        var merged = new Dictionary<Guid, SearchResultDto>();
        var scores = new Dictionary<Guid, double>();

        // Process keyword results
        for (int i = 0; i < keywordResults.Count; i++)
        {
            var result = keywordResults[i];
            scores[result.ChunkId] = 1.0 / (k + i + 1);
            merged[result.ChunkId] = result with { KeywordScore = result.Score };
        }

        // Process vector results
        for (int i = 0; i < vectorResults.Count; i++)
        {
            var result = vectorResults[i];
            var rrfScore = 1.0 / (k + i + 1);

            if (scores.TryGetValue(result.ChunkId, out var existingScore))
            {
                scores[result.ChunkId] = existingScore + rrfScore;
                var existing = merged[result.ChunkId];
                merged[result.ChunkId] = existing with { VectorScore = result.Score };
            }
            else
            {
                scores[result.ChunkId] = rrfScore;
                merged[result.ChunkId] = result with { VectorScore = result.Score };
            }
        }

        // Update final scores and return sorted
        return merged.Values
            .Select(r => r with { Score = scores[r.ChunkId] })
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    private static List<SearchResultDto> ApplyFilters(
        List<SearchResultDto> results,
        Dictionary<string, object> filters)
    {
        return results.Where(r =>
        {
            if (r.Metadata == null) return false;

            foreach (var filter in filters)
            {
                if (!r.Metadata.TryGetValue(filter.Key, out var value))
                    return false;

                if (!value.Equals(filter.Value))
                    return false;
            }

            return true;
        }).ToList();
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
            Mode = request.Mode
        };
    }

    private async Task<List<SearchResultDto>> ApplyRerankingAsync(
        string query,
        List<SearchResultDto> results,
        int topK,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Applying reranking to {Count} results", results.Count);

            // Convert SearchResultDto to RetrievalCandidate
            var candidates = results.Select((r, index) => new RetrievalCandidate
            {
                Id = r.ChunkId.ToString(),
                DocumentId = r.DocumentId.ToString(),
                ChunkId = r.ChunkId.ToString(),
                Content = r.Content ?? string.Empty,
                InitialScore = (float)r.Score,
                InitialRank = index + 1,
                Metadata = r.Metadata
            }).ToList();

            // Apply reranking
            var rerankOptions = new RerankOptions
            {
                TopN = topK,
                IncludeExplanation = false,
                MaxContentLength = 512
            };

            var rerankedResults = await _reranker!.RerankAsync(
                query,
                candidates,
                rerankOptions,
                cancellationToken);

            // Convert back to SearchResultDto with updated scores
            var resultLookup = results.ToDictionary(r => r.ChunkId);
            var rerankedList = rerankedResults.ToList();

            return rerankedList
                .Where(rr => Guid.TryParse(rr.ChunkId, out var chunkId) && resultLookup.ContainsKey(chunkId))
                .Select(rr =>
                {
                    var chunkId = Guid.Parse(rr.ChunkId);
                    var original = resultLookup[chunkId];
                    return original with
                    {
                        Score = rr.RerankScore,
                        RerankScore = rr.RerankScore,
                        RerankExplanation = rr.Explanation
                    };
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reranking failed, returning original results");
            return results;
        }
    }

    public async Task<SemanticCacheEntryDto?> GetCachedResponseAsync(
        string query,
        double similarityThreshold = 0.95,
        CancellationToken cancellationToken = default)
    {
        // Try Redis semantic cache first if available
        if (_semanticCache != null)
        {
            try
            {
                var cachedResult = await _semanticCache.GetCachedResultAsync(
                    query,
                    (float)similarityThreshold,
                    cancellationToken);

                if (cachedResult != null)
                {
                    _logger.LogDebug("Redis semantic cache hit for query: {Query} (similarity: {Similarity:F3})",
                        query, cachedResult.SimilarityScore);

                    // Return cached response - serialize results to JSON for response field
                    var responseJson = System.Text.Json.JsonSerializer.Serialize(cachedResult.Results);
                    return new SemanticCacheEntryDto
                    {
                        Query = query,
                        Response = responseJson,
                        Similarity = cachedResult.SimilarityScore,
                        CachedAt = cachedResult.CachedAt
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis semantic cache lookup failed, falling back to in-memory cache");
            }
        }

        // Fallback to in-memory cache
        lock (_cacheLock)
        {
            var normalizedQuery = query.ToLowerInvariant().Trim();

            if (_fallbackCache.TryGetValue(normalizedQuery, out var cached))
            {
                // Check if not expired (1 hour TTL)
                if (DateTime.UtcNow - cached.CachedAt < TimeSpan.FromHours(1))
                {
                    return new SemanticCacheEntryDto
                    {
                        Query = query,
                        Response = cached.Response,
                        Similarity = 1.0,
                        CachedAt = cached.CachedAt
                    };
                }

                _fallbackCache.Remove(normalizedQuery);
            }
        }

        return null;
    }

    public async Task CacheResponseAsync(
        string query,
        string response,
        CancellationToken cancellationToken = default)
    {
        // Try Redis semantic cache first if available
        if (_semanticCache != null)
        {
            try
            {
                // Parse response JSON to create cache chunks
                var chunks = new List<CacheDocumentChunk>();
                try
                {
                    var results = System.Text.Json.JsonSerializer.Deserialize<List<SearchResultDto>>(response);
                    if (results != null)
                    {
                        chunks = results.Select(r => CacheDocumentChunk.Create(
                            documentId: r.DocumentId.ToString(),
                            content: r.Content ?? string.Empty,
                            chunkIndex: r.ChunkIndex,
                            score: (float)r.Score,
                            metadata: r.Metadata
                        )).ToList();
                    }
                }
                catch
                {
                    // If response is not parseable, create a single chunk with the response
                    chunks.Add(CacheDocumentChunk.Create(
                        documentId: "response",
                        content: response,
                        chunkIndex: 0
                    ));
                }

                await _semanticCache.SetCachedResultAsync(
                    query,
                    chunks,
                    metadata: new SearchMetadata { SearchAlgorithm = "semantic_search" },
                    cancellationToken: cancellationToken);

                _logger.LogDebug("Cached response in Redis semantic cache for query: {Query}", query);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cache in Redis, falling back to in-memory cache");
            }
        }

        // Fallback to in-memory cache
        lock (_cacheLock)
        {
            var normalizedQuery = query.ToLowerInvariant().Trim();
            _fallbackCache[normalizedQuery] = (response, DateTime.UtcNow);
        }
    }

    public async Task ClearCacheAsync(Guid? collectionId = null, CancellationToken cancellationToken = default)
    {
        // Try to clear Redis semantic cache if available
        if (_semanticCache != null)
        {
            try
            {
                var pattern = collectionId.HasValue ? $"*{collectionId}*" : "*";
                await _semanticCache.InvalidateCacheAsync(pattern, cancellationToken);
                _logger.LogInformation("Redis semantic cache cleared for collection: {CollectionId}",
                    collectionId ?? Guid.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear Redis semantic cache");
            }
        }

        // Also clear fallback in-memory cache
        lock (_cacheLock)
        {
            _fallbackCache.Clear();
        }

        _logger.LogInformation("In-memory fallback cache cleared for collection: {CollectionId}",
            collectionId ?? Guid.Empty);
    }
}
