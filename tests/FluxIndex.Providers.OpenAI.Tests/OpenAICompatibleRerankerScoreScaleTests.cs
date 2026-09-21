using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Providers.OpenAI.Extensions;
using FluxIndex.Providers.OpenAI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Providers.OpenAI.Tests;

/// <summary>
/// "OpenAI-compatible" fixes the wire shape of <c>/v1/rerank</c>, not the scale of
/// <c>relevance_score</c>: hosted APIs answer in (0, 1), a llama.cpp server answers with the
/// cross-encoder's raw logit. One <see cref="IReranker"/> must mean one scale.
/// </summary>
public sealed class OpenAICompatibleRerankerScoreScaleTests
{
    private static readonly List<RetrievalCandidate> Candidates =
    [
        new() { Id = "a", Content = "first", InitialRank = 1 },
        new() { Id = "b", Content = "second", InitialRank = 2 },
        new() { Id = "c", Content = "third", InitialRank = 3 }
    ];

    [Fact]
    public async Task ALogitResponse_ComesBackInZeroToOne_InTheSameOrder()
    {
        // A llama-server --rerank answer as a consumer measured it.
        var results = await RerankAsync(ScoreScale.Auto, (1, 5.02f), (0, -10.98f), (2, -11.01f));

        results.Select(r => r.Id).Should().Equal("b", "a", "c");
        results.Should().OnlyContain(r => r.RerankScore > 0f && r.RerankScore < 1f);
        results[0].RerankScore.Should().BeApproximately(0.9934f, 0.001f);
    }

    [Fact]
    public async Task AProbabilityResponse_IsLeftAsItIs()
    {
        var results = await RerankAsync(ScoreScale.Auto, (1, 0.95f), (0, 0.70f), (2, 0.50f));

        results.Select(r => r.RerankScore).Should().Equal(0.95f, 0.70f, 0.50f);
    }

    [Fact]
    public async Task Auto_DecidesPerResponse_NotPerRow()
    {
        // One value outside [0, 1] says the whole response is on the logit scale: 0.4 here is a
        // logit too, and mapping only the out-of-range rows would put -3 above it.
        var results = await RerankAsync(ScoreScale.Auto, (0, 2.5f), (1, 0.4f), (2, -3f));

        results.Select(r => r.Id).Should().Equal("a", "b", "c");
        results[1].RerankScore.Should().BeApproximately(0.5987f, 0.001f);
    }

    [Fact]
    public async Task ExplicitLogit_MapsAResponseThatHappensToFitInZeroToOne()
    {
        var results = await RerankAsync(ScoreScale.Logit, (0, 0.9f), (1, 0.2f), (2, 0.1f));

        results[0].RerankScore.Should().BeApproximately(0.7109f, 0.001f);
        results[2].RerankScore.Should().BeApproximately(0.5250f, 0.001f);
    }

    [Fact]
    public async Task ExplicitProbability_NeverMaps()
    {
        var results = await RerankAsync(ScoreScale.Probability, (0, 5.02f), (1, -10.98f));

        results.Select(r => r.RerankScore).Should().Equal(5.02f, -10.98f);
    }

    [Fact]
    public async Task LargeLogitsThatSaturate_KeepTheServersOrder()
    {
        // sigmoid(30) and sigmoid(40) are both 1.0f; the order must come from the raw scores.
        var results = await RerankAsync(ScoreScale.Auto, (2, 40f), (0, 30f), (1, -1f));

        results.Select(r => r.Id).Should().Equal("c", "a", "b");
    }

    [Fact]
    public async Task AThreshold_IsAppliedToTheNormalizedScore()
    {
        // A logit of 0.3 is sigmoid 0.574: above a 0.5 threshold once normalized, below it raw.
        var results = await RerankAsync(
            ScoreScale.Auto, new RerankOptions { ScoreThreshold = 0.5f }, (1, 5.02f), (0, 0.3f), (2, -11.01f));

        results.Select(r => r.Id).Should().Equal("b", "a");
    }

    [Fact]
    public void TheRegistration_TakesTheScale_AndTheModelInfoDeclaresIt()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenAICompatibleReranker("http://localhost/v1", null, "reranker", ScoreScale.Logit);

        using var provider = services.BuildServiceProvider();
        var info = provider.GetRequiredService<IReranker>().GetModelInfo();

        info.Capabilities.Should().ContainKey("ScoreScale").WhoseValue.Should().Be(nameof(ScoreScale.Logit));
    }

    private static Task<List<RerankResult>> RerankAsync(ScoreScale scale, params (int Index, float Score)[] rows)
        => RerankAsync(scale, null, rows);

    private static async Task<List<RerankResult>> RerankAsync(
        ScoreScale scale, RerankOptions? options, params (int Index, float Score)[] rows)
    {
        var json = JsonSerializer.Serialize(new
        {
            results = rows.Select(r => new { index = r.Index, relevance_score = r.Score })
        });
        using var httpClient = new HttpClient(new MockHttpMessageHandler(json, HttpStatusCode.OK))
        {
            BaseAddress = new Uri("http://localhost/v1/")
        };
        using var sut = new OpenAICompatibleRerankerService(
            httpClient, "reranker", Substitute.For<ILogger<OpenAICompatibleRerankerService>>(), scale);

        return (await sut.RerankAsync("query", Candidates, options, TestContext.Current.CancellationToken)).ToList();
    }
}
