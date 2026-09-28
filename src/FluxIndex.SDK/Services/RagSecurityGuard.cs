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
/// One judgement, one shape adapter per result type. Every public search path applies it to what it
/// returns — once, after its own filtering, so an internal call that feeds a larger path (a hybrid
/// leg, the vector leg of <c>SearchAsync(query, SearchOptions)</c>) is not validated twice.
/// </remarks>
internal static partial class RagSecurityGuard
{
    /// <summary>
    /// Guards <see cref="SearchResult"/> rows. They are built per call, so a sanitized one is rewritten in place.
    /// </summary>
    public static async Task<List<SearchResult>> ApplyAsync(
        IRAGSecurityPipeline pipeline, ILogger logger, List<SearchResult> results, CancellationToken cancellationToken)
    {
        if (results.Count == 0)
            return results;

        var validations = await ValidateAsync(
            pipeline, results.Select(r => (r.Id, r.Content, r.DocumentId, (double)r.Score)), cancellationToken);

        var kept = new List<SearchResult>(results.Count);
        foreach (var result in results)
        {
            switch (Decide(logger, validations, result.Id, result.DocumentId))
            {
                case { Block: true }:
                    continue;
                case { Replacement: { } replacement }:
                    result.Content = replacement;
                    break;
            }

            kept.Add(result);
        }

        return kept;
    }

    /// <summary>
    /// Guards <see cref="VectorSearchResult"/> rows. Their chunk is the instance the store or cache holds, so a
    /// sanitized one is handed out as a copy (<see cref="Core.Domain.Entities.DocumentChunk.WithContent"/>) —
    /// rewriting it in place would make the sanitized text the stored text for every later reader.
    /// </summary>
    public static async Task<List<VectorSearchResult>> ApplyAsync(
        IRAGSecurityPipeline pipeline, ILogger logger, IEnumerable<VectorSearchResult> results, CancellationToken cancellationToken)
    {
        var list = results as List<VectorSearchResult> ?? results.ToList();
        if (list.Count == 0)
            return list;

        var validations = await ValidateAsync(
            pipeline, list.Select(r => (r.DocumentChunk.Id, r.DocumentChunk.Content, r.DocumentChunk.DocumentId, r.Score)), cancellationToken);

        var kept = new List<VectorSearchResult>(list.Count);
        foreach (var result in list)
        {
            var chunk = result.DocumentChunk;
            switch (Decide(logger, validations, chunk.Id, chunk.DocumentId))
            {
                case { Block: true }:
                    continue;
                case { Replacement: { } replacement }:
                    kept.Add(new VectorSearchResult
                    {
                        DocumentChunk = chunk.WithContent(replacement),
                        Score = result.Score,
                        Rank = result.Rank,
                        Distance = result.Distance,
                        Metadata = result.Metadata,
                    });
                    continue;
            }

            kept.Add(result);
        }

        return kept;
    }

    private static async Task<Dictionary<string, RAGDocumentValidation>> ValidateAsync(
        IRAGSecurityPipeline pipeline,
        IEnumerable<(string Id, string Content, string Source, double Score)> rows,
        CancellationToken cancellationToken)
    {
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
        return byId;
    }

    private static Verdict Decide(
        ILogger logger, Dictionary<string, RAGDocumentValidation> validations, string? id, string? documentId)
    {
        if (!validations.TryGetValue(id ?? string.Empty, out var validation))
            return default;

        if (validation.SuggestedAction == RAGAction.Block)
        {
            LogBlocked(logger, documentId ?? string.Empty, id ?? string.Empty, validation.RiskScore);
            return new Verdict(Block: true, Replacement: null);
        }

        return validation is { SuggestedAction: RAGAction.Sanitize, SanitizedContent: { } sanitized }
            ? new Verdict(Block: false, Replacement: sanitized)
            : default;
    }

    private readonly record struct Verdict(bool Block, string? Replacement);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAG security pipeline blocked document '{DocumentId}' (chunk '{ChunkId}', risk score {RiskScore:F2}) from search results")]
    private static partial void LogBlocked(ILogger logger, string documentId, string chunkId, double riskScore);
}
