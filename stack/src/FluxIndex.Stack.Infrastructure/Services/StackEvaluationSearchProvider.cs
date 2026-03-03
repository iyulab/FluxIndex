using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Shared.DTOs.Search;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Stack implementation of IEvaluationSearchProvider.
/// Bridges Stack's search infrastructure with Core's RAG evaluation framework.
/// </summary>
public partial class StackEvaluationSearchProvider : IEvaluationSearchProvider
{
    private readonly ISearchService _searchService;
    private readonly ITextCompletionService? _textCompletionService;
    private readonly ISemanticCacheService? _semanticCacheService;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly ILogger<StackEvaluationSearchProvider> _logger;

    // Cache evaluation tracking
    private bool _evaluateCacheEnabled;
    private float _cacheSimilarityThreshold = 0.95f;
    private readonly List<CacheEvaluationEntry> _cacheEvaluationEntries = new();

    /// <summary>
    /// Initializes a new instance of the StackEvaluationSearchProvider.
    /// </summary>
    public StackEvaluationSearchProvider(
        ISearchService searchService,
        IDocumentChunkRepository chunkRepository,
        ILogger<StackEvaluationSearchProvider> logger,
        ITextCompletionService? textCompletionService = null,
        ISemanticCacheService? semanticCacheService = null)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _chunkRepository = chunkRepository ?? throw new ArgumentNullException(nameof(chunkRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _textCompletionService = textCompletionService;
        _semanticCacheService = semanticCacheService;
    }

    /// <summary>
    /// Enables cache evaluation mode with specified threshold.
    /// </summary>
    public void EnableCacheEvaluation(float similarityThreshold = 0.95f)
    {
        _evaluateCacheEnabled = true;
        _cacheSimilarityThreshold = similarityThreshold;
        _cacheEvaluationEntries.Clear();
    }

    /// <summary>
    /// Disables cache evaluation mode.
    /// </summary>
    public void DisableCacheEvaluation()
    {
        _evaluateCacheEnabled = false;
    }

    /// <summary>
    /// Gets cache evaluation results collected during the evaluation run.
    /// </summary>
    public CacheEvaluationSummary GetCacheEvaluationSummary()
    {
        if (_cacheEvaluationEntries.Count == 0)
        {
            return new CacheEvaluationSummary();
        }

        var hits = _cacheEvaluationEntries.Where(e => e.CacheHit).ToList();
        var misses = _cacheEvaluationEntries.Where(e => !e.CacheHit).ToList();

        return new CacheEvaluationSummary
        {
            TotalQueries = _cacheEvaluationEntries.Count,
            CacheHits = hits.Count,
            CacheMisses = misses.Count,
            HitRate = _cacheEvaluationEntries.Count > 0
                ? (double)hits.Count / _cacheEvaluationEntries.Count
                : 0,
            AverageSimilarity = hits.Count > 0
                ? hits.Average(h => h.Similarity)
                : 0,
            AverageLatencySavingsMs = hits.Count > 0
                ? hits.Average(h => h.LatencySavedMs)
                : 0,
            Entries = _cacheEvaluationEntries.ToList()
        };
    }

    /// <summary>
    /// Clears cache evaluation entries.
    /// </summary>
    public void ClearCacheEvaluationEntries()
    {
        _cacheEvaluationEntries.Clear();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<DocumentChunk>> RetrieveChunksAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        LogRetrievingChunks(_logger, query, topK);

        var searchRequest = new SearchRequest
        {
            Query = query,
            TopK = topK,
            Mode = SearchMode.Auto,
            QualityPreference = QualityPreference.Quality,
            IncludeContent = true,
            IncludeMetadata = true
        };

        // Track cache evaluation if enabled
        CacheEvaluationEntry? cacheEntry = null;
        if (_evaluateCacheEnabled && _semanticCacheService != null)
        {
            cacheEntry = new CacheEvaluationEntry { Query = query };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var cachedResult = await _semanticCacheService.GetCachedResultAsync(
                    query, _cacheSimilarityThreshold, cancellationToken);

                if (cachedResult != null)
                {
                    stopwatch.Stop();
                    cacheEntry.CacheHit = true;
                    cacheEntry.Similarity = cachedResult.SimilarityScore;
                    cacheEntry.LatencySavedMs = stopwatch.ElapsedMilliseconds;

                    // Convert cached results to DocumentChunk
                    var cachedChunks = cachedResult.Results.Select((r, idx) => new DocumentChunk
                    {
                        Id = r.Id,
                        DocumentId = r.DocumentId,
                        Content = r.Content,
                        ChunkIndex = r.ChunkIndex,
                        Metadata = new Dictionary<string, object>
                        {
                            ["search_score"] = r.Score,
                            ["from_cache"] = true
                        }
                    }).ToList();

                    _cacheEvaluationEntries.Add(cacheEntry);
                    LogCacheHit(_logger, cachedResult.SimilarityScore);
                    return cachedChunks;
                }
            }
            catch (Exception ex)
            {
                LogCacheCheckFailed(_logger, ex);
            }
        }

        try
        {
            var queryStartTime = Stopwatch.StartNew();
            var response = await _searchService.SearchAsync(searchRequest, cancellationToken: cancellationToken);
            queryStartTime.Stop();

            // Convert Stack search results to Core DocumentChunk entities
            var chunks = new List<DocumentChunk>();

            foreach (var result in response.Results)
            {
                var chunk = new DocumentChunk
                {
                    Id = result.ChunkId.ToString(),
                    DocumentId = result.DocumentId.ToString(),
                    Content = result.Content ?? string.Empty,
                    ChunkIndex = result.ChunkIndex,
                    Metadata = result.Metadata ?? new Dictionary<string, object>()
                };

                // Add search score to metadata for evaluation
                chunk.Metadata["search_score"] = result.Score;
                chunk.Metadata["document_title"] = result.DocumentTitle ?? string.Empty;

                chunks.Add(chunk);
            }

            // Record cache miss and estimate latency savings
            if (cacheEntry != null)
            {
                cacheEntry.CacheHit = false;
                cacheEntry.LatencySavedMs = queryStartTime.ElapsedMilliseconds; // What could have been saved
                _cacheEvaluationEntries.Add(cacheEntry);
            }

            LogRetrievedChunks(_logger, chunks.Count);
            return chunks;
        }
        catch (Exception ex)
        {
            LogRetrieveChunksFailed(_logger, query, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> GenerateAnswerAsync(
        string query,
        IEnumerable<DocumentChunk> retrievedChunks,
        CancellationToken cancellationToken = default)
    {
        if (_textCompletionService == null)
        {
            LogTextCompletionNotAvailable(_logger);
            return ConcatenateChunkContents(retrievedChunks);
        }

        LogGeneratingAnswer(_logger, query);

        try
        {
            // Build context from retrieved chunks
            var context = BuildContextFromChunks(retrievedChunks);

            // Generate answer using RAG prompt
            var prompt = BuildRAGPrompt(query, context);
            var answer = await _textCompletionService.CompleteAsync(
                prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = 1000, Temperature = 0.3f }, cancellationToken);

            var answerLength = answer?.Length ?? 0;
            LogGeneratedAnswer(_logger, answerLength);
            return answer ?? string.Empty;
        }
        catch (Exception ex)
        {
            LogGenerateAnswerFailed(_logger, query, ex);
            throw;
        }
    }

    /// <summary>
    /// Builds context string from retrieved chunks.
    /// </summary>
    private static string BuildContextFromChunks(IEnumerable<DocumentChunk> chunks)
    {
        var contextParts = new List<string>();
        var index = 1;

        foreach (var chunk in chunks)
        {
            var title = chunk.Metadata!.TryGetValue("document_title", out var t) ? t?.ToString() : "Unknown";
            contextParts.Add($"[Document {index}: {title}]\n{chunk.Content}");
            index++;
        }

        return string.Join("\n\n", contextParts);
    }

    /// <summary>
    /// Builds a RAG prompt for answer generation.
    /// </summary>
    private static string BuildRAGPrompt(string query, string context)
    {
        return $"""
            You are a helpful assistant that answers questions based on the provided context.
            Answer the question accurately and concisely using only the information from the context.
            If the context doesn't contain enough information to answer the question, say so.

            Context:
            {context}

            Question: {query}

            Answer:
            """;
    }

    /// <summary>
    /// Concatenates chunk contents as fallback when LLM is not available.
    /// </summary>
    private static string ConcatenateChunkContents(IEnumerable<DocumentChunk> chunks)
    {
        return string.Join("\n\n---\n\n", chunks.Select(c => c.Content));
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Retrieving chunks for evaluation. Query: {Query}, TopK: {TopK}")]
    private static partial void LogRetrievingChunks(ILogger logger, string query, int topK);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache hit for query with similarity {Similarity}")]
    private static partial void LogCacheHit(ILogger logger, double similarity);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to check semantic cache during evaluation")]
    private static partial void LogCacheCheckFailed(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Retrieved {Count} chunks for evaluation query")]
    private static partial void LogRetrievedChunks(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to retrieve chunks for evaluation. Query: {Query}")]
    private static partial void LogRetrieveChunksFailed(ILogger logger, string query, Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Text completion service not available. Returning concatenated context as answer.")]
    private static partial void LogTextCompletionNotAvailable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Generating answer for evaluation. Query: {Query}")]
    private static partial void LogGeneratingAnswer(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Generated answer of length {Length} for evaluation query")]
    private static partial void LogGeneratedAnswer(ILogger logger, int length);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to generate answer for evaluation. Query: {Query}")]
    private static partial void LogGenerateAnswerFailed(ILogger logger, string query, Exception? exception);

    #endregion
}

/// <summary>
/// Individual cache evaluation entry for tracking.
/// </summary>
public class CacheEvaluationEntry
{
    /// <summary>
    /// The evaluated query.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Whether the query hit the cache.
    /// </summary>
    public bool CacheHit { get; set; }

    /// <summary>
    /// Similarity score for cache hit (0-1).
    /// </summary>
    public double Similarity { get; set; }

    /// <summary>
    /// Latency saved (or potential savings) in milliseconds.
    /// </summary>
    public double LatencySavedMs { get; set; }
}

/// <summary>
/// Summary of cache evaluation across all queries.
/// </summary>
public class CacheEvaluationSummary
{
    /// <summary>
    /// Total number of queries evaluated.
    /// </summary>
    public int TotalQueries { get; set; }

    /// <summary>
    /// Number of cache hits.
    /// </summary>
    public int CacheHits { get; set; }

    /// <summary>
    /// Number of cache misses.
    /// </summary>
    public int CacheMisses { get; set; }

    /// <summary>
    /// Cache hit rate (0-1).
    /// </summary>
    public double HitRate { get; set; }

    /// <summary>
    /// Average similarity score for cache hits.
    /// </summary>
    public double AverageSimilarity { get; set; }

    /// <summary>
    /// Average latency savings in milliseconds.
    /// </summary>
    public double AverageLatencySavingsMs { get; set; }

    /// <summary>
    /// Individual cache evaluation entries.
    /// </summary>
    public List<CacheEvaluationEntry> Entries { get; set; } = new();
}
