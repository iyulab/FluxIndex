using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Application.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace FluxIndex.Core.Tests.Services;

public class ChunkClassificationServiceTests
{
    private readonly ILogger<ClassificationValidationService> _validationLoggerMock;
    private readonly ILogger<LlmChunkClassificationService> _classificationLoggerMock;
    private readonly ClassificationOptions _options;

    public ChunkClassificationServiceTests()
    {
        _validationLoggerMock = Substitute.For<ILogger<ClassificationValidationService>>();
        _classificationLoggerMock = Substitute.For<ILogger<LlmChunkClassificationService>>();
        _options = new ClassificationOptions
        {
            Enabled = true,
            MaxTopics = 5,
            MaxCategories = 3,
            MaxTags = 10,
            MaxQuestions = 5,
            MaxSummaryLength = 200,
            Validation = new ClassificationValidationOptions
            {
                MinQualityThreshold = 0.3,
                MinContentLength = 50,
                MinExistingKeywords = 3
            }
        };
    }

    #region Validation Tests

    [Fact]
    public async Task ValidateAsync_LowQuality_SkipsClassification()
    {
        // Arrange
        var validationService = CreateValidationService();
        var chunk = CreateTestChunk(quality: 0.1);

        // Act
        var result = await validationService.ValidateAsync(chunk, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.RequiresLlmClassification);
        Assert.Contains("Quality below threshold", result.SkipReason);
    }

    [Fact]
    public async Task ValidateAsync_ShortContent_SkipsClassification()
    {
        // Arrange
        var validationService = CreateValidationService();
        var chunk = CreateTestChunk(content: "Short");

        // Act
        var result = await validationService.ValidateAsync(chunk, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.RequiresLlmClassification);
        Assert.Contains("Content too short", result.SkipReason);
    }

    [Fact]
    public async Task ValidateAsync_ValidChunk_RequiresClassification()
    {
        // Arrange
        var validationService = CreateValidationService();
        var chunk = CreateTestChunk();

        // Act
        var result = await validationService.ValidateAsync(chunk, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.RequiresLlmClassification);
        Assert.Null(result.SkipReason);
    }

    [Fact]
    public async Task ValidateAsync_SufficientKeywords_ReducesScope()
    {
        // Arrange
        var validationService = CreateValidationService();
        var chunk = CreateTestChunk();
        ((TestSourceMetadata)chunk.Source).Keywords = new List<string>
        {
            "keyword1", "keyword2", "keyword3", "keyword4"
        };

        // Act
        var result = await validationService.ValidateAsync(chunk, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.RequiresLlmClassification);
        Assert.False(result.RecommendedScope.HasFlag(ClassificationScope.Keywords));
    }

    [Fact]
    public async Task ValidateAsync_LowContextDependency_ReducesScope()
    {
        // Arrange
        var validationService = CreateValidationService();
        var chunk = CreateTestChunk(contextDependency: 0.3);

        // Act
        var result = await validationService.ValidateAsync(chunk, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.RequiresLlmClassification);
        Assert.False(result.RecommendedScope.HasFlag(ClassificationScope.Summary));
        Assert.False(result.RecommendedScope.HasFlag(ClassificationScope.Questions));
    }

    [Fact]
    public async Task ValidateBatchAsync_MixedQuality_FiltersCorrectly()
    {
        // Arrange
        var validationService = CreateValidationService();
        var chunks = new[]
        {
            CreateTestChunk("chunk1", quality: 0.1, content: "First chunk content that is long enough for validation testing purposes."),
            CreateTestChunk("chunk2", quality: 0.8, content: "Second chunk content that is different and long enough for validation testing."),
            CreateTestChunk("chunk3", quality: 0.5, content: "Third chunk content with unique text that passes minimum length requirements.")
        };

        // Act
        var results = await validationService.ValidateBatchAsync(chunks, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.False(results["chunk1"].RequiresLlmClassification);
        Assert.True(results["chunk2"].RequiresLlmClassification);
        Assert.True(results["chunk3"].RequiresLlmClassification);
    }

    [Fact]
    public void ValidateOutput_ValidClassification_ReturnsTrue()
    {
        // Arrange
        var validationService = CreateValidationService();
        var classification = new ChunkClassification
        {
            Topics = new List<string> { "Topic1", "Topic2" },
            Categories = new List<string> { "Category1" },
            Confidence = 0.8
        };

        // Act
        var isValid = validationService.ValidateOutput(classification);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateOutput_LowConfidence_ReturnsFalse()
    {
        // Arrange
        var validationService = CreateValidationService();
        var classification = new ChunkClassification
        {
            Topics = new List<string> { "Topic1" },
            Confidence = 0.3
        };

        // Act
        var isValid = validationService.ValidateOutput(classification);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateOutput_NoContent_ReturnsFalse()
    {
        // Arrange
        var validationService = CreateValidationService();
        var classification = new ChunkClassification
        {
            Confidence = 0.8
        };

        // Act
        var isValid = validationService.ValidateOutput(classification);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateOutput_ExceedsMaxCounts_ReturnsFalse()
    {
        // Arrange
        var validationService = CreateValidationService();
        var classification = new ChunkClassification
        {
            Topics = Enumerable.Range(1, 10).Select(i => $"Topic{i}").ToList(),
            Confidence = 0.8
        };

        // Act
        var isValid = validationService.ValidateOutput(classification);

        // Assert
        Assert.False(isValid);
    }

    #endregion

    #region Classification Service Tests

    [Fact]
    public async Task ClassifyAsync_NoLlmService_ReturnsSkipped()
    {
        // Arrange
        var validationService = CreateValidationService();
        var classificationService = new LlmChunkClassificationService(
            MsOptions.Create(_options),
            validationService,
            _classificationLoggerMock,
            textCompletion: null);

        var chunk = CreateTestChunk();

        // Act
        var result = await classificationService.ClassifyAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClassificationSource.Skipped, result.Source);
    }

    [Fact]
    public async Task ClassifyAsync_FailsValidation_ReturnsSkipped()
    {
        // Arrange
        var validationService = CreateValidationService();
        var classificationService = new LlmChunkClassificationService(
            MsOptions.Create(_options),
            validationService,
            _classificationLoggerMock);

        var chunk = CreateTestChunk(quality: 0.1);

        // Act
        var result = await classificationService.ClassifyAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClassificationSource.Skipped, result.Source);
    }

    [Fact]
    public async Task ClassifyAsync_WithLlm_ReturnsClassification()
    {
        // Arrange
        var textCompletionMock = Substitute.For<ITextCompletionService>();
        textCompletionMock.CompleteAsync(
                Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns(@"{
                ""topics"": [""RAG"", ""Vector Search""],
                ""categories"": [""Technical""],
                ""tags"": [""search"", ""ai""],
                ""summary"": ""This chunk explains RAG concepts."",
                ""confidence"": 0.85
            }");

        var validationService = CreateValidationService();
        var classificationService = new LlmChunkClassificationService(
            MsOptions.Create(_options),
            validationService,
            _classificationLoggerMock,
            textCompletionMock);

        var chunk = CreateTestChunk();

        // Act
        var result = await classificationService.ClassifyAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClassificationSource.Llm, result.Source);
        Assert.Equal(2, result.Topics.Count);
        Assert.Contains("RAG", result.Topics);
        Assert.Equal(0.85, result.Confidence);
    }

    [Fact]
    public async Task ClassifyBatchAsync_MixedChunks_ProcessesCorrectly()
    {
        // Arrange
        var textCompletionMock = Substitute.For<ITextCompletionService>();
        textCompletionMock.CompleteAsync(
                Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns(@"{
                ""topics"": [""Topic1""],
                ""confidence"": 0.8
            }");

        var validationService = CreateValidationService();
        var classificationService = new LlmChunkClassificationService(
            MsOptions.Create(_options),
            validationService,
            _classificationLoggerMock,
            textCompletionMock);

        var chunks = new[]
        {
            CreateTestChunk("chunk1", quality: 0.1, content: "First chunk with low quality that should be skipped during processing."),
            CreateTestChunk("chunk2", quality: 0.8, content: "Second chunk with high quality that should be processed by the LLM service."),
            CreateTestChunk("chunk3", quality: 0.7, content: "Third chunk with medium quality that should also be processed by LLM service.")
        };

        // Act
        var results = await classificationService.ClassifyBatchAsync(chunks, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal(ClassificationSource.Skipped, results["chunk1"].Source);
        Assert.Equal(ClassificationSource.Llm, results["chunk2"].Source);
        Assert.Equal(ClassificationSource.Llm, results["chunk3"].Source);

        // LLM should be called only twice
        await textCompletionMock.Received(2).CompleteAsync(
            Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_InvalidJsonResponse_ReturnsEmptyClassification()
    {
        // Arrange
        var textCompletionMock = Substitute.For<ITextCompletionService>();
        textCompletionMock.CompleteAsync(
                Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("Invalid JSON response");

        var validationService = CreateValidationService();
        var classificationService = new LlmChunkClassificationService(
            MsOptions.Create(_options),
            validationService,
            _classificationLoggerMock,
            textCompletionMock);

        var chunk = CreateTestChunk();

        // Act
        var result = await classificationService.ClassifyAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Should fail validation and return skipped after retries
        Assert.Equal(ClassificationSource.Skipped, result.Source);
    }

    #endregion

    #region Helper Methods

    private ClassificationValidationService CreateValidationService()
    {
        return new ClassificationValidationService(
            MsOptions.Create(_options),
            _validationLoggerMock);
    }

    private static TestEnrichedChunk CreateTestChunk(
        string? chunkId = null,
        double quality = 0.8,
        double contextDependency = 0.5,
        string? content = null)
    {
        return new TestEnrichedChunk
        {
            Content = content ?? "This is a test content that is long enough to pass the minimum content length validation for classification.",
            ChunkId = chunkId ?? Guid.NewGuid().ToString(),
            ChunkIndex = 0,
            HeadingPath = new List<string> { "Chapter 1", "Section 1.1" },
            SectionTitle = "Section 1.1",
            Quality = quality,
            ContextDependency = contextDependency,
            TokenCount = 100,
            Source = new TestSourceMetadata
            {
                SourceId = "test-doc-1",
                SourceType = "pdf",
                Title = "Test Document",
                Language = "en",
                WordCount = 1000,
                ChunkCount = 10
            }
        };
    }

    #endregion
}
