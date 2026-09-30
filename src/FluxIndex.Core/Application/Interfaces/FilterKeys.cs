namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Filter keys that name a field of the chunk itself rather than an entry of its metadata.
/// </summary>
/// <remarks>
/// <para>
/// A search filter (<see cref="IVectorStore.SearchAsync"/>, <see cref="IVectorStore.DeleteByFilterAsync"/>,
/// <see cref="KeywordSearchOptions.MetadataFilter"/>, <c>HybridSearchOptions.Filters</c>) is otherwise a
/// metadata filter. These keys are the exception, and every store and keyword index resolves them the
/// same way — that is what lets one filter object scope both legs of a hybrid search.
/// </para>
/// <para>
/// Without the reservation, a scope by document id matched only chunks that happened to repeat their
/// document id inside their metadata. Chunks indexed without that copy were invisible to the scoped
/// search in every backend that read metadata, while a backend that read the chunk field found them —
/// so one leg of a hybrid search saw documents the other leg never could.
/// </para>
/// </remarks>
public static class FilterKeys
{
    /// <summary>
    /// Matches the chunk's own <c>DocumentId</c>. A metadata entry of the same name is not consulted.
    /// Accepts a scalar or a collection (match any), like every other filter value.
    /// </summary>
    public const string DocumentId = "document_id";
}
