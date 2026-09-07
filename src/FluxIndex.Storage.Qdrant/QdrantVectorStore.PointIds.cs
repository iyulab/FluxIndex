using FluxIndex.Core.Application.Utilities;
using Google.Protobuf.Collections;
using Qdrant.Client.Grpc;

namespace FluxIndex.Storage.Qdrant;

/// <summary>
/// Point-id handling. Qdrant accepts only UUIDs or unsigned integers as point ids, while
/// <c>DocumentChunk.Id</c> is a free string across the <c>IVectorStore</c> contract. The derivation
/// itself lives in <see cref="ChunkStorageId"/> because the PostgreSQL adapter keys on <c>uuid</c>
/// and needs exactly the same one; what belongs here is only how this store carries the caller's
/// original id alongside the derived point id.
/// </summary>
public partial class QdrantVectorStore
{
    /// <summary>Payload key holding the chunk id the caller supplied.</summary>
    private const string ChunkIdPayloadKey = "chunk_id";

    /// <summary>Maps a chunk id onto the Qdrant point id it is stored under.</summary>
    internal static Guid ToPointId(string chunkId) => ChunkStorageId.ToStorageGuid(chunkId);

    /// <summary>
    /// Recovers the caller's original chunk id from a point's payload, or <c>null</c> when the
    /// point predates this derivation (those were written under a UUID chunk id, so the point id
    /// itself is still the right answer).
    /// </summary>
    private static string? ChunkIdFromPayload(MapField<string, Value> payload)
        => payload.TryGetValue(ChunkIdPayloadKey, out var value) && !string.IsNullOrEmpty(value.StringValue)
            ? value.StringValue
            : null;
}
