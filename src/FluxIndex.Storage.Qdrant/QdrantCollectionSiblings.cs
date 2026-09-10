using System.Text.RegularExpressions;

namespace FluxIndex.Storage.Qdrant;

/// <summary>
/// Identifies collections that this store could have created for the same
/// <see cref="QdrantOptions.BaseCollectionName"/> under a different naming outcome.
/// </summary>
/// <remarks>
/// <para>
/// A change of <see cref="CollectionNamingStrategy"/>, or of the bound embedding identity, resolves
/// the same logical index to a different collection name. Qdrant has no notion of that relationship:
/// the store simply creates the new name, reports ready, and every subsequent search returns zero
/// results while the previous collection still holds the data. Nothing throws, so the deployment
/// looks healthy.
/// </para>
/// <para>
/// The only place that relationship is visible is here, at initialization, where the resolved name
/// and the server's collection list are both in hand.
/// </para>
/// </remarks>
internal static partial class QdrantCollectionSiblings
{
    /// <summary>
    /// Suffixes this store is known to emit: the dimension fallback ("_384") and the embedding
    /// fingerprint ("_a1b2c3d4", 8 lowercase hex characters — see EmbeddingIdentity.Fingerprint).
    /// </summary>
    /// <remarks>
    /// Deliberately narrower than a bare prefix match. A consumer collection that merely starts with
    /// the same base name — "aims_chunks_archive" — is not something this store could have produced,
    /// and treating it as a sibling would let strict mode fail a boot over an unrelated collection.
    /// </remarks>
    [GeneratedRegex(@"^([0-9]+|[0-9a-f]{8})$")]
    private static partial Regex StoreEmittedSuffix();

    /// <summary>
    /// Returns the collections that share <paramref name="baseCollectionName"/> with
    /// <paramref name="resolvedCollectionName"/> and could hold data written under a previous
    /// naming outcome, in the order the server listed them.
    /// </summary>
    /// <param name="baseCollectionName">The configured base name.</param>
    /// <param name="resolvedCollectionName">The name this store just resolved to.</param>
    /// <param name="existingCollections">Collection names as reported by the server.</param>
    public static IReadOnlyList<string> Find(
        string baseCollectionName,
        string resolvedCollectionName,
        IEnumerable<string> existingCollections)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseCollectionName);
        ArgumentException.ThrowIfNullOrEmpty(resolvedCollectionName);
        ArgumentNullException.ThrowIfNull(existingCollections);

        var prefix = baseCollectionName + "_";

        return existingCollections
            .Where(name => !string.Equals(name, resolvedCollectionName, StringComparison.Ordinal))
            .Where(name => IsSibling(name, baseCollectionName, prefix))
            .ToList();
    }

    /// <summary>
    /// Renders the siblings that hold data, or <c>null</c> when none of them do and there is
    /// nothing worth reporting.
    /// </summary>
    /// <param name="counted">
    /// Sibling names with their point count, or <c>null</c> where the count could not be read.
    /// A sibling whose count is unknown is reported: it may hold the data, and the whole point of
    /// the check is that silence here is indistinguishable from safety.
    /// </param>
    public static string? DescribePopulated(IEnumerable<(string Name, long? Count)> counted)
    {
        ArgumentNullException.ThrowIfNull(counted);

        var populated = counted
            .Where(entry => entry.Count is null or > 0)
            .Select(entry => entry.Count is { } count
                ? $"{entry.Name} ({count} points)"
                : $"{entry.Name} (count unknown)")
            .ToList();

        return populated.Count == 0 ? null : string.Join(", ", populated);
    }

    private static bool IsSibling(string name, string baseCollectionName, string prefix)
    {
        // The Fixed strategy writes the base name itself, so a Fixed -> ModelFingerprint switch
        // leaves the data there.
        if (string.Equals(name, baseCollectionName, StringComparison.Ordinal))
            return true;

        if (!name.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        return StoreEmittedSuffix().IsMatch(name[prefix.Length..]);
    }
}
