using System.Diagnostics;
using FluxIndex.Service.Application.Interfaces.Repositories;
using FluxIndex.Service.Application.Interfaces.Services;
using FluxIndex.Service.Domain.Entities;
using FluxIndex.Service.Shared.DTOs.Search;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Service.Application.Services;

/// <summary>
/// Service implementation for search operations.
/// </summary>
public class SearchService : ISearchService
{
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly ISearchHistoryRepository _searchHistoryRepository;
    private readonly ILogger<SearchService> _logger;

    // Simple in-memory cache for semantic cache (could be Redis in production)
    private static readonly Dictionary<string, (string Response, DateTime CachedAt)> _semanticCache = new();
    private static readonly object _cacheLock = new();

    public SearchService(
        IDocumentChunkRepository chunkRepository,
        IDocumentRepository documentRepository,
        ISearchHistoryRepository searchHistoryRepository,
        ILogger<SearchService> logger)
    {
        _chunkRepository = chunkRepository;
        _documentRepository = documentRepository;
        _searchHistoryRepository = searchHistoryRepository;
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

    private Task<List<SearchResultDto>> VectorSearchAsync(
        SearchRequest request,
        List<DocumentChunk> chunks,
        List<Document> documents,
        CancellationToken cancellationToken)
    {
        // Vector search requires embeddings - for now, fall back to keyword search
        // TODO: Implement proper vector similarity search with pgvector
        // This would require generating embedding for the query and using cosine distance
        _logger.LogWarning("Vector search not fully implemented, falling back to keyword search");
        return Task.FromResult(KeywordSearch(request, chunks, documents));
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

    public Task<SemanticCacheEntryDto?> GetCachedResponseAsync(
        string query,
        double similarityThreshold = 0.95,
        CancellationToken cancellationToken = default)
    {
        lock (_cacheLock)
        {
            // Simple exact match cache (semantic similarity would require embeddings)
            var normalizedQuery = query.ToLowerInvariant().Trim();

            if (_semanticCache.TryGetValue(normalizedQuery, out var cached))
            {
                // Check if not expired (1 hour TTL)
                if (DateTime.UtcNow - cached.CachedAt < TimeSpan.FromHours(1))
                {
                    return Task.FromResult<SemanticCacheEntryDto?>(new SemanticCacheEntryDto
                    {
                        Query = query,
                        Response = cached.Response,
                        Similarity = 1.0,
                        CachedAt = cached.CachedAt
                    });
                }

                _semanticCache.Remove(normalizedQuery);
            }
        }

        return Task.FromResult<SemanticCacheEntryDto?>(null);
    }

    public Task CacheResponseAsync(
        string query,
        string response,
        CancellationToken cancellationToken = default)
    {
        lock (_cacheLock)
        {
            var normalizedQuery = query.ToLowerInvariant().Trim();
            _semanticCache[normalizedQuery] = (response, DateTime.UtcNow);
        }

        return Task.CompletedTask;
    }

    public Task ClearCacheAsync(Guid? collectionId = null, CancellationToken cancellationToken = default)
    {
        lock (_cacheLock)
        {
            // For now, clear all cache (collection-specific clearing would need more structure)
            _semanticCache.Clear();
        }

        _logger.LogInformation("Semantic cache cleared for collection: {CollectionId}", collectionId ?? Guid.Empty);
        return Task.CompletedTask;
    }
}
