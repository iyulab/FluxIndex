using System.Security.Cryptography;
using System.Text;

namespace FluxIndex.Core.Application.Utilities;

/// <summary>
/// Maps a free-string <c>DocumentChunk.Id</c> onto the GUID that stores requiring one use as their
/// key, and names the metadata/payload key those stores keep the caller's original id under.
/// </summary>
/// <remarks>
/// <para>
/// <c>DocumentChunk.Id</c> is a free string across the <c>IVectorStore</c> contract, but some
/// backends cannot store it as one: Qdrant accepts only UUIDs or unsigned integers as point ids,
/// and the PostgreSQL schema keys its vector tables on <c>uuid</c>. Absorbing that difference is
/// each adapter's job — a consumer that has to know which store is underneath has lost the reason
/// the abstraction exists — and this is the one derivation they share, so the same chunk id maps to
/// the same row whichever of them wrote it.
/// </para>
/// <para>
/// Adapters that can store a string id directly (SQLite, Neo4j) do not use this and should not
/// start: passing the id through unchanged is strictly better where the backend allows it.
/// </para>
/// </remarks>
public static class ChunkStorageId
{
    /// <summary>
    /// Key under which a store keeps the chunk id its caller supplied, so reads can return that id
    /// rather than the derived one.
    /// </summary>
    public const string OriginalIdKey = "chunkId";

    /// <summary>
    /// Namespace for name-based chunk storage ids.
    /// </summary>
    /// <remarks>
    /// <b>Never change this value.</b> It is what makes the derivation stable across processes and
    /// releases, which is what keeps re-storing a chunk an update rather than a duplicate insert.
    /// </remarks>
    private static readonly Guid Namespace = new("3f2a7c18-9b64-4d1e-8a05-6c7e1f4b2d93");

    /// <summary>
    /// Returns the storage GUID a chunk id is keyed under.
    /// </summary>
    /// <remarks>
    /// A chunk id that already is a UUID is used verbatim, so rows written before this derivation
    /// existed keep their keys and stay readable. Anything else is hashed into a name-based UUID,
    /// which is deterministic: the same chunk id always resolves to the same row, in this process
    /// and the next.
    /// </remarks>
    public static Guid ToStorageGuid(string chunkId)
        => Guid.TryParse(chunkId, out var parsed)
            ? parsed
            : CreateNameBasedUuid(Namespace, chunkId);

    /// <summary>
    /// Builds a deterministic name-based UUID from a namespace and a name, tagged as RFC 9562
    /// version 8 (implementation-defined).
    /// </summary>
    /// <remarks>
    /// The classic name-based version is 5, but RFC 4122 fixes that to SHA-1 — a weak digest that
    /// static analysis rightly rejects, and suppressing the rule to keep a version number would be
    /// the wrong trade. RFC 9562 §5.8 defines version 8 precisely for a custom construction like
    /// this one, so the hash is SHA-256 truncated to 128 bits and the result is labelled honestly.
    /// The bytes are still laid out per the RFC: namespace first in big-endian field order, then
    /// the UTF-8 name.
    /// </remarks>
    private static Guid CreateNameBasedUuid(Guid namespaceId, string name)
    {
        // Guid.ToByteArray emits the first three fields little-endian, so both ends of this method
        // swap to keep the hashed form and the produced value in the RFC's byte order.
        var namespaceBytes = namespaceId.ToByteArray();
        SwapToBigEndian(namespaceBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, namespaceBytes.Length);

        var hash = SHA256.HashData(input);

        var result = new byte[16];
        Array.Copy(hash, result, 16);
        result[6] = (byte)((result[6] & 0x0F) | 0x80); // version 8
        result[8] = (byte)((result[8] & 0x3F) | 0x80); // RFC 9562 variant

        SwapToBigEndian(result);
        return new Guid(result);
    }

    private static void SwapToBigEndian(byte[] guidBytes)
    {
        (guidBytes[0], guidBytes[3]) = (guidBytes[3], guidBytes[0]);
        (guidBytes[1], guidBytes[2]) = (guidBytes[2], guidBytes[1]);
        (guidBytes[4], guidBytes[5]) = (guidBytes[5], guidBytes[4]);
        (guidBytes[6], guidBytes[7]) = (guidBytes[7], guidBytes[6]);
    }
}
