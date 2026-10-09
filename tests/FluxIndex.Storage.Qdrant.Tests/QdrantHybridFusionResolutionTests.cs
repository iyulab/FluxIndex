using FluxIndex.Core.Domain.Models;
using Xunit;

namespace FluxIndex.Storage.Qdrant.Tests;

/// <summary>
/// The Qdrant hybrid service used to fuse with RRF whatever <see cref="HybridSearchOptions.FusionMethod"/> said. It now
/// fuses through the shared <c>HybridFusion</c>, so what it has of its own is how it fills the values a caller left unset.
/// </summary>
public class QdrantHybridFusionResolutionTests
{
    [Theory]
    [InlineData(FusionMethod.RRF)]
    [InlineData(FusionMethod.WeightedSum)]
    [InlineData(FusionMethod.Product)]
    [InlineData(FusionMethod.Maximum)]
    [InlineData(FusionMethod.HarmonicMean)]
    [InlineData(FusionMethod.RelativeScoreFusion)]
    public void ResolveFusion_EverythingSet_IsUsedAsGiven(FusionMethod method)
    {
        var fusion = QdrantHybridSearchService.ResolveFusion(new HybridSearchOptions
        {
            FusionMethod = method,
            VectorWeight = 0.25,
            SparseWeight = 0.75,
            RrfK = 20,
        });

        Assert.Equal(new AppliedFusion(method, 0.25, 0.75, 20, FusionSelection.Caller), fusion);
    }

    [Fact]
    public void ResolveFusion_NothingSet_FillsTheServiceDefaults()
    {
        var fusion = QdrantHybridSearchService.ResolveFusion(new HybridSearchOptions());

        Assert.Equal(new AppliedFusion(FusionMethod.RRF, 0.7, 0.3, 60, FusionSelection.ServiceDefault), fusion);
    }

    // It used to recommend keyword-first for a quoted query and vector-first for a short one, and apply neither.
    [Theory]
    [InlineData("\"exact phrase\"")]
    [InlineData("API")]
    [InlineData("how does the retention policy apply to archived invoices")]
    public void Recommend_ReportsTheFusionThisServiceApplies_WhateverTheQuery(string query)
    {
        var recommended = QdrantHybridSearchService.Recommend(query);
        var applied = QdrantHybridSearchService.ResolveFusion(new HybridSearchOptions());

        Assert.Equal(applied.Method, recommended.RecommendedFusion);
        Assert.Equal((applied.VectorWeight, applied.SparseWeight), recommended.RecommendedWeights);
    }

    [Fact]
    public void ResolveFusion_OnlyTheMethodSet_KeepsItAndFillsTheWeights()
    {
        var fusion = QdrantHybridSearchService.ResolveFusion(new HybridSearchOptions { FusionMethod = FusionMethod.WeightedSum });

        Assert.Equal(new AppliedFusion(FusionMethod.WeightedSum, 0.7, 0.3, 60, FusionSelection.ServiceDefault), fusion);
    }
}
