using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests;

/// <summary>
/// A fusion value the caller sets reaches the fusion; only the unset ones are chosen per query. Before 0.80.0 an
/// <c>EnableAutoStrategy</c> flag (default on) replaced all three with the query heuristic's picks, so a caller that
/// configured relative-score fusion got rank-sized RRF scores and nothing said so. Every result now reports the fusion
/// that ran, which is what lets a caller assert it.
/// </summary>
public class HybridSearchFusionSelectionTests
{
    // Eight tokens, no technical term: the heuristic picks VectorFirst (0.8 / 0.2) with RRF.
    private const string Query = "please email the maintainers about the rain";

    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly IKeywordSearchService _keywordSearch = Substitute.For<IKeywordSearchService>();
    private readonly IEmbeddingService _embedding = Substitute.For<IEmbeddingService>();
    private readonly IDynamicFusionService _dynamicFusion = Substitute.For<IDynamicFusionService>();

    public HybridSearchFusionSelectionTests()
    {
        var embedding = new[] { 0.1f, 0.2f, 0.3f };
        _embedding.GenerateQueryEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(embedding);
        _vectorStore.SearchAsync(embedding, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new DocumentChunk { Id = "a", Content = "a", Score = 0.9f },
                new DocumentChunk { Id = "b", Content = "b", Score = 0.5f },
            }.AsEnumerable());
        _keywordSearch.SearchAsync(Arg.Any<string>(), Arg.Any<KeywordSearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<KeywordSearchResult>
            {
                new() { Chunk = new DocumentChunk { Id = "b", Content = "b" }, Score = 4.0, MatchedTerms = ["rain"] },
                new() { Chunk = new DocumentChunk { Id = "c", Content = "c" }, Score = 2.0, MatchedTerms = ["email"] },
            });
    }

    private HybridSearchService CreateService(bool withDynamicFusion = false) =>
        new(_vectorStore, _keywordSearch, _embedding, NullLogger<HybridSearchService>.Instance, null, withDynamicFusion ? _dynamicFusion : null);

    [Fact]
    public async Task SearchAsync_CallerSetsMethodAndWeights_TheyAreAppliedAsGiven()
    {
        var results = await CreateService().SearchAsync(Query, new HybridSearchOptions
        {
            FusionMethod = FusionMethod.RelativeScoreFusion,
            VectorWeight = 0.2,
            SparseWeight = 0.8,
        }, TestContext.Current.CancellationToken);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(
            new AppliedFusion(FusionMethod.RelativeScoreFusion, 0.2, 0.8, 60, FusionSelection.Caller), r.Fusion));
        // Relative-score fusion keeps score magnitude: the top row is far above the 1/61 ceiling of an RRF score.
        Assert.True(results[0].FusedScore > 0.5, $"top fused score {results[0].FusedScore} is rank-sized");
    }

    [Fact]
    public async Task SearchAsync_NothingSet_TheQueryHeuristicChoosesAndSaysSo()
    {
        var results = await CreateService().SearchAsync(Query, new HybridSearchOptions(), TestContext.Current.CancellationToken);

        Assert.All(results, r => Assert.Equal(
            new AppliedFusion(FusionMethod.RRF, 0.8, 0.2, 60, FusionSelection.QueryHeuristic), r.Fusion));
    }

    [Fact]
    public async Task SearchAsync_OnlyWeightsSet_WeightsAreKeptAndOnlyTheMethodIsChosen()
    {
        var results = await CreateService().SearchAsync(Query, new HybridSearchOptions
        {
            VectorWeight = 0.0,
            SparseWeight = 1.0,
        }, TestContext.Current.CancellationToken);

        Assert.All(results, r => Assert.Equal(
            new AppliedFusion(FusionMethod.RRF, 0.0, 1.0, 60, FusionSelection.QueryHeuristic), r.Fusion));
        // A keyword-only weighting gives a vector-only row no score at all.
        Assert.Equal(0.0, results.Single(r => r.Chunk.Id == "a").FusedScore);
    }

    [Fact]
    public async Task SearchAsync_DynamicAlphaTuning_FillsOnlyTheUnsetValues()
    {
        _dynamicFusion.CalculateDynamicWeightsAsync(Query, Arg.Any<CancellationToken>())
            .Returns(new DynamicFusionConfiguration
            {
                VectorWeight = 0.35,
                SparseWeight = 0.65,
                RecommendedFusion = FusionMethod.HarmonicMean,
            });

        var results = await CreateService(withDynamicFusion: true).SearchAsync(Query, new HybridSearchOptions
        {
            EnableDynamicAlphaTuning = true,
            FusionMethod = FusionMethod.WeightedSum,
        }, TestContext.Current.CancellationToken);

        Assert.All(results, r => Assert.Equal(
            new AppliedFusion(FusionMethod.WeightedSum, 0.35, 0.65, 60, FusionSelection.DynamicAlphaTuning), r.Fusion));
    }

    [Fact]
    public async Task SearchAsync_DynamicAlphaTuningRequestedWithoutTheService_Throws()
    {
        // Falling back to the heuristic ran the default fusion under an option that said otherwise, silently.
        var search = () => CreateService(withDynamicFusion: false).SearchAsync(
            Query, new HybridSearchOptions { EnableDynamicAlphaTuning = true }, TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(search);
        Assert.Contains("AddDynamicAlphaTuning", error.Message);
    }

    [Fact]
    public async Task AContainerWithTuningAndNoQuantizer_ResolvesTheTuningService()
    {
        // With one constructor per combination the container picked the longest it could fill; a tuning service
        // without a quantizer matched none but the plain one, and the option was dropped.
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(_vectorStore);
        services.AddSingleton(_keywordSearch);
        services.AddSingleton(_embedding);
        services.AddSingleton(_dynamicFusion);
        services.AddScoped<IHybridSearchService, HybridSearchService>();
        _dynamicFusion.CalculateDynamicWeightsAsync(Query, Arg.Any<CancellationToken>())
            .Returns(new DynamicFusionConfiguration { VectorWeight = 0.9, SparseWeight = 0.1, RecommendedFusion = FusionMethod.RRF });

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var results = await scope.ServiceProvider.GetRequiredService<IHybridSearchService>().SearchAsync(
            Query, new HybridSearchOptions { EnableDynamicAlphaTuning = true }, TestContext.Current.CancellationToken);

        Assert.All(results, r => Assert.Equal(FusionSelection.DynamicAlphaTuning, r.Fusion.SelectedBy));
    }

    [Fact]
    public async Task SearchAsync_EverythingSet_DynamicAlphaTuningIsNotConsulted()
    {
        await CreateService(withDynamicFusion: true).SearchAsync(Query, new HybridSearchOptions
        {
            EnableDynamicAlphaTuning = true,
            FusionMethod = FusionMethod.WeightedSum,
            VectorWeight = 0.5,
            SparseWeight = 0.5,
        }, TestContext.Current.CancellationToken);

        await _dynamicFusion.DidNotReceive().CalculateDynamicWeightsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
