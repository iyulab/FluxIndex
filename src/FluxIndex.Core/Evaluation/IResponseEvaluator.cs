namespace FluxIndex.Core.Evaluation;

/// <summary>
/// Evaluates the quality of a RAG-generated response.
/// Implementations may use keyword overlap (deterministic) or an LLM (requires model).
/// </summary>
public interface IResponseEvaluator
{
    /// <summary>
    /// Evaluate a generated answer against the retrieved contexts and the original query.
    /// </summary>
    /// <param name="query">The original user question.</param>
    /// <param name="retrievedContexts">Chunks retrieved from the knowledge base.</param>
    /// <param name="generatedAnswer">The answer produced by the LLM or fallback.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// (faithfulness, relevancy) scores each in [0..1].
    /// faithfulness = how grounded in contexts; relevancy = how on-topic.
    /// </returns>
    Task<(double Faithfulness, double Relevancy)> EvaluateAsync(
        string query,
        IReadOnlyList<string> retrievedContexts,
        string generatedAnswer,
        CancellationToken ct = default);
}
