namespace FluxIndex.SDK;

/// <summary>
/// Maps the SDK's <see cref="HybridSearchOptions"/> onto the Core options the hybrid search service
/// consumes. Single-sourced so the two SDK entry points — <c>FluxIndexContext.HybridSearchV2Async</c>
/// and <c>Retriever.SearchAsync</c> — cannot drift in how they translate weights.
/// </summary>
internal static class HybridSearchOptionsMapper
{
    /// <summary>Weights used when the caller passes plain <see cref="SearchOptions"/>.</summary>
    internal const double DefaultVectorWeight = 0.7;

    internal const double DefaultSparseWeight = 0.3;

    public static Core.Domain.Models.HybridSearchOptions ToCore(HybridSearchOptions sdkOptions)
    {
        var coreOptions = new Core.Domain.Models.HybridSearchOptions
        {
            MaxResults = sdkOptions.TopK,
            VectorWeight = sdkOptions.VectorWeight,
            SparseWeight = sdkOptions.KeywordWeight,
            Filters = ToCoreFilters(sdkOptions),
            // The caller named the weights and the fusion, so the service must not re-derive them from the
            // query: with auto strategy on (its default) it replaces all three and these settings are dead.
            EnableAutoStrategy = false,
            // An explicit fusion method wins over the two-value strategy, which cannot name the others.
            FusionMethod = sdkOptions.FusionMethod ?? sdkOptions.RerankingStrategy switch
            {
                RerankingStrategy.WeightedAverage => Core.Domain.Models.FusionMethod.WeightedSum,
                RerankingStrategy.ReciprocalRankFusion => Core.Domain.Models.FusionMethod.RRF,
                _ => Core.Domain.Models.FusionMethod.RRF
            }
        };

        if (sdkOptions.RrfK is { } rrfK)
            coreOptions.RrfK = rrfK;

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

    /// <summary>
    /// Core options for a search driven by <see cref="SearchOptions"/>. When the caller actually
    /// passed a <see cref="HybridSearchOptions"/>, its weights and fusion strategy are honoured —
    /// they used to be discarded in favour of hardcoded 0.7/0.3.
    /// </summary>
    public static Core.Domain.Models.HybridSearchOptions FromSearchOptions(SearchOptions options)
    {
        var coreOptions = options is HybridSearchOptions hybridOptions
            ? ToCore(hybridOptions)
            : new Core.Domain.Models.HybridSearchOptions
            {
                MaxResults = options.TopK,
                VectorWeight = DefaultVectorWeight,
                SparseWeight = DefaultSparseWeight,
                Filters = ToCoreFilters(options)
            };

        // A similarity floor belongs on the vector leg. Compared with the fused score — rank-sized under
        // reciprocal rank fusion, about 0.016 at best — a similarity-sized threshold drops every result.
        coreOptions.VectorOptions.MinScore = options.MinSimilarity;
        return coreOptions;
    }
}
