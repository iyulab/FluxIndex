using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 벡터 저장소 인터페이스
/// </summary>
public interface IVectorStore
{
    Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> StoreBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default);
    Task<DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default);
    Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the ids of every chunk belonging to a document, without loading their content,
    /// metadata or embeddings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A caller that only needs to know *which* points belong to a document - resolving a generation
    /// to delete, checking what is indexed - would otherwise call
    /// <see cref="GetByDocumentIdAsync"/> and discard everything but the id. On a store that fetches
    /// payload and vectors that is not merely wasteful: it makes the response grow with document
    /// size, and a transport with a message-size limit turns a large document into a hard failure.
    /// </para>
    /// <para>
    /// The default implementation derives the ids from <see cref="GetByDocumentIdAsync"/>, so every
    /// store answers this correctly. Stores that can fetch ids without the rest override it.
    /// </para>
    /// </remarks>
    async Task<IReadOnlyList<string>> GetChunkIdsByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var chunks = await GetByDocumentIdAsync(documentId, cancellationToken);
        return chunks.Select(c => c.Id).ToList();
    }

    /// <summary>
    /// Searches for the <paramref name="topK"/> most similar chunks, optionally restricted by
    /// metadata <paramref name="filters"/>.
    /// </summary>
    /// <param name="queryEmbedding">Query vector.</param>
    /// <param name="topK">Maximum results to return.</param>
    /// <param name="minScore">Minimum similarity score; lower-scoring results are dropped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="filters">
    /// Metadata filter conditions. Semantics are identical across every store implementation:
    /// <list type="bullet">
    /// <item><description><b>Keys combine with AND</b> — a chunk must satisfy every entry.</description></item>
    /// <item><description><b><see cref="FilterKeys.DocumentId"/></b> (<c>document_id</c>) matches the
    /// chunk's own <c>DocumentId</c>, not a metadata entry — every other key is a metadata key.</description></item>
    /// <item><description><b>Scalar value</b> (string / number / bool / scalar JsonElement) — the
    /// chunk's metadata value must equal it (ordinal, JSON-normalized comparison).</description></item>
    /// <item><description><b>Collection value</b> (any non-string <c>IEnumerable</c> of scalars, or a
    /// JsonElement array) — the chunk's metadata value must equal <b>ANY</b> element (OR within the
    /// key; e.g. Qdrant MatchAny, SQL <c>IN</c>).</description></item>
    /// <item><description><b>Anything else</b> (arbitrary objects, nested collections, empty
    /// collections) is unsupported and throws <see cref="System.ArgumentException"/> — never a
    /// silent zero-result.</description></item>
    /// </list>
    /// </param>
    Task<IEnumerable<DocumentChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        Dictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces every chunk the store holds for <paramref name="documentIds"/> with <paramref name="chunks"/>: afterwards
    /// those documents consist of exactly the given chunks. A document id with no chunk in <paramref name="chunks"/> is
    /// removed; a chunk whose document id is not listed is stored beside what the store already holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what indexing a document under an id that is already indexed means: the new version takes the place of
    /// the old one instead of joining it. Storing new chunks without it leaves the old chunks searchable, because chunk
    /// ids are not derived from the document.
    /// </para>
    /// <para>
    /// The write is atomic where the backend offers a transaction (the sqlite-vec store). The default implementation
    /// stores the new chunks first and then deletes the old ones that are not among them, so an interrupted call leaves
    /// a document duplicated rather than missing — calling it again repairs it.
    /// </para>
    /// </remarks>
    /// <param name="documentIds">The documents to replace. Blank ids are ignored.</param>
    /// <param name="chunks">The chunks the documents consist of from now on (may be empty).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ids of the stored chunks, in input order.</returns>
    async Task<IReadOnlyList<string>> ReplaceDocumentsAsync(
        IReadOnlyCollection<string> documentIds,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        System.ArgumentNullException.ThrowIfNull(documentIds);
        System.ArgumentNullException.ThrowIfNull(chunks);

        var previous = new List<string>();
        foreach (var documentId in documentIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(System.StringComparer.Ordinal))
            previous.AddRange(await GetChunkIdsByDocumentIdAsync(documentId, cancellationToken));

        var stored = chunks.Count == 0
            ? new List<string>()
            : (await StoreBatchAsync(chunks, cancellationToken)).ToList();

        var kept = new HashSet<string>(stored, System.StringComparer.Ordinal);
        foreach (var id in previous.Where(id => !kept.Contains(id)))
            await DeleteAsync(id, cancellationToken);

        return stored;
    }

    /// <summary>
    /// Deletes every chunk whose metadata matches ALL of the given key/value filters.
    /// Returns the number of chunks deleted. Enables a bulk tenant/source purge in one call
    /// instead of a per-document delete loop. An empty filter is rejected — use
    /// <see cref="ClearAsync"/> to drop the whole store. Filter value semantics are the same as
    /// <see cref="SearchAsync"/>: keys AND-combine; a collection value matches ANY of its elements;
    /// unsupported value types throw <see cref="System.ArgumentException"/>.
    /// </summary>
    /// <remarks>
    /// Default implementation throws <see cref="System.NotSupportedException"/>; stores that
    /// support metadata-scoped deletion override this.
    /// </remarks>
    Task<int> DeleteByFilterAsync(
        Dictionary<string, object> filters,
        CancellationToken cancellationToken = default)
        => throw new System.NotSupportedException(
            $"{GetType().Name} does not support DeleteByFilterAsync.");

    /// <summary>
    /// Moves every chunk of <paramref name="oldDocumentId"/> to <paramref name="newDocumentId"/>, renaming each
    /// chunk to the id <paramref name="chunkIdMap"/> gives it and applying <paramref name="metadataUpdates"/> to its
    /// metadata. Content, token counts and embeddings are kept exactly as stored — nothing is re-embedded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is how a document whose identity derives from something that changed (a file path, say) keeps its index
    /// rows instead of being deleted and indexed again: the vectors already describe the content, only the keys and
    /// the provenance around them are stale.
    /// </para>
    /// <para>
    /// Everything is checked before anything is written, and a failed check throws with the store unchanged:
    /// <list type="bullet">
    /// <item><description>Every chunk the store holds for <paramref name="oldDocumentId"/> must have an entry in
    /// <paramref name="chunkIdMap"/> — otherwise <see cref="System.ArgumentException"/>. Entries for ids the store does
    /// not hold are ignored, so one map can serve every index of a hybrid setup.</description></item>
    /// <item><description><paramref name="newDocumentId"/> must have no chunks, and no new chunk id may already be
    /// stored — otherwise <see cref="System.InvalidOperationException"/>. A reassignment never merges documents or
    /// overwrites rows.</description></item>
    /// <item><description>Argument rules (blank or equal document ids, an empty map, duplicate new ids) are those of
    /// <see cref="Utilities.DocumentReassignment.ValidateArguments"/>.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// A metadata update with a null value removes that key. The keys a store maintains itself follow the move (see
    /// <see cref="Utilities.DocumentReassignment.RewriteMetadata"/>). The write is atomic where the backend offers a
    /// transaction; a store without one writes the new rows before deleting the old, so an interrupted call leaves
    /// the document duplicated rather than missing.
    /// </para>
    /// <para>
    /// The default implementation throws <see cref="System.NotSupportedException"/>; every store shipped with
    /// FluxIndex overrides it.
    /// </para>
    /// </remarks>
    /// <param name="oldDocumentId">The document whose chunks move.</param>
    /// <param name="newDocumentId">The document id the chunks move to.</param>
    /// <param name="chunkIdMap">Old chunk id to new chunk id, covering every chunk of the old document.</param>
    /// <param name="metadataUpdates">Metadata keys to set on every moved chunk; a null value removes the key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of chunks moved; 0 when the store holds nothing for <paramref name="oldDocumentId"/>.</returns>
    Task<int> ReassignDocumentAsync(
        string oldDocumentId,
        string newDocumentId,
        IReadOnlyDictionary<string, string> chunkIdMap,
        IReadOnlyDictionary<string, object?>? metadataUpdates = null,
        CancellationToken cancellationToken = default)
        => throw new System.NotSupportedException(
            $"{GetType().Name} does not support ReassignDocumentAsync.");

    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);
    Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    /// <summary>
    /// Replaces the stored content, token count, metadata and (when supplied) embedding of an
    /// existing chunk.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the chunk exists and the store has persisted the update — including the
    /// case where the supplied values are identical to what is stored, which is a successful
    /// no-op. <c>false</c> when no chunk with this id exists, or when the update had changes that
    /// reached no row.
    /// </returns>
    /// <remarks>
    /// The return value is meaningful and must not be discarded. Implementations backed by a
    /// change-tracking ORM can accept an update, report success, and write nothing; a caller that
    /// ignores the result cannot tell that apart from a completed write, which is how such a
    /// defect stays silent all the way to the consumer.
    /// </remarks>
    Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the number of distinct documents stored in this vector store.
    /// Used for statistics reporting without relying on the document repository.
    /// </summary>
    Task<int> GetDistinctDocumentCountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    /// <summary>
    /// Checks if any vectors exist for a given document.
    /// Used by integrity checks to detect missing embeddings.
    /// </summary>
    async Task<bool> HasVectorsForDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        // Only the presence of ids matters, so this goes through the ids-only path rather than
        // loading every chunk's content and embedding to then discard them.
        var chunkIds = await GetChunkIdsByDocumentIdAsync(documentId, cancellationToken);
        return chunkIds.Count > 0;
    }

    /// <summary>
    /// The runtime-resolved store/collection name. Null if not yet initialized.
    /// </summary>
    string? ResolvedStoreName => null;

    /// <summary>
    /// The runtime-detected embedding dimension. Null if not yet detected.
    /// </summary>
    int? DetectedDimension => null;

    /// <summary>
    /// The embedding identity bound to this store. Null if not yet initialized.
    /// Once bound, any attempt to store vectors from a different model will throw
    /// <see cref="FluxIndex.Core.Domain.Exceptions.EmbeddingModelMismatchException"/>.
    /// </summary>
    EmbeddingIdentity? BoundIdentity => null;

    /// <summary>
    /// Binds an embedding identity to this store.
    /// Once bound, the store uses the identity's fingerprint for collection/table naming
    /// and validates that subsequent operations use the same model.
    /// </summary>
    /// <remarks>
    /// Default implementation is a no-op for stores that don't support identity binding.
    /// </remarks>
    void BindIdentity(EmbeddingIdentity identity) { }

    /// <summary>
    /// Verifies that the vector store is operational by testing a write+delete cycle.
    /// Returns false if the store is corrupted and attempts self-healing.
    /// </summary>
    Task<bool> VerifyHealthAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}