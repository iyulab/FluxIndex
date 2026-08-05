using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace FluxIndex.Core.Application.Services.KeywordSearch;

/// <summary>
/// Turns metadata values into the text form the keyword index stores and compares them by.
/// </summary>
/// <remarks>
/// <para>
/// Indexing and querying must agree on this exactly, or a filter matches nothing for reasons no
/// caller can see: <c>1</c> written by the indexer and <c>"1"</c> supplied by the query are the same
/// value to a consumer and different strings to SQL. Both sides therefore go through
/// <see cref="TryFormat"/> — the shared function is what makes that agreement structural rather
/// than a convention two call sites happen to follow.
/// </para>
/// <para>
/// The same reasoning applies across backends. Formatting here rather than in the SQL dialects keeps
/// PostgreSQL and SQLite comparing byte-identical text, so backend equivalence does not depend on
/// two databases agreeing on how to render a double or a timestamp — they never see the original
/// type at all.
/// </para>
/// </remarks>
public static class KeywordMetadataFilter
{
    /// <summary>
    /// Renders a scalar metadata value as its index text, or reports that it is not filterable.
    /// </summary>
    /// <remarks>
    /// Filterability is decided by <em>value type</em>, not by key name. A key-based rule needs a
    /// list of known keys and silently drops anything a consumer adds later; the type rule admits
    /// whatever is comparable and is stable as vocabularies grow.
    /// </remarks>
    public static bool TryFormat(object? value, out string formatted)
    {
        formatted = string.Empty;

        switch (value)
        {
            case null:
                return false;

            case string s:
                formatted = s;
                return true;

            case bool b:
                // Lower-case so it matches the JSON rendering of the same value, which is what a
                // consumer sees when reading the metadata back off the chunk.
                formatted = b ? "true" : "false";
                return true;

            case DateTime dt:
                formatted = dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                return true;

            case DateTimeOffset dto:
                formatted = dto.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                return true;

            case Guid g:
                formatted = g.ToString("D", CultureInfo.InvariantCulture);
                return true;

            case IFormattable f when IsNumeric(value):
                formatted = f.ToString(null, CultureInfo.InvariantCulture);
                return true;

            // Metadata that has been through a JSON round trip arrives as JsonElement rather than as
            // the type it was written with. Deserializing to Dictionary<string, object> is how the
            // relational backend reads its own metadata column back, so this is the common shape on
            // any path that re-reads a stored chunk, not an edge case.
            case JsonElement json:
                return TryFormatJson(json, out formatted);

            default:
                return false;
        }
    }

    /// <summary>
    /// Expands one filter entry into the set of accepted texts. A collection means match-any; a
    /// scalar means match-one. Returns false when nothing in the entry is filterable, which callers
    /// must treat as an error rather than as "no condition" — see <see cref="Expand"/>'s remarks.
    /// </summary>
    public static bool TryExpand(object? value, out IReadOnlyList<string> accepted)
    {
        if (value is not null and not string and IEnumerable enumerable)
        {
            var values = new List<string>();
            foreach (var item in enumerable)
            {
                if (TryFormat(item, out var formattedItem))
                    values.Add(formattedItem);
            }

            accepted = values;
            return values.Count > 0;
        }

        if (TryFormat(value, out var formatted))
        {
            accepted = [formatted];
            return true;
        }

        accepted = [];
        return false;
    }

    /// <summary>
    /// Expands a whole filter, throwing on any entry that cannot be filtered.
    /// </summary>
    /// <remarks>
    /// Skipping an unusable entry would widen the filter — the caller asked for A AND B, and dropping
    /// B returns rows they meant to exclude. In a tenant-scoped delete that silently destroys another
    /// tenant's rows, so an unfilterable entry fails loudly at the call instead.
    /// </remarks>
    /// <exception cref="ArgumentException">The filter is empty, or an entry is not filterable.</exception>
    public static IReadOnlyList<(string Key, IReadOnlyList<string> Accepted)> Expand(
        IReadOnlyDictionary<string, object> filter,
        string paramName)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.Count == 0)
            throw new ArgumentException("Filter must contain at least one condition.", paramName);

        var expanded = new List<(string, IReadOnlyList<string>)>(filter.Count);
        foreach (var (key, value) in filter)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Filter keys must be non-empty.", paramName);

            if (!TryExpand(value, out var accepted))
            {
                throw new ArgumentException(
                    $"Filter value for '{key}' is not filterable. Only scalars (string, number, " +
                    "bool, date, guid) and collections of scalars can be matched.",
                    paramName);
            }

            expanded.Add((key, accepted));
        }

        return expanded;
    }

    /// <summary>
    /// Projects a chunk's metadata to the (key, value) rows the index can filter on. Non-scalar
    /// entries are left out; they remain readable in the stored metadata payload.
    /// </summary>
    public static IEnumerable<(string Key, string Value)> Project(IReadOnlyDictionary<string, object>? metadata)
    {
        if (metadata is null)
            yield break;

        foreach (var (key, value) in metadata)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            // A collection becomes several rows, so a chunk tagged with many values for one key
            // matches a filter naming any of them - the storage-side half of match-any.
            if (value is not null and not string and IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (TryFormat(item, out var formattedItem))
                        yield return (key, formattedItem);
                }

                continue;
            }

            if (TryFormat(value, out var formatted))
                yield return (key, formatted);
        }
    }

    /// <summary>
    /// Evaluates an expanded filter against a chunk's metadata in memory, with the semantics the SQL
    /// backends implement: every entry must match, and an entry matches when any of its accepted
    /// values is present.
    /// </summary>
    /// <remarks>
    /// This runs the metadata through <see cref="Project"/> — the same projection the relational
    /// backends persist — so an in-memory index and a SQL index answer the same filter identically.
    /// Comparing the raw values here instead would reintroduce exactly the type-vs-text mismatch the
    /// shared formatting exists to remove.
    /// </remarks>
    public static bool Matches(
        IReadOnlyDictionary<string, object>? metadata,
        IReadOnlyList<(string Key, IReadOnlyList<string> Accepted)> expanded)
    {
        if (expanded.Count == 0)
            return true;

        if (metadata is null || metadata.Count == 0)
            return false;

        var projected = new HashSet<(string, string)>(Project(metadata));

        foreach (var (key, accepted) in expanded)
        {
            var satisfied = false;
            foreach (var value in accepted)
            {
                if (projected.Contains((key, value)))
                {
                    satisfied = true;
                    break;
                }
            }

            if (!satisfied)
                return false;
        }

        return true;
    }

    private static bool IsNumeric(object value) => value
        is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static bool TryFormatJson(JsonElement json, out string formatted)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.String:
                formatted = json.GetString() ?? string.Empty;
                return true;
            case JsonValueKind.Number:
                // Integers take the integer path first: routing everything through double would
                // render a long past 2^53 in exponential form, so the same value would index
                // differently depending on whether it had been through JSON. Non-integers then
                // render as double, which matches how a CLR double formats.
                formatted = json.TryGetInt64(out var asLong)
                    ? asLong.ToString(CultureInfo.InvariantCulture)
                    : json.GetDouble().ToString(CultureInfo.InvariantCulture);
                return true;
            case JsonValueKind.True:
                formatted = "true";
                return true;
            case JsonValueKind.False:
                formatted = "false";
                return true;
            default:
                formatted = string.Empty;
                return false;
        }
    }
}
