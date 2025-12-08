using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Agentic Retrieval Router interface.
/// Intelligently routes queries to the most appropriate retrieval strategy based on
/// query analysis, context, and historical performance.
/// </summary>
public interface IAgenticRetrievalRouter
{
    /// <summary>
    /// Routes the query to the most appropriate retrieval strategy and returns results.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="context">Optional context for routing decisions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Routing result with retrieved documents</returns>
    Task<RoutingResult> RouteAndRetrieveAsync(
        string query,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes a query to determine the best retrieval strategy without executing it.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="context">Optional context for analysis</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recommended routing decision</returns>
    Task<RoutingDecision> AnalyzeQueryAsync(
        string query,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a multi-step retrieval plan for complex queries.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="plan">Retrieval plan to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Combined results from all retrieval steps</returns>
    Task<MultiStepRetrievalResult> ExecuteRetrievalPlanAsync(
        string query,
        RetrievalPlan plan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a retrieval plan for complex queries that may require multiple steps.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="context">Optional context for planning</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generated retrieval plan</returns>
    Task<RetrievalPlan> GenerateRetrievalPlanAsync(
        string query,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records feedback for a routing decision to improve future routing.
    /// </summary>
    /// <param name="routingId">The routing ID from the original decision</param>
    /// <param name="feedback">Feedback on the routing decision</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordRoutingFeedbackAsync(
        string routingId,
        RoutingFeedback feedback,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Context for routing decisions.
/// </summary>
public class RoutingContext
{
    /// <summary>
    /// User identifier for personalized routing.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Session identifier for context continuity.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Previous queries in the session for context-aware routing.
    /// </summary>
    public IReadOnlyList<string> PreviousQueries { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Domain or collection to search within.
    /// </summary>
    public string? Domain { get; set; }

    /// <summary>
    /// Required capabilities for the retrieval strategy.
    /// </summary>
    public RetrievalCapabilities RequiredCapabilities { get; set; } = RetrievalCapabilities.None;

    /// <summary>
    /// Preferred retrieval strategy (optional hint).
    /// </summary>
    public RetrievalStrategy? PreferredStrategy { get; set; }

    /// <summary>
    /// Maximum acceptable latency for the retrieval.
    /// </summary>
    public TimeSpan? MaxLatency { get; set; }

    /// <summary>
    /// Minimum required result quality score.
    /// </summary>
    public double MinQualityScore { get; set; } = 0.5;

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int MaxResults { get; set; } = 10;

    /// <summary>
    /// Additional metadata for routing decisions.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Required capabilities for retrieval.
/// </summary>
[Flags]
public enum RetrievalCapabilities
{
    /// <summary>
    /// No specific capabilities required.
    /// </summary>
    None = 0,

    /// <summary>
    /// Semantic understanding of query intent.
    /// </summary>
    SemanticSearch = 1,

    /// <summary>
    /// Keyword-based exact matching.
    /// </summary>
    KeywordSearch = 2,

    /// <summary>
    /// Multi-hop reasoning across documents.
    /// </summary>
    MultiHopReasoning = 4,

    /// <summary>
    /// Temporal awareness for time-sensitive queries.
    /// </summary>
    TemporalAwareness = 8,

    /// <summary>
    /// Entity extraction and linking.
    /// </summary>
    EntityRecognition = 16,

    /// <summary>
    /// Query decomposition for complex questions.
    /// </summary>
    QueryDecomposition = 32,

    /// <summary>
    /// Self-correction and verification.
    /// </summary>
    SelfCorrection = 64,

    /// <summary>
    /// Hybrid search combining multiple approaches.
    /// </summary>
    HybridSearch = 128
}

/// <summary>
/// Available retrieval strategies.
/// </summary>
public enum RetrievalStrategy
{
    /// <summary>
    /// Simple semantic vector search.
    /// </summary>
    SemanticSearch,

    /// <summary>
    /// BM25/TF-IDF keyword search.
    /// </summary>
    KeywordSearch,

    /// <summary>
    /// Hybrid semantic + keyword search.
    /// </summary>
    HybridSearch,

    /// <summary>
    /// Multi-hop retrieval for complex queries.
    /// </summary>
    MultiHopRetrieval,

    /// <summary>
    /// Self-RAG with iterative refinement.
    /// </summary>
    SelfRAG,

    /// <summary>
    /// Corrective RAG with document grading.
    /// </summary>
    CorrectiveRAG,

    /// <summary>
    /// Small-to-big contextual expansion.
    /// </summary>
    SmallToBig,

    /// <summary>
    /// Graph-based traversal retrieval.
    /// </summary>
    GraphTraversal,

    /// <summary>
    /// Iterative retrieval with feedback.
    /// </summary>
    IterativeRetrieval,

    /// <summary>
    /// Query decomposition with sub-query retrieval.
    /// </summary>
    QueryDecomposition,

    /// <summary>
    /// Ensemble of multiple strategies.
    /// </summary>
    Ensemble
}

/// <summary>
/// Result of query routing and retrieval.
/// </summary>
public class RoutingResult
{
    /// <summary>
    /// Unique identifier for this routing decision.
    /// </summary>
    public string RoutingId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Retrieved documents.
    /// </summary>
    public IReadOnlyList<RoutedDocument> Documents { get; init; } = Array.Empty<RoutedDocument>();

    /// <summary>
    /// The routing decision that was made.
    /// </summary>
    public RoutingDecision Decision { get; init; } = new();

    /// <summary>
    /// Strategy that was actually executed.
    /// </summary>
    public RetrievalStrategy ExecutedStrategy { get; init; }

    /// <summary>
    /// Whether alternative strategies were tried.
    /// </summary>
    public bool UsedFallback { get; init; }

    /// <summary>
    /// Fallback strategies tried (if any).
    /// </summary>
    public IReadOnlyList<RetrievalStrategy> FallbacksTriedList { get; init; } = Array.Empty<RetrievalStrategy>();

    /// <summary>
    /// Total retrieval time including routing.
    /// </summary>
    public TimeSpan TotalTime { get; init; }

    /// <summary>
    /// Time spent on routing decision.
    /// </summary>
    public TimeSpan RoutingTime { get; init; }

    /// <summary>
    /// Time spent on actual retrieval.
    /// </summary>
    public TimeSpan RetrievalTime { get; init; }

    /// <summary>
    /// Overall quality score of the results.
    /// </summary>
    public double QualityScore { get; init; }

    /// <summary>
    /// Whether the retrieval was successful.
    /// </summary>
    public bool IsSuccessful { get; init; }

    /// <summary>
    /// Error message if retrieval failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Routing explanation for debugging/transparency.
    /// </summary>
    public string? RoutingExplanation { get; init; }

    /// <summary>
    /// Metadata about the routing process.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// A document retrieved through the router.
/// </summary>
public class RoutedDocument
{
    /// <summary>
    /// The document chunk.
    /// </summary>
    public DocumentChunk Chunk { get; init; } = new();

    /// <summary>
    /// Relevance score (0.0-1.0).
    /// </summary>
    public double RelevanceScore { get; init; }

    /// <summary>
    /// Strategy that retrieved this document.
    /// </summary>
    public RetrievalStrategy RetrievedBy { get; init; }

    /// <summary>
    /// Retrieval step number (for multi-step retrieval).
    /// </summary>
    public int RetrievalStep { get; init; }

    /// <summary>
    /// Confidence in the relevance score.
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Explanation of why this document was retrieved.
    /// </summary>
    public string? RetrievalReason { get; init; }
}

/// <summary>
/// Routing decision made by the router.
/// </summary>
public class RoutingDecision
{
    /// <summary>
    /// Selected primary strategy.
    /// </summary>
    public RetrievalStrategy PrimaryStrategy { get; init; }

    /// <summary>
    /// Fallback strategies in priority order.
    /// </summary>
    public IReadOnlyList<RetrievalStrategy> FallbackStrategies { get; init; } = Array.Empty<RetrievalStrategy>();

    /// <summary>
    /// Confidence in this routing decision (0.0-1.0).
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Analysis of the query that led to this decision.
    /// </summary>
    public RoutingQueryAnalysis QueryAnalysis { get; init; } = new();

    /// <summary>
    /// Explanation of why this strategy was chosen.
    /// </summary>
    public string? Explanation { get; init; }

    /// <summary>
    /// Estimated latency for the chosen strategy.
    /// </summary>
    public TimeSpan EstimatedLatency { get; init; }

    /// <summary>
    /// Estimated quality score for the chosen strategy.
    /// </summary>
    public double EstimatedQuality { get; init; }

    /// <summary>
    /// Features detected in the query that influenced the decision.
    /// </summary>
    public IReadOnlyList<QueryFeature> DetectedFeatures { get; init; } = Array.Empty<QueryFeature>();
}

/// <summary>
/// Analysis of a query for routing purposes.
/// </summary>
public class RoutingQueryAnalysis
{
    /// <summary>
    /// Detected query type.
    /// </summary>
    public RoutingQueryType Type { get; init; }

    /// <summary>
    /// Complexity score (0.0-1.0).
    /// </summary>
    public double Complexity { get; init; }

    /// <summary>
    /// Whether the query requires multi-hop reasoning.
    /// </summary>
    public bool RequiresMultiHop { get; init; }

    /// <summary>
    /// Whether the query is time-sensitive.
    /// </summary>
    public bool IsTimeSensitive { get; init; }

    /// <summary>
    /// Detected entities in the query.
    /// </summary>
    public IReadOnlyList<string> DetectedEntities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Key concepts extracted from the query.
    /// </summary>
    public IReadOnlyList<string> KeyConcepts { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Detected sub-queries (for complex queries).
    /// </summary>
    public IReadOnlyList<string> SubQueries { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Query intent classification.
    /// </summary>
    public RoutingQueryIntent Intent { get; init; }

    /// <summary>
    /// Estimated optimal result count.
    /// </summary>
    public int EstimatedOptimalResultCount { get; init; } = 5;
}

/// <summary>
/// Types of queries for routing purposes.
/// </summary>
public enum RoutingQueryType
{
    /// <summary>
    /// Simple factual question.
    /// </summary>
    Factual,

    /// <summary>
    /// Comparison between concepts.
    /// </summary>
    Comparison,

    /// <summary>
    /// Procedural/how-to question.
    /// </summary>
    Procedural,

    /// <summary>
    /// Causal/why question.
    /// </summary>
    Causal,

    /// <summary>
    /// Definition or explanation request.
    /// </summary>
    Definition,

    /// <summary>
    /// Opinion or recommendation request.
    /// </summary>
    Opinion,

    /// <summary>
    /// Complex multi-part question.
    /// </summary>
    Complex,

    /// <summary>
    /// Aggregation or summary request.
    /// </summary>
    Aggregation,

    /// <summary>
    /// Navigation or location request.
    /// </summary>
    Navigation,

    /// <summary>
    /// Unknown or ambiguous query type.
    /// </summary>
    Unknown
}

/// <summary>
/// Query intent classification for routing.
/// </summary>
public enum RoutingQueryIntent
{
    /// <summary>
    /// Seeking specific information.
    /// </summary>
    Informational,

    /// <summary>
    /// Looking to navigate to a resource.
    /// </summary>
    Navigational,

    /// <summary>
    /// Intending to perform a transaction.
    /// </summary>
    Transactional,

    /// <summary>
    /// Researching or exploring a topic.
    /// </summary>
    Research,

    /// <summary>
    /// Seeking help with a problem.
    /// </summary>
    Support,

    /// <summary>
    /// Conversational or follow-up query.
    /// </summary>
    Conversational
}

/// <summary>
/// A feature detected in a query.
/// </summary>
public class QueryFeature
{
    /// <summary>
    /// Name of the feature.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Value or description of the feature.
    /// </summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// Confidence in the feature detection.
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Impact on routing decision.
    /// </summary>
    public RoutingImpact Impact { get; init; }
}

/// <summary>
/// Impact of a feature on routing.
/// </summary>
public enum RoutingImpact
{
    /// <summary>
    /// No significant impact.
    /// </summary>
    None,

    /// <summary>
    /// Suggests a particular strategy.
    /// </summary>
    Suggests,

    /// <summary>
    /// Strongly indicates a strategy.
    /// </summary>
    StrongIndicator,

    /// <summary>
    /// Requires a specific strategy.
    /// </summary>
    Requires,

    /// <summary>
    /// Excludes certain strategies.
    /// </summary>
    Excludes
}

/// <summary>
/// Multi-step retrieval plan for complex queries.
/// </summary>
public class RetrievalPlan
{
    /// <summary>
    /// Plan identifier.
    /// </summary>
    public string PlanId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Original query.
    /// </summary>
    public string OriginalQuery { get; init; } = string.Empty;

    /// <summary>
    /// Retrieval steps to execute.
    /// </summary>
    public IReadOnlyList<RetrievalStep> Steps { get; init; } = Array.Empty<RetrievalStep>();

    /// <summary>
    /// Dependencies between steps.
    /// </summary>
    public IReadOnlyList<StepDependency> Dependencies { get; init; } = Array.Empty<StepDependency>();

    /// <summary>
    /// Estimated total execution time.
    /// </summary>
    public TimeSpan EstimatedDuration { get; init; }

    /// <summary>
    /// Plan generation explanation.
    /// </summary>
    public string? PlanExplanation { get; init; }
}

/// <summary>
/// A step in a retrieval plan.
/// </summary>
public class RetrievalStep
{
    /// <summary>
    /// Step identifier.
    /// </summary>
    public string StepId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Step number in sequence.
    /// </summary>
    public int StepNumber { get; init; }

    /// <summary>
    /// Strategy to use for this step.
    /// </summary>
    public RetrievalStrategy Strategy { get; init; }

    /// <summary>
    /// Query or sub-query for this step.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Maximum results for this step.
    /// </summary>
    public int MaxResults { get; init; } = 5;

    /// <summary>
    /// Purpose of this step.
    /// </summary>
    public string Purpose { get; init; } = string.Empty;

    /// <summary>
    /// Whether this step can be executed in parallel.
    /// </summary>
    public bool CanParallelize { get; init; }

    /// <summary>
    /// Step-specific options.
    /// </summary>
    public Dictionary<string, object> Options { get; init; } = new();
}

/// <summary>
/// Dependency between retrieval steps.
/// </summary>
public class StepDependency
{
    /// <summary>
    /// Dependent step ID.
    /// </summary>
    public string DependentStepId { get; init; } = string.Empty;

    /// <summary>
    /// Prerequisite step ID.
    /// </summary>
    public string PrerequisiteStepId { get; init; } = string.Empty;

    /// <summary>
    /// Type of dependency.
    /// </summary>
    public DependencyType Type { get; init; }
}

/// <summary>
/// Types of step dependencies.
/// </summary>
public enum DependencyType
{
    /// <summary>
    /// Must complete before starting.
    /// </summary>
    Sequential,

    /// <summary>
    /// Uses results from prerequisite.
    /// </summary>
    DataFlow,

    /// <summary>
    /// Conditionally depends on prerequisite outcome.
    /// </summary>
    Conditional
}

/// <summary>
/// Result of multi-step retrieval.
/// </summary>
public class MultiStepRetrievalResult
{
    /// <summary>
    /// The executed plan.
    /// </summary>
    public RetrievalPlan Plan { get; init; } = new();

    /// <summary>
    /// Results from each step.
    /// </summary>
    public IReadOnlyList<StepResult> StepResults { get; init; } = Array.Empty<StepResult>();

    /// <summary>
    /// Final merged documents.
    /// </summary>
    public IReadOnlyList<RoutedDocument> MergedDocuments { get; init; } = Array.Empty<RoutedDocument>();

    /// <summary>
    /// Total execution time.
    /// </summary>
    public TimeSpan TotalTime { get; init; }

    /// <summary>
    /// Number of steps completed successfully.
    /// </summary>
    public int CompletedSteps { get; init; }

    /// <summary>
    /// Number of steps that failed.
    /// </summary>
    public int FailedSteps { get; init; }

    /// <summary>
    /// Overall success status.
    /// </summary>
    public bool IsSuccessful { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result of a single retrieval step.
/// </summary>
public class StepResult
{
    /// <summary>
    /// Step ID.
    /// </summary>
    public string StepId { get; init; } = string.Empty;

    /// <summary>
    /// Step number.
    /// </summary>
    public int StepNumber { get; init; }

    /// <summary>
    /// Documents retrieved in this step.
    /// </summary>
    public IReadOnlyList<RoutedDocument> Documents { get; init; } = Array.Empty<RoutedDocument>();

    /// <summary>
    /// Step execution time.
    /// </summary>
    public TimeSpan ExecutionTime { get; init; }

    /// <summary>
    /// Whether the step succeeded.
    /// </summary>
    public bool IsSuccessful { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Feedback on a routing decision.
/// </summary>
public class RoutingFeedback
{
    /// <summary>
    /// Whether the results were satisfactory.
    /// </summary>
    public bool WasSatisfactory { get; set; }

    /// <summary>
    /// Quality rating (1-5).
    /// </summary>
    public int QualityRating { get; set; }

    /// <summary>
    /// Whether different strategy would have been better.
    /// </summary>
    public RetrievalStrategy? BetterStrategy { get; set; }

    /// <summary>
    /// Free-form feedback text.
    /// </summary>
    public string? FeedbackText { get; set; }

    /// <summary>
    /// Specific issues encountered.
    /// </summary>
    public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();
}
