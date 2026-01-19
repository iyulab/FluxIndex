using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.Core.Application.Services.Base;

/// <summary>
/// Base class for reranker services.
/// Provides default implementations for common reranking patterns.
/// Consumers implementing AI providers (LMSupply, Cohere, etc.) should extend this class.
/// </summary>
/// <example>
/// // LMSupply implementation (~20 lines):
/// public class LMSupplyReranker : RerankerBase
/// {
///     private readonly IRerankerModel _model;
///     public LMSupplyReranker(IRerankerModel model) => _model = model;
///
///     protected override async Task&lt;IEnumerable&lt;(int Index, float Score)&gt;&gt; RerankCoreAsync(
///         string query, IReadOnlyList&lt;string&gt; documents, int topN, CancellationToken ct)
///     {
///         var results = await _model.RerankAsync(query, documents.ToList(), topN, ct);
///         return results.Select(r => (r.OriginalIndex, r.Score));
///     }
///
///     public override RerankModelInfo GetModelInfo() => new()
///     {
///         Name = _model.ModelId, Type = RerankModel.Local, RequiresApiKey = false
///     };
/// }
///
/// // Cohere implementation (~15 lines):
/// public class CohereReranker : RerankerBase
/// {
///     private readonly CohereClient _client;
///     public CohereReranker(CohereClient client) => _client = client;
///
///     protected override async Task&lt;IEnumerable&lt;(int Index, float Score)&gt;&gt; RerankCoreAsync(
///         string query, IReadOnlyList&lt;string&gt; documents, int topN, CancellationToken ct)
///     {
///         var response = await _client.RerankAsync(query, documents.ToList(), topN, ct);
///         return response.Results.Select(r => (r.Index, r.RelevanceScore));
///     }
/// }
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
            if (score < opts.ScoreThreshold)
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
