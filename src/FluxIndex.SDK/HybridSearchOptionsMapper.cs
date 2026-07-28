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
        return new Core.Domain.Models.HybridSearchOptions
        {
            MaxResults = sdkOptions.TopK,
            VectorWeight = sdkOptions.VectorWeight,
            SparseWeight = sdkOptions.KeywordWeight,
            FusionMethod = sdkOptions.RerankingStrategy switch
            {
                RerankingStrategy.WeightedAverage => Core.Domain.Models.FusionMethod.WeightedSum,
                RerankingStrategy.ReciprocalRankFusion => Core.Domain.Models.FusionMethod.RRF,
                _ => Core.Domain.Models.FusionMethod.RRF
            }
        };
    }

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
                SparseWeight = DefaultSparseWeight
            };

        coreOptions.MinFusedScore = options.MinSimilarity;
        return coreOptions;
    }
}
