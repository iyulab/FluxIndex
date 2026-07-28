using FluxImprover.Options;
using FluxImprover.Evaluation;

namespace FluxIndex.Integrations.FluxImprover.Services;

/// <summary>
/// RAG (Retrieval-Augmented Generation) evaluation service that wraps FluxImprover's evaluation capabilities.
/// Provides metrics for answerability, faithfulness, and relevancy to assess RAG pipeline quality.
/// </summary>
public sealed class RAGEvaluationService
{
    private readonly AnswerabilityEvaluator _answerabilityEvaluator;
    private readonly FaithfulnessEvaluator _faithfulnessEvaluator;
    private readonly RelevancyEvaluator _relevancyEvaluator;

    /// <summary>
    /// Creates a new RAG evaluation service with the specified evaluators.
    /// </summary>
    /// <param name="answerabilityEvaluator">Evaluator for answerability metrics.</param>
    /// <param name="faithfulnessEvaluator">Evaluator for faithfulness metrics.</param>
    /// <param name="relevancyEvaluator">Evaluator for relevancy metrics.</param>
    public RAGEvaluationService(
        AnswerabilityEvaluator answerabilityEvaluator,
        FaithfulnessEvaluator faithfulnessEvaluator,
        RelevancyEvaluator relevancyEvaluator)
    {
        _answerabilityEvaluator = answerabilityEvaluator ?? throw new ArgumentNullException(nameof(answerabilityEvaluator));
        _faithfulnessEvaluator = faithfulnessEvaluator ?? throw new ArgumentNullException(nameof(faithfulnessEvaluator));
        _relevancyEvaluator = relevancyEvaluator ?? throw new ArgumentNullException(nameof(relevancyEvaluator));
    }

    /// <summary>
    /// Evaluates if the given question can be answered based on the provided context.
    /// </summary>
    /// <param name="context">The retrieval context (combined chunks).</param>
    /// <param name="question">The user question.</param>
    /// <param name="options">Optional evaluation options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Answerability evaluation result with score and reasoning.</returns>
    public async Task<MetricResult> EvaluateAnswerabilityAsync(
        string context,
        string question,
        EvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(question);

        return await _answerabilityEvaluator.EvaluateAsync(context, question, options, cancellationToken);
    }

    /// <summary>
    /// Evaluates if the answer is faithful to (grounded in) the provided context.
    /// </summary>
    /// <param name="context">The retrieval context (combined chunks).</param>
    /// <param name="answer">The generated answer.</param>
    /// <param name="options">Optional evaluation options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Faithfulness evaluation result with score and reasoning.</returns>
    public async Task<MetricResult> EvaluateFaithfulnessAsync(
        string context,
        string answer,
        EvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(answer);

        return await _faithfulnessEvaluator.EvaluateAsync(context, answer, options, cancellationToken);
    }

    /// <summary>
    /// Evaluates if the answer is relevant to the given question.
    /// </summary>
    /// <param name="question">The user question.</param>
    /// <param name="answer">The generated answer.</param>
    /// <param name="options">Optional evaluation options.</param>
    /// <param name="context">Optional context for evaluation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Relevancy evaluation result with score and reasoning.</returns>
    public async Task<MetricResult> EvaluateRelevancyAsync(
        string question,
        string answer,
        EvaluationOptions? options = null,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(answer);

        return await _relevancyEvaluator.EvaluateAsync(question, answer, options, context, cancellationToken);
    }

    /// <summary>
    /// Performs a comprehensive RAG evaluation including all metrics.
    /// </summary>
    /// <param name="context">The retrieval context (combined chunks).</param>
    /// <param name="question">The user question.</param>
    /// <param name="answer">The generated answer.</param>
    /// <param name="options">Optional evaluation options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Complete RAG evaluation result with all metrics.</returns>
    public async Task<RAGEvaluationResult> EvaluateAsync(
        string context,
        string question,
        string answer,
        EvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(answer);

        // Run evaluations in parallel for efficiency
        var answerabilityTask = EvaluateAnswerabilityAsync(context, question, options, cancellationToken);
        var faithfulnessTask = EvaluateFaithfulnessAsync(context, answer, options, cancellationToken);
        var relevancyTask = EvaluateRelevancyAsync(question, answer, options, context, cancellationToken);

        await Task.WhenAll(answerabilityTask, faithfulnessTask, relevancyTask);

        return new RAGEvaluationResult
        {
            Answerability = await answerabilityTask,
            Faithfulness = await faithfulnessTask,
            Relevancy = await relevancyTask
        };
    }

    /// <summary>
    /// Evaluates multiple context-question-answer triples in batch.
    /// </summary>
    /// <param name="evaluations">Collection of triples to evaluate.</param>
    /// <param name="options">Optional evaluation options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of RAG evaluation results.</returns>
    public async Task<IReadOnlyList<RAGEvaluationResult>> EvaluateBatchAsync(
        IEnumerable<RAGEvaluationInput> evaluations,
        EvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<RAGEvaluationResult>();

        foreach (var eval in evaluations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await EvaluateAsync(eval.Context, eval.Question, eval.Answer, options, cancellationToken);
            results.Add(result);
        }

        return results;
    }
}

/// <summary>
/// Input data for RAG evaluation.
/// </summary>
public sealed record RAGEvaluationInput
{
    /// <summary>
    /// The retrieval context (combined chunks).
    /// </summary>
    public required string Context { get; init; }

    /// <summary>
    /// The user question.
    /// </summary>
    public required string Question { get; init; }

    /// <summary>
    /// The generated answer.
    /// </summary>
    public required string Answer { get; init; }
}

/// <summary>
/// Complete RAG evaluation result containing all metrics.
/// </summary>
public sealed class RAGEvaluationResult
{
    /// <summary>
    /// Answerability evaluation result - can the question be answered from the context?
    /// </summary>
    public required MetricResult Answerability { get; init; }

    /// <summary>
    /// Faithfulness evaluation result - is the answer grounded in the context?
    /// </summary>
    public required MetricResult Faithfulness { get; init; }

    /// <summary>
    /// Relevancy evaluation result - is the answer relevant to the question?
    /// </summary>
    public required MetricResult Relevancy { get; init; }

    /// <summary>
    /// Calculates an overall score as the average of all metrics.
    /// </summary>
    public double OverallScore => (Answerability.Score + Faithfulness.Score + Relevancy.Score) / 3.0;

    /// <summary>
    /// Indicates if the RAG response passes minimum quality thresholds.
    /// </summary>
    /// <param name="minimumScore">Minimum acceptable score (0.0 - 1.0).</param>
    /// <returns>True if all metrics meet the minimum threshold.</returns>
    public bool PassesThreshold(double minimumScore = 0.7)
    {
        return Answerability.Score >= minimumScore
               && Faithfulness.Score >= minimumScore
               && Relevancy.Score >= minimumScore;
    }
}
