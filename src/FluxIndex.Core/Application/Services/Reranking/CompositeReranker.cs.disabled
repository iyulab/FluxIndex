using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services.Reranking;

/// <summary>
/// Composite reranker that can dynamically select or combine multiple reranking strategies
/// </summary>
public class CompositeReranker : IReranker
{
    private readonly ILogger<CompositeReranker> _logger;
    private readonly CompositeRerankerOptions _options;
    private readonly Dictionary<string, IReranker> _rerankers;
    private readonly IRerankerSelector? _selector;

    public CompositeReranker(
        IEnumerable<IReranker> rerankers,
        CompositeRerankerOptions options,
        ILogger<CompositeReranker> logger,
        IRerankerSelector? selector = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _selector = selector;
        
        // Register rerankers
        _rerankers = new Dictionary<string, IReranker>();
        foreach (var reranker in rerankers)
        {
            var type = reranker.GetType().Name;
            _rerankers[type] = reranker;
            _logger.LogDebug("Registered reranker: {Type}", type);
        }

        if (!_rerankers.Any())
        {
            throw new ArgumentException("At least one reranker must be provided", nameof(rerankers));
        }
    }

    public async Task<IEnumerable<Document>> RerankAsync(
        string query,
        IEnumerable<Document> documents,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        var docList = documents.ToList();
        if (!docList.Any())
            return docList;

        // Select reranking strategy
        var strategy = await SelectStrategyAsync(query, docList, cancellationToken);
        _logger.LogDebug("Selected reranking strategy: {Strategy}", strategy);

        switch (strategy)
        {
            case RerankerStrategy.Single:
                return await SingleRerankerAsync(query, docList, topK, cancellationToken);
                
            case RerankerStrategy.Sequential:
                return await SequentialRerankerAsync(query, docList, topK, cancellationToken);
                
            case RerankerStrategy.Ensemble:
                return await EnsembleRerankerAsync(query, docList, topK, cancellationToken);
                
            case RerankerStrategy.Adaptive:
                return await AdaptiveRerankerAsync(query, docList, topK, cancellationToken);
                
            default:
                throw new NotSupportedException($"Strategy {strategy} is not supported");
        }
    }

    /// <summary>
    /// Select the best reranking strategy based on query and document characteristics
    /// </summary>
    private async Task<RerankerStrategy> SelectStrategyAsync(
        string query,
        List<Document> documents,
        CancellationToken cancellationToken)
    {
        // Use custom selector if provided
        if (_selector != null)
        {
            return await _selector.SelectStrategyAsync(query, documents, cancellationToken);
        }

        // Default strategy selection logic
        if (_options.ForceStrategy != RerankerStrategy.Auto)
        {
            return _options.ForceStrategy;
        }

        // Analyze query complexity
        var queryComplexity = AnalyzeQueryComplexity(query);
        var documentCount = documents.Count;

        // Simple heuristics for strategy selection
        if (queryComplexity == QueryComplexity.Simple && documentCount <= 20)
        {
            // For simple queries with few documents, use fastest reranker
            return RerankerStrategy.Single;
        }
        else if (queryComplexity == QueryComplexity.Complex || documentCount > 100)
        {
            // For complex queries or many documents, use ensemble for best quality
            return RerankerStrategy.Ensemble;
        }
        else if (_options.QualityTarget == QualityTarget.Speed)
        {
            // Speed priority: use single fast reranker
            return RerankerStrategy.Single;
        }
        else if (_options.QualityTarget == QualityTarget.Quality)
        {
            // Quality priority: use ensemble or sequential
            return _rerankers.Count >= 3 ? RerankerStrategy.Ensemble : RerankerStrategy.Sequential;
        }
        else
        {
            // Balanced: use sequential for good quality/speed trade-off
            return RerankerStrategy.Sequential;
        }
    }

    /// <summary>
    /// Use a single reranker (fastest)
    /// </summary>
    private async Task<IEnumerable<Document>> SingleRerankerAsync(
        string query,
        List<Document> documents,
        int topK,
        CancellationToken cancellationToken)
    {
        // Select the best single reranker
        var reranker = SelectSingleReranker();
        _logger.LogDebug("Using single reranker: {Type}", reranker.GetType().Name);
        
        return await reranker.RerankAsync(query, documents, topK, cancellationToken);
    }

    /// <summary>
    /// Use multiple rerankers in sequence (progressive refinement)
    /// </summary>
    private async Task<IEnumerable<Document>> SequentialRerankerAsync(
        string query,
        List<Document> documents,
        int topK,
        CancellationToken cancellationToken)
    {
        var currentDocs = documents;
        var sequentialRerankers = GetSequentialRerankers();
        
        foreach (var (reranker, intermediateCutoff) in sequentialRerankers)
        {
            _logger.LogDebug("Sequential reranking with {Type}, cutoff: {Cutoff}",
                reranker.GetType().Name, intermediateCutoff);
            
            currentDocs = (await reranker.RerankAsync(
                query, 
                currentDocs, 
                intermediateCutoff, 
                cancellationToken)).ToList();
            
            if (!currentDocs.Any())
                break;
        }
        
        return currentDocs.Take(topK);
    }

    /// <summary>
    /// Use multiple rerankers and combine their scores (highest quality)
    /// </summary>
    private async Task<IEnumerable<Document>> EnsembleRerankerAsync(
        string query,
        List<Document> documents,
        int topK,
        CancellationToken cancellationToken)
    {
        var ensembleRerankers = GetEnsembleRerankers();
        var rerankTasks = new List<Task<IEnumerable<Document>>>();
        
        // Run all rerankers in parallel
        foreach (var reranker in ensembleRerankers)
        {
            rerankTasks.Add(reranker.RerankAsync(query, documents, documents.Count, cancellationToken));
        }
        
        var allResults = await Task.WhenAll(rerankTasks);
        
        // Combine scores using weighted voting
        var scoreAggregator = new Dictionary<string, List<float>>();
        var documentMap = new Dictionary<string, Document>();
        
        for (int i = 0; i < allResults.Length; i++)
        {
            var weight = _options.EnsembleWeights?.ElementAtOrDefault(i) ?? 1.0f;
            var results = allResults[i].ToList();
            
            for (int j = 0; j < results.Count; j++)
            {
                var doc = results[j];
                var docId = doc.Id;
                
                if (!scoreAggregator.ContainsKey(docId))
                {
                    scoreAggregator[docId] = new List<float>();
                    documentMap[docId] = doc;
                }
                
                // Combine using rank and score
                var rankScore = 1.0f / (j + 1); // Higher rank = higher score
                var normalizedScore = doc.Score * weight;
                scoreAggregator[docId].Add((rankScore + normalizedScore) / 2);
            }
        }
        
        // Calculate final scores
        var finalScores = scoreAggregator.Select(kvp => new
        {
            Document = documentMap[kvp.Key],
            Score = kvp.Value.Average() // Can also use other aggregation methods
        })
        .OrderByDescending(x => x.Score)
        .Take(topK)
        .Select((x, index) =>
        {
            x.Document.Score = x.Score;
            x.Document.Metadata ??= new();
            x.Document.Metadata["ensemble_score"] = x.Score.ToString("F4");
            x.Document.Metadata["ensemble_position"] = (index + 1).ToString();
            return x.Document;
        });
        
        _logger.LogDebug("Ensemble reranking complete with {Count} rerankers", ensembleRerankers.Length);
        
        return finalScores;
    }

    /// <summary>
    /// Adaptively select rerankers based on query characteristics
    /// </summary>
    private async Task<IEnumerable<Document>> AdaptiveRerankerAsync(
        string query,
        List<Document> documents,
        int topK,
        CancellationToken cancellationToken)
    {
        // Analyze query to determine best approach
        var queryFeatures = AnalyzeQueryFeatures(query);
        
        // Select rerankers based on query features
        if (queryFeatures.IsMultilingual)
        {
            // Use Cohere for multilingual queries
            if (_rerankers.TryGetValue(nameof(CohereReranker), out var cohereReranker))
            {
                _logger.LogDebug("Using Cohere for multilingual query");
                return await cohereReranker.RerankAsync(query, documents, topK, cancellationToken);
            }
        }
        
        if (queryFeatures.RequiresDeepUnderstanding)
        {
            // Use ONNX Cross-Encoder for complex semantic queries
            if (_rerankers.TryGetValue(nameof(OnnxCrossEncoderReranker), out var onnxReranker))
            {
                _logger.LogDebug("Using ONNX Cross-Encoder for complex query");
                return await onnxReranker.RerankAsync(query, documents, topK, cancellationToken);
            }
        }
        
        // Default to local reranker for simple queries
        if (_rerankers.TryGetValue(nameof(LocalReranker), out var localReranker))
        {
            _logger.LogDebug("Using Local reranker for simple query");
            return await localReranker.RerankAsync(query, documents, topK, cancellationToken);
        }
        
        // Fallback to first available reranker
        return await _rerankers.Values.First().RerankAsync(query, documents, topK, cancellationToken);
    }

    /// <summary>
    /// Select the best single reranker based on configuration
    /// </summary>
    private IReranker SelectSingleReranker()
    {
        // Priority order based on quality target
        var priorityOrder = _options.QualityTarget switch
        {
            QualityTarget.Speed => new[] { nameof(LocalReranker), nameof(CohereReranker), nameof(OnnxCrossEncoderReranker) },
            QualityTarget.Quality => new[] { nameof(OnnxCrossEncoderReranker), nameof(CohereReranker), nameof(LocalReranker) },
            _ => new[] { nameof(CohereReranker), nameof(OnnxCrossEncoderReranker), nameof(LocalReranker) }
        };
        
        foreach (var name in priorityOrder)
        {
            if (_rerankers.TryGetValue(name, out var reranker))
                return reranker;
        }
        
        return _rerankers.Values.First();
    }

    /// <summary>
    /// Get rerankers for sequential processing
    /// </summary>
    private (IReranker reranker, int cutoff)[] GetSequentialRerankers()
    {
        var rerankers = new List<(IReranker, int)>();
        
        // First pass: fast local reranker to reduce candidates
        if (_rerankers.TryGetValue(nameof(LocalReranker), out var localReranker))
        {
            rerankers.Add((localReranker, 50));
        }
        
        // Second pass: high-quality reranker for final ranking
        if (_rerankers.TryGetValue(nameof(OnnxCrossEncoderReranker), out var onnxReranker))
        {
            rerankers.Add((onnxReranker, 20));
        }
        else if (_rerankers.TryGetValue(nameof(CohereReranker), out var cohereReranker))
        {
            rerankers.Add((cohereReranker, 20));
        }
        
        if (!rerankers.Any())
        {
            rerankers.Add((_rerankers.Values.First(), 20));
        }
        
        return rerankers.ToArray();
    }

    /// <summary>
    /// Get rerankers for ensemble processing
    /// </summary>
    private IReranker[] GetEnsembleRerankers()
    {
        // Use all available rerankers for ensemble
        var rerankers = new List<IReranker>();
        
        if (_options.MaxEnsembleRerankers > 0)
        {
            return _rerankers.Values.Take(_options.MaxEnsembleRerankers).ToArray();
        }
        
        return _rerankers.Values.ToArray();
    }

    /// <summary>
    /// Analyze query complexity
    /// </summary>
    private QueryComplexity AnalyzeQueryComplexity(string query)
    {
        var wordCount = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var hasOperators = query.Contains(" AND ") || query.Contains(" OR ") || query.Contains(" NOT ");
        var hasQuotes = query.Contains('"');
        
        if (wordCount <= 3 && !hasOperators && !hasQuotes)
            return QueryComplexity.Simple;
        
        if (wordCount > 10 || hasOperators || hasQuotes)
            return QueryComplexity.Complex;
        
        return QueryComplexity.Medium;
    }

    /// <summary>
    /// Analyze query features for adaptive selection
    /// </summary>
    private QueryFeatures AnalyzeQueryFeatures(string query)
    {
        return new QueryFeatures
        {
            IsMultilingual = ContainsNonAscii(query),
            RequiresDeepUnderstanding = query.Length > 50 || query.Contains("?"),
            IsNavigational = query.StartsWith("find ") || query.StartsWith("get "),
            IsInformational = query.Contains("what ") || query.Contains("how ") || query.Contains("why ")
        };
    }

    private static bool ContainsNonAscii(string text)
    {
        return text.Any(c => c > 127);
    }
}

/// <summary>
/// Configuration for composite reranker
/// </summary>
public class CompositeRerankerOptions
{
    /// <summary>
    /// Force a specific strategy (overrides auto-selection)
    /// </summary>
    public RerankerStrategy ForceStrategy { get; set; } = RerankerStrategy.Auto;

    /// <summary>
    /// Quality vs speed target
    /// </summary>
    public QualityTarget QualityTarget { get; set; } = QualityTarget.Balanced;

    /// <summary>
    /// Weights for ensemble voting (if using ensemble strategy)
    /// </summary>
    public float[]? EnsembleWeights { get; set; }

    /// <summary>
    /// Maximum number of rerankers to use in ensemble
    /// </summary>
    public int MaxEnsembleRerankers { get; set; } = 3;

    /// <summary>
    /// Enable A/B testing mode
    /// </summary>
    public bool EnableABTesting { get; set; } = false;

    /// <summary>
    /// A/B test sample rate (0-1)
    /// </summary>
    public float ABTestSampleRate { get; set; } = 0.1f;
}

/// <summary>
/// Reranking strategies
/// </summary>
public enum RerankerStrategy
{
    Auto,
    Single,
    Sequential,
    Ensemble,
    Adaptive
}

/// <summary>
/// Query complexity levels
/// </summary>
public enum QueryComplexity
{
    Simple,
    Medium,
    Complex
}

/// <summary>
/// Query feature analysis
/// </summary>
public class QueryFeatures
{
    public bool IsMultilingual { get; set; }
    public bool RequiresDeepUnderstanding { get; set; }
    public bool IsNavigational { get; set; }
    public bool IsInformational { get; set; }
}

/// <summary>
/// Interface for custom strategy selection
/// </summary>
public interface IRerankerSelector
{
    Task<RerankerStrategy> SelectStrategyAsync(
        string query,
        IEnumerable<Document> documents,
        CancellationToken cancellationToken = default);
}