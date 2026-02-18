using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using QueryType = FluxIndex.Core.Application.Interfaces.QueryType;

namespace FluxIndex.Core.Application.Services.Fusion;

/// <summary>
/// Learning-based fusion service that uses lightweight machine learning
/// to predict optimal fusion weights based on query characteristics.
/// Implements online learning for continuous improvement.
/// </summary>
public partial class LearningBasedFusionService : ILearningBasedFusionService
{
    private readonly ILogger<LearningBasedFusionService> _logger;

    // Learned parameters per query type
    private readonly ConcurrentDictionary<QueryType, LearnedQueryTypeModel> _queryTypeModels;

    // Training data storage for online learning
    private readonly ConcurrentDictionary<string, FusionTrainingExample> _trainingBuffer;

    // Feedback storage for relevance tracking
    private readonly ConcurrentDictionary<string, List<(string resultId, double relevance)>> _feedbackBuffer;

    // Model statistics
    private int _trainingCount;
    private int _onlineUpdateCount;
    private DateTimeOffset? _lastTrainedAt;
    private DateTimeOffset? _lastUpdatedAt;
    private bool _isModelTrained;

    // Online learning parameters
    private const double LearningRate = 0.1;
    private const int MinTrainingExamples = 10;
    private const int MaxTrainingBufferSize = 1000;

    // Feature normalization parameters
    private double[] _featureMeans;
    private double[] _featureStdDevs;

    public LearningBasedFusionService(ILogger<LearningBasedFusionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _queryTypeModels = new ConcurrentDictionary<QueryType, LearnedQueryTypeModel>();
        _trainingBuffer = new ConcurrentDictionary<string, FusionTrainingExample>();
        _feedbackBuffer = new ConcurrentDictionary<string, List<(string, double)>>();
        _featureMeans = new double[11];  // Feature vector size
        _featureStdDevs = Enumerable.Repeat(1.0, 11).ToArray();

        InitializeDefaultModels();
    }

    /// <inheritdoc />
    public async Task TrainAsync(
        IEnumerable<FusionTrainingExample> examples,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(examples);

        var examplesList = examples.ToList();
        if (examplesList.Count == 0)
        {
            LogLearningBasedFusion14(_logger);
            return;
        }

        LogLearningBasedFusion13(_logger, examplesList.Count);

        try
        {
            // 1. Extract and normalize features
            var featuresAndLabels = ExtractFeaturesAndLabels(examplesList);

            // 2. Update feature normalization parameters
            UpdateNormalizationParameters(featuresAndLabels.features);

            // 3. Group examples by query type
            var groupedByType = examplesList.GroupBy(e => DetectQueryType(e.Query));

            // 4. Train model for each query type
            foreach (var group in groupedByType)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await TrainQueryTypeModelAsync(group.Key, group.ToList(), cancellationToken);
            }

            // 5. Store examples in training buffer (bounded)
            foreach (var example in examplesList.TakeLast(MaxTrainingBufferSize))
            {
                var key = GenerateExampleKey(example);
                _trainingBuffer[key] = example;
            }

            _trainingCount += examplesList.Count;
            _lastTrainedAt = DateTimeOffset.UtcNow;
            _isModelTrained = _trainingCount >= MinTrainingExamples;

            if (_logger.IsEnabled(LogLevel.Warning))
                LogLearningBasedFusion12(_logger, _trainingCount, _isModelTrained);
        }
        catch (OperationCanceledException)
        {
            LogLearningBasedFusion11(_logger);
            throw;
        }
        catch (Exception ex)
        {
            LogLearningBasedFusion10(_logger, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<FusionWeightPrediction> PredictWeightsAsync(
        string query,
        IEnumerable<RankedResult> vectorResults,
        IEnumerable<RankedResult> sparseResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(vectorResults);
        ArgumentNullException.ThrowIfNull(sparseResults);

        var vectorList = vectorResults.ToList();
        var sparseList = sparseResults.ToList();

        // Extract features
        var features = ExtractQueryFeatures(query, vectorList, sparseList);
        var queryType = features.QueryType;

        // Check if we have a trained model for this query type
        if (_isModelTrained && _queryTypeModels.TryGetValue(queryType, out var model))
        {
            var prediction = PredictFromModel(features, model);
            prediction = prediction with
            {
                IsLearnedPrediction = true,
                Reasoning = $"Learned prediction for query type {queryType} " +
                           $"(confidence: {prediction.Confidence:P0}, " +
                           $"based on {model.TrainingCount} examples)"
            };

            if (_logger.IsEnabled(LogLevel.Warning))
                LogLearningBasedFusion9(_logger, queryType, prediction.Weights.VectorWeight, prediction.Weights.SparseWeight);

            return await Task.FromResult(prediction);
        }

        // Fall back to heuristic-based prediction
        var heuristicPrediction = PredictFromHeuristics(features, vectorList, sparseList);

        if (_logger.IsEnabled(LogLevel.Warning))
            LogLearningBasedFusion8(_logger, queryType, heuristicPrediction.Weights.VectorWeight, heuristicPrediction.Weights.SparseWeight);

        return await Task.FromResult(heuristicPrediction);
    }

    /// <inheritdoc />
    public async Task UpdateOnlineAsync(
        FusionFeedback feedback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feedback);

        LogLearningBasedFusion7(_logger, feedback.Query);

        try
        {
            // Calculate relevance score from feedback
            var relevanceScore = CalculateRelevanceFromFeedback(feedback);
            var queryType = feedback.Features.QueryType;

            // Get or create model for this query type
            var model = _queryTypeModels.GetOrAdd(queryType, _ => CreateDefaultModel(queryType));

            // Compute gradient and update weights
            var featureVector = NormalizeFeatures(feedback.Features.ToFeatureVector());
            var predictedScore = ComputePredictedScore(featureVector, model);
            var error = relevanceScore - predictedScore;

            // Gradient descent update
            lock (model)
            {
                for (int i = 0; i < model.Weights.Length && i < featureVector.Length; i++)
                {
                    model.Weights[i] += LearningRate * error * featureVector[i];
                }

                // Update running average for optimal weights
                var learningFactor = 1.0 / (model.OnlineUpdateCount + 1);
                model.AverageVectorWeight = (1 - learningFactor) * model.AverageVectorWeight +
                                            learningFactor * feedback.UsedWeights.VectorWeight * relevanceScore;
                model.AverageSparseWeight = (1 - learningFactor) * model.AverageSparseWeight +
                                            learningFactor * feedback.UsedWeights.SparseWeight * relevanceScore;

                model.OnlineUpdateCount++;
                model.LastUpdatedAt = DateTimeOffset.UtcNow;
            }

            _onlineUpdateCount++;
            _lastUpdatedAt = DateTimeOffset.UtcNow;

            // Create training example from feedback for future batch training
            var example = CreateExampleFromFeedback(feedback, relevanceScore);
            var key = GenerateExampleKey(example);
            _trainingBuffer[key] = example;

            // Trim buffer if too large
            while (_trainingBuffer.Count > MaxTrainingBufferSize)
            {
                var oldestKey = _trainingBuffer.Keys.FirstOrDefault();
                if (oldestKey != null)
                    _trainingBuffer.TryRemove(oldestKey, out _);
            }

            if (_logger.IsEnabled(LogLevel.Warning))
                LogLearningBasedFusion6(_logger, queryType, relevanceScore, error);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LogLearningBasedFusion5(_logger, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RecordFeedbackAsync(
        string queryId,
        string resultId,
        double relevanceScore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryId);
        ArgumentNullException.ThrowIfNull(resultId);

        relevanceScore = Math.Clamp(relevanceScore, 0.0, 1.0);

        var feedbackList = _feedbackBuffer.GetOrAdd(queryId, _ => new List<(string, double)>());
        lock (feedbackList)
        {
            feedbackList.Add((resultId, relevanceScore));
        }

        if (_logger.IsEnabled(LogLevel.Warning))
            LogLearningBasedFusion4(_logger, queryId, resultId, relevanceScore);

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public FusionModelStatistics GetModelStatistics()
    {
        var queryTypeDistribution = new Dictionary<QueryType, int>();
        var learnedWeightsPerType = new Dictionary<QueryType, FusionWeights>();

        foreach (var kvp in _queryTypeModels)
        {
            queryTypeDistribution[kvp.Key] = kvp.Value.TrainingCount;

            var total = kvp.Value.AverageVectorWeight + kvp.Value.AverageSparseWeight;
            if (total > 0)
            {
                learnedWeightsPerType[kvp.Key] = new FusionWeights
                {
                    VectorWeight = kvp.Value.AverageVectorWeight / total,
                    SparseWeight = kvp.Value.AverageSparseWeight / total
                };
            }
        }

        return new FusionModelStatistics
        {
            TrainingExampleCount = _trainingCount,
            OnlineUpdateCount = _onlineUpdateCount,
            TrainingAccuracy = CalculateTrainingAccuracy(),
            AveragePredictionConfidence = CalculateAverageConfidence(),
            QueryTypeDistribution = queryTypeDistribution,
            LearnedWeightsPerType = learnedWeightsPerType,
            LastTrainedAt = _lastTrainedAt,
            LastUpdatedAt = _lastUpdatedAt,
            IsModelTrained = _isModelTrained,
            ModelVersion = "1.0.0"
        };
    }

    /// <inheritdoc />
    public async Task<FusionModelState> ExportModelAsync(CancellationToken cancellationToken = default)
    {
        var learnedWeights = new Dictionary<string, double[]>();
        var queryTypeCentroids = new Dictionary<string, double[]>();

        foreach (var kvp in _queryTypeModels)
        {
            learnedWeights[kvp.Key.ToString()] = kvp.Value.Weights.ToArray();
            queryTypeCentroids[kvp.Key.ToString()] = kvp.Value.FeatureCentroid.ToArray();
        }

        var state = new FusionModelState
        {
            Version = "1.0.0",
            LearnedWeights = learnedWeights,
            FeatureMeans = _featureMeans.ToArray(),
            FeatureStdDevs = _featureStdDevs.ToArray(),
            QueryTypeCentroids = queryTypeCentroids,
            Statistics = GetModelStatistics(),
            ExportedAt = DateTimeOffset.UtcNow
        };

        // Calculate checksum
        var json = JsonSerializer.Serialize(new
        {
            state.LearnedWeights,
            state.FeatureMeans,
            state.FeatureStdDevs,
            state.QueryTypeCentroids
        });
        state = state with { Checksum = ComputeChecksum(json) };

        LogLearningBasedFusion3(_logger, learnedWeights.Count);

        return await Task.FromResult(state);
    }

    /// <inheritdoc />
    public async Task ImportModelAsync(FusionModelState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Verify checksum if present
        if (!string.IsNullOrEmpty(state.Checksum))
        {
            var json = JsonSerializer.Serialize(new
            {
                state.LearnedWeights,
                state.FeatureMeans,
                state.FeatureStdDevs,
                state.QueryTypeCentroids
            });
            var computedChecksum = ComputeChecksum(json);
            if (computedChecksum != state.Checksum)
            {
                throw new InvalidOperationException("Model state checksum mismatch - data may be corrupted");
            }
        }

        // Import feature normalization
        if (state.FeatureMeans.Length > 0)
            _featureMeans = state.FeatureMeans.ToArray();
        if (state.FeatureStdDevs.Length > 0)
            _featureStdDevs = state.FeatureStdDevs.ToArray();

        // Import query type models
        _queryTypeModels.Clear();
        foreach (var kvp in state.LearnedWeights)
        {
            if (Enum.TryParse<QueryType>(kvp.Key, out var queryType))
            {
                var model = new LearnedQueryTypeModel
                {
                    QueryType = queryType,
                    Weights = kvp.Value.ToArray(),
                    FeatureCentroid = state.QueryTypeCentroids.GetValueOrDefault(kvp.Key, new double[11]),
                    TrainingCount = state.Statistics.QueryTypeDistribution.GetValueOrDefault(queryType, 0)
                };

                if (state.Statistics.LearnedWeightsPerType.TryGetValue(queryType, out var weights))
                {
                    model.AverageVectorWeight = weights.VectorWeight;
                    model.AverageSparseWeight = weights.SparseWeight;
                }

                _queryTypeModels[queryType] = model;
            }
        }

        // Restore statistics
        _trainingCount = state.Statistics.TrainingExampleCount;
        _onlineUpdateCount = state.Statistics.OnlineUpdateCount;
        _lastTrainedAt = state.Statistics.LastTrainedAt;
        _lastUpdatedAt = state.Statistics.LastUpdatedAt;
        _isModelTrained = state.Statistics.IsModelTrained;

        if (_logger.IsEnabled(LogLevel.Warning))
            LogLearningBasedFusion2(_logger, _queryTypeModels.Count, _trainingCount);

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public void ResetModel()
    {
        _queryTypeModels.Clear();
        _trainingBuffer.Clear();
        _feedbackBuffer.Clear();
        _trainingCount = 0;
        _onlineUpdateCount = 0;
        _lastTrainedAt = null;
        _lastUpdatedAt = null;
        _isModelTrained = false;
        _featureMeans = new double[11];
        _featureStdDevs = Enumerable.Repeat(1.0, 11).ToArray();

        InitializeDefaultModels();

        LogLearningBasedFusion1(_logger);
    }

    #region Private Methods

    private void InitializeDefaultModels()
    {
        // Initialize with heuristic-based defaults for each query type
        // Using existing QueryType enum values from IQueryComplexityAnalyzer
        var defaults = new Dictionary<QueryType, (double vector, double sparse)>
        {
            { QueryType.SimpleKeyword, (0.3, 0.7) },         // Simple keyword queries favor sparse
            { QueryType.NaturalQuestion, (0.7, 0.3) },       // Natural questions favor semantic
            { QueryType.ComplexSearch, (0.6, 0.4) },         // Complex searches slightly favor vector
            { QueryType.ReasoningQuery, (0.8, 0.2) },        // Reasoning queries favor semantic
            { QueryType.ComparisonQuery, (0.6, 0.4) },       // Comparison queries slightly favor vector
            { QueryType.TemporalQuery, (0.5, 0.5) },         // Temporal queries balanced
            { QueryType.MultiHopQuery, (0.7, 0.3) }          // Multi-hop queries favor semantic
        };

        foreach (var kvp in defaults)
        {
            _queryTypeModels[kvp.Key] = CreateDefaultModel(kvp.Key, kvp.Value.vector, kvp.Value.sparse);
        }
    }

    private static LearnedQueryTypeModel CreateDefaultModel(QueryType queryType, double vectorWeight = 0.5, double sparseWeight = 0.5)
    {
        return new LearnedQueryTypeModel
        {
            QueryType = queryType,
            Weights = new double[11],  // Feature vector size
            FeatureCentroid = new double[11],
            AverageVectorWeight = vectorWeight,
            AverageSparseWeight = sparseWeight,
            TrainingCount = 0,
            OnlineUpdateCount = 0
        };
    }

    private static QueryPredictionFeatures ExtractQueryFeatures(
        string query,
        List<RankedResult> vectorResults,
        List<RankedResult> sparseResults)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var uniqueTerms = words.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var queryType = DetectQueryType(query);
        var complexity = DetectComplexity(query);

        var vectorScores = vectorResults.Select(r => (double)r.Score).ToList();
        var sparseScores = sparseResults.Select(r => (double)r.Score).ToList();

        // Calculate result overlap
        var vectorIds = new HashSet<string>(vectorResults.Take(10).Select(r => r.Id));
        var sparseIds = new HashSet<string>(sparseResults.Take(10).Select(r => r.Id));
        var overlapCount = vectorIds.Intersect(sparseIds).Count();
        var unionCount = vectorIds.Union(sparseIds).Count();
        var overlapRatio = unionCount > 0 ? (double)overlapCount / unionCount : 0;

        return new QueryPredictionFeatures
        {
            QueryLength = words.Length,
            UniqueTermCount = uniqueTerms,
            QueryType = queryType,
            Complexity = complexity,
            VectorAvgScore = vectorScores.Count != 0 ? vectorScores.Average() : 0,
            VectorScoreVariance = CalculateVariance(vectorScores),
            SparseAvgScore = sparseScores.Count != 0 ? sparseScores.Average() : 0,
            SparseScoreVariance = CalculateVariance(sparseScores),
            ResultOverlapRatio = overlapRatio,
            ContainsTechnicalTerms = ContainsTechnicalTerms(query),
            IsNaturalLanguageQuestion = IsNaturalLanguageQuestion(query)
        };
    }

    private static QueryType DetectQueryType(string query)
    {
        var lowerQuery = query.ToLowerInvariant();

        // Check for multi-hop indicators first
        if (ContainsMultiHopIndicators(lowerQuery))
            return QueryType.MultiHopQuery;

        // Check for reasoning indicators
        if (ContainsReasoningIndicators(lowerQuery))
            return QueryType.ReasoningQuery;

        // Check for comparison indicators
        if (ContainsComparisonIndicators(lowerQuery))
            return QueryType.ComparisonQuery;

        // Check for temporal indicators
        if (ContainsTemporalIndicators(lowerQuery))
            return QueryType.TemporalQuery;

        // Check if it's a natural language question
        if (IsNaturalLanguageQuestion(query))
            return QueryType.NaturalQuestion;

        // Check for complex search indicators
        if (ContainsSemanticIndicators(lowerQuery))
            return QueryType.ComplexSearch;

        // Default to simple keyword
        return QueryType.SimpleKeyword;
    }

    private static ComplexityLevel DetectComplexity(string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length <= 2)
            return ComplexityLevel.Simple;
        if (words.Length <= 5)
            return ComplexityLevel.Moderate;
        if (words.Length <= 10)
            return ComplexityLevel.Complex;
        return ComplexityLevel.VeryComplex;
    }

    private static bool IsNaturalLanguageQuestion(string query)
    {
        var questionStarters = new[] { "what", "how", "why", "when", "where", "who", "which", "can", "could", "would", "should", "is", "are", "does", "do" };
        var lowerQuery = query.ToLowerInvariant().Trim();
        return query.Contains('?') || questionStarters.Any(qs => lowerQuery.StartsWith(qs + " ", StringComparison.Ordinal));
    }

    private static bool ContainsSemanticIndicators(string query)
    {
        var indicators = new[] { "similar to", "like", "related to", "meaning", "concept", "explain", "describe" };
        return indicators.Any(ind => query.Contains(ind));
    }

    private static bool ContainsMultiHopIndicators(string query)
    {
        var indicators = new[] { "and then", "after that", "which leads to", "in order to", "step by step", "first...then" };
        return indicators.Any(ind => query.Contains(ind));
    }

    private static bool ContainsReasoningIndicators(string query)
    {
        var indicators = new[] { "why", "because", "reason", "cause", "explain why", "how come" };
        return indicators.Any(ind => query.Contains(ind));
    }

    private static bool ContainsComparisonIndicators(string query)
    {
        var indicators = new[] { "compare", "versus", "vs", "difference between", "better", "worse", "or" };
        return indicators.Any(ind => query.Contains(ind));
    }

    private static bool ContainsTemporalIndicators(string query)
    {
        var indicators = new[] { "when", "before", "after", "during", "since", "until", "latest", "recent", "history" };
        return indicators.Any(ind => query.Contains(ind));
    }

    private static bool ContainsTechnicalTerms(string query)
    {
        var technicalPatterns = new[]
        {
            @"\b(API|SDK|HTTP|REST|JSON|XML|SQL|NoSQL)\b",
            @"\b(async|await|thread|process|memory)\b",
            @"\b(function|method|class|interface|enum)\b",
            @"\b[A-Z][a-z]+[A-Z]",  // CamelCase
            @"[a-z]+_[a-z]+"        // snake_case
        };

        return technicalPatterns.Any(pattern =>
            System.Text.RegularExpressions.Regex.IsMatch(query, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    private static (List<double[]> features, List<double> labels) ExtractFeaturesAndLabels(List<FusionTrainingExample> examples)
    {
        var features = new List<double[]>();
        var labels = new List<double>();

        foreach (var example in examples)
        {
            var featureVector = example.QueryFeatures ??
                ExtractQueryFeaturesFromExample(example).ToFeatureVector();
            features.Add(featureVector);
            labels.Add(example.RelevanceScore);
        }

        return (features, labels);
    }

    private static QueryPredictionFeatures ExtractQueryFeaturesFromExample(FusionTrainingExample example)
    {
        var vectorScores = example.VectorResults.Select(r => r.Score).ToList();
        var sparseScores = example.SparseResults.Select(r => r.Score).ToList();
        var words = example.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Calculate overlap
        var vectorIds = new HashSet<string>(example.VectorResults.Take(10).Select(r => r.Id));
        var sparseIds = new HashSet<string>(example.SparseResults.Take(10).Select(r => r.Id));
        var overlapRatio = vectorIds.Count > 0 && sparseIds.Count > 0
            ? (double)vectorIds.Intersect(sparseIds).Count() / vectorIds.Union(sparseIds).Count()
            : 0;

        return new QueryPredictionFeatures
        {
            QueryLength = words.Length,
            UniqueTermCount = words.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            QueryType = DetectQueryType(example.Query),
            Complexity = DetectComplexity(example.Query),
            VectorAvgScore = vectorScores.Count != 0 ? vectorScores.Average() : 0,
            VectorScoreVariance = CalculateVariance(vectorScores),
            SparseAvgScore = sparseScores.Count != 0 ? sparseScores.Average() : 0,
            SparseScoreVariance = CalculateVariance(sparseScores),
            ResultOverlapRatio = overlapRatio,
            ContainsTechnicalTerms = ContainsTechnicalTerms(example.Query),
            IsNaturalLanguageQuestion = IsNaturalLanguageQuestion(example.Query)
        };
    }

    private void UpdateNormalizationParameters(List<double[]> features)
    {
        if (features.Count == 0) return;

        var featureCount = features[0].Length;
        _featureMeans = new double[featureCount];
        _featureStdDevs = new double[featureCount];

        for (int i = 0; i < featureCount; i++)
        {
            var values = features.Select(f => i < f.Length ? f[i] : 0).ToList();
            _featureMeans[i] = values.Average();
            _featureStdDevs[i] = Math.Sqrt(CalculateVariance(values));
            if (_featureStdDevs[i] < 0.001) _featureStdDevs[i] = 1.0;  // Prevent division by zero
        }
    }

    private double[] NormalizeFeatures(double[] features)
    {
        var normalized = new double[features.Length];
        for (int i = 0; i < features.Length; i++)
        {
            if (i < _featureMeans.Length && i < _featureStdDevs.Length)
            {
                normalized[i] = (features[i] - _featureMeans[i]) / _featureStdDevs[i];
            }
            else
            {
                normalized[i] = features[i];
            }
        }
        return normalized;
    }

    private async Task TrainQueryTypeModelAsync(
        QueryType queryType,
        List<FusionTrainingExample> examples,
        CancellationToken cancellationToken)
    {
        var model = _queryTypeModels.GetOrAdd(queryType, _ => CreateDefaultModel(queryType));

        // Calculate centroid and optimal weights
        var features = examples.Select(e => e.QueryFeatures ?? ExtractQueryFeaturesFromExample(e).ToFeatureVector()).ToList();
        var optimalVectorWeights = examples.Select(e => e.OptimalWeights.VectorWeight * e.RelevanceScore).ToList();
        var optimalSparseWeights = examples.Select(e => e.OptimalWeights.SparseWeight * e.RelevanceScore).ToList();

        lock (model)
        {
            // Update centroid (average of all feature vectors)
            for (int i = 0; i < model.FeatureCentroid.Length; i++)
            {
                var values = features.Where(f => i < f.Length).Select(f => f[i]).ToList();
                if (values.Count != 0)
                    model.FeatureCentroid[i] = values.Average();
            }

            // Update average weights
            var totalRelevance = examples.Sum(e => e.RelevanceScore);
            if (totalRelevance > 0)
            {
                model.AverageVectorWeight = optimalVectorWeights.Sum() / totalRelevance;
                model.AverageSparseWeight = optimalSparseWeights.Sum() / totalRelevance;

                // Normalize to sum to 1
                var total = model.AverageVectorWeight + model.AverageSparseWeight;
                if (total > 0)
                {
                    model.AverageVectorWeight /= total;
                    model.AverageSparseWeight /= total;
                }
            }

            // Train simple linear model with gradient descent
            var normalizedFeatures = features.Select(f => NormalizeFeatures(f)).ToList();
            var labels = examples.Select(e => e.RelevanceScore).ToList();

            for (int epoch = 0; epoch < 100; epoch++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double totalError = 0;
                for (int j = 0; j < normalizedFeatures.Count; j++)
                {
                    var predicted = ComputePredictedScore(normalizedFeatures[j], model);
                    var error = labels[j] - predicted;
                    totalError += error * error;

                    // Update weights
                    for (int k = 0; k < model.Weights.Length && k < normalizedFeatures[j].Length; k++)
                    {
                        model.Weights[k] += LearningRate * error * normalizedFeatures[j][k];
                    }
                }

                // Early stopping if error is low
                if (totalError / normalizedFeatures.Count < 0.001)
                    break;
            }

            model.TrainingCount += examples.Count;
            model.LastUpdatedAt = DateTimeOffset.UtcNow;
        }

        await Task.CompletedTask;
    }

    private static double ComputePredictedScore(double[] features, LearnedQueryTypeModel model)
    {
        double score = 0;
        for (int i = 0; i < model.Weights.Length && i < features.Length; i++)
        {
            score += model.Weights[i] * features[i];
        }
        return Math.Clamp(Sigmoid(score), 0, 1);
    }

    private static double Sigmoid(double x)
    {
        return 1.0 / (1.0 + Math.Exp(-x));
    }

    private FusionWeightPrediction PredictFromModel(QueryPredictionFeatures features, LearnedQueryTypeModel model)
    {
        var featureVector = NormalizeFeatures(features.ToFeatureVector());
        var predictedScore = ComputePredictedScore(featureVector, model);

        // Calculate confidence based on distance to centroid
        var distance = CalculateEuclideanDistance(featureVector, model.FeatureCentroid);
        var confidence = Math.Exp(-distance / 5.0);  // Decay based on distance

        // Interpolate between learned weights and heuristic weights based on confidence
        var vectorWeight = model.AverageVectorWeight;
        var sparseWeight = model.AverageSparseWeight;

        return new FusionWeightPrediction
        {
            Weights = FusionWeights.Normalize(vectorWeight, sparseWeight),
            Confidence = confidence,
            Features = features,
            Reasoning = $"Model prediction based on {model.TrainingCount} examples",
            IsLearnedPrediction = true
        };
    }

    private static FusionWeightPrediction PredictFromHeuristics(
        QueryPredictionFeatures features,
        List<RankedResult> vectorResults,
        List<RankedResult> sparseResults)
    {
        double vectorWeight, sparseWeight;
        string reasoning;

        switch (features.QueryType)
        {
            case QueryType.SimpleKeyword:
                vectorWeight = 0.3;
                sparseWeight = 0.7;
                reasoning = "Simple keyword query favors sparse search";
                break;

            case QueryType.NaturalQuestion:
                vectorWeight = 0.7;
                sparseWeight = 0.3;
                reasoning = "Natural question favors semantic understanding";
                break;

            case QueryType.ComplexSearch:
                vectorWeight = 0.6;
                sparseWeight = 0.4;
                reasoning = "Complex search slightly favors vector search";
                break;

            case QueryType.ReasoningQuery:
                vectorWeight = 0.8;
                sparseWeight = 0.2;
                reasoning = "Reasoning query strongly favors semantic search";
                break;

            case QueryType.ComparisonQuery:
                vectorWeight = 0.6;
                sparseWeight = 0.4;
                reasoning = "Comparison query slightly favors vector search";
                break;

            case QueryType.TemporalQuery:
                vectorWeight = 0.5;
                sparseWeight = 0.5;
                reasoning = "Temporal query uses balanced weights";
                break;

            case QueryType.MultiHopQuery:
                vectorWeight = 0.7;
                sparseWeight = 0.3;
                reasoning = "Multi-hop query favors semantic search";
                break;

            default:
                // Adaptive based on result quality
                var vectorQuality = features.VectorAvgScore * (1 - features.VectorScoreVariance);
                var sparseQuality = features.SparseAvgScore * (1 - features.SparseScoreVariance);
                var total = vectorQuality + sparseQuality;

                if (total > 0)
                {
                    vectorWeight = vectorQuality / total;
                    sparseWeight = sparseQuality / total;
                }
                else
                {
                    vectorWeight = 0.5;
                    sparseWeight = 0.5;
                }
                reasoning = "Default query with adaptive weights based on result quality";
                break;
        }

        // Adjust based on result overlap
        if (features.ResultOverlapRatio > 0.7)
        {
            // High overlap - either method works, balance them
            vectorWeight = (vectorWeight + 0.5) / 2;
            sparseWeight = (sparseWeight + 0.5) / 2;
            reasoning += " (adjusted for high result overlap)";
        }
        else if (features.ResultOverlapRatio < 0.2)
        {
            // Low overlap - methods are complementary, may need different fusion
            reasoning += " (low overlap suggests complementary results)";
        }

        return new FusionWeightPrediction
        {
            Weights = FusionWeights.Normalize(vectorWeight, sparseWeight),
            Confidence = 0.5,  // Lower confidence for heuristic predictions
            Features = features,
            Reasoning = reasoning,
            IsLearnedPrediction = false
        };
    }

    private static double CalculateRelevanceFromFeedback(FusionFeedback feedback)
    {
        // Start with implicit relevance from click behavior
        var relevance = feedback.CalculateImplicitRelevance();

        // Factor in explicit relevance judgments if available
        if (feedback.RelevanceJudgments.Count != 0)
        {
            var avgExplicit = feedback.RelevanceJudgments.Values.Average();
            relevance = (relevance + avgExplicit) / 2;
        }

        return Math.Clamp(relevance, 0, 1);
    }

    private static FusionTrainingExample CreateExampleFromFeedback(FusionFeedback feedback, double relevanceScore)
    {
        return new FusionTrainingExample
        {
            Query = feedback.Query,
            QueryFeatures = feedback.Features.ToFeatureVector(),
            OptimalWeights = feedback.UsedWeights,
            RelevanceScore = relevanceScore,
            CollectedAt = feedback.Timestamp
        };
    }

    private double? CalculateTrainingAccuracy()
    {
        if (_trainingBuffer.IsEmpty) return null;

        double totalError = 0;
        int count = 0;

        foreach (var example in _trainingBuffer.Values)
        {
            var queryType = DetectQueryType(example.Query);
            if (_queryTypeModels.TryGetValue(queryType, out var model))
            {
                var features = NormalizeFeatures(example.QueryFeatures ??
                    ExtractQueryFeaturesFromExample(example).ToFeatureVector());
                var predicted = ComputePredictedScore(features, model);
                totalError += Math.Abs(example.RelevanceScore - predicted);
                count++;
            }
        }

        return count > 0 ? 1.0 - (totalError / count) : null;
    }

    private double CalculateAverageConfidence()
    {
        var confidences = new List<double>();
        foreach (var model in _queryTypeModels.Values)
        {
            if (model.TrainingCount > 0)
            {
                // Confidence increases with more training data
                confidences.Add(Math.Min(1.0, model.TrainingCount / 100.0));
            }
        }
        return confidences.Count != 0 ? confidences.Average() : 0.5;
    }

    private static double CalculateVariance(List<double> values)
    {
        if (values.Count <= 1) return 0;
        var avg = values.Average();
        return values.Sum(v => (v - avg) * (v - avg)) / values.Count;
    }

    private static double CalculateEuclideanDistance(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var diff = a[i] - b[i];
            sum += diff * diff;
        }
        return Math.Sqrt(sum);
    }

    private static string GenerateExampleKey(FusionTrainingExample example)
    {
        return $"{example.CollectedAt:O}_{example.Query.GetHashCode():X8}";
    }

    private static string ComputeChecksum(string data)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(bytes);
    }

    #endregion

    #region Internal Types

    private sealed class LearnedQueryTypeModel
    {
        public QueryType QueryType { get; init; }
        public double[] Weights { get; set; } = new double[11];
        public double[] FeatureCentroid { get; set; } = new double[11];
        public double AverageVectorWeight { get; set; } = 0.5;
        public double AverageSparseWeight { get; set; } = 0.5;
        public int TrainingCount { get; set; }
        public int OnlineUpdateCount { get; set; }
        public DateTimeOffset? LastUpdatedAt { get; set; }
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "No training examples provided")]
    private static partial void LogLearningBasedFusion14(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Training fusion model with {Count} examples")]
    private static partial void LogLearningBasedFusion13(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Information, Message = "Training completed. Total examples: {Total}, Model trained: {IsTrained}")]
    private static partial void LogLearningBasedFusion12(ILogger logger, int total, bool isTrained);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Training was cancelled")]
    private static partial void LogLearningBasedFusion11(ILogger logger);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error during training")]
    private static partial void LogLearningBasedFusion10(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Predicted weights for query type {QueryType}: Vector={VectorWeight:F3}, Sparse={SparseWeight:F3}")]
    private static partial void LogLearningBasedFusion9(ILogger logger, QueryType queryType, double vectorWeight, double sparseWeight);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Using heuristic prediction for query type {QueryType}: Vector={VectorWeight:F3}, Sparse={SparseWeight:F3}")]
    private static partial void LogLearningBasedFusion8(ILogger logger, QueryType queryType, double vectorWeight, double sparseWeight);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing online update for query: {Query}")]
    private static partial void LogLearningBasedFusion7(ILogger logger, string query);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Online update completed for query type {QueryType}. Relevance: {Relevance:F3}, Error: {Error:F3}")]
    private static partial void LogLearningBasedFusion6(ILogger logger, QueryType queryType, double relevance, double error);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error during online update")]
    private static partial void LogLearningBasedFusion5(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Recorded feedback for query {QueryId}, result {ResultId}: {Relevance:F3}")]
    private static partial void LogLearningBasedFusion4(ILogger logger, string queryId, string resultId, double relevance);
    [LoggerMessage(Level = LogLevel.Information, Message = "Exported model state with {TypeCount} query type models")]
    private static partial void LogLearningBasedFusion3(ILogger logger, int typeCount);
    [LoggerMessage(Level = LogLevel.Information, Message = "Imported model state: {TypeCount} query type models, {TrainingCount} training examples")]
    private static partial void LogLearningBasedFusion2(ILogger logger, int typeCount, int trainingCount);
    [LoggerMessage(Level = LogLevel.Information, Message = "Model reset to initial state")]
    private static partial void LogLearningBasedFusion1(ILogger logger);

    #endregion
}
