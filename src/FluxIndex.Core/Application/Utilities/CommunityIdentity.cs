using System.Security.Cryptography;
using System.Text;

namespace FluxIndex.Core.Application.Utilities;

/// <summary>
/// Derives community identity from what a community is — its level and its member chunks — so detecting the same
/// communities again yields the same ids and a graph store upserts them instead of adding new rows.
/// </summary>
/// <remarks>
/// Chunk ids are themselves deterministic (a re-index of unchanged content writes the same chunk ids), so an unchanged
/// document produces the same community ids build after build. Ids are GUID-formatted, name-based (RFC 9562 version 8,
/// the same construction as <see cref="ChunkStorageId"/>), so stores that key communities on a UUID accept them.
/// </remarks>
internal static class CommunityIdentity
{
    /// <summary>
    /// Namespace for community ids. <b>Never change this value</b> — it is what keeps a rebuilt community the same row.
    /// </summary>
    private static readonly Guid Namespace = new("8c1d4e7a-2f36-4b95-9a0c-5d7e3b1f6a24");

    /// <summary>
    /// The id of the community at <paramref name="level"/> whose members are <paramref name="chunkIds"/>, in any order,
    /// in <paramref name="graphPartition"/>. The same chunk ids can exist in two graph partitions (two tenants indexing
    /// the same content), so the partition is part of what a community is; the default partition derives the ids it
    /// always has, so an unpartitioned store keeps its rows.
    /// </summary>
    public static string For(int level, IEnumerable<string> chunkIds, string graphPartition = Interfaces.GraphPartition.Default)
    {
        var members = $"level|{level}|{string.Join('\n', chunkIds.Order(StringComparer.Ordinal))}";
        var name = graphPartition.Length == 0 ? members : $"partition|{graphPartition.Length}|{graphPartition}|{members}";
        return ChunkStorageId.CreateNameBasedUuid(Namespace, name).ToString();
    }

    /// <summary>
    /// A detection seed that is a function of the input: the same chunks give the same seed, so a randomized algorithm
    /// partitions them the same way every time. Stable across processes (not <see cref="string.GetHashCode()"/>).
    /// </summary>
    public static int SeedFor(IEnumerable<string> chunkIds)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', chunkIds.Order(StringComparer.Ordinal))));
        return BitConverter.ToInt32(digest, 0);
    }
}
