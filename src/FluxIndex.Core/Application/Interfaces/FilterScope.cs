using System.Security.Cryptography;
using System.Text;
using FluxIndex.Core.Application.Services.Base;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// A stable name for the scope a metadata filter selects, for keying anything cached per scope (search results, a
/// semantic cache partition). Two filters that admit the same chunks under the <see cref="IVectorStore.SearchAsync"/>
/// filter contract get the same name: keys are compared in ordinal order, a collection value is the set of its elements
/// (order and duplicates do not matter), and a scalar is the one-element set — so <c>{"tag": "a"}</c> and
/// <c>{"tag": ["a"]}</c> name the same scope.
/// </summary>
public static class FilterScope
{
    /// <summary>
    /// The name of the scope <paramref name="filters"/> selects, or <see cref="Unscoped"/> when there is no filter.
    /// </summary>
    /// <exception cref="ArgumentException">A value the filter contract does not accept (an object, a nested or empty collection).</exception>
    public static string Of(IReadOnlyDictionary<string, object>? filters)
    {
        if (filters is not { Count: > 0 })
            return Unscoped;

        var canonical = new StringBuilder();
        foreach (var key in filters.Keys.Order(StringComparer.Ordinal))
        {
            var values = VectorStoreBase.ExpandFilterValue(key, filters[key])
                .Select(v => v is null ? "\u0000" : v)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);

            AppendEscaped(canonical, key);
            canonical.Append('=');
            canonical.AppendJoin('|', values.Select(Escape));
            canonical.Append(';');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    /// <summary>The name of the scope of a search without a filter.</summary>
    public const string Unscoped = "";

    private static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        AppendEscaped(sb, value);
        return sb.ToString();
    }

    // The separators of the canonical form are escaped inside keys and values, so «a|b» as one value and «a», «b» as two
    // cannot produce the same text.
    private static void AppendEscaped(StringBuilder sb, string value)
    {
        foreach (var c in value)
        {
            if (c is '\\' or '=' or '|' or ';')
                sb.Append('\\');
            sb.Append(c);
        }
    }
}
