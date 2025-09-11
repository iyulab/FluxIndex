using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Services.Reranking;

/// <summary>
/// Local reranker using similarity-based scoring without external dependencies
/// Uses TF-IDF, BM25, and semantic similarity for reranking
/// </summary>
public class LocalReranker : IReranker
{
    private readonly ILogger<LocalReranker> _logger;
    private readonly IEmbeddingService? _embeddingService;
    private readonly LocalRerankOptions _options;

    public LocalReranker(
        IEmbeddingService? embeddingService = null,
        LocalRerankOptions? options = null,
        ILogger<LocalReranker>? logger = null)
    {
        _embeddingService = embeddingService;
        _options = options ?? new LocalRerankOptions();
        _logger = logger ?? new NullLogger<LocalReranker>();
    }

    public async Task<IEnumerable<RerankResult>> RerankAsync(
        string query,
        IEnumerable<RetrievalCandidate> candidates,
        RerankOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var rerankOptions = options ?? new RerankOptions();
        var candidateList = candidates.ToList();

        if (!candidateList.Any())
        {
            _logger.LogWarning("No candidates provided for reranking");
            return Enumerable.Empty<RerankResult>();
        }

        _logger.LogInformation("Reranking {Count} candidates with local similarity", candidateList.Count);

        var results = new List<RerankResult>();

        // Prepare query for similarity calculation
        var queryTokens = TokenizeText(query);
        var queryEmbedding = await GetQueryEmbeddingAsync(query, cancellationToken);

        foreach (var candidate in candidateList)
        {
            var rerankScore = await CalculateLocalRerankScore(
                query, 
                queryTokens, 
                queryEmbedding,
                candidate, 
                cancellationToken);

            results.Add(new RerankResult
            {
                Id = candidate.Id,
                DocumentId = candidate.DocumentId,
                ChunkId = candidate.ChunkId,
                Content = candidate.Content,
                InitialScore = candidate.InitialScore,
                InitialRank = candidate.InitialRank,
                RerankScore = rerankScore,
                Metadata = candidate.Metadata,
                Explanation = rerankOptions.IncludeExplanation ? 
                    GenerateExplanation(candidate.InitialScore, rerankScore) : null
            });
        }

        // Sort by rerank score and assign new ranks
        var rankedResults = results
            .OrderByDescending(r => r.RerankScore)
            .Select((r, index) =>
            {
                r.NewRank = index + 1;
                return r;
            })
            .Where(r => r.RerankScore >= rerankOptions.ScoreThreshold)
            .Take(rerankOptions.TopN)
            .ToList();

        _logger.LogInformation("Local reranking completed: {Original} → {Final} results", 
            candidateList.Count, rankedResults.Count);

        return rankedResults;
    }

    public RerankModelInfo GetModelInfo()
    {
        return new RerankModelInfo
        {
            Name = "Local Similarity Reranker",
            Type = RerankModel.Local,
            Version = "1.0.0",
            SupportsMultilingual = true,
            MaxInputLength = 2048,
            EstimatedLatencyMs = 10.0f,
            RequiresApiKey = false,
            Capabilities = new Dictionary<string, object>
            {
                ["supports_tf_idf"] = true,
                ["supports_bm25"] = true,
                ["supports_embeddings"] = _embeddingService != null,
                ["supports_explanation"] = true
            }
        };
    }

    private async Task<float> CalculateLocalRerankScore(
        string query,
        List<string> queryTokens,
        float[]? queryEmbedding,
        RetrievalCandidate candidate,
        CancellationToken cancellationToken)
    {
        var contentLength = Math.Min(candidate.Content.Length, _options.MaxContentLength);
        var content = candidate.Content.Substring(0, contentLength);
        var contentTokens = TokenizeText(content);

        // Component 1: TF-IDF Similarity (40% weight)
        var tfidfScore = CalculateTfIdfSimilarity(queryTokens, contentTokens);
        
        // Component 2: BM25 Score (30% weight)
        var bm25Score = CalculateBM25Score(queryTokens, contentTokens, content.Length);
        
        // Component 3: Semantic Similarity (30% weight if available)
        var semanticScore = 0.0f;
        if (_embeddingService != null && queryEmbedding != null)
        {
            semanticScore = await CalculateSemanticSimilarity(
                queryEmbedding, content, cancellationToken);
        }

        // Combine scores with weights
        var combinedScore = 
            (tfidfScore * _options.TfIdfWeight) +
            (bm25Score * _options.Bm25Weight) +
            (semanticScore * _options.SemanticWeight);

        // Apply position bias (earlier results get slight boost)
        var positionBoost = Math.Max(0, (10 - candidate.InitialRank) * 0.01f);
        
        var finalScore = combinedScore + positionBoost;

        return Math.Max(0.0f, Math.Min(1.0f, finalScore));
    }

    private float CalculateTfIdfSimilarity(List<string> queryTokens, List<string> contentTokens)
    {
        if (!queryTokens.Any() || !contentTokens.Any())
            return 0.0f;

        var queryTermFreq = CalculateTermFrequency(queryTokens);
        var contentTermFreq = CalculateTermFrequency(contentTokens);

        var intersection = queryTermFreq.Keys.Intersect(contentTermFreq.Keys);
        if (!intersection.Any())
            return 0.0f;

        var similarity = 0.0f;
        var queryNorm = 0.0f;
        var contentNorm = 0.0f;

        foreach (var term in queryTermFreq.Keys.Union(contentTermFreq.Keys))
        {
            var queryTf = queryTermFreq.GetValueOrDefault(term, 0);
            var contentTf = contentTermFreq.GetValueOrDefault(term, 0);

            // Simple TF weighting (can be enhanced with IDF)
            var queryWeight = queryTf > 0 ? 1 + Math.Log(queryTf) : 0;
            var contentWeight = contentTf > 0 ? 1 + Math.Log(contentTf) : 0;

            similarity += queryWeight * contentWeight;
            queryNorm += queryWeight * queryWeight;
            contentNorm += contentWeight * contentWeight;
        }

        if (queryNorm == 0 || contentNorm == 0)
            return 0.0f;

        return (float)(similarity / (Math.Sqrt(queryNorm) * Math.Sqrt(contentNorm)));
    }

    private float CalculateBM25Score(List<string> queryTokens, List<string> contentTokens, int contentLength)
    {
        if (!queryTokens.Any() || !contentTokens.Any())
            return 0.0f;

        var k1 = _options.BM25K1;
        var b = _options.BM25B;
        var avgDocLength = _options.AverageDocumentLength;

        var contentTermFreq = CalculateTermFrequency(contentTokens);
        var score = 0.0f;

        foreach (var queryTerm in queryTokens.Distinct())
        {
            if (!contentTermFreq.TryGetValue(queryTerm, out var termFreq))
                continue;

            var idf = Math.Log((1000.0 + 0.5) / (1.0 + 0.5)); // Simplified IDF
            var tfComponent = (termFreq * (k1 + 1)) / 
                (termFreq + k1 * (1 - b + b * contentLength / avgDocLength));

            score += (float)(idf * tfComponent);
        }

        return Math.Max(0.0f, score / queryTokens.Count);
    }

    private async Task<float> CalculateSemanticSimilarity(
        float[] queryEmbedding,
        string content,
        CancellationToken cancellationToken)
    {
        if (_embeddingService == null)
            return 0.0f;

        try
        {
            var contentEmbedding = await _embeddingService.GenerateEmbeddingAsync(content, cancellationToken);
            return CalculateCosineSimilarity(queryEmbedding, contentEmbedding.Values);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate semantic similarity");
            return 0.0f;
        }
    }

    private float CalculateCosineSimilarity(float[] vec1, float[] vec2)
    {
        if (vec1.Length != vec2.Length)
            return 0.0f;

        var dotProduct = 0.0f;
        var norm1 = 0.0f;
        var norm2 = 0.0f;

        for (int i = 0; i < vec1.Length; i++)
        {
            dotProduct += vec1[i] * vec2[i];
            norm1 += vec1[i] * vec1[i];
            norm2 += vec2[i] * vec2[i];
        }

        if (norm1 == 0 || norm2 == 0)
            return 0.0f;

        return dotProduct / (float)(Math.Sqrt(norm1) * Math.Sqrt(norm2));
    }

    private async Task<float[]?> GetQueryEmbeddingAsync(string query, CancellationToken cancellationToken)
    {
        if (_embeddingService == null)
            return null;

        try
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
            return embedding.Values;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate query embedding");
            return null;
        }
    }

    private List<string> TokenizeText(string text)
    {
        return text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?' }, 
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 1)
            .ToList();
    }

    private Dictionary<string, int> CalculateTermFrequency(List<string> tokens)
    {
        var termFreq = new Dictionary<string, int>();
        
        foreach (var token in tokens)
        {
            if (termFreq.ContainsKey(token))
                termFreq[token]++;
            else
                termFreq[token] = 1;
        }
        
        return termFreq;
    }

    private string GenerateExplanation(float initialScore, float rerankScore)
    {
        var change = rerankScore - initialScore;
        var direction = change > 0 ? "increased" : "decreased";
        var magnitude = Math.Abs(change);

        if (magnitude < 0.01f)
            return "Score remained similar after reranking";
        
        return $"Score {direction} by {magnitude:F3} due to local similarity analysis";
    }
}

/// <summary>
/// Configuration options for LocalReranker
/// </summary>
public class LocalRerankOptions
{
    /// <summary>
    /// Weight for TF-IDF similarity component
    /// </summary>
    public float TfIdfWeight { get; set; } = 0.4f;

    /// <summary>
    /// Weight for BM25 score component
    /// </summary>
    public float Bm25Weight { get; set; } = 0.3f;

    /// <summary>
    /// Weight for semantic similarity component (if available)
    /// </summary>
    public float SemanticWeight { get; set; } = 0.3f;

    /// <summary>
    /// BM25 k1 parameter
    /// </summary>
    public float BM25K1 { get; set; } = 1.2f;

    /// <summary>
    /// BM25 b parameter
    /// </summary>
    public float BM25B { get; set; } = 0.75f;

    /// <summary>
    /// Assumed average document length for BM25
    /// </summary>
    public float AverageDocumentLength { get; set; } = 100.0f;

    /// <summary>
    /// Maximum content length to process
    /// </summary>
    public int MaxContentLength { get; set; } = 1024;
}