using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests;

/// <summary>
/// <see cref="AdaptiveSearchResult.UsedStrategy"/> is the strategy that ran. The analyzer used to recommend HyDE,
/// SelfRAG and Adaptive, which adaptive search ran as Hybrid while reporting the recommendation; a fallback that
/// replaced the selected strategy was reported as the selected one.
/// </summary>
public class AdaptiveSearchExecutedStrategyTests
{
    // "machine learning" makes MultiQuery's expansion visible: it searches three phrasings, Hybrid searches one.
    private const string Query = "machine learning";

    private readonly IHybridSearchService _hybrid = Substitute.For<IHybridSearchService>();
    private readonly ISmallToBigRetriever _smallToBig = Substitute.For<ISmallToBigRetriever>();
    private readonly IQueryComplexityAnalyzer _analyzer = Substitute.For<IQueryComplexityAnalyzer>();
    private readonly List<(string Query, HybridSearchOptions Options)> _hybridCalls = [];
    private int _smallToBigCalls;

    public AdaptiveSearchExecutedStrategyTests()
    {
        _hybrid.SearchAsync(Arg.Any<string>(), Arg.Any<HybridSearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _hybridCalls.Add((call.Arg<string>(), call.Arg<HybridSearchOptions>()));
                return (IReadOnlyList<HybridSearchResult>)[Hit()];
            });
        _smallToBig.SearchAsync(Arg.Any<string>(), Arg.Any<SmallToBigOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _smallToBigCalls++;
                return (IReadOnlyList<SmallToBigResult>)[new SmallToBigResult { PrimaryChunk = Chunk() }];
            });
    }

    private static DocumentChunk Chunk() => new() { Id = "c1", DocumentId = "d1", Content = "text" };

    private static HybridSearchResult Hit() => new() { Chunk = Chunk(), FusedScore = 0.9 };

    private AdaptiveSearchService Service() =>
        new(_hybrid, _smallToBig, _analyzer, NullLogger<AdaptiveSearchService>.Instance);

    private static AdaptiveSearchOptions Options(SearchStrategy? force = null) =>
        new() { UseCache = false, MaxResults = 1, ForceStrategy = force };

    /// <summary>Which strategy's code path ran, read from the calls the backends received.</summary>
    private SearchStrategy ObservedStrategy()
    {
        if (_smallToBigCalls > 0 && _hybridCalls.Count == 0) return SearchStrategy.TwoStage;
        if (_smallToBigCalls == 0 && _hybridCalls.Count == 3 && _hybridCalls.Select(c => c.Query).Distinct().Count() == 3)
            return SearchStrategy.MultiQuery;
        Assert.Equal(0, _smallToBigCalls);
        var options = Assert.Single(_hybridCalls).Options;
        return (options.VectorWeight, options.SparseWeight) switch
        {
            (1.0f, 0.0f) => SearchStrategy.DirectVector,
            (0.0f, 1.0f) => SearchStrategy.KeywordOnly,
            _ => SearchStrategy.Hybrid,
        };
    }

    public static TheoryData<ComplexityLevel, string[], bool, bool, bool> AnalysisGrid() =>
        QueryComplexityAnalyzerTests.AnalysisGrid();

    [Theory]
    [MemberData(nameof(AnalysisGrid))]
    public async Task EveryRecommendedStrategy_IsTheOneThatRuns_AndIsReported(
        ComplexityLevel complexity, string[] domains, bool reasoning, bool multiHop, bool comparative)
    {
        var real = new QueryComplexityAnalyzer(NullLogger<QueryComplexityAnalyzer>.Instance);
        var analysis = new QueryAnalysis
        {
            Type = QueryType.SimpleKeyword,
            Complexity = complexity,
            TechnicalDomains = [.. domains],
            RequiresReasoning = reasoning,
            IsMultiHop = multiHop,
            HasComparativeContext = comparative,
            ConfidenceScore = 0.5, // below the per-type preference threshold: the recommendation decides
        };
        var recommended = real.RecommendStrategy(analysis);
        _analyzer.AnalyzeAsync(Query, Arg.Any<CancellationToken>()).Returns(analysis);
        _analyzer.RecommendStrategy(analysis).Returns(recommended);

        var result = await Service().SearchAsync(Query, Options(), TestContext.Current.CancellationToken);

        Assert.Equal(recommended, result.SelectedStrategy);
        Assert.Equal(recommended, result.UsedStrategy);
        Assert.Equal(recommended, ObservedStrategy());
    }

    [Theory]
    [InlineData(QueryType.SimpleKeyword)]
    [InlineData(QueryType.NaturalQuestion)]
    [InlineData(QueryType.ComplexSearch)]
    [InlineData(QueryType.ReasoningQuery)]
    [InlineData(QueryType.ComparisonQuery)]
    [InlineData(QueryType.TemporalQuery)]
    [InlineData(QueryType.MultiHopQuery)]
    public async Task EveryPerTypePreferredStrategy_IsTheOneThatRuns_AndIsReported(QueryType type)
    {
        // At confidence >= 0.8 the per-type preference decides instead of the analyzer's recommendation.
        var analysis = new QueryAnalysis { Type = type, ConfidenceScore = 0.9 };
        _analyzer.AnalyzeAsync(Query, Arg.Any<CancellationToken>()).Returns(analysis);

        var result = await Service().SearchAsync(Query, Options(), TestContext.Current.CancellationToken);

        Assert.Contains(result.UsedStrategy, AdaptiveSearchService.ExecutableStrategies);
        Assert.Equal(result.SelectedStrategy, result.UsedStrategy);
        Assert.Equal(result.UsedStrategy, ObservedStrategy());
    }

    [Theory]
    [InlineData(SearchStrategy.HyDE)]
    [InlineData(SearchStrategy.StepBack)]
    [InlineData(SearchStrategy.Adaptive)]
    [InlineData(SearchStrategy.SelfRAG)]
    public async Task AStrategyAdaptiveSearchDoesNotExecute_RunsAsHybrid_AndIsReportedAsHybrid(SearchStrategy strategy)
    {
        _analyzer.AnalyzeAsync(Query, Arg.Any<CancellationToken>()).Returns(new QueryAnalysis { ConfidenceScore = 0.5 });

        var forced = await Service().SearchAsync(Query, Options(strategy), TestContext.Current.CancellationToken);

        Assert.Equal(strategy, forced.SelectedStrategy);
        Assert.Equal(SearchStrategy.Hybrid, forced.UsedStrategy);
        Assert.Equal(SearchStrategy.Hybrid, ObservedStrategy());
        Assert.Contains(forced.StrategyReasons, r => r.Contains(strategy.ToString(), StringComparison.Ordinal) && r.Contains("not executed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACustomAnalyzersUnexecutedRecommendation_IsReportedAsTheHybridThatRan()
    {
        var analysis = new QueryAnalysis { ConfidenceScore = 0.5 };
        _analyzer.AnalyzeAsync(Query, Arg.Any<CancellationToken>()).Returns(analysis);
        _analyzer.RecommendStrategy(analysis).Returns(SearchStrategy.HyDE);

        var result = await Service().SearchAsync(Query, Options(), TestContext.Current.CancellationToken);

        Assert.Equal(SearchStrategy.HyDE, result.SelectedStrategy);
        Assert.Equal(SearchStrategy.Hybrid, result.UsedStrategy);
        Assert.Equal(SearchStrategy.Hybrid, ObservedStrategy());
    }

    [Fact]
    public async Task AFallbackThatReplacedTheSelectedStrategy_IsReportedAsTheStrategyThatRan()
    {
        // DirectVector finds nothing; the fallback chain's Hybrid finds a hit and its results are returned.
        _hybrid.SearchAsync(Arg.Any<string>(), Arg.Any<HybridSearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<HybridSearchOptions>().SparseWeight == 0.0f
                ? (IReadOnlyList<HybridSearchResult>)[]
                : [Hit()]);
        _analyzer.AnalyzeAsync(Query, Arg.Any<CancellationToken>()).Returns(new QueryAnalysis { ConfidenceScore = 0.5 });

        var result = await Service().SearchAsync(Query, Options(SearchStrategy.DirectVector), TestContext.Current.CancellationToken);

        Assert.Single(result.Documents);
        Assert.Equal(SearchStrategy.DirectVector, result.SelectedStrategy);
        Assert.Equal(SearchStrategy.Hybrid, result.UsedStrategy);
        Assert.Equal("Hybrid", result.Performance.ResourceUsage["strategy"]);
    }
}
