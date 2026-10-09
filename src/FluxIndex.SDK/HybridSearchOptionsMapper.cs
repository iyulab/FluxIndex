namespace FluxIndex.SDK;

/// <summary>
/// Maps <see cref="SearchOptions"/> onto the Core options the hybrid search service consumes. Every hybrid knob passes
/// through as given; one left unset is chosen by the service per query.
/// </summary>
internal static class HybridSearchOptionsMapper
{
    /// <summary>
    /// Core options for a search driven by <see cref="SearchOptions"/>.
    /// </summary>
    public static Core.Domain.Models.HybridSearchOptions FromSearchOptions(SearchOptions options)
    {
        var coreOptions = new Core.Domain.Models.HybridSearchOptions
        {
            MaxResults = options.TopK,
            VectorWeight = options.VectorWeight,
            SparseWeight = options.KeywordWeight,
            FusionMethod = options.FusionMethod,
            Filters = ToCoreFilters(options)
        };

        if (options.RrfK is { } rrfK)
            coreOptions.RrfK = rrfK;

        // A similarity floor belongs on the vector leg. Compared with the fused score — rank-sized under
        // reciprocal rank fusion, about 0.016 at best — a similarity-sized threshold drops every result.
        coreOptions.VectorOptions.MinScore = options.MinSimilarity;
        return coreOptions;
    }

    /// <summary>
    /// Carries <see cref="SearchOptions.MetadataFilters"/> across to the Core options.
    /// </summary>
    /// <remarks>
    /// Vector-only search applied these filters and the hybrid path discarded them, so turning
    /// hybrid search on silently widened the result set to the whole index. That is the worst shape
    /// a scoping bug can take: the caller sees results, they are simply the wrong ones.
    /// </remarks>
    private static Dictionary<string, object> ToCoreFilters(SearchOptions options)
        => options.MetadataFilters?.Count > 0
            ? options.MetadataFilters.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value)
            : [];
}
