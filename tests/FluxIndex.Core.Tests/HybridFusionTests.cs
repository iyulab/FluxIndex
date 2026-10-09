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
}
