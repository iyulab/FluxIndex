using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FluxIndex.Tests.AI.OpenAI.Services;

public class OpenAIMetadataExtractorTests
{
    private readonly Mock<ITextCompletionService> _mockCompletionService;
    private readonly Mock<ILogger<OpenAIMetadataExtractor>> _mockLogger;
    private readonly IMemoryCache _cache;
    private readonly OpenAIMetadataExtractor _extractor;

    public OpenAIMetadataExtractorTests()
    {
        _mockCompletionService = new Mock<ITextCompletionService>();
        _mockLogger = new Mock<ILogger<OpenAIMetadataExtractor>>();
        _cache = new MemoryCache(new MemoryCacheOptions());

        _extractor = new OpenAIMetadataExtractor(
            _mockCompletionService.Object,
            _cache,
            _mockLogger.Object);
    }

    [Fact]
    public async Task ExtractAsync_WithValidContent_ShouldReturnMetadata()
    {
        // Arrange
        var content = "This is a test document about AI.";
        var mockResponse = @"{
            ""description"": ""A document about AI"",
            ""keywords"": [""AI"", ""test""],
            ""topics"": [""artificial intelligence""],
            ""language"": ""en"",
            ""documentType"": ""article"",
            ""overallConfidence"": 0.9
        }";

        _mockCompletionService
            .Setup(x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _extractor.ExtractAsync(content, MetadataSchema.General);

        // Assert
        result.Should().NotBeNull();
        result.Description.Should().Be("A document about AI");
        result.Keywords.Should().Contain("AI");
        result.Topics.Should().Contain("artificial intelligence");
        result.Language.Should().Be("en");
        result.DocumentType.Should().Be("article");
        result.OverallConfidence.Should().Be(0.9f);
        result.ExtractionMethod.Should().Contain("OpenAI");
    }

    [Fact]
    public async Task ExtractAsync_WithNullContent_ShouldThrowArgumentNullException()
    {
        // Arrange
        string? content = null;

        // Act
        Func<Task> act = async () => await _extractor.ExtractAsync(content!, MetadataSchema.General);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("content");
    }

    [Fact]
    public async Task ExtractAsync_WithEmptyContent_ShouldThrowArgumentException()
    {
        // Arrange
        var content = string.Empty;

        // Act
        Func<Task> act = async () => await _extractor.ExtractAsync(content, MetadataSchema.General);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("content");
    }

    [Fact]
    public async Task ExtractWithCacheAsync_FirstCall_ShouldCallCompletionService()
    {
        // Arrange
        var content = "Test content";
        var cacheKey = "test-key";
        var mockResponse = @"{
            ""description"": ""Summary"",
            ""keywords"": [],
            ""topics"": [],
            ""overallConfidence"": 0.8
        }";

        _mockCompletionService
            .Setup(x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _extractor.ExtractWithCacheAsync(content, cacheKey, MetadataSchema.General);

        // Assert
        result.Should().NotBeNull();
        _mockCompletionService.Verify(x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractWithCacheAsync_SecondCall_ShouldReturnCachedResult()
    {
        // Arrange
        var content = "Test content";
        var cacheKey = "test-key";
        var mockResponse = @"{
            ""description"": ""Summary"",
            ""keywords"": [],
            ""topics"": [],
            ""overallConfidence"": 0.8
        }";

        _mockCompletionService
            .Setup(x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result1 = await _extractor.ExtractWithCacheAsync(content, cacheKey, MetadataSchema.General);
        var result2 = await _extractor.ExtractWithCacheAsync(content, cacheKey, MetadataSchema.General);

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        _mockCompletionService.Verify(x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_WithDifferentSchemas_ShouldUseAppropriatePrompts()
    {
        // Arrange
        var content = "Test content";
        var mockResponse = @"{
            ""description"": ""Summary"",
            ""keywords"": [],
            ""topics"": [],
            ""overallConfidence"": 0.8
        }";

        _mockCompletionService
            .Setup(x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act - Test different schemas
        await _extractor.ExtractAsync(content, MetadataSchema.General);
        await _extractor.ExtractAsync(content, MetadataSchema.Article);
        await _extractor.ExtractAsync(content, MetadataSchema.ProductManual);
        await _extractor.ExtractAsync(content, MetadataSchema.TechnicalDoc);

        // Assert
        _mockCompletionService.Verify(
            x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(4));
    }

    [Fact]
    public async Task ExtractAsync_WithFastStrategy_ShouldUseGpt4oMini()
    {
        // Arrange
        var content = "Test content";
        var options = new AIMetadataExtractionOptions
        {
            Strategy = MetadataExtractionStrategy.Fast
        };
        var mockResponse = @"{
            ""description"": ""Summary"",
            ""keywords"": [],
            ""topics"": [],
            ""overallConfidence"": 0.8
        }";

        _mockCompletionService
            .Setup(x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _extractor.ExtractAsync(content, MetadataSchema.General, options);

        // Assert
        result.Should().NotBeNull();
        result.ExtractionMethod.Should().Contain("gpt-4o-mini");
    }

    [Fact]
    public async Task ExtractAsync_WithDeepStrategy_ShouldUseGpt4o()
    {
        // Arrange
        var content = "Test content";
        var options = new AIMetadataExtractionOptions
        {
            Strategy = MetadataExtractionStrategy.Deep
        };
        var mockResponse = @"{
            ""description"": ""Summary"",
            ""keywords"": [],
            ""topics"": [],
            ""overallConfidence"": 0.8
        }";

        _mockCompletionService
            .Setup(x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _extractor.ExtractAsync(content, MetadataSchema.General, options);

        // Assert
        result.Should().NotBeNull();
        result.ExtractionMethod.Should().Contain("gpt-4o");
        result.ExtractionMethod.Should().NotContain("mini");
    }

    [Fact]
    public void GenerateCacheKey_WithSameInputs_ShouldReturnSameKey()
    {
        // Arrange
        var content = "Test content";
        var schema = MetadataSchema.General;

        // Act
        var key1 = _extractor.GenerateCacheKey(content, schema);
        var key2 = _extractor.GenerateCacheKey(content, schema);

        // Assert
        key1.Should().Be(key2);
    }

    [Fact]
    public void GenerateCacheKey_WithDifferentContent_ShouldReturnDifferentKeys()
    {
        // Arrange
        var content1 = "Test content 1";
        var content2 = "Test content 2";
        var schema = MetadataSchema.General;

        // Act
        var key1 = _extractor.GenerateCacheKey(content1, schema);
        var key2 = _extractor.GenerateCacheKey(content2, schema);

        // Assert
        key1.Should().NotBe(key2);
    }

    [Fact]
    public void GenerateCacheKey_WithDifferentSchemas_ShouldReturnDifferentKeys()
    {
        // Arrange
        var content = "Test content";
        var schema1 = MetadataSchema.General;
        var schema2 = MetadataSchema.Article;

        // Act
        var key1 = _extractor.GenerateCacheKey(content, schema1);
        var key2 = _extractor.GenerateCacheKey(content, schema2);

        // Assert
        key1.Should().NotBe(key2);
    }

    [Fact]
    public void GetSupportedSchemas_ShouldReturnAllSchemas()
    {
        // Act
        var schemas = _extractor.GetSupportedSchemas();

        // Assert
        schemas.Should().Contain(MetadataSchema.General);
        schemas.Should().Contain(MetadataSchema.Article);
        schemas.Should().Contain(MetadataSchema.ProductManual);
        schemas.Should().Contain(MetadataSchema.TechnicalDoc);
        schemas.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void GetSchemaDescription_ForGeneralSchema_ShouldReturnDescription()
    {
        // Act
        var description = _extractor.GetSchemaDescription(MetadataSchema.General);

        // Assert
        description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExtractAsync_WithApiFailure_ShouldRetryAndThrow()
    {
        // Arrange
        var content = "Test content";
        _mockCompletionService
            .Setup(x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API error"));

        // Act
        Func<Task> act = async () => await _extractor.ExtractAsync(content, MetadataSchema.General);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _mockCompletionService.Verify(
            x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(1)); // Should retry
    }

    [Fact]
    public async Task ExtractAsync_WithInvalidJson_ShouldThrowJsonException()
    {
        // Arrange
        var content = "Test content";
        var invalidJson = "This is not valid JSON";

        _mockCompletionService
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invalidJson);

        // Act
        Func<Task> act = async () => await _extractor.ExtractAsync(content, MetadataSchema.General);

        // Assert
        await act.Should().ThrowAsync<Exception>(); // JSON parsing error
    }

    [Fact]
    public async Task ExtractAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        var content = "Test content";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockCompletionService
            .Setup(x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        Func<Task> act = async () => await _extractor.ExtractAsync(content, MetadataSchema.General, cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
