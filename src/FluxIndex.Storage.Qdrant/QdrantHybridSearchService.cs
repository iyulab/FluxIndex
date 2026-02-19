using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Services;
using Microsoft.Extensions.Logging;
using DomainSearchStrategy = FluxIndex.Core.Domain.Models.SearchStrategy;

namespace FluxIndex.Storage.Qdrant;

/// <summary>
/// Qdrant-backed hybrid search service combining vector similarity search with BM25 keyword search.
/// Uses Qdrant for vector operations and in-memory BM25 index for sparse retrieval.
/// </summary>
public partial class QdrantHybridSearchService : IHybridSearchService
{
    private readonly QdrantVectorStore _vectorStore;
    private readonly BM25SparseRetriever _bm25Retriever;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<QdrantHybridSearchService> _logger;

    public QdrantHybridSearchService(
        QdrantVectorStore vectorStore,
        BM25SparseRetriever bm25Retriever,
        IEmbeddingService embeddingService,
        ILogger<QdrantHybridSearchService> logger)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _bm25Retriever = bm25Retriever ?? throw new ArgumentNullException(nameof(bm25Retriever));
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
        var candidateCount = options.MaxResults * 3; // Fetch more candidates for fusion

        LogHybridSearch(_logger, query);

        // Execute vector and BM25 searches in parallel
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        var vectorTask = _vectorStore.SearchAsync(queryEmbedding, candidateCount, 0.0f, filters: null, cancellationToken);
        var bm25Task = _bm25Retriever.SearchAsync(query, new SparseSearchOptions
        {
            MaxResults = candidateCount,
            MinScore = 0.0
        }, cancellationToken);

        await Task.WhenAll(vectorTask, bm25Task);

        var vectorResults = (await vectorTask).ToList();
        var bm25Results = (await bm25Task).ToList();

        LogSearchResults(_logger, vectorResults.Count, bm25Results.Count);

        // Fuse results using RRF
        var fusedResults = FuseResultsRRF(vectorResults, bm25Results, options);

        return fusedResults.Take(options.MaxResults).ToList();
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

    /// <inheritdoc/>
    public Task<DomainSearchStrategy> RecommendSearchStrategyAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        // Analyze query characteristics
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hasSpecificTerms = words.Any(w => w.Length > 8 || (w.Length > 0 && char.IsUpper(w[0])));
        var isShortQuery = words.Length <= 3;
        var hasQuotes = query.Contains('"') || query.Contains('\'');

        // Recommend strategy based on query analysis
        if (hasQuotes || hasSpecificTerms)
        {
            // Exact terms suggest keyword-heavy search
            return Task.FromResult(new DomainSearchStrategy
            {
                Type = SearchStrategyType.SparseFirst,
                RecommendedFusion = FusionMethod.RRF,
                RecommendedWeights = (0.4, 0.6),
                Confidence = 0.8,
                Reasoning = "Query contains specific terms or quoted phrases, favoring keyword matching"
            });
        }

        if (isShortQuery)
        {
            // Short queries often need semantic expansion
            return Task.FromResult(new DomainSearchStrategy
            {
                Type = SearchStrategyType.VectorFirst,
                RecommendedFusion = FusionMethod.RRF,
                RecommendedWeights = (0.8, 0.2),
                Confidence = 0.7,
                Reasoning = "Short query benefits from semantic expansion via vector search"
            });
        }

        // Default balanced approach
        return Task.FromResult(new DomainSearchStrategy
        {
            Type = SearchStrategyType.Balanced,
            RecommendedFusion = FusionMethod.RRF,
            RecommendedWeights = (0.7, 0.3),
            Confidence = 0.75,
            Reasoning = "Balanced hybrid search for general queries"
        });
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
    /// Fuses vector and BM25 results using Reciprocal Rank Fusion (RRF).
    /// </summary>
    private static List<HybridSearchResult> FuseResultsRRF(
        List<DocumentChunk> vectorResults,
        List<SparseSearchResult> bm25Results,
        HybridSearchOptions options)
    {
        var k = options.RrfK;
        var scoreMap = new Dictionary<string, (double vectorScore, double bm25Score, int vectorRank, int bm25Rank, DocumentChunk chunk, IReadOnlyList<string> matchedTerms)>();

        // Add vector results
        for (int i = 0; i < vectorResults.Count; i++)
        {
            var chunk = vectorResults[i];
            scoreMap[chunk.Id] = (chunk.Score ?? 0, 0, i + 1, 0, chunk, Array.Empty<string>());
        }

        // Merge BM25 results
        for (int i = 0; i < bm25Results.Count; i++)
        {
            var result = bm25Results[i];
            var chunk = result.Chunk;

            if (scoreMap.TryGetValue(chunk.Id, out var existing))
            {
                scoreMap[chunk.Id] = (existing.vectorScore, result.Score, existing.vectorRank, i + 1, existing.chunk, result.MatchedTerms);
            }
            else
            {
                scoreMap[chunk.Id] = (0, result.Score, 0, i + 1, chunk, result.MatchedTerms);
            }
        }

        // Calculate final RRF scores
        var fusedResults = scoreMap
            .Select(kv =>
            {
                var vectorRrfScore = kv.Value.vectorRank > 0 ? 1.0 / (k + kv.Value.vectorRank) : 0;
                var bm25RrfScore = kv.Value.bm25Rank > 0 ? 1.0 / (k + kv.Value.bm25Rank) : 0;
                var fusedScore = options.VectorWeight * vectorRrfScore + options.SparseWeight * bm25RrfScore;

                var source = (kv.Value.vectorRank > 0, kv.Value.bm25Rank > 0) switch
                {
                    (true, true) => SearchSource.Both,
                    (true, false) => SearchSource.Vector,
                    (false, true) => SearchSource.Sparse,
                    _ => SearchSource.Vector
                };

                // Calculate confidence based on source agreement and score quality
                var confidence = source == SearchSource.Both
                    ? Math.Min(1.0, (kv.Value.vectorScore + kv.Value.bm25Score) / 2)
                    : Math.Min(1.0, Math.Max(kv.Value.vectorScore, kv.Value.bm25Score) * 0.8);

                return new HybridSearchResult
                {
                    Chunk = kv.Value.chunk,
                    FusedScore = fusedScore,
                    VectorScore = kv.Value.vectorScore,
                    SparseScore = kv.Value.bm25Score,
                    VectorRank = kv.Value.vectorRank,
                    SparseRank = kv.Value.bm25Rank,
                    FusionMethod = FusionMethod.RRF,
                    Source = source,
                    Confidence = confidence,
                    MatchedTerms = kv.Value.matchedTerms
                };
            })
            .OrderByDescending(r => r.FusedScore)
            .ToList();

        // Assign final ranks
        for (int i = 0; i < fusedResults.Count; i++)
        {
            fusedResults[i] = fusedResults[i] with { FusedRank = i + 1 };
        }

        return fusedResults;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Executing hybrid search for query: {Query}")]
    private static partial void LogHybridSearch(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Vector results: {VectorCount}, BM25 results: {BM25Count}")]
    private static partial void LogSearchResults(ILogger logger, int vectorCount, int bm25Count);

    #endregion
}
