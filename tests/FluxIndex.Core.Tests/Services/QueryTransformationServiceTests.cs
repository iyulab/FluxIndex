using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// Tests for QueryTransformationService
/// </summary>
public class QueryTransformationServiceTests
{
    private readonly Mock<ITextCompletionService> _mockCompletionService;
    private readonly ILogger<QueryTransformationService> _logger;

    public QueryTransformationServiceTests()
    {
        _mockCompletionService = new Mock<ITextCompletionService>();
        _logger = NullLogger<QueryTransformationService>.Instance;
    }

    private QueryTransformationService CreateService(bool withLlm = true)
    {
        var options = new Application.Services.QueryTransformationOptions();
        return new QueryTransformationService(
            withLlm ? _mockCompletionService.Object : null,
            Microsoft.Extensions.Options.Options.Create(options),
            _logger);
    }

    #region HyDE Tests

    [Fact]
    public async Task GenerateHypotheticalDocumentAsync_WithoutLLM_ReturnsEmptyResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "What is machine learning?";

        // Act
        var result = await service.GenerateHypotheticalDocumentAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
        Assert.True(string.IsNullOrEmpty(result.HypotheticalDocument));
    }

    [Fact]
    public async Task GenerateHypotheticalDocumentAsync_WithLLM_GeneratesDocument()
    {
        // Arrange
        var service = CreateService(withLlm: true);
        var query = "What is machine learning?";
        var hypotheticalDoc = "Machine learning is a subset of artificial intelligence...";

        _mockCompletionService
            .Setup(x => x.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hypotheticalDoc);

        // Act
        var result = await service.GenerateHypotheticalDocumentAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
        Assert.Equal(hypotheticalDoc, result.HypotheticalDocument);
    }

    #endregion

    #region Multi-Query Tests

    [Fact]
    public async Task GenerateMultipleQueriesAsync_WithoutLLM_UsesRuleBased()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "machine learning algorithms";

        // Act
        var result = await service.GenerateMultipleQueriesAsync(query, count: 3);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(query, result); // Original query should be included
    }

    [Fact]
    public async Task GenerateMultipleQueriesAsync_WithLLM_GeneratesVariants()
    {
        // Arrange
        var service = CreateService(withLlm: true);
        var query = "How does photosynthesis work?";
        var llmResponse = "[\"What is photosynthesis?\", \"Explain photosynthesis\"]";

        _mockCompletionService
            .Setup(x => x.GenerateCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await service.GenerateMultipleQueriesAsync(query, count: 3);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count >= 1);
    }

    #endregion

    #region Query Decomposition Tests

    [Fact]
    public async Task DecomposeQueryAsync_SimpleQuery_ReturnsResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "What is AI?";

        // Act
        var result = await service.DecomposeQueryAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
    }

    [Fact]
    public async Task DecomposeQueryAsync_ComplexQuery_DecomposesIntoSubQueries()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "Compare machine learning and deep learning approaches";

        // Act
        var result = await service.DecomposeQueryAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.SubQueries.Count >= 1);
    }

    #endregion

    #region Intent Analysis Tests

    [Fact]
    public async Task AnalyzeQueryIntentAsync_ValidQuery_ReturnsResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "What is the population of Japan?";

        // Act
        var result = await service.AnalyzeQueryIntentAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
    }

    #endregion
}
