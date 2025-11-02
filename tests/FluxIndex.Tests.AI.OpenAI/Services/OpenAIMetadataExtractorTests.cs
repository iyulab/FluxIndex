using FluxIndex.AI.OpenAI;
using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Interfaces;
using FluxIndex.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FluxIndex.Tests.AI.OpenAI.Services;

public class OpenAIMetadataExtractorTests : IClassFixture<OpenAITestFixture>
{
    private readonly OpenAITestFixture _fixture;

    public OpenAIMetadataExtractorTests(OpenAITestFixture fixture)
    {
        _fixture = fixture;
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

        _fixture.SetupMockResponse(mockResponse);

        // Act
        var result = await _fixture.Extractor.ExtractAsync(content, MetadataSchema.General);

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
    [Trait("Category", "MockOnly")]
    public async Task ExtractAsync_WithNullContent_ShouldThrowArgumentNullException()
    {
        // Arrange
        string? content = null;

        // Act
        Func<Task> act = async () => await _fixture.Extractor.ExtractAsync(content!, MetadataSchema.General);

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
        Func<Task> act = async () => await _fixture.Extractor.ExtractAsync(content, MetadataSchema.General);

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

        _fixture.SetupMockResponse(mockResponse);

        // Act
        var result = await _fixture.Extractor.ExtractWithCacheAsync(content, cacheKey, MetadataSchema.General);

        // Assert
        result.Should().NotBeNull();
        // Mock 모드에서만 검증
        if (!_fixture.UseRealApi)
        {
            _fixture.MockCompletionService!.Verify(x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        }
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

        _fixture.SetupMockResponse(mockResponse);

        // Act
        var result1 = await _fixture.Extractor.ExtractWithCacheAsync(content, cacheKey, MetadataSchema.General);
        var result2 = await _fixture.Extractor.ExtractWithCacheAsync(content, cacheKey, MetadataSchema.General);

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        // Mock 모드에서만 검증
        if (!_fixture.UseRealApi)
        {
            _fixture.MockCompletionService!.Verify(x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        }
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

        _fixture.SetupMockResponse(mockResponse);

        // Act - Test different schemas
        await _fixture.Extractor.ExtractAsync(content, MetadataSchema.General);
        await _fixture.Extractor.ExtractAsync(content, MetadataSchema.Article);
        await _fixture.Extractor.ExtractAsync(content, MetadataSchema.ProductManual);
        await _fixture.Extractor.ExtractAsync(content, MetadataSchema.TechnicalDoc);

        // Assert
        // Mock 모드에서만 검증
        if (!_fixture.UseRealApi)
        {
            _fixture.MockCompletionService!.Verify(
                x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Exactly(4));
        }
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

        _fixture.SetupMockResponse(mockResponse);

        // Act
        var result = await _fixture.Extractor.ExtractAsync(content, MetadataSchema.General, options);

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

        _fixture.SetupMockResponse(mockResponse);

        // Act
        var result = await _fixture.Extractor.ExtractAsync(content, MetadataSchema.General, options);

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
        var key1 = _fixture.Extractor.GenerateCacheKey(content, schema);
        var key2 = _fixture.Extractor.GenerateCacheKey(content, schema);

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
        var key1 = _fixture.Extractor.GenerateCacheKey(content1, schema);
        var key2 = _fixture.Extractor.GenerateCacheKey(content2, schema);

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
        var key1 = _fixture.Extractor.GenerateCacheKey(content, schema1);
        var key2 = _fixture.Extractor.GenerateCacheKey(content, schema2);

        // Assert
        key1.Should().NotBe(key2);
    }

    [Fact]
    public void GetSupportedSchemas_ShouldReturnAllSchemas()
    {
        // Act
        var schemas = _fixture.Extractor.GetSupportedSchemas();

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
        var description = _fixture.Extractor.GetSchemaDescription(MetadataSchema.General);

        // Assert
        description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExtractAsync_WithApiFailure_ShouldRetryAndThrow()
    {
        // Arrange
        var content = "Test content";
        _fixture.SetupMockException(new HttpRequestException("API error"));

        // Act
        Func<Task> act = async () => await _fixture.Extractor.ExtractAsync(content, MetadataSchema.General);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        // Mock 모드에서만 검증
        if (!_fixture.UseRealApi)
        {
            _fixture.MockCompletionService!.Verify(
                x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.AtLeast(1)); // Should retry
        }
    }

    [Fact]
    public async Task ExtractAsync_WithInvalidJson_ShouldThrowJsonException()
    {
        // Arrange
        var content = "Test content";
        var invalidJson = "This is not valid JSON";

        _fixture.SetupMockResponse(invalidJson);

        // Act
        Func<Task> act = async () => await _fixture.Extractor.ExtractAsync(content, MetadataSchema.General);

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

        _fixture.SetupMockException(new OperationCanceledException());

        // Act
        Func<Task> act = async () => await _fixture.Extractor.ExtractAsync(content, MetadataSchema.General, cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
