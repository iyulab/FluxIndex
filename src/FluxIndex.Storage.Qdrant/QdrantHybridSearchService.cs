using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Application.Services;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Storage.Qdrant;

/// <summary>
/// Qdrant-backed hybrid search service combining vector similarity search with BM25 keyword search.
/// Uses Qdrant for vector operations and the registered IKeywordSearchService for sparse retrieval
/// (in-memory BM25 by default, a persistent backend when one is registered).
/// </summary>
public partial class QdrantHybridSearchService : IHybridSearchService
{
    private readonly QdrantVectorStore _vectorStore;
    private readonly IKeywordSearchService _keywordSearchService;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<QdrantHybridSearchService> _logger;

    public QdrantHybridSearchService(
        QdrantVectorStore vectorStore,
        IKeywordSearchService keywordSearchService,
        IEmbeddingService embeddingService,
        ILogger<QdrantHybridSearchService> logger)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _keywordSearchService = keywordSearchService ?? throw new ArgumentNullException(nameof(keywordSearchService));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<HybridSearchResult>> SearchAsync(
        string query,
        HybridSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new HybridSearchOptions();
        var fusion = ResolveFusion(options);
        var candidateCount = options.MaxResults * 3; // Fetch more candidates for fusion

        LogHybridSearch(_logger, query);

        // Execute vector and BM25 searches in parallel
        var queryEmbedding = await _embeddingService.GenerateQueryEmbeddingAsync(query, cancellationToken);

        // Both legs take the query's scope. Passing filters: null here meant that configuring Qdrant
        // for hybrid search silently dropped every metadata condition the caller set — the same
        // request answered differently depending on which IHybridSearchService was registered.
        var vectorFilters = options.EffectiveVectorFilters is { Count: > 0 } vf
            ? new Dictionary<string, object>(vf)
            : null;
        var sparseFilters = options.EffectiveSparseFilters;

        var vectorTask = _vectorStore.SearchAsync(queryEmbedding, candidateCount, 0.0f, vectorFilters, cancellationToken);
        var bm25Task = _keywordSearchService.SearchAsync(query, new KeywordSearchOptions
        {
            MaxResults = candidateCount,
            MinScore = 0.0,
            MetadataFilter = sparseFilters is { Count: > 0 } ? sparseFilters : null
        }, cancellationToken);

        await Task.WhenAll(vectorTask, bm25Task);

        var vectorResults = (await vectorTask).ToList();
        var bm25Results = (await bm25Task).ToList();

        LogSearchResults(_logger, vectorResults.Count, bm25Results.Count);

        // Same fusion as the in-process hybrid service, so a FusionMethod means the same thing whichever is registered.
        return HybridFusion.Fuse(
            vectorResults.Select((chunk, i) => new VectorSearchResult { DocumentChunk = chunk, Score = chunk.Score ?? 0, Rank = i + 1 }).ToList(),
            bm25Results.Select(r => new SparseSearchResult { Chunk = r.Chunk, Score = r.Score, MatchedTerms = r.MatchedTerms }).ToList(),
            fusion,
            options.MaxResults,
            options.MinFusedScore);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BatchHybridSearchResult>> SearchBatchAsync(
        IReadOnlyList<string> queries,
        HybridSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<BatchHybridSearchResult>();

        foreach (var query in queries)
        {
            var searchResults = await SearchAsync(query, options, cancellationToken);
            results.Add(new BatchHybridSearchResult
            {
                Query = query,
                Results = searchResults
            });
        }

        return results;
    }

    /// <summary>
    /// What this service applies when the caller leaves fusion unset: its fixed defaults (RRF, 0.7 / 0.3,
    /// <see cref="FusionSelection.ServiceDefault"/>), whatever the query — it has no per-query heuristic. It used to answer
    /// per-query recommendations it never applied.
    /// </summary>
    public Task<HybridSearchStrategy> RecommendSearchStrategyAsync(
        string query,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Recommend(query));

    internal static HybridSearchStrategy Recommend(string query)
    {
        var applied = ResolveFusion(new HybridSearchOptions());
        return new HybridSearchStrategy
        {
            Type = SearchStrategyType.Balanced,
            RecommendedFusion = applied.Method,
            RecommendedWeights = (applied.VectorWeight, applied.SparseWeight),
            Confidence = 1.0,
            Reasoning = "fixed defaults: this service does not adapt fusion to the query",
            QueryCharacteristics = new QueryCharacteristics { Length = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length },
        };
    }

    /// <inheritdoc/>
    public async Task<FusionPerformanceMetrics> EvaluateFusionPerformanceAsync(
        IReadOnlyList<string> testQueries,
        IReadOnlyList<IReadOnlyList<string>> groundTruth,
        CancellationToken cancellationToken = default)
    {
        if (testQueries.Count != groundTruth.Count)
            throw new ArgumentException("Test queries and ground truth must have the same count");

        var precisionSum = 0.0;
        var recallSum = 0.0;
        var mrr = 0.0;

        for (int i = 0; i < testQueries.Count; i++)
        {
            var results = await SearchAsync(testQueries[i], null, cancellationToken);
            var relevantIds = groundTruth[i].ToHashSet();
            var retrievedIds = results.Select(r => r.Chunk.Id).ToList();

            // Calculate precision@k
            var hits = retrievedIds.Count(id => relevantIds.Contains(id));
            precisionSum += retrievedIds.Count > 0 ? (double)hits / retrievedIds.Count : 0;

            // Calculate recall
            recallSum += relevantIds.Count > 0 ? (double)hits / relevantIds.Count : 0;

            // Calculate MRR
            var firstRelevantRank = retrievedIds
                .Select((id, rank) => new { id, rank = rank + 1 })
                .FirstOrDefault(x => relevantIds.Contains(x.id));
            if (firstRelevantRank != null)
            {
                mrr += 1.0 / firstRelevantRank.rank;
            }
        }

        var queryCount = testQueries.Count;
        var avgPrecision = precisionSum / queryCount;
        var avgRecall = recallSum / queryCount;

        return new FusionPerformanceMetrics
        {
            Precision = avgPrecision,
            Recall = avgRecall,
            F1Score = avgPrecision + avgRecall > 0 ? 2 * avgPrecision * avgRecall / (avgPrecision + avgRecall) : 0,
            MRR = mrr / queryCount
        };
    }

    /// <inheritdoc/>
    public async Task<DocumentChunk?> GetChunkByIdAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        return await _vectorStore.GetByIdAsync(chunkId, cancellationToken);
    }

    /// <summary>
    /// Fills the fusion values the caller left unset with this service's fixed defaults (RRF, 0.7 / 0.3).
    /// </summary>
    internal static AppliedFusion ResolveFusion(HybridSearchOptions options)
    {
        var method = options.FusionMethod ?? FusionMethod.RRF;
        var selectedBy = options.FusionMethod is null || options.VectorWeight is null || options.SparseWeight is null
            ? FusionSelection.ServiceDefault
            : FusionSelection.Caller;
        return new AppliedFusion(method, options.VectorWeight ?? 0.7, options.SparseWeight ?? 0.3, options.RrfK, selectedBy);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Executing hybrid search for query: {Query}")]
    private static partial void LogHybridSearch(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Vector results: {VectorCount}, BM25 results: {BM25Count}")]
    private static partial void LogSearchResults(ILogger logger, int vectorCount, int bm25Count);

    #endregion
}
