using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Core.Application.Services.Reranking;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Reranking;

/// <summary>
/// The default <c>ScoreThreshold</c> must mean "no threshold" on <b>every</b> score scale.
/// </summary>
/// <remarks>
/// A default of <c>0f</c> is "off" only for a reranker that answers in (0, 1). A cross-encoder that
/// returns raw logits puts relevant-but-not-top documents below zero, and a zero default silently
/// empties the result set. Each "everything survives" fact has its control: an explicit threshold
/// still filters, including an explicit <c>0f</c> and a negative one.
/// </remarks>
public class RerankScoreThresholdTests
{
    private static readonly float[] LogitScores = [2.1f, -0.4f, -3.2f];

    [Fact]
    public async Task DefaultOptions_KeepEveryRow_OnALogitScaleReranker()
    {
        var reranker = new FixedScoreReranker(LogitScores);

        var results = (await reranker.RerankAsync(
            "q", Candidates(3), cancellationToken: TestContext.Current.CancellationToken)).ToList();

        results.Select(r => r.RerankScore).Should().Equal(LogitScores);
    }

    [Fact]
    public async Task AnExplicitZeroThreshold_IsAThreshold()
    {
        var reranker = new FixedScoreReranker(LogitScores);

        var results = (await reranker.RerankAsync(
            "q", Candidates(3), new RerankOptions { ScoreThreshold = 0f },
            TestContext.Current.CancellationToken)).ToList();

        results.Select(r => r.RerankScore).Should().Equal(2.1f);
    }

    [Fact]
    public async Task ANegativeThreshold_FiltersOnTheLogitScale()
    {
        var reranker = new FixedScoreReranker(LogitScores);

        var results = (await reranker.RerankAsync(
            "q", Candidates(3), new RerankOptions { ScoreThreshold = -1f },
            TestContext.Current.CancellationToken)).ToList();

        results.Select(r => r.RerankScore).Should().Equal(2.1f, -0.4f);
    }

    [Fact]
    public async Task Listwise_DefaultOptions_KeepCandidatesWhoseBlendedScoreIsNegative()
    {
        // No LLM and no embedding service: the listwise score is derived from the initial scores,
        // so candidates that arrive with negative initial scores (a logit-scale first stage) blend
        // to negative listwise scores.
        var reranker = new ListwiseReranker();
        var candidates = new[]
        {
            Candidate("1", -0.2f, 1), Candidate("2", -1.5f, 2), Candidate("3", -4.0f, 3),
        };

        var results = await reranker.RerankAsync(
            "q", candidates, cancellationToken: TestContext.Current.CancellationToken);

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task Listwise_AnExplicitThreshold_StillFilters()
    {
        var reranker = new ListwiseReranker();
        var candidates = new[]
        {
            Candidate("1", -0.2f, 1), Candidate("2", -1.5f, 2), Candidate("3", -4.0f, 3),
        };

        var unfiltered = await reranker.RerankAsync(
            "q", candidates, cancellationToken: TestContext.Current.CancellationToken);
        var cut = unfiltered.OrderByDescending(r => r.ListwiseScore).First().ListwiseScore;

        var results = await reranker.RerankAsync(
            "q", candidates, new ListwiseRerankOptions { ScoreThreshold = cut },
            TestContext.Current.CancellationToken);

        results.Should().HaveCountLessThan(3).And.OnlyContain(r => r.ListwiseScore >= cut);
    }

    private static List<RetrievalCandidate> Candidates(int count) =>
        Enumerable.Range(1, count).Select(i => Candidate(i.ToString(), 1f / i, i)).ToList();

    private static RetrievalCandidate Candidate(string id, float initialScore, int rank) => new()
    {
        Id = id,
        DocumentId = $"doc-{id}",
        ChunkId = $"chunk-{id}",
        Content = $"content {id}",
        InitialScore = initialScore,
        InitialRank = rank,
    };

    private sealed class FixedScoreReranker(float[] scores) : RerankerBase
    {
        protected override Task<IEnumerable<(int Index, float Score)>> RerankCoreAsync(
            string query, IReadOnlyList<string> documents, int topN, CancellationToken cancellationToken) =>
            Task.FromResult(scores.Select((s, i) => (i, s)).Take(topN));

        public override RerankModelInfo GetModelInfo() => new() { Name = "fixed" };
    }
}
