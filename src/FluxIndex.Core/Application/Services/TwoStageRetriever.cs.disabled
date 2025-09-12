using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Two-stage retrieval: Fast bi-encoder recall → Precise cross-encoder reranking
/// Stage 1: Vector similarity search (bi-encoder) - retrieve top 50-100
/// Stage 2: Reranking (cross-encoder) - refine to top 5-10
/// </summary>
public class TwoStageRetriever : ITwoStageRetriever
{
    private readonly IVectorStore _vectorStore;
    private readonly IDocumentRepository _documentRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly IReranker _reranker;
    private readonly ILogger<TwoStageRetriever> _logger;
    private readonly TwoStageOptions _options;

    public TwoStageRetriever(
        IVectorStore vectorStore,
        IDocumentRepository documentRepository,
        IEmbeddingService embeddingService,
        IReranker reranker,
        TwoStageOptions? options = null,
        ILogger<TwoStageRetriever>? logger = null)
    {
        _vectorStore = vectorStore;
        _documentRepository = documentRepository;
        _embeddingService = embeddingService;
        _reranker = reranker;
        _options = options ?? new TwoStageOptions();
        _logger = logger ?? new NullLogger<TwoStageRetriever>();
    }

    /// <summary>
    /// Performs two-stage retrieval with metrics collection
    /// </summary>
    public async Task<TwoStageResult> SearchAsync(
        string query,
        TwoStageSearchOptions? searchOptions = null,
        CancellationToken cancellationToken = default)
    {
        var options = searchOptions ?? new TwoStageSearchOptions();
        var stopwatch = Stopwatch.StartNew();
        var result = new TwoStageResult { Query = query };

        _logger.LogInformation("Starting two-stage search for: {Query}", query);

        try
        {
            // Stage 1: Fast Recall (Bi-encoder)
            var stage1Stopwatch = Stopwatch.StartNew();
            var stage1Results = await PerformStage1RecallAsync(query, options, cancellationToken);
            stage1Stopwatch.Stop();

            result.Stage1Results = stage1Results.ToList();
            result.Stage1LatencyMs = stage1Stopwatch.ElapsedMilliseconds;
            result.RecallCount = result.Stage1Results.Count;

            _logger.LogInformation("Stage 1 completed: {Count} candidates in {Ms}ms",
                result.RecallCount, result.Stage1LatencyMs);

            if (!result.Stage1Results.Any())
            {
                _logger.LogWarning("No candidates found in Stage 1");
                result.FinalResults = Enumerable.Empty<TwoStageSearchResult>();
                result.TotalLatencyMs = stopwatch.ElapsedMilliseconds;
                return result;
            }

            // Stage 2: Precise Reranking (Cross-encoder)
            var stage2Stopwatch = Stopwatch.StartNew();
            var stage2Results = await PerformStage2RerankingAsync(query, result.Stage1Results, options, cancellationToken);
            stage2Stopwatch.Stop();

            result.Stage2LatencyMs = stage2Stopwatch.ElapsedMilliseconds;
            result.FinalResults = stage2Results;
            result.FinalCount = result.FinalResults.Count();

            _logger.LogInformation("Stage 2 completed: {Final} final results in {Ms}ms",
                result.FinalCount, result.Stage2LatencyMs);

            // Calculate metrics
            result.TotalLatencyMs = stopwatch.ElapsedMilliseconds;
            result.RecallToRerankRatio = result.RecallCount > 0 ? (float)result.FinalCount / result.RecallCount : 0;
            result.AverageScoreImprovement = CalculateAverageScoreImprovement(result.FinalResults);

            _logger.LogInformation("Two-stage search completed: {Total}ms total, {Ratio:P1} recall-to-final ratio",
                result.TotalLatencyMs, result.RecallToRerankRatio);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during two-stage search");
            result.Error = ex.Message;
            result.TotalLatencyMs = stopwatch.ElapsedMilliseconds;
            return result;
        }
    }

    /// <summary>
    /// Stage 1: Fast recall using bi-encoder vector similarity
    /// </summary>
    private async Task<IEnumerable<RetrievalCandidate>> PerformStage1RecallAsync(
        string query,
        TwoStageSearchOptions options,
        CancellationToken cancellationToken)
    {
        // Generate query embedding
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        // Vector similarity search with larger recall set
        var vectorResults = await _vectorStore.SearchAsync(
            queryEmbedding,
            options.RecallTopK,
            options.RecallMinScore,
            cancellationToken);

        // Convert to RetrievalCandidate format
        var candidates = vectorResults.Select((chunk, index) => new RetrievalCandidate
        {
            Id = chunk.Id,
            DocumentId = chunk.DocumentId,
            ChunkId = chunk.Id,
            Content = chunk.Content,
            InitialScore = chunk.Score ?? 0,
            InitialRank = index + 1,
            Metadata = chunk.Properties
        });

        return candidates;
    }

    /// <summary>
    /// Stage 2: Precise reranking using cross-encoder or advanced scoring
    /// </summary>
    private async Task<IEnumerable<TwoStageSearchResult>> PerformStage2RerankingAsync(
        string query,
        List<RetrievalCandidate> candidates,
        TwoStageSearchOptions options,
        CancellationToken cancellationToken)
    {
        // Prepare rerank options
        var rerankOptions = new RerankOptions
        {
            TopN = options.FinalTopK,
            Model = options.RerankModel,
            ScoreThreshold = options.FinalMinScore,
            IncludeExplanation = options.IncludeExplanation,
            MaxContentLength = options.MaxContentLength,
            ModelParameters = options.ModelParameters
        };

        // Perform reranking
        var rerankedResults = await _reranker.RerankAsync(query, candidates, rerankOptions, cancellationToken);

        // Convert to final result format with enriched information
        var finalResults = new List<TwoStageSearchResult>();

        foreach (var reranked in rerankedResults)
        {
            var searchResult = new TwoStageSearchResult
            {
                DocumentId = reranked.DocumentId,
                ChunkId = reranked.ChunkId,
                Content = reranked.Content,
                InitialScore = reranked.InitialScore,
                FinalScore = reranked.RerankScore,
                InitialRank = reranked.InitialRank,
                FinalRank = reranked.NewRank,
                ScoreImprovement = reranked.ScoreChange,
                RankImprovement = reranked.RankChange,
                Explanation = reranked.Explanation,
                Metadata = reranked.Metadata
            };

            // Optionally enrich with document metadata
            if (options.EnrichWithDocumentInfo)
            {
                searchResult = await EnrichWithDocumentInfoAsync(searchResult, cancellationToken);
            }

            finalResults.Add(searchResult);
        }

        return finalResults.OrderBy(r => r.FinalRank);
    }

    /// <summary>
    /// Enriches search result with additional document information
    /// </summary>
    private async Task<TwoStageSearchResult> EnrichWithDocumentInfoAsync(
        TwoStageSearchResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = await _documentRepository.GetByIdAsync(result.DocumentId, cancellationToken);
            if (document != null)
            {
                result.DocumentTitle = document.FileName;
                result.DocumentMetadata = document.Metadata;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich result with document info for {DocumentId}", result.DocumentId);
        }

        return result;
    }

    private float CalculateAverageScoreImprovement(IEnumerable<TwoStageSearchResult> results)
    {
        var improvements = results.Select(r => r.ScoreImprovement).Where(i => !float.IsNaN(i));
        return improvements.Any() ? improvements.Average() : 0.0f;
    }
}

/// <summary>
/// Configuration options for TwoStageRetriever
/// </summary>
public class TwoStageOptions
{
    /// <summary>
    /// Default number of candidates to recall in Stage 1
    /// </summary>
    public int DefaultRecallTopK { get; set; } = 50;

    /// <summary>
    /// Default number of final results after reranking
    /// </summary>
    public int DefaultFinalTopK { get; set; } = 10;

    /// <summary>
    /// Whether to collect detailed metrics
    /// </summary>
    public bool CollectMetrics { get; set; } = true;

    /// <summary>
    /// Whether to log performance information
    /// </summary>
    public bool LogPerformance { get; set; } = true;
}

/// <summary>
/// Search options for a specific two-stage search
/// </summary>
public class TwoStageSearchOptions
{
    /// <summary>
    /// Number of candidates to recall in Stage 1 (bi-encoder)
    /// </summary>
    public int RecallTopK { get; set; } = 50;

    /// <summary>
    /// Minimum score threshold for Stage 1 recall
    /// </summary>
    public float RecallMinScore { get; set; } = 0.0f;

    /// <summary>
    /// Number of final results after Stage 2 reranking
    /// </summary>
    public int FinalTopK { get; set; } = 10;

    /// <summary>
    /// Minimum score threshold for final results
    /// </summary>
    public float FinalMinScore { get; set; } = 0.0f;

    /// <summary>
    /// Reranking model to use in Stage 2
    /// </summary>
    public RerankModel RerankModel { get; set; } = RerankModel.Local;

    /// <summary>
    /// Whether to include explanations for reranking decisions
    /// </summary>
    public bool IncludeExplanation { get; set; } = false;

    /// <summary>
    /// Maximum content length for reranking
    /// </summary>
    public int MaxContentLength { get; set; } = 512;

    /// <summary>
    /// Whether to enrich results with document information
    /// </summary>
    public bool EnrichWithDocumentInfo { get; set; } = false;

    /// <summary>
    /// Custom parameters for reranking model
    /// </summary>
    public Dictionary<string, object>? ModelParameters { get; set; }
}

/// <summary>
/// Result from two-stage retrieval
/// </summary>
public class TwoStageResult
{
    public string Query { get; set; } = string.Empty;
    public List<RetrievalCandidate> Stage1Results { get; set; } = new();
    public IEnumerable<TwoStageSearchResult> FinalResults { get; set; } = Enumerable.Empty<TwoStageSearchResult>();
    
    // Metrics
    public long Stage1LatencyMs { get; set; }
    public long Stage2LatencyMs { get; set; }
    public long TotalLatencyMs { get; set; }
    public int RecallCount { get; set; }
    public int FinalCount { get; set; }
    public float RecallToRerankRatio { get; set; }
    public float AverageScoreImprovement { get; set; }
    
    // Error handling
    public string? Error { get; set; }
    public bool IsSuccessful => string.IsNullOrEmpty(Error);
}

/// <summary>
/// Enhanced search result from two-stage retrieval
/// </summary>
public class TwoStageSearchResult
{
    public string DocumentId { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    
    // Scoring information
    public float InitialScore { get; set; }
    public float FinalScore { get; set; }
    public int InitialRank { get; set; }
    public int FinalRank { get; set; }
    public float ScoreImprovement { get; set; }
    public int RankImprovement { get; set; }
    
    // Additional information
    public string? Explanation { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    
    // Document enrichment
    public string? DocumentTitle { get; set; }
    public Dictionary<string, object>? DocumentMetadata { get; set; }
}