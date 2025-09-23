using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services;
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
        var result = await _analyzer.AnalyzeAsync(query);

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
        var result = await _analyzer.AnalyzeAsync(query);

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
        var result = await _analyzer.AnalyzeAsync(query);

        // Assert
        Assert.Equal(QueryType.ComparisonQuery, result.Type);
        Assert.True(result.HasComparativeContext);
        Assert.Equal(Language.English, result.Language);
    }

    [Fact]
    public async Task AnalyzeAsync_ReasoningQuery_ReturnsCorrectType()
    {
        // Arrange
        var query = "Why are neural networks effective for pattern recognition?";

        // Act
        var result = await _analyzer.AnalyzeAsync(query);

        // Assert
        Assert.Equal(QueryType.ReasoningQuery, result.Type);
        Assert.True(result.RequiresReasoning);
        Assert.True(result.Complexity >= ComplexityLevel.Complex);
    }

    [Fact]
    public async Task AnalyzeAsync_KoreanQuery_DetectsLanguage()
    {
        // Arrange
        var query = "머신러닝이 무엇인가요?";

        // Act
        var result = await _analyzer.AnalyzeAsync(query);

        // Assert
        Assert.Equal(Language.Korean, result.Language);
        Assert.True(result.ConfidenceScore > 0);
    }

    [Fact]
    public async Task AnalyzeAsync_MixedLanguageQuery_DetectsMixed()
    {
        // Arrange
        var query = "What is 머신러닝?";

        // Act
        var result = await _analyzer.AnalyzeAsync(query);

        // Assert
        Assert.Equal(Language.Mixed, result.Language);
    }

    [Fact]
    public async Task AnalyzeAsync_EmptyQuery_ReturnsSimple()
    {
        // Arrange
        var query = "";

        // Act
        var result = await _analyzer.AnalyzeAsync(query);

        // Assert
        Assert.Equal(QueryType.SimpleKeyword, result.Type);
        Assert.Equal(ComplexityLevel.Simple, result.Complexity);
        Assert.Equal(1.0, result.ConfidenceScore);
    }

    [Theory]
    [InlineData("AI", SearchStrategy.DirectVector)]
    [InlineData("machine learning algorithms detailed explanation", SearchStrategy.Hybrid)]
    [InlineData("How does deep learning work and why is it effective?", SearchStrategy.TwoStage)]
    [InlineData("Compare TensorFlow vs PyTorch for production", SearchStrategy.MultiQuery)]
    public async Task RecommendStrategy_VariousQueries_ReturnsExpectedStrategy(string query, SearchStrategy expectedStrategy)
    {
        // Arrange
        var analysis = await _analyzer.AnalyzeAsync(query);

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
        var result = await _analyzer.AnalyzeAsync(query);

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
        var result = await _analyzer.AnalyzeAsync(query);

        // Assert
        Assert.True(result.Complexity >= ComplexityLevel.Complex);
        Assert.True(result.EstimatedProcessingTime.TotalMilliseconds > 1000);
    }
}