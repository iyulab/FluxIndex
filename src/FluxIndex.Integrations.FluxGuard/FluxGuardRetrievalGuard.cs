using FluxGuard.Remote.RAG;
using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.Integrations.FluxGuard;

/// <summary>
/// An <see cref="IRetrievalGuard"/> over a FluxGuard <see cref="IRAGSecurityPipeline"/>: every search result is validated
/// as a RAG document, and the pipeline's suggested action decides it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><see cref="RAGAction.Block"/> drops the result.</description></item>
/// <item><description><see cref="RAGAction.Sanitize"/> hands it out with
/// <see cref="RAGDocumentValidation.SanitizedContent"/> when the pipeline provided one.</description></item>
/// <item><description><see cref="RAGAction.Review"/> and <see cref="RAGAction.Include"/> keep it — the pipeline judged it
/// safe enough to include; review is for the consumer's own inspection of guard results.</description></item>
/// <item><description>A result the pipeline returned no validation for is kept.</description></item>
/// </list>
/// Register it with <see cref="FluxGuardRetrievalGuardServiceCollectionExtensions.AddFluxGuardRetrievalGuard"/>.
/// </remarks>
public sealed class FluxGuardRetrievalGuard : IRetrievalGuard
{
    private readonly IRAGSecurityPipeline _pipeline;

    /// <summary>Creates the guard over <paramref name="pipeline"/>.</summary>
    public FluxGuardRetrievalGuard(IRAGSecurityPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrievalVerdict>> JudgeAsync(
        IReadOnlyList<RetrievedItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            return [];

        var documents = items.Select(item => new RAGDocument
        {
            Id = item.Id,
            Content = item.Content,
            Source = item.Source,
            RelevanceScore = item.Score
        }).ToList();

        var validations = await _pipeline.ValidateDocumentsAsync(documents, cancellationToken).ConfigureAwait(false);

        var byId = new Dictionary<string, RAGDocumentValidation>(StringComparer.Ordinal);
        foreach (var validation in validations)
            byId[validation.Document.Id ?? string.Empty] = validation;

        var verdicts = new RetrievalVerdict[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            if (!byId.TryGetValue(items[i].Id, out var validation))
                continue;

            verdicts[i] = validation switch
            {
                { SuggestedAction: RAGAction.Block } => new RetrievalVerdict(Block: true, Replacement: null, validation.RiskScore),
                { SuggestedAction: RAGAction.Sanitize, SanitizedContent: { } sanitized } =>
                    new RetrievalVerdict(Block: false, Replacement: sanitized, validation.RiskScore),
                _ => RetrievalVerdict.Keep,
            };
        }

        return verdicts;
    }
}
