using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Recording the vector-space revision on chunks a writer has just embedded — the step every writer of vectors takes, not only
/// the indexer.
/// </summary>
public static class EmbeddingServiceVectorSpaceExtensions
{
    /// <summary>
    /// Sets <see cref="DocumentChunk.VectorSpaceRevision"/> on every chunk that has a vector to the revision
    /// <paramref name="embeddingService"/> reports now. Call it after embedding and before the store write: a service that
    /// loads its model lazily knows its revision once it has embedded, and a store that keeps revisions can then tell which
    /// vectors a later revision has not re-embedded (<see cref="IVectorStore.CountVectorsOutsideSpaceAsync"/>). A writer that
    /// skips it stores vectors without a revision, which count as outside every space.
    /// </summary>
    /// <param name="embeddingService">The service that computed the chunks' vectors.</param>
    /// <param name="chunks">The chunks just embedded; chunks without a vector are left as they are.</param>
    /// <returns>The revision recorded — null when the service reports none.</returns>
    public static string? StampVectorSpace(this IEmbeddingService embeddingService, IEnumerable<DocumentChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(embeddingService);
        ArgumentNullException.ThrowIfNull(chunks);

        // A test double that never configured GetIdentity returns null; a write must not fail on the stamp.
        if (embeddingService.GetIdentity() is not { } identity)
            return null;

        var revision = identity.VectorSpaceRevision;
        foreach (var chunk in chunks)
        {
            if (chunk.Embedding is { Length: > 0 })
                chunk.VectorSpaceRevision = revision;
        }

        return revision;
    }
}
