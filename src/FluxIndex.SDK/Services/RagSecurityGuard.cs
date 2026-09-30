using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FluxIndex.SDK.Services;

/// <summary>
/// Runs retrieved chunks through the opt-in <see cref="IRetrievalGuard"/> (RAG poisoning / indirect prompt injection
/// detection) before they reach the caller. A blocked chunk is dropped from the result set; one with a replacement is
/// handed out with that content; anything else passes through unchanged.
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
    /// <summary>
    /// Judges <paramref name="rows"/> in one guard call and answers a verdict per row, in order.
    /// </summary>
    /// <exception cref="InvalidOperationException">The guard answered a different number of verdicts.</exception>
    public static async Task<RetrievalVerdict[]> JudgeAsync(
        IRetrievalGuard guard, ILogger logger, IReadOnlyList<RetrievedItem> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return [];

        var verdicts = await guard.JudgeAsync(rows, cancellationToken);
        if (verdicts.Count != rows.Count)
        {
            // A guard that loses track of rows cannot be applied safely: guessing which rows it meant would hand out
            // content it may have blocked.
            throw new InvalidOperationException(
                $"{guard.GetType().Name} returned {verdicts.Count} verdicts for {rows.Count} retrieved items; " +
                "an IRetrievalGuard answers one verdict per item, in order.");
        }

        var result = new RetrievalVerdict[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            result[i] = verdicts[i];
            if (verdicts[i].Block)
                LogBlocked(logger, rows[i].Source, rows[i].Id, verdicts[i].RiskScore);
        }

        return result;
    }

    /// <summary>
    /// Guards a flat result list: <paramref name="read"/> says what the guard sees of an item,
    /// <paramref name="sanitize"/> returns the item to hand out with the replacement content.
    /// </summary>
    public static async Task<List<T>> ApplyAsync<T>(
        IRetrievalGuard guard,
        ILogger logger,
        IEnumerable<T> items,
        Func<T, RetrievedItem> read,
        Func<T, string, T> sanitize,
        CancellationToken cancellationToken)
    {
        var list = items as IReadOnlyList<T> ?? items.ToList();
        var verdicts = await JudgeAsync(guard, logger, list.Select(read).ToList(), cancellationToken);

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
        IRetrievalGuard guard, ILogger logger, List<SearchResult> results, CancellationToken cancellationToken) =>
        ApplyAsync(
            guard, logger, results,
            r => new RetrievedItem(r.Id, r.Content, r.DocumentId, r.Score),
            (r, content) => { r.Content = content; return r; },
            cancellationToken);

    /// <summary>
    /// <see cref="VectorSearchResult"/> rows carry the chunk the store or cache holds — sanitized on a copy.
    /// </summary>
    public static Task<List<VectorSearchResult>> ApplyAsync(
        IRetrievalGuard guard, ILogger logger, IEnumerable<VectorSearchResult> results, CancellationToken cancellationToken) =>
        ApplyAsync(
            guard, logger, results,
            r => new RetrievedItem(r.DocumentChunk.Id, r.DocumentChunk.Content, r.DocumentChunk.DocumentId, r.Score),
            (r, content) => new VectorSearchResult
            {
                DocumentChunk = r.DocumentChunk.WithContent(content),
                Score = r.Score,
                Rank = r.Rank,
                Distance = r.Distance,
                Metadata = r.Metadata,
            },
            cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retrieval guard blocked document '{DocumentId}' (chunk '{ChunkId}', risk score {RiskScore:F2}) from search results")]
    private static partial void LogBlocked(ILogger logger, string documentId, string chunkId, double riskScore);
}
