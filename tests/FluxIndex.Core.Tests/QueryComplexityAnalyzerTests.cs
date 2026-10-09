using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.Core.Tests;

public class QueryComplexityAnalyzerTests
{
    private readonly ILogger<QueryComplexityAnalyzer> _logger;
    private readonly QueryComplexityAnalyzer _analyzer;

    public QueryComplexityAnalyzerTests()
    {
        _logger = NullLogger<QueryComplexityAnalyzer>.Instance;
        _analyzer = new QueryComplexityAnalyzer(_logger);
    }

    [Fact]
    public async Task AnalyzeAsync_SimpleKeyword_ReturnsSimpleComplexity()
    {
        // Arrange
        var query = "machine learning";

        // Act
        var result = await _analyzer.AnalyzeAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(QueryType.SimpleKeyword, result.Type);
        Assert.Equal(ComplexityLevel.Simple, result.Complexity);
        Assert.True(result.ConfidenceScore > 0);
        Assert.Contains("machine", result.Keywords);
        Assert.Contains("learning", result.Keywords);
    }

    [Fact]
    public async Task AnalyzeAsync_NaturalQuestion_ReturnsCorrectType()
    {
        // Arrange
        var query = "What is machine learning?";

        // Act
        var result = await _analyzer.AnalyzeAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(QueryType.NaturalQuestion, result.Type);
        Assert.True(result.Complexity >= ComplexityLevel.Moderate);
        Assert.True(result.ConfidenceScore > 0.7);
    }

    [Fact]
    public async Task AnalyzeAsync_ComparisonQuery_ReturnsCorrectType()
    {
        // Arrange
        var query = "Compare TensorFlow vs PyTorch";

        // Act
        var result = await _analyzer.AnalyzeAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(QueryType.ComparisonQuery, result.Type);
        Assert.True(result.HasComparativeContext);
    }

    [Fact]
    public async Task AnalyzeAsync_ReasoningQuery_ReturnsCorrectType()
    {
        // Arrange
        var query = "Why are neural networks effective for pattern recognition?";

        // Act
        var result = await _analyzer.AnalyzeAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(QueryType.ReasoningQuery, result.Type);
        Assert.True(result.RequiresReasoning);
        Assert.True(result.Complexity >= ComplexityLevel.Complex);
    }

    [Fact]
    public async Task AnalyzeAsync_KoreanQuery_HasConfidence()
    {
        var result = await _analyzer.AnalyzeAsync("머신러닝이 무엇인가요?", TestContext.Current.CancellationToken);

        Assert.True(result.ConfidenceScore > 0);
    }

    [Theory]
    // Ordinary prose: long words are concepts, not technical terms.
    [InlineData("retention policy for archived invoices", false)]
    [InlineData("invoices", false)]
    [InlineData("quarterly reconciliation of supplier statements", false)]
    // Positive controls: terms from a technical domain.
    [InlineData("SQL index JSON API", true)]
    [InlineData("kubernetes deployment pipeline", true)]
    [InlineData("벡터 검색", true)]
    public async Task ContainsTechnicalTerms_IsTrueOnlyForATechnicalDomain(string query, bool technical)
    {
        var result = await _analyzer.AnalyzeAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(technical, result.ContainsTechnicalTerms);
        Assert.Equal(technical, result.TechnicalDomains.Count > 0);
    }

    [Fact]
    public async Task Concepts_StaySeparateFromTechnicalTerms()
    {
        var result = await _analyzer.AnalyzeAsync("retention policy for archived invoices", TestContext.Current.CancellationToken);

        Assert.Contains("retention", result.Concepts);
        Assert.False(result.ContainsTechnicalTerms);
    }

    [Fact]
    public async Task RecommendStrategy_SimpleQuery_StaysHybrid_TechnicalOrNot()
    {
        // Correcting the technical flag must not move retrieval: a simple query ran Hybrid before (the flag was almost always
        // true) and still does — its keyword half is what a short query needs most.
        var prose = await _analyzer.AnalyzeAsync("retention policy for archived invoices", TestContext.Current.CancellationToken);
        var technical = await _analyzer.AnalyzeAsync("JSON", TestContext.Current.CancellationToken);

        Assert.Equal(ComplexityLevel.Simple, prose.Complexity);
        Assert.False(prose.ContainsTechnicalTerms);
        Assert.Equal(SearchStrategy.Hybrid, _analyzer.RecommendStrategy(prose));
        Assert.Equal(ComplexityLevel.Simple, technical.Complexity);
        Assert.Equal(SearchStrategy.Hybrid, _analyzer.RecommendStrategy(technical));
    }

    public static TheoryData<ComplexityLevel, string[], bool, bool, bool> AnalysisGrid()
    {
        var data = new TheoryData<ComplexityLevel, string[], bool, bool, bool>();
        string[][] domains = [[], ["ai_ml"], ["programming", "database"], ["korean"]];
        foreach (var complexity in Enum.GetValues<ComplexityLevel>())
            foreach (var d in domains)
                foreach (var reasoning in new[] { false, true })
                    foreach (var multiHop in new[] { false, true })
                        foreach (var comparative in new[] { false, true })
                            data.Add(complexity, d, reasoning, multiHop, comparative);
        return data;
    }

    [Theory]
    [MemberData(nameof(AnalysisGrid))]
    public void RecommendStrategy_OnlyNamesStrategiesAdaptiveSearchExecutes(
        ComplexityLevel complexity, string[] domains, bool reasoning, bool multiHop, bool comparative)
    {
        var analysis = new QueryAnalysis
        {
            Complexity = complexity,
            TechnicalDomains = [.. domains],
            RequiresReasoning = reasoning,
            IsMultiHop = multiHop,
            HasComparativeContext = comparative,
        };

        var strategy = _analyzer.RecommendStrategy(analysis);

        Assert.Contains(strategy, AdaptiveSearchService.ExecutableStrategies);
    }

    [Fact]
    public async Task AnalyzeAsync_EmptyQuery_ReturnsSimple()
    {
        // Arrange
        var query = "";

        // Act
        var result = await _analyzer.AnalyzeAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(QueryType.SimpleKeyword, result.Type);
        Assert.Equal(ComplexityLevel.Simple, result.Complexity);
        Assert.Equal(1.0, result.ConfidenceScore);
    }

    [Theory]
    [InlineData("AI", SearchStrategy.Hybrid)] // "AI" contains technical terms → Hybrid
    [InlineData("machine learning algorithms detailed explanation", SearchStrategy.Hybrid)] // Moderate + ai_ml: was HyDE, which ran as Hybrid
    [InlineData("How does deep learning work and why is it effective?", SearchStrategy.Hybrid)] // VeryComplex + reasoning + technical domain: was SelfRAG, which ran as Hybrid
    [InlineData("Compare TensorFlow vs PyTorch for production", SearchStrategy.MultiQuery)]
    public async Task RecommendStrategy_VariousQueries_ReturnsExpectedStrategy(string query, SearchStrategy expectedStrategy)
    {
        // Arrange
        var analysis = await _analyzer.AnalyzeAsync(query, TestContext.Current.CancellationToken);

        // Act
        var strategy = _analyzer.RecommendStrategy(analysis);

        // Assert
        Assert.Equal(expectedStrategy, strategy);
    }

    [Fact]
    public async Task AnalyzeAsync_TechnicalTerms_HighSpecificity()
    {
        // Arrange
        var query = "CNN architecture for computer vision applications";

        // Act
        var result = await _analyzer.AnalyzeAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Specificity > 0.3);
        Assert.True(result.Concepts.Any());
    }

    [Fact]
    public async Task AnalyzeAsync_LongComplexQuery_VeryComplexLevel()
    {
        // Arrange
        var query = "Explain the mathematical foundations behind transformer attention mechanisms and their advantages over traditional RNN architectures in natural language processing tasks";

        // Act
        var result = await _analyzer.AnalyzeAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Complexity >= ComplexityLevel.Complex);
    }
}