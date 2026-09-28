using FluxGuard.Remote.RAG;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FluxIndex.SDK.Services;

/// <summary>
/// Runs retrieved chunks through the opt-in <see cref="IRAGSecurityPipeline"/> (indirect prompt
/// injection / RAG poisoning detection) before they reach the caller. A chunk the pipeline suggests
/// blocking is dropped from the result set; one it suggests sanitizing is handed out with
/// <see cref="RAGDocumentValidation.SanitizedContent"/> when the pipeline provided one.
/// <see cref="RAGAction.Review"/> and <see cref="RAGAction.Include"/> pass through unchanged — the
/// pipeline judged them safe enough to include; review is a logging concern for the consumer's own
/// guard result inspection, not this SDK's to enforce.
/// </summary>
/// <remarks>
/// One judgement (<see cref="JudgeAsync"/>), one shape adapter per result type. Every public search path
/// applies it to what it returns — once, after its own filtering, so an internal call that feeds a larger
/// path (a hybrid leg, the vector leg of <c>SearchAsync(query, SearchOptions)</c>) is not validated twice.
/// A result that carries a chunk the store or cache still holds is sanitized on a copy
/// (<see cref="Core.Domain.Entities.DocumentChunk.WithContent"/>), never in place.
/// </remarks>
internal static partial class RagSecurityGuard
{
    /// <summary>What the pipeline sees of one result.</summary>
    internal readonly record struct Row(string Id, string Content, string Source, double Score);

    /// <summary>Drop it, hand it out with <see cref="Replacement"/>, or (default) keep it as is.</summary>
    internal readonly record struct Verdict(bool Block, string? Replacement);

    /// <summary>
    /// Validates <paramref name="rows"/> in one pipeline call and answers a verdict per row, in order.
    /// Rows are matched to validations by id; a row the pipeline did not answer for is kept.
    /// </summary>
    public static async Task<Verdict[]> JudgeAsync(
        IRAGSecurityPipeline pipeline, ILogger logger, IReadOnlyList<Row> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return [];

        var documents = rows.Select(r => new RAGDocument
        {
            Id = r.Id,
            Content = r.Content,
            Source = r.Source,
            RelevanceScore = r.Score
        }).ToList();

        var validations = await pipeline.ValidateDocumentsAsync(documents, cancellationToken);

        var byId = new Dictionary<string, RAGDocumentValidation>(StringComparer.Ordinal);
        foreach (var validation in validations)
            byId[validation.Document.Id ?? string.Empty] = validation;

        var verdicts = new Verdict[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            if (!byId.TryGetValue(rows[i].Id, out var validation))
                continue;

            if (validation.SuggestedAction == RAGAction.Block)
            {
                LogBlocked(logger, rows[i].Source, rows[i].Id, validation.RiskScore);
                verdicts[i] = new Verdict(Block: true, Replacement: null);
            }
            else if (validation is { SuggestedAction: RAGAction.Sanitize, SanitizedContent: { } sanitized })
            {
                verdicts[i] = new Verdict(Block: false, Replacement: sanitized);
            }
        }

        return verdicts;
    }

    /// <summary>
    /// Guards a flat result list: <paramref name="read"/> says what the pipeline sees of an item,
    /// <paramref name="sanitize"/> returns the item to hand out with the replacement content.
    /// </summary>
    public static async Task<List<T>> ApplyAsync<T>(
        IRAGSecurityPipeline pipeline,
        ILogger logger,
        IEnumerable<T> items,
        Func<T, Row> read,
        Func<T, string, T> sanitize,
        CancellationToken cancellationToken)
    {
        var list = items as IReadOnlyList<T> ?? items.ToList();
        var verdicts = await JudgeAsync(pipeline, logger, list.Select(read).ToList(), cancellationToken);

        var kept = new List<T>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            if (verdicts[i].Block)
                continue;
            kept.Add(verdicts[i].Replacement is { } replacement ? sanitize(list[i], replacement) : list[i]);
        }

        return kept;
    }

    /// <summary>
    /// <see cref="SearchResult"/> rows are built per call, so a sanitized one is rewritten in place.
    /// </summary>
    public static Task<List<SearchResult>> ApplyAsync(
        IRAGSecurityPipeline pipeline, ILogger logger, List<SearchResult> results, CancellationToken cancellationToken) =>
        ApplyAsync(
            pipeline, logger, results,
            r => new Row(r.Id, r.Content, r.DocumentId, r.Score),
            (r, content) => { r.Content = content; return r; },
            cancellationToken);

    /// <summary>
    /// <see cref="VectorSearchResult"/> rows carry the chunk the store or cache holds — sanitized on a copy.
    /// </summary>
    public static Task<List<VectorSearchResult>> ApplyAsync(
        IRAGSecurityPipeline pipeline, ILogger logger, IEnumerable<VectorSearchResult> results, CancellationToken cancellationToken) =>
        ApplyAsync(
            pipeline, logger, results,
            r => new Row(r.DocumentChunk.Id, r.DocumentChunk.Content, r.DocumentChunk.DocumentId, r.Score),
            (r, content) => new VectorSearchResult
            {
                DocumentChunk = r.DocumentChunk.WithContent(content),
                Score = r.Score,
                Rank = r.Rank,
                Distance = r.Distance,
                Metadata = r.Metadata,
            },
            cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAG security pipeline blocked document '{DocumentId}' (chunk '{ChunkId}', risk score {RiskScore:F2}) from search results")]
    private static partial void LogBlocked(ILogger logger, string documentId, string chunkId, double riskScore);
}
