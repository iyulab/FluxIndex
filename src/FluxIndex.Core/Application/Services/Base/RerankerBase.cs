using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.Core.Application.Services.Base;

/// <summary>
/// Base class for reranker services.
/// Provides default implementations for common reranking patterns.
/// Consumers implementing AI providers (LMSupply, Cohere, etc.) should extend this class.
/// </summary>
/// <example>
/// Implement <see cref="RerankCoreAsync"/> (original index and score, best first) and <see cref="GetModelInfo"/>.
/// <code>
/// public sealed class MyReranker(MyClient client) : RerankerBase
/// {
///     protected override async Task&lt;IEnumerable&lt;(int Index, float Score)&gt;&gt; RerankCoreAsync(
///         string query, IReadOnlyList&lt;string&gt; documents, int topN, CancellationToken cancellationToken)
///     {
///         var scores = await client.ScoreAsync(query, documents, cancellationToken);
///         return scores.Select((score, index) =&gt; (index, score)).OrderByDescending(r =&gt; r.score).Take(topN);
///     }
///     public override RerankModelInfo GetModelInfo() =&gt; new() { Name = "my-reranker", Type = RerankModel.Local };
/// }
/// </code>
/// <para>Complete samples for a Cohere API reranker, compiled against this version, are in the repository's
/// <c>docs/AI_PROVIDER_INTEGRATION.md</c>.</para>
/// </example>
public abstract class RerankerBase : IReranker
{
    /// <summary>
    /// Core reranking method to implement.
    /// Returns tuples of (original index, relevance score) ordered by relevance.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="documents">Document contents to rerank</param>
    /// <param name="topN">Number of top results to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Ordered list of (original index, score) tuples</returns>
    protected abstract Task<IEnumerable<(int Index, float Score)>> RerankCoreAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    public async Task<IEnumerable<RerankResult>> RerankAsync(
        string query,
        IEnumerable<RetrievalCandidate> candidates,
        RerankOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new RerankOptions();
        var candidateList = candidates.ToList();

        if (candidateList.Count == 0)
            return [];

        // Prepare documents for reranking
        var documents = candidateList
            .Select(c => TruncateContent(c.Content, opts.MaxContentLength))
            .ToList();

        // Call provider-specific reranking
        var rankedResults = await RerankCoreAsync(query, documents, opts.TopN, cancellationToken);

        // Convert to RerankResult
        var results = new List<RerankResult>();
        var newRank = 1;

        foreach (var (index, score) in rankedResults)
        {
            if (opts.ScoreThreshold is { } threshold && score < threshold)
                continue;

            var original = candidateList[index];
            results.Add(new RerankResult
            {
                Id = original.Id,
                DocumentId = original.DocumentId,
                ChunkId = original.ChunkId,
                Content = original.Content,
                InitialScore = original.InitialScore,
                InitialRank = original.InitialRank,
                RerankScore = score,
                NewRank = newRank++,
                Metadata = original.Metadata,
                Explanation = opts.IncludeExplanation
                    ? $"Rerank score: {score:F4}, rank changed: {original.InitialRank} -> {newRank - 1}"
                    : null
            });
        }

        return results;
    }

    /// <inheritdoc />
    public abstract RerankModelInfo GetModelInfo();

    /// <summary>
    /// Truncates content to the specified maximum length.
    /// </summary>
    protected static string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;

        return content[..maxLength];
    }
}
