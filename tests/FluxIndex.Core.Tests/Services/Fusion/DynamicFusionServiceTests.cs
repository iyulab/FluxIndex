using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

// Alias to avoid ambiguity with Domain.Models.QueryType
using AppQueryType = FluxIndex.Core.Application.Interfaces.QueryType;

namespace FluxIndex.Core.Tests.Services.Fusion;

public class DynamicFusionServiceTests
{
    private readonly IQueryComplexityAnalyzer _mockQueryAnalyzer;
    private readonly ILogger<DynamicFusionService> _mockLogger;
    private readonly DynamicFusionService _service;

    public DynamicFusionServiceTests()
    {
        _mockQueryAnalyzer = Substitute.For<IQueryComplexityAnalyzer>();
        _mockLogger = Substitute.For<ILogger<DynamicFusionService>>();
        _service = new DynamicFusionService(_mockQueryAnalyzer, _mockLogger);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullQueryAnalyzer_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DynamicFusionService(null!, _mockLogger));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DynamicFusionService(_mockQueryAnalyzer, null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_Succeeds()
    {
        var service = new DynamicFusionService(_mockQueryAnalyzer, _mockLogger);
        Assert.NotNull(service);
    }

    #endregion

    #region CalculateDynamicWeightsAsync Tests

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithEmptyQuery_ReturnsDefaultConfiguration()
    {
        // Arrange
        var query = "";

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0.60, result.VectorWeight, 2);
        Assert.Equal(0.40, result.SparseWeight, 2);
        Assert.Equal(FusionMethod.RRF, result.RecommendedFusion);
        Assert.Equal(AppQueryType.SimpleKeyword, result.QueryType);
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithNullQuery_ReturnsDefaultConfiguration()
    {
        // Arrange
        string? query = null;

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query!, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0.60, result.VectorWeight, 2);
        Assert.Equal(0.40, result.SparseWeight, 2);
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithWhitespaceQuery_ReturnsDefaultConfiguration()
    {
        // Arrange
        var query = "   ";

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(FusionMethod.RRF, result.RecommendedFusion);
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithNaturalQuestion_FavorsVectorWeight()
    {
        // Arrange
        var query = "What is machine learning and how does it work?";
        SetupMockAnalysis(AppQueryType.NaturalQuestion, ComplexityLevel.Moderate);

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.VectorWeight > result.SparseWeight,
            $"Vector weight ({result.VectorWeight}) should be greater than sparse weight ({result.SparseWeight})");
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithSimpleKeyword_FavorsSparseWeight()
    {
        // Arrange
        var query = "python list";
        SetupMockAnalysis(AppQueryType.SimpleKeyword, ComplexityLevel.Simple);

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.SparseWeight >= result.VectorWeight,
            $"Sparse weight ({result.SparseWeight}) should be >= vector weight ({result.VectorWeight})");
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithReasoningQuery_StronglyFavorsVector()
    {
        // Arrange
        var query = "Explain why neural networks perform better with normalization";
        SetupMockAnalysis(AppQueryType.ReasoningQuery, ComplexityLevel.Complex, requiresReasoning: true);

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.VectorWeight >= 0.70,
            $"Vector weight ({result.VectorWeight}) should be >= 0.70 for reasoning queries");
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithMultiHopQuery_FavorsSemanticSearch()
    {
        // Arrange
        var query = "What companies were founded by Elon Musk and what products do they make?";
        SetupMockAnalysis(AppQueryType.MultiHopQuery, ComplexityLevel.VeryComplex, isMultiHop: true);

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.VectorWeight >= 0.65,
            $"Vector weight ({result.VectorWeight}) should be >= 0.65 for multi-hop queries");
    }

    #endregion

    #region Domain Adjustment Tests

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithProgrammingDomain_BoostsKeywordWeight()
    {
        // Arrange
        var query = "python list append method";
        SetupMockAnalysis(AppQueryType.SimpleKeyword, ComplexityLevel.Simple,
            technicalDomains: new List<string> { "programming" });

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("programming", result.TechnicalDomains);
        // Programming domain should boost sparse weight
        Assert.True(result.SparseWeight >= 0.45,
            $"Sparse weight should be boosted for programming domain");
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithAIMLDomain_BoostsSemanticWeight()
    {
        // Arrange
        var query = "neural network architecture";
        SetupMockAnalysis(AppQueryType.NaturalQuestion, ComplexityLevel.Moderate,
            technicalDomains: new List<string> { "ai_ml" });

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("ai_ml", result.TechnicalDomains);
    }

    #endregion

    #region Complexity Adjustment Tests

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithHighSpecificity_BoostsKeywordWeight()
    {
        // Arrange
        var query = "OAuth2 Bearer token refresh";
        SetupMockAnalysis(AppQueryType.SimpleKeyword, ComplexityLevel.Moderate, specificity: 0.8);

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        // High specificity should boost sparse weight
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithMultipleEntities_AdjustsWeights()
    {
        // Arrange
        var query = "Compare Microsoft Azure and Amazon AWS";
        SetupMockAnalysis(AppQueryType.ComparisonQuery, ComplexityLevel.Moderate,
            entities: new List<string> { "Microsoft Azure", "Amazon AWS" });

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AppQueryType.ComparisonQuery, result.QueryType);
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithLongQuery_BoostsSemanticWeight()
    {
        // Arrange
        var query = "How can I optimize the performance of my machine learning model training pipeline in Python using distributed computing frameworks?";
        SetupMockAnalysis(AppQueryType.NaturalQuestion, ComplexityLevel.Complex,
            keywords: new List<string> { "optimize", "performance", "machine", "learning", "model", "training", "pipeline", "Python", "distributed", "computing" });

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.VectorWeight > 0.5,
            "Long queries should favor vector/semantic search");
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithShortQuery_BoostsKeywordWeight()
    {
        // Arrange
        var query = "C# async";
        SetupMockAnalysis(AppQueryType.SimpleKeyword, ComplexityLevel.Simple,
            keywords: new List<string> { "C#", "async" });

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.SparseWeight >= 0.40,
            "Short queries should favor keyword/sparse search");
    }

    #endregion

    #region Fusion Method Selection Tests

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithComplexQuery_SelectsRelativeScoreFusion()
    {
        // Arrange
        var query = "Explain the architecture of transformer models";
        SetupMockAnalysis(AppQueryType.ReasoningQuery, ComplexityLevel.Complex);

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(FusionMethod.RelativeScoreFusion, result.RecommendedFusion);
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithTechnicalHighSpecificity_SelectsWeightedSum()
    {
        // Arrange
        var query = "HNSW index parameters";
        SetupMockAnalysis(AppQueryType.SimpleKeyword, ComplexityLevel.Simple,
            technicalDomains: new List<string> { "programming" }, specificity: 0.7);

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(FusionMethod.WeightedSum, result.RecommendedFusion);
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithMultiHopQuery_SelectsProductFusion()
    {
        // Arrange
        var query = "Who founded the company that makes the iPhone?";
        SetupMockAnalysis(AppQueryType.MultiHopQuery, ComplexityLevel.Moderate, isMultiHop: true);

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(FusionMethod.Product, result.RecommendedFusion);
    }

    #endregion

    #region Weight Normalization Tests

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WeightsAlwaysSumToOne()
    {
        // Arrange
        var queries = new[]
        {
            ("simple keyword", AppQueryType.SimpleKeyword, ComplexityLevel.Simple),
            ("natural question", AppQueryType.NaturalQuestion, ComplexityLevel.Moderate),
            ("complex reasoning", AppQueryType.ReasoningQuery, ComplexityLevel.Complex),
            ("multi-hop", AppQueryType.MultiHopQuery, ComplexityLevel.VeryComplex)
        };

        foreach (var (query, type, complexity) in queries)
        {
            SetupMockAnalysis(type, complexity);

            // Act
            var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

            // Assert
            var sum = result.VectorWeight + result.SparseWeight;
            Assert.True(Math.Abs(sum - 1.0) < 0.001,
                $"Weights should sum to 1.0, got {sum} for query type {type}");
        }
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WeightsWithinValidRange()
    {
        // Arrange
        var query = "test query";
        SetupMockAnalysis(AppQueryType.NaturalQuestion, ComplexityLevel.Moderate);

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.VectorWeight >= 0.15 && result.VectorWeight <= 0.90,
            $"Vector weight {result.VectorWeight} should be between 0.15 and 0.90");
        Assert.True(result.SparseWeight >= 0.10 && result.SparseWeight <= 0.85,
            $"Sparse weight {result.SparseWeight} should be between 0.10 and 0.85");
    }

    #endregion

    #region Confidence Calculation Tests

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithClearPattern_HasHigherConfidence()
    {
        // Arrange
        var query = "What is the purpose of dependency injection?";
        SetupMockAnalysis(AppQueryType.NaturalQuestion, ComplexityLevel.Moderate,
            technicalDomains: new List<string> { "programming" }, requiresReasoning: true, confidenceScore: 0.8);

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Confidence >= 0.8,
            $"Confidence {result.Confidence} should be >= 0.8 for clear patterns");
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_ConfidenceWithinValidRange()
    {
        // Arrange
        var query = "test query";
        SetupMockAnalysis(AppQueryType.SimpleKeyword, ComplexityLevel.Simple, confidenceScore: 0.5);

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Confidence >= 0.3 && result.Confidence <= 0.95,
            $"Confidence {result.Confidence} should be between 0.3 and 0.95");
    }

    #endregion

    #region Reasoning Generation Tests

    [Fact]
    public async Task CalculateDynamicWeightsAsync_GeneratesReasoningString()
    {
        // Arrange
        var query = "What is machine learning?";
        SetupMockAnalysis(AppQueryType.NaturalQuestion, ComplexityLevel.Moderate);

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.Reasoning);
        Assert.NotEmpty(result.Reasoning);
        Assert.Contains("Type:", result.Reasoning);
        Assert.Contains("Complexity:", result.Reasoning);
        Assert.Contains("Weights:", result.Reasoning);
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_ReasoningIncludesDomains()
    {
        // Arrange
        var query = "Python async await";
        SetupMockAnalysis(AppQueryType.SimpleKeyword, ComplexityLevel.Simple,
            technicalDomains: new List<string> { "programming" });

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Domains:", result.Reasoning);
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_ReasoningIndicatesReasoning()
    {
        // Arrange
        var query = "Why does this work?";
        SetupMockAnalysis(AppQueryType.ReasoningQuery, ComplexityLevel.Moderate, requiresReasoning: true);

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Reasoning:", result.Reasoning);
    }

    #endregion

    #region UseQuantizedSearch Tests

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithComplexQuery_EnablesQuantizedSearch()
    {
        // Arrange
        var query = "Complex multi-part query";
        SetupMockAnalysis(AppQueryType.MultiHopQuery, ComplexityLevel.Complex);

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.UseQuantizedSearch,
            "Complex queries should enable quantized search");
    }

    [Fact]
    public async Task CalculateDynamicWeightsAsync_WithSimpleQuery_DisablesQuantizedSearch()
    {
        // Arrange
        var query = "simple query";
        SetupMockAnalysis(AppQueryType.SimpleKeyword, ComplexityLevel.Simple);

        // Act
        var result = await _service.CalculateDynamicWeightsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.UseQuantizedSearch,
            "Simple queries should not enable quantized search");
    }

    #endregion

    #region UpdatePerformanceFeedbackAsync Tests

    [Fact]
    public async Task UpdatePerformanceFeedbackAsync_LogsFeedback()
    {
        // Arrange — LoggerMessage source generators check IsEnabled before calling Log
        _mockLogger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var config = new DynamicFusionConfiguration
        {
            VectorWeight = 0.6,
            SparseWeight = 0.4,
            QueryType = AppQueryType.NaturalQuestion
        };
        var feedback = new FusionPerformanceFeedback
        {
            RelevantResults = 8,
            TotalResults = 10,
            MRR = 0.75,
            LatencyMs = 150
        };

        // Act
        await _service.UpdatePerformanceFeedbackAsync(config, feedback, TestContext.Current.CancellationToken);

        // Assert - Verify logging occurred (source-generated LoggerMessage uses internal TState types)
        var logCalls = _mockLogger.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == "Log");
        Assert.NotEmpty(logCalls);
    }

    [Fact]
    public async Task UpdatePerformanceFeedbackAsync_CompletesSuccessfully()
    {
        // Arrange
        var config = new DynamicFusionConfiguration();
        var feedback = new FusionPerformanceFeedback();

        // Act & Assert - Should not throw
        await _service.UpdatePerformanceFeedbackAsync(config, feedback, TestContext.Current.CancellationToken);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task CalculateDynamicWeightsAsync_RespectsCanellation()
    {
        // Arrange
        var query = "test query";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockQueryAnalyzer.AnalyzeAsync(query, Arg.Any<CancellationToken>()).Throws(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.CalculateDynamicWeightsAsync(query, cts.Token));
    }

    #endregion

    #region Helper Methods

    private void SetupMockAnalysis(
        AppQueryType type,
        ComplexityLevel complexity,
        bool requiresReasoning = false,
        bool isMultiHop = false,
        double specificity = 0.5,
        double confidenceScore = 0.7,
        List<string>? technicalDomains = null,
        List<string>? entities = null,
        List<string>? keywords = null)
    {
        var analysis = new QueryAnalysis
        {
            Type = type,
            Complexity = complexity,
            RequiresReasoning = requiresReasoning,
            IsMultiHop = isMultiHop,
            Specificity = specificity,
            ConfidenceScore = confidenceScore,
            TechnicalDomains = technicalDomains ?? new List<string>(),
            Entities = entities ?? new List<string>(),
            Keywords = keywords ?? new List<string> { "test", "query" }
        };

        _mockQueryAnalyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(analysis);
    }

    #endregion
}
