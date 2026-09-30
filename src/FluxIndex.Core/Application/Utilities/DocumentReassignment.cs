using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.Core.Application.Utilities;

/// <summary>
/// The rules every implementation of <see cref="IVectorStore.ReassignDocumentAsync"/> and
/// <see cref="IKeywordSearchService.ReassignDocumentAsync"/> shares: which arguments are rejected, when
/// a stored document is not fully covered by the id map, and how a chunk's metadata changes when it moves.
/// </summary>
/// <remarks>
/// Kept in one place so that every store rejects the same inputs with the same exception and rewrites
/// metadata the same way — a store that quietly accepted a partial map would move some chunks and leave
/// the rest behind under a document id nobody asks for any more.
/// </remarks>
public static class DocumentReassignment
{
    /// <summary>
    /// Validates the arguments of a reassignment before anything is read or written.
    /// </summary>
    /// <param name="oldDocumentId">The document whose chunks move.</param>
    /// <param name="newDocumentId">The document id they move to.</param>
    /// <param name="chunkIdMap">Old chunk id to new chunk id.</param>
    /// <exception cref="ArgumentNullException">The map is null.</exception>
    /// <exception cref="ArgumentException">
    /// A document id is blank, both ids are equal, the map is empty, a key or value is blank, two keys map
    /// to the same new id, or a new id is also an old id in the map.
    /// </exception>
    public static void ValidateArguments(
        string oldDocumentId,
        string newDocumentId,
        IReadOnlyDictionary<string, string> chunkIdMap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldDocumentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newDocumentId);
        ArgumentNullException.ThrowIfNull(chunkIdMap);

        if (string.Equals(oldDocumentId, newDocumentId, StringComparison.Ordinal))
            throw new ArgumentException(
                $"The old and new document ids are both '{oldDocumentId}'; a reassignment must change the document id.",
                nameof(newDocumentId));

        if (chunkIdMap.Count == 0)
            throw new ArgumentException("The chunk id map is empty.", nameof(chunkIdMap));

        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (oldId, newId) in chunkIdMap)
        {
            if (string.IsNullOrWhiteSpace(oldId) || string.IsNullOrWhiteSpace(newId))
                throw new ArgumentException("The chunk id map contains a blank key or value.", nameof(chunkIdMap));

            if (!targets.Add(newId))
                throw new ArgumentException(
                    $"The chunk id map sends more than one chunk to '{newId}'; new chunk ids must be distinct.",
                    nameof(chunkIdMap));
        }

        foreach (var newId in targets)
        {
            if (chunkIdMap.ContainsKey(newId))
                throw new ArgumentException(
                    $"The chunk id map uses '{newId}' both as an old and as a new chunk id.",
                    nameof(chunkIdMap));
        }
    }

    /// <summary>
    /// Throws unless every chunk id a store holds for the old document has an entry in the map. Call it
    /// with the store's own ids, after reading and before writing.
    /// </summary>
    /// <param name="storedChunkIds">The ids the store holds for the old document.</param>
    /// <param name="chunkIdMap">Old chunk id to new chunk id.</param>
    /// <exception cref="ArgumentException">At least one stored id has no entry; the message names them.</exception>
    public static void EnsureCovered(IEnumerable<string> storedChunkIds, IReadOnlyDictionary<string, string> chunkIdMap)
    {
        ArgumentNullException.ThrowIfNull(storedChunkIds);
        ArgumentNullException.ThrowIfNull(chunkIdMap);

        var missing = storedChunkIds.Where(id => !chunkIdMap.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToList();
        if (missing.Count == 0)
            return;

        const int shown = 10;
        var listed = string.Join(", ", missing.Take(shown));
        var more = missing.Count > shown ? $" and {missing.Count - shown} more" : string.Empty;
        throw new ArgumentException(
            $"The chunk id map has no entry for {missing.Count} stored chunk(s): {listed}{more}. Nothing was changed.",
            nameof(chunkIdMap));
    }

    /// <summary>
    /// The exception a store throws when the new document id already has chunks.
    /// </summary>
    public static InvalidOperationException TargetDocumentNotEmpty(string newDocumentId) =>
        new($"Document '{newDocumentId}' already has chunks; a reassignment does not merge documents. Nothing was changed.");

    /// <summary>
    /// The exception a store throws when a new chunk id is already taken by a stored chunk.
    /// </summary>
    public static InvalidOperationException TargetChunkIdsTaken(IReadOnlyCollection<string> takenIds)
    {
        ArgumentNullException.ThrowIfNull(takenIds);
        var listed = string.Join(", ", takenIds.Take(10));
        return new InvalidOperationException(
            $"{takenIds.Count} new chunk id(s) are already stored: {listed}. Nothing was changed.");
    }

    /// <summary>
    /// Returns a chunk's metadata as it reads after the move.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Each key of <paramref name="metadataUpdates"/> is set to its value, or removed when the value is null.</description></item>
    /// <item><description>Keys a store writes itself follow the move: <see cref="ChunkStorageId.OriginalIdKey"/> becomes the new chunk
    /// id, and <see cref="MetadataHelper.StandardKeys.DocumentId"/> becomes the new document id when it held the old one.</description></item>
    /// <item><description>Serialized chunk relationships (<see cref="MetadataHelper.ReservedKeys.ChunkRelationships"/>) that point at
    /// a moved chunk point at its new id.</description></item>
    /// </list>
    /// Updates are applied last, so a caller can still set any of those keys explicitly.
    /// </remarks>
    /// <param name="metadata">The chunk's current metadata; not modified.</param>
    /// <param name="oldDocumentId">The document the chunk leaves.</param>
    /// <param name="newDocumentId">The document the chunk joins.</param>
    /// <param name="newChunkId">The chunk's new id.</param>
    /// <param name="chunkIdMap">The full id map, for relationships between moved chunks.</param>
    /// <param name="metadataUpdates">Keys to set (or, with a null value, remove).</param>
    public static Dictionary<string, object> RewriteMetadata(
        IReadOnlyDictionary<string, object>? metadata,
        string oldDocumentId,
        string newDocumentId,
        string newChunkId,
        IReadOnlyDictionary<string, string> chunkIdMap,
        IReadOnlyDictionary<string, object?>? metadataUpdates)
    {
        ArgumentNullException.ThrowIfNull(chunkIdMap);

        var result = metadata == null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(metadata);

        if (result.ContainsKey(ChunkStorageId.OriginalIdKey))
            result[ChunkStorageId.OriginalIdKey] = newChunkId;

        if (result.TryGetValue(MetadataHelper.StandardKeys.DocumentId, out var storedDocumentId)
            && string.Equals(MetadataValueAsString(storedDocumentId), oldDocumentId, StringComparison.Ordinal))
        {
            result[MetadataHelper.StandardKeys.DocumentId] = newDocumentId;
        }

        var relationships = MetadataHelper.DeserializeRelationships(result);
        if (relationships != null && relationships.Count > 0)
        {
            var changed = false;
            foreach (var relationship in relationships)
            {
                if (chunkIdMap.TryGetValue(relationship.SourceChunkId, out var source))
                {
                    relationship.SourceChunkId = source;
                    changed = true;
                }

                if (chunkIdMap.TryGetValue(relationship.TargetChunkId, out var target))
                {
                    relationship.TargetChunkId = target;
                    changed = true;
                }
            }

            if (changed)
                MetadataHelper.SerializeRelationships(result, relationships);
        }

        if (metadataUpdates != null)
        {
            foreach (var (key, value) in metadataUpdates)
            {
                if (value is null)
                    result.Remove(key);
                else
                    result[key] = value;
            }
        }

        return result;
    }

    private static string? MetadataValueAsString(object? value) => value switch
    {
        null => null,
        string s => s,
        System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } je => je.GetString(),
        _ => value.ToString()
    };
}
