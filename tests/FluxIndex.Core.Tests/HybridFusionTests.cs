using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Application.Services;
using Xunit;

namespace FluxIndex.Core.Tests;

/// <summary>
/// The fusion both hybrid services share: every method stamps the fusion it applied on each row, honours the
/// limits, and derives a confidence the same way.
/// </summary>
public class HybridFusionTests
{
    private static readonly IReadOnlyList<VectorSearchResult> Vector =
    [
        new() { DocumentChunk = new DocumentChunk { Id = "a" }, Score = 0.9, Rank = 1 },
        new() { DocumentChunk = new DocumentChunk { Id = "b" }, Score = 0.5, Rank = 2 },
    ];

    private static readonly IReadOnlyList<SparseSearchResult> Sparse =
    [
        new() { Chunk = new DocumentChunk { Id = "b" }, Score = 4.0, MatchedTerms = ["x"] },
        new() { Chunk = new DocumentChunk { Id = "c" }, Score = 2.0, MatchedTerms = ["y"] },
    ];

    public static TheoryData<FusionMethod> Methods => new(Enum.GetValues<FusionMethod>());

    [Theory]
    [MemberData(nameof(Methods))]
    public void Fuse_EveryMethod_StampsTheAppliedFusionAndRanksFromOne(FusionMethod method)
    {
        var fusion = new AppliedFusion(method, 0.6, 0.4, 60, FusionSelection.Caller);

        var rows = HybridFusion.Fuse(Vector, Sparse, fusion, maxResults: 10, minFusedScore: 0);

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Same(fusion, r.Fusion));
        Assert.Equal(Enumerable.Range(1, rows.Count), rows.Select(r => r.FusedRank));
        Assert.Equal(rows.OrderByDescending(r => r.FusedScore).Select(r => r.Chunk.Id), rows.Select(r => r.Chunk.Id));
        Assert.All(rows, r => Assert.InRange(r.Confidence, 0.0, 1.0));
    }

    [Fact]
    public void Fuse_RowBothLegsRankFirst_IsTheMostConfident()
    {
        IReadOnlyList<VectorSearchResult> vector =
        [
            new() { DocumentChunk = new DocumentChunk { Id = "b" }, Score = 0.9, Rank = 1 },
            new() { DocumentChunk = new DocumentChunk { Id = "a" }, Score = 0.5, Rank = 2 },
        ];

        var rows = HybridFusion.Fuse(vector, Sparse, new AppliedFusion(FusionMethod.RRF, 0.5, 0.5, 60, FusionSelection.Caller), 10, 0);

        var both = rows.Single(r => r.Chunk.Id == "b");
        Assert.Equal(SearchSource.Both, both.Source);
        Assert.True(rows.Where(r => r != both).All(r => r.Confidence < both.Confidence));
    }

    [Fact]
    public void Fuse_HonoursMaxResultsAndMinFusedScore()
    {
        var fusion = new AppliedFusion(FusionMethod.WeightedSum, 0.5, 0.5, 60, FusionSelection.Caller);

        Assert.Single(HybridFusion.Fuse(Vector, Sparse, fusion, maxResults: 1, minFusedScore: 0));
        Assert.Empty(HybridFusion.Fuse(Vector, Sparse, fusion, maxResults: 10, minFusedScore: 2.0));
    }

    private static readonly IReadOnlyList<GraphChunkHit> Graph =
    [
        new(new DocumentChunk { Id = "g" }, 2.0, 1, ["e1"], 0),
        new(new DocumentChunk { Id = "a" }, 1.0, 2, ["e1"], 1),
    ];

    public static TheoryData<FusionMethod> GraphMethods => new(Enum.GetValues<FusionMethod>().Where(HybridFusion.AcceptsGraphLeg));

    [Theory]
    [MemberData(nameof(GraphMethods))]
    public void Fuse_WithGraphLeg_KeepsGraphOnlyRowsAndMarksSharedOnes(FusionMethod method)
    {
        var fusion = new AppliedFusion(method, 0.6, 0.4, 60, FusionSelection.Caller, GraphWeight: 0.3);

        var rows = HybridFusion.Fuse(Vector, Sparse, Graph, fusion, maxResults: 10, minFusedScore: 0);

        var g = Assert.Single(rows, r => r.Chunk.Id == "g");
        Assert.Equal(SearchSource.Graph, g.Source);
        Assert.Equal((2.0, 1), (g.GraphScore, g.GraphRank));
        Assert.Equal((int.MaxValue, int.MaxValue), (g.VectorRank, g.SparseRank));
        var a = rows.Single(r => r.Chunk.Id == "a");
        Assert.Equal(SearchSource.Vector | SearchSource.Graph, a.Source);
        Assert.Equal(2, a.GraphRank);
        Assert.Equal(int.MaxValue, rows.Single(r => r.Chunk.Id == "c").GraphRank);
        Assert.Equal(Enumerable.Range(1, rows.Count), rows.Select(r => r.FusedRank));
        Assert.All(rows, r => Assert.InRange(r.Confidence, 0.0, 1.0));
    }

    [Theory]
    [MemberData(nameof(GraphMethods))]
    public void Fuse_GraphLegAddsOnTop_SoTextRowsTheGraphMissedScoreAsBefore(FusionMethod method)
    {
        var withoutGraph = HybridFusion.Fuse(Vector, Sparse, new AppliedFusion(method, 0.6, 0.4, 60, FusionSelection.Caller), 10, 0);
        var withGraph = HybridFusion.Fuse(Vector, Sparse, Graph, new AppliedFusion(method, 0.6, 0.4, 60, FusionSelection.Caller, GraphWeight: 0.3), 10, 0);

        foreach (var id in new[] { "b", "c" })
            Assert.Equal(withoutGraph.Single(r => r.Chunk.Id == id).FusedScore, withGraph.Single(r => r.Chunk.Id == id).FusedScore, 12);
        Assert.True(withGraph.Single(r => r.Chunk.Id == "a").FusedScore >= withoutGraph.Single(r => r.Chunk.Id == "a").FusedScore);
    }

    [Theory]
    [InlineData(FusionMethod.Product)]
    [InlineData(FusionMethod.HarmonicMean)]
    public void Fuse_IntersectingMethodWithGraphHits_Throws(FusionMethod method)
    {
        Assert.False(HybridFusion.AcceptsGraphLeg(method));
        Assert.Throws<ArgumentException>(() =>
            HybridFusion.Fuse(Vector, Sparse, Graph, new AppliedFusion(method, 0.6, 0.4, 60, FusionSelection.Caller, 0.3), 10, 0));
    }

    [Fact]
    public void SearchSource_ValuesAreStableFlags()
    {
        Assert.Equal([0, 1, 2, 3, 4], new[] { SearchSource.None, SearchSource.Vector, SearchSource.Sparse, SearchSource.Both, SearchSource.Graph }.Select(s => (int)s));
        Assert.True(new HybridSearchResult { Source = SearchSource.Both | SearchSource.Graph, Confidence = 0.9 }.IsHighConfidence);
        Assert.False(new HybridSearchResult { Source = SearchSource.Vector | SearchSource.Graph, Confidence = 0.9 }.IsHighConfidence);
    }

    [Fact]
    public void RelativeScoreFusion_ALoneKeywordHit_CountsAsItsLegsBest()
    {
        // A keyword leg with one hit (or all hits tied) has no spread to normalise over. The hit is that leg's best and
        // must carry the full sparse weight — min-max with a zero range used to turn it into 0.
        IReadOnlyList<VectorSearchResult> vector =
        [
            new() { DocumentChunk = new DocumentChunk { Id = "a" }, Score = 0.9, Rank = 1 },
            new() { DocumentChunk = new DocumentChunk { Id = "b" }, Score = 0.5, Rank = 2 },
            new() { DocumentChunk = new DocumentChunk { Id = "c" }, Score = 0.4, Rank = 3 },
        ];
        IReadOnlyList<SparseSearchResult> sparse =
        [
            new() { Chunk = new DocumentChunk { Id = "k" }, Score = 3.1, MatchedTerms = ["x"] },
        ];
        var fusion = new AppliedFusion(FusionMethod.RelativeScoreFusion, 0.4, 0.6, 60, FusionSelection.Caller);

        var rows = HybridFusion.Fuse(vector, sparse, fusion, maxResults: 10, minFusedScore: 0);

        Assert.Equal("k", rows[0].Chunk.Id);
        Assert.Equal(0.6, rows[0].FusedScore, precision: 9);
    }

    [Fact]
    public void RelativeScoreFusion_TiedVectorHits_AllNormaliseToOne()
    {
        IReadOnlyList<VectorSearchResult> vector =
        [
            new() { DocumentChunk = new DocumentChunk { Id = "a" }, Score = 0.7, Rank = 1 },
            new() { DocumentChunk = new DocumentChunk { Id = "b" }, Score = 0.7, Rank = 2 },
        ];
        var fusion = new AppliedFusion(FusionMethod.RelativeScoreFusion, 0.4, 0.6, 60, FusionSelection.Caller);

        var rows = HybridFusion.Fuse(vector, [], fusion, maxResults: 10, minFusedScore: 0);

        Assert.All(rows, r => Assert.Equal(0.4, r.FusedScore, precision: 9));
    }
}
