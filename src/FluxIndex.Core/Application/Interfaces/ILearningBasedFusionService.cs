using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Learning-based fusion service that trains on historical query-relevance pairs
/// to predict optimal fusion weights for new queries.
/// Supports online learning with continuous feedback integration.
/// </summary>
public interface ILearningBasedFusionService
{
    /// <summary>
    /// Trains the fusion model on historical query-relevance examples.
    /// </summary>
    /// <param name="examples">Training examples with queries, results, and relevance labels</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task TrainAsync(
        IEnumerable<FusionTrainingExample> examples,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Predicts optimal fusion weights for a new query based on learned patterns.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="vectorResults">Results from vector/semantic search</param>
    /// <param name="sparseResults">Results from sparse/keyword search</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Predicted optimal fusion weights</returns>
    Task<FusionWeightPrediction> PredictWeightsAsync(
        string query,
        IEnumerable<RankedResult> vectorResults,
        IEnumerable<RankedResult> sparseResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs online learning update with a single feedback example.
    /// Uses incremental learning to refine the model without full retraining.
    /// </summary>
    /// <param name="feedback">Single training feedback from user interaction</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateOnlineAsync(
        FusionFeedback feedback,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records relevance feedback for a specific query-result pair.
    /// </summary>
    /// <param name="queryId">Unique query identifier</param>
    /// <param name="resultId">Result document/chunk ID</param>
    /// <param name="relevanceScore">Relevance score (0.0-1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordFeedbackAsync(
        string queryId,
        string resultId,
        double relevanceScore,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current model statistics and performance metrics.
    /// </summary>
    /// <returns>Model statistics including training count, accuracy, etc.</returns>
    FusionModelStatistics GetModelStatistics();

    /// <summary>
    /// Exports the trained model for persistence or transfer.
    /// </summary>
    /// <returns>Serialized model state</returns>
    Task<FusionModelState> ExportModelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a previously exported model state.
    /// </summary>
    /// <param name="state">Serialized model state to import</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ImportModelAsync(FusionModelState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the model to initial untrained state.
    /// </summary>
    void ResetModel();
}

/// <summary>
/// Training example for the fusion model.
/// </summary>
public class FusionTrainingExample
{
    /// <summary>
    /// The search query text.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Query feature vector extracted from the query.
    /// </summary>
    public double[]? QueryFeatures { get; init; }

    /// <summary>
    /// Results from vector/semantic search with scores.
    /// </summary>
    public IReadOnlyList<ResultWithRelevance> VectorResults { get; init; } = Array.Empty<ResultWithRelevance>();

    /// <summary>
    /// Results from sparse/keyword search with scores.
    /// </summary>
    public IReadOnlyList<ResultWithRelevance> SparseResults { get; init; } = Array.Empty<ResultWithRelevance>();

    /// <summary>
    /// The fusion weights that produced optimal results for this query.
    /// </summary>
    public FusionWeights OptimalWeights { get; init; } = new();

    /// <summary>
    /// Overall relevance score achieved with optimal weights (0.0-1.0).
    /// </summary>
    public double RelevanceScore { get; init; }

    /// <summary>
    /// Timestamp when this example was collected.
    /// </summary>
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Search result with relevance label.
/// </summary>
public class ResultWithRelevance
{
    /// <summary>
    /// Result identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Search score from the retrieval system.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Rank position in the result list.
    /// </summary>
    public int Rank { get; init; }

    /// <summary>
    /// Human-judged relevance label (0.0-1.0).
    /// </summary>
    public double RelevanceLabel { get; init; }
}

/// <summary>
/// Fusion weights for combining vector and sparse search results.
/// </summary>
public class FusionWeights
{
    /// <summary>
    /// Weight for vector/semantic search results (0.0-1.0).
    /// </summary>
    public double VectorWeight { get; init; } = 0.5;

    /// <summary>
    /// Weight for sparse/keyword search results (0.0-1.0).
    /// </summary>
    public double SparseWeight { get; init; } = 0.5;

    /// <summary>
    /// Recommended fusion method.
    /// </summary>
    public LearningFusionMethod FusionMethod { get; init; } = LearningFusionMethod.WeightedSum;

    /// <summary>
    /// Optional parameters for specific fusion methods.
    /// </summary>
    public Dictionary<string, double> AdditionalParameters { get; init; } = new();

    /// <summary>
    /// Validates that weights are normalized.
    /// </summary>
    public bool IsValid => Math.Abs(VectorWeight + SparseWeight - 1.0) < 0.001;

    /// <summary>
    /// Creates normalized weights from any input values.
    /// </summary>
    public static FusionWeights Normalize(double vectorWeight, double sparseWeight)
    {
        var total = vectorWeight + sparseWeight;
        if (total <= 0)
        {
            return new FusionWeights { VectorWeight = 0.5, SparseWeight = 0.5 };
        }
        return new FusionWeights
        {
            VectorWeight = vectorWeight / total,
            SparseWeight = sparseWeight / total
        };
    }
}

/// <summary>
/// Fusion method for learning-based fusion.
/// </summary>
public enum LearningFusionMethod
{
    /// <summary>
    /// Simple weighted sum of scores.
    /// </summary>
    WeightedSum,

    /// <summary>
    /// Reciprocal Rank Fusion with learned K parameter.
    /// </summary>
    ReciprocalRankFusion,

    /// <summary>
    /// Learned combination function using neural network.
    /// </summary>
    LearnedCombination,

    /// <summary>
    /// Cascade fusion with learned thresholds.
    /// </summary>
    CascadeFusion
}

/// <summary>
/// Prediction result from the fusion model.
/// </summary>
public record FusionWeightPrediction
{
    /// <summary>
    /// Predicted optimal weights.
    /// </summary>
    public FusionWeights Weights { get; init; } = new();

    /// <summary>
    /// Confidence score for the prediction (0.0-1.0).
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Features used to make the prediction.
    /// </summary>
    public QueryPredictionFeatures Features { get; init; } = new();

    /// <summary>
    /// Reasoning for the prediction (for debugging/explainability).
    /// </summary>
    public string Reasoning { get; init; } = string.Empty;

    /// <summary>
    /// Whether this prediction is based on learned patterns or fallback heuristics.
    /// </summary>
    public bool IsLearnedPrediction { get; init; }

    /// <summary>
    /// Similar training examples that influenced this prediction.
    /// </summary>
    public IReadOnlyList<string> SimilarExampleIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Features extracted from query and results for prediction.
/// </summary>
public class QueryPredictionFeatures
{
    /// <summary>
    /// Query length in tokens.
    /// </summary>
    public int QueryLength { get; init; }

    /// <summary>
    /// Number of unique terms in query.
    /// </summary>
    public int UniqueTermCount { get; init; }

    /// <summary>
    /// Detected query type.
    /// </summary>
    public QueryType QueryType { get; init; }

    /// <summary>
    /// Complexity level of the query.
    /// </summary>
    public ComplexityLevel Complexity { get; init; }

    /// <summary>
    /// Average score from vector results.
    /// </summary>
    public double VectorAvgScore { get; init; }

    /// <summary>
    /// Score variance in vector results.
    /// </summary>
    public double VectorScoreVariance { get; init; }

    /// <summary>
    /// Average score from sparse results.
    /// </summary>
    public double SparseAvgScore { get; init; }

    /// <summary>
    /// Score variance in sparse results.
    /// </summary>
    public double SparseScoreVariance { get; init; }

    /// <summary>
    /// Overlap ratio between vector and sparse top-K results.
    /// </summary>
    public double ResultOverlapRatio { get; init; }

    /// <summary>
    /// Whether query contains technical/domain-specific terms.
    /// </summary>
    public bool ContainsTechnicalTerms { get; init; }

    /// <summary>
    /// Whether query is a natural language question.
    /// </summary>
    public bool IsNaturalLanguageQuestion { get; init; }

    /// <summary>
    /// Full feature vector for model input.
    /// </summary>
    public double[] ToFeatureVector()
    {
        return new double[]
        {
            QueryLength / 100.0,  // Normalized
            UniqueTermCount / 20.0,  // Normalized
            (int)QueryType / 4.0,  // Normalized enum
            (int)Complexity / 3.0,  // Normalized enum
            VectorAvgScore,
            VectorScoreVariance,
            SparseAvgScore,
            SparseScoreVariance,
            ResultOverlapRatio,
            ContainsTechnicalTerms ? 1.0 : 0.0,
            IsNaturalLanguageQuestion ? 1.0 : 0.0
        };
    }
}

/// <summary>
/// Online learning feedback from user interaction.
/// </summary>
public class FusionFeedback
{
    /// <summary>
    /// Unique identifier for this feedback.
    /// </summary>
    public string FeedbackId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The original query.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Query features at prediction time.
    /// </summary>
    public QueryPredictionFeatures Features { get; init; } = new();

    /// <summary>
    /// Weights that were used for fusion.
    /// </summary>
    public FusionWeights UsedWeights { get; init; } = new();

    /// <summary>
    /// Result IDs that were clicked/selected.
    /// </summary>
    public IReadOnlyList<string> ClickedResults { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Explicit relevance judgments from user.
    /// </summary>
    public Dictionary<string, double> RelevanceJudgments { get; init; } = new();

    /// <summary>
    /// Whether the user was satisfied with results.
    /// </summary>
    public bool? UserSatisfied { get; init; }

    /// <summary>
    /// Time spent examining results (engagement signal).
    /// </summary>
    public TimeSpan? DwellTime { get; init; }

    /// <summary>
    /// Timestamp of the feedback.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Calculates implicit relevance score based on click behavior.
    /// </summary>
    public double CalculateImplicitRelevance()
    {
        if (ClickedResults.Count == 0 && UserSatisfied != true)
            return 0.0;

        double score = 0.0;

        // Click-based score (position-weighted)
        for (int i = 0; i < ClickedResults.Count; i++)
        {
            score += 1.0 / (i + 1);  // Position decay
        }

        // Explicit satisfaction
        if (UserSatisfied == true)
            score += 0.5;
        else if (UserSatisfied == false)
            score -= 0.3;

        // Dwell time signal
        if (DwellTime.HasValue)
        {
            if (DwellTime.Value.TotalSeconds > 30)
                score += 0.2;
            else if (DwellTime.Value.TotalSeconds < 5)
                score -= 0.1;
        }

        return Math.Clamp(score / 2.0, 0.0, 1.0);  // Normalize
    }
}

/// <summary>
/// Statistics about the fusion model's state and performance.
/// </summary>
public class FusionModelStatistics
{
    /// <summary>
    /// Total number of training examples processed.
    /// </summary>
    public int TrainingExampleCount { get; init; }

    /// <summary>
    /// Number of online updates applied.
    /// </summary>
    public int OnlineUpdateCount { get; init; }

    /// <summary>
    /// Training accuracy (if evaluated).
    /// </summary>
    public double? TrainingAccuracy { get; init; }

    /// <summary>
    /// Cross-validation accuracy (if evaluated).
    /// </summary>
    public double? ValidationAccuracy { get; init; }

    /// <summary>
    /// Average prediction confidence.
    /// </summary>
    public double AveragePredictionConfidence { get; init; }

    /// <summary>
    /// Most common query types seen in training.
    /// </summary>
    public Dictionary<QueryType, int> QueryTypeDistribution { get; init; } = new();

    /// <summary>
    /// Average learned weights per query type.
    /// </summary>
    public Dictionary<QueryType, FusionWeights> LearnedWeightsPerType { get; init; } = new();

    /// <summary>
    /// Timestamp of last training.
    /// </summary>
    public DateTimeOffset? LastTrainedAt { get; init; }

    /// <summary>
    /// Timestamp of last online update.
    /// </summary>
    public DateTimeOffset? LastUpdatedAt { get; init; }

    /// <summary>
    /// Whether the model is ready for predictions.
    /// </summary>
    public bool IsModelTrained { get; init; }

    /// <summary>
    /// Model version identifier.
    /// </summary>
    public string ModelVersion { get; init; } = "1.0.0";
}

/// <summary>
/// Serializable model state for persistence.
/// </summary>
public record FusionModelState
{
    /// <summary>
    /// Model version for compatibility checking.
    /// </summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>
    /// Learned weights per query type.
    /// </summary>
    public Dictionary<string, double[]> LearnedWeights { get; init; } = new();

    /// <summary>
    /// Feature scaling parameters.
    /// </summary>
    public double[] FeatureMeans { get; init; } = Array.Empty<double>();

    /// <summary>
    /// Feature standard deviations for normalization.
    /// </summary>
    public double[] FeatureStdDevs { get; init; } = Array.Empty<double>();

    /// <summary>
    /// Query type centroids for similarity-based prediction.
    /// </summary>
    public Dictionary<string, double[]> QueryTypeCentroids { get; init; } = new();

    /// <summary>
    /// Training statistics.
    /// </summary>
    public FusionModelStatistics Statistics { get; init; } = new();

    /// <summary>
    /// Timestamp when state was exported.
    /// </summary>
    public DateTimeOffset ExportedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Checksum for integrity verification.
    /// </summary>
    public string? Checksum { get; init; }
}
