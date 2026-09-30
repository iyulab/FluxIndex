using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Base;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// Translates an <c>IVectorStore</c> metadata filter into a jsonb containment predicate that
/// PostgreSQL can evaluate, so the filter runs as part of the query rather than over rows already
/// selected by similarity.
/// </summary>
/// <remarks>
/// <para>
/// This lives outside either store because both need it and they map different entity types. A
/// filter applied after a candidate window silently loses matches — higher-scoring non-matching
/// rows fill the window first — so the two stores must not drift apart on where the filter runs.
/// </para>
/// <para>
/// Each branch is authored as a lambda over the metadata dictionary itself, then re-parented onto
/// the entity's own property access. That keeps the emitted tree identical to a hand-written
/// <c>v =&gt; EF.Functions.JsonContains(v.Metadata, json)</c> — deliberately not an interface member
/// access, whose translation is provider-dependent.
/// </para>
/// </remarks>
internal static class MetadataPredicateBuilder
{
    /// <summary>
    /// Builds the predicate for <paramref name="filters"/> against <typeparamref name="TEntity"/>.
    /// </summary>
    /// <param name="filters">Filter contract values; a collection matches ANY of its elements.</param>
    /// <param name="metadata">Selects the entity's jsonb metadata column.</param>
    /// <param name="documentId">
    /// Selects the entity's document id column, which a <see cref="FilterKeys.DocumentId"/> entry
    /// compares instead of the metadata.
    /// </param>
    public static Expression<Func<TEntity, bool>> Build<TEntity>(
        Dictionary<string, object> filters,
        Expression<Func<TEntity, Dictionary<string, object>>> metadata,
        Expression<Func<TEntity, string>> documentId)
    {
        // Contract validation (throws on unsupported / empty-collection values — fail-loud).
        VectorStoreBase.ValidateFilters(filters);

        var parameter = metadata.Parameters[0];
        var metadataAccess = metadata.Body;
        var documentIdAccess = new ParameterReplaceVisitor(documentId.Parameters[0], parameter).Visit(documentId.Body)!;

        Expression? predicate = null;
        var scalars = new Dictionary<string, object?>();

        foreach (var (key, value) in filters)
        {
            if (string.Equals(key, FilterKeys.DocumentId, StringComparison.Ordinal))
            {
                // The column, not a metadata copy: `document_id = ANY(@ids)`, as the other stores and
                // the keyword index resolve the same key.
                var ids = VectorStoreBase.ExpandFilterValue(key, value)
                    .Where(id => id is not null)
                    .Select(id => id!)
                    .ToList();
                var inIds = Expression.Call(
                    typeof(Enumerable), nameof(Enumerable.Contains), [typeof(string)],
                    Expression.Constant(ids), documentIdAccess);
                predicate = predicate is null ? inIds : Expression.AndAlso(predicate, inIds);
                continue;
            }

            var rawAlternatives = EnumerateRawAlternatives(value);
            if (rawAlternatives is null)
            {
                scalars[key] = value;
                continue;
            }

            Expression? keyPredicate = null;
            foreach (var raw in rawAlternatives)
            {
                var branch = ContainsJson(metadataAccess, new Dictionary<string, object?> { [key] = raw });
                keyPredicate = keyPredicate is null ? branch : Expression.OrElse(keyPredicate, branch);
            }

            predicate = predicate is null ? keyPredicate : Expression.AndAlso(predicate, keyPredicate!);
        }

        if (scalars.Count > 0)
        {
            var scalarPredicate = ContainsJson(metadataAccess, scalars);
            predicate = predicate is null ? scalarPredicate : Expression.AndAlso(predicate, scalarPredicate);
        }

        return Expression.Lambda<Func<TEntity, bool>>(predicate!, parameter);
    }

    /// <summary>
    /// Builds <c>EF.Functions.JsonContains(&lt;metadata&gt;, "{...}")</c> for one containment fragment.
    /// </summary>
    private static Expression ContainsJson(Expression metadataAccess, Dictionary<string, object?> fragment)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(fragment);

        // Authored as a lambda so the compiler resolves the JsonContains overload, then re-parented
        // onto the entity's metadata access.
        Expression<Func<Dictionary<string, object>, bool>> template =
            m => EF.Functions.JsonContains(m, json);

        return new ParameterReplaceVisitor(template.Parameters[0], metadataAccess).Visit(template.Body)!;
    }

    /// <summary>
    /// Returns the raw elements of a collection-typed filter value, or null when the value is a
    /// scalar. Values are NOT normalized to strings here — jsonb containment must compare against
    /// the natively-typed JSON stored in the metadata column.
    /// </summary>
    private static List<object?>? EnumerateRawAlternatives(object? value)
    {
        switch (value)
        {
            case string or bool or null:
                return null;
            case System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } je:
                var fromJson = new List<object?>();
                foreach (var item in je.EnumerateArray())
                    fromJson.Add(item);
                return fromJson;
            case System.Text.Json.JsonElement:
                return null;
            case System.Collections.IEnumerable enumerable:
                var raw = new List<object?>();
                foreach (var item in enumerable)
                    raw.Add(item);
                return raw;
            default:
                return null;
        }
    }

    private sealed class ParameterReplaceVisitor(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
