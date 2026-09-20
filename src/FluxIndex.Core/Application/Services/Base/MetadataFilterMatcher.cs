namespace FluxIndex.Core.Application.Services.Base;

/// <summary>
/// A metadata filter with its match alternatives already expanded and normalized, so matching many
/// rows against one filter costs one lookup per row instead of re-expanding the filter every time.
/// </summary>
/// <remarks>
/// <para>
/// Match semantics are exactly <see cref="VectorStoreBase.MatchesMetadataFilter"/>'s — keys
/// AND-combine, a collection-valued entry matches when the metadata value equals ANY of its
/// elements — because this type composes the same
/// <see cref="VectorStoreBase.ExpandFilterValue"/> and
/// <see cref="VectorStoreBase.NormalizeFilterValue"/> rather than restating them.
/// </para>
/// <para>
/// Why it exists: a store that cannot push a filter into its query walks candidate rows and matches
/// in memory. Expanding a filter per row makes a vault-wide scope quadratic in allowed values — a
/// scope of 100 documents over 6,000 rows was 600,000 normalizations for one search. Compiling once
/// makes that 6,000 set lookups.
/// </para>
/// <para>
/// Compiling validates: an unsupported filter value throws here rather than on the first row, so a
/// malformed filter fails the search even when no row reaches the match.
/// </para>
/// </remarks>
public sealed class MetadataFilterMatcher
{
    private readonly (string Key, HashSet<string> Alternatives, bool AllowsNull)[] _entries;

    private MetadataFilterMatcher((string, HashSet<string>, bool)[] entries) => _entries = entries;

    /// <summary>
    /// An empty filter, which every row matches. Returned by <see cref="Compile"/> for a null or
    /// empty filter dictionary so callers need no null branch of their own.
    /// </summary>
    public static MetadataFilterMatcher MatchAll { get; } = new([]);

    /// <summary>True when this matcher constrains nothing, so walking rows to apply it is wasted work.</summary>
    public bool IsMatchAll => _entries.Length == 0;

    /// <summary>
    /// Expands and normalizes <paramref name="filters"/> once. Throws the same exceptions
    /// <see cref="VectorStoreBase.ExpandFilterValue"/> throws for an unsupported or empty value.
    /// </summary>
    public static MetadataFilterMatcher Compile(IReadOnlyDictionary<string, object>? filters)
    {
        if (filters is not { Count: > 0 })
            return MatchAll;

        var entries = new (string, HashSet<string>, bool)[filters.Count];
        var i = 0;
        foreach (var (key, value) in filters)
        {
            var alternatives = new HashSet<string>(StringComparer.Ordinal);
            var allowsNull = false;
            foreach (var alternative in VectorStoreBase.ExpandFilterValue(key, value))
            {
                // A null alternative cannot live in the set (and a null metadata value normalizes to
                // null), so it is carried as its own flag rather than silently dropped.
                if (alternative is null)
                    allowsNull = true;
                else
                    alternatives.Add(alternative);
            }

            entries[i++] = (key, alternatives, allowsNull);
        }

        return new MetadataFilterMatcher(entries);
    }

    /// <summary>
    /// Returns true if <paramref name="metadata"/> satisfies every entry of the compiled filter.
    /// Null metadata matches only an empty filter, matching <see cref="VectorStoreBase.MatchesMetadataFilter"/>.
    /// </summary>
    public bool Matches(IReadOnlyDictionary<string, object>? metadata)
    {
        if (_entries.Length == 0)
            return metadata is not null;

        if (metadata is null)
            return false;

        foreach (var (key, alternatives, allowsNull) in _entries)
        {
            if (!metadata.TryGetValue(key, out var metaValue))
                return false;

            var normalized = VectorStoreBase.NormalizeFilterValue(metaValue);
            if (normalized is null ? !allowsNull : !alternatives.Contains(normalized))
                return false;
        }

        return true;
    }
}
