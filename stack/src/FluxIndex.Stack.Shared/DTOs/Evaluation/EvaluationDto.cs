namespace FluxIndex.Stack.Shared.DTOs.Evaluation;

/// <summary>
/// Request to run a RAG evaluation job.
/// </summary>
public class RunEvaluationRequest
{
    /// <summary>
    /// Name of the evaluation job for identification.
    /// </summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Collection ID to evaluate against. If null, searches all collections.
    /// </summary>
    public Guid? CollectionId { get; set; }

    /// <summary>
    /// Evaluation dataset with query-answer pairs.
    /// </summary>
    public List<EvaluationQueryDto> Queries { get; set; } = new();

    /// <summary>
    /// Number of chunks to retrieve per query.
    /// </summary>
    public int TopK { get; set; } = 5;

    /// <summary>
    /// Whether to generate answers using LLM (requires ITextCompletionService).
    /// If false, only retrieval quality is evaluated.
    /// </summary>
    public bool GenerateAnswers { get; set; } = true;

    /// <summary>
    /// System version identifier for result caching.
    /// </summary>
    public string? Version { get; set; }
}

/// <summary>
/// Individual evaluation query with expected answer.
/// </summary>
public class EvaluationQueryDto
{
    /// <summary>
    /// The query to evaluate.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Expected answer for comparison (ground truth).
    /// </summary>
    public string ExpectedAnswer { get; set; } = string.Empty;

    /// <summary>
    /// Optional relevant document IDs for precision/recall calculation.
    /// </summary>
    public List<string>? RelevantDocumentIds { get; set; }
}

/// <summary>
/// Response after starting an evaluation job.
/// </summary>
public class EvaluationJobResponseDto
{
    /// <summary>
    /// Unique job ID for tracking.
    /// </summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>
    /// Job name.
    /// </summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Current status: Queued, Running, Completed, Failed.
    /// </summary>
    public string Status { get; set; } = "Queued";

    /// <summary>
    /// Number of queries to evaluate.
    /// </summary>
    public int TotalQueries { get; set; }

    /// <summary>
    /// Job creation time.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Estimated completion time (if running).
    /// </summary>
    public DateTime? EstimatedCompletionAt { get; set; }
}

/// <summary>
/// Complete evaluation result with metrics.
/// </summary>
public class EvaluationResultDto
{
    /// <summary>
    /// Job ID.
    /// </summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>
    /// Job name.
    /// </summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Job status.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Total queries evaluated.
    /// </summary>
    public int TotalQueries { get; set; }

    /// <summary>
    /// Successfully evaluated queries.
    /// </summary>
    public int SuccessfulQueries { get; set; }

    /// <summary>
    /// Failed queries.
    /// </summary>
    public int FailedQueries { get; set; }

    /// <summary>
    /// Aggregated quality metrics.
    /// </summary>
    public EvaluationMetricsDto? Metrics { get; set; }

    /// <summary>
    /// Individual query results (if requested).
    /// </summary>
    public List<QueryEvaluationResultDto>? QueryResults { get; set; }

    /// <summary>
    /// Job start time.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Job completion time.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Total duration in milliseconds.
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Aggregated evaluation metrics.
/// </summary>
public class EvaluationMetricsDto
{
    /// <summary>
    /// Mean Reciprocal Rank.
    /// </summary>
    public double MRR { get; set; }

    /// <summary>
    /// Precision@K.
    /// </summary>
    public double PrecisionAtK { get; set; }

    /// <summary>
    /// Recall@K.
    /// </summary>
    public double RecallAtK { get; set; }

    /// <summary>
    /// Normalized Discounted Cumulative Gain.
    /// </summary>
    public double NDCG { get; set; }

    /// <summary>
    /// Average answer faithfulness score (0-1).
    /// </summary>
    public double? AverageFaithfulness { get; set; }

    /// <summary>
    /// Average answer relevancy score (0-1).
    /// </summary>
    public double? AverageRelevancy { get; set; }

    /// <summary>
    /// Average context precision score (0-1).
    /// </summary>
    public double? AverageContextPrecision { get; set; }

    /// <summary>
    /// Overall quality score (0-1).
    /// </summary>
    public double OverallScore { get; set; }

    /// <summary>
    /// Quality tier: Low, Medium, High, Excellent.
    /// </summary>
    public string QualityTier { get; set; } = "Medium";
}

/// <summary>
/// Individual query evaluation result.
/// </summary>
public class QueryEvaluationResultDto
{
    /// <summary>
    /// Original query.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Expected answer.
    /// </summary>
    public string ExpectedAnswer { get; set; } = string.Empty;

    /// <summary>
    /// Generated answer (if LLM enabled).
    /// </summary>
    public string? GeneratedAnswer { get; set; }

    /// <summary>
    /// Retrieved chunk count.
    /// </summary>
    public int RetrievedChunks { get; set; }

    /// <summary>
    /// Relevant chunks found (if ground truth provided).
    /// </summary>
    public int? RelevantChunksFound { get; set; }

    /// <summary>
    /// Query-level metrics.
    /// </summary>
    public QueryMetricsDto? Metrics { get; set; }

    /// <summary>
    /// Retrieval latency in milliseconds.
    /// </summary>
    public double RetrievalLatencyMs { get; set; }

    /// <summary>
    /// Generation latency in milliseconds (if applicable).
    /// </summary>
    public double? GenerationLatencyMs { get; set; }

    /// <summary>
    /// Whether this query evaluation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Query-level evaluation metrics.
/// </summary>
public class QueryMetricsDto
{
    /// <summary>
    /// Reciprocal rank for this query.
    /// </summary>
    public double ReciprocalRank { get; set; }

    /// <summary>
    /// Precision for this query.
    /// </summary>
    public double Precision { get; set; }

    /// <summary>
    /// Recall for this query.
    /// </summary>
    public double Recall { get; set; }

    /// <summary>
    /// Faithfulness score (0-1).
    /// </summary>
    public double? Faithfulness { get; set; }

    /// <summary>
    /// Relevancy score (0-1).
    /// </summary>
    public double? Relevancy { get; set; }

    /// <summary>
    /// Context precision score (0-1).
    /// </summary>
    public double? ContextPrecision { get; set; }
}

/// <summary>
/// Request to list evaluation jobs.
/// </summary>
public class ListEvaluationJobsRequest
{
    /// <summary>
    /// Filter by status (optional).
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Page number (1-based).
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Page size.
    /// </summary>
    public int PageSize { get; set; } = 20;
}
