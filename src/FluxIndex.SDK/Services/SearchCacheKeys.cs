using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.SDK.Services;

/// <summary>
/// The keys the <see cref="Retriever"/> caches under and the <see cref="Indexer"/> invalidates, in one place so the two
/// cannot drift.
/// </summary>
/// <remarks>
/// Search results are keyed under a <em>generation</em> that every indexer write replaces, rather than tracked per
/// document: a write can change the result of any query — a new document can enter a result set it was never in —
/// so a per-document index would miss exactly the case of an add. The generation lives in the same
/// <see cref="ICacheService"/> as the results, so a cache shared between processes (Redis) shares it too. Replacing it
/// leaves the old entries unreachable until they expire. A generation that is evicted is replaced by a fresh value,
/// never a reused one, so eviction can only cost a cache miss.
/// </remarks>
internal static class SearchCacheKeys
{
    private const string GenerationKey = "fluxindex:search:generation";

    // Long enough to outlive every result stored under it; an expired generation only costs a miss.
    private static readonly TimeSpan s_generationLifetime = TimeSpan.FromDays(30);

    public static string Document(string documentId) => $"doc:{documentId}";

    // The filter enters a key as its scope's name (FilterScope): entry order and how a value is spelled (a scalar or a
    // one-element list) do not split one scope, and two collections can never share a key — printing a value would give
    // every List<string> the same «System.Collections.Generic.List`1[System.String]».
    public static string Search(string generation, string query, int maxResults, float minScore, Dictionary<string, object>? filter)
        => FormattableString.Invariant($"search:{generation}:{FilterScope.Of(filter)}:{maxResults}:{minScore}:{query}");

    /// <summary>
    /// The key of a hybrid search's results: everything <see cref="HybridSearchOptionsMapper"/> carries to the hybrid
    /// service, so two searches share an entry only when the service would see the same request.
    /// </summary>
    public static string HybridSearch(string generation, string query, int fetchCount, SearchOptions options)
        => FormattableString.Invariant(
            $"hsearch:{generation}:{FilterScope.Of(options.MetadataFilters)}:{fetchCount}:{options.MinSimilarity}:{options.VectorWeight}:{options.KeywordWeight}:{options.FusionMethod}:{options.RrfK}:{query}");

    public static async Task<string> CurrentGenerationAsync(ICacheService cache, CancellationToken cancellationToken)
    {
        var generation = await cache.GetAsync<string>(GenerationKey, cancellationToken).ConfigureAwait(false);
        if (generation != null)
            return generation;

        generation = Guid.NewGuid().ToString("N");
        await cache.SetAsync(GenerationKey, generation, s_generationLifetime, cancellationToken).ConfigureAwait(false);
        return generation;
    }

    /// <summary>Makes every cached search result unreachable and drops the cached copy of <paramref name="documentId"/>.</summary>
    public static async Task InvalidateAsync(ICacheService cache, string? documentId, CancellationToken cancellationToken)
    {
        await cache.SetAsync(GenerationKey, Guid.NewGuid().ToString("N"), s_generationLifetime, cancellationToken).ConfigureAwait(false);
        if (documentId != null)
            await cache.RemoveAsync(Document(documentId), cancellationToken).ConfigureAwait(false);
    }
}
