using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Options;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FluxIndex.AI.OpenAI.Tests.Services;

/// <summary>
/// OpenAIMetadataEnrichmentService 단위 테스트
/// </summary>
public class OpenAIMetadataEnrichmentServiceTests
{
    private readonly Mock<IOpenAIClient> _mockClient;
    private readonly MetadataExtractionOptions _options;
    private readonly ILogger<OpenAIMetadataEnrichmentService> _logger;
    private readonly OpenAIMetadataEnrichmentService _service;

    public OpenAIMetadataEnrichmentServiceTests()
    {
        _mockClient = new Mock<IOpenAIClient>();
        _options = MetadataExtractionOptions.CreateForTesting();
        _logger = new NullLogger<OpenAIMetadataEnrichmentService>();

        _service = OpenAIMetadataEnrichmentService.CreateForTesting(
            _mockClient.Object,
            _options,
            _logger);
    }

    [Fact]
    public async Task ExtractMetadataAsync_ValidResponse_ReturnsMetadata()
    {
        // Arrange
        var content = "This is test content for metadata extraction.";
        var expectedResponse = """
        {
            "title": "Test Content Analysis",
            "summary": "Analysis of test content for metadata extraction purposes",
            "keywords": ["test", "content", "metadata", "extraction"],
            "entities": ["TestEntity"],
            "generated_questions": ["What is the purpose of this test?"],
            "quality_score": 0.85
        }
        """;

        _mockClient.Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.ExtractMetadataAsync(content);

        // Assert
        Assert.Equal("Test Content Analysis", result.Title);
        Assert.Equal("Analysis of test content for metadata extraction purposes", result.Summary);
        Assert.Contains("test", result.Keywords);
        Assert.Contains("TestEntity", result.Entities);
        Assert.Contains("What is the purpose of this test?", result.GeneratedQuestions);
        Assert.Equal(0.85f, result.QualityScore);

        _mockClient.Verify(c => c.CompleteAsync(
            It.Is<string>(prompt => prompt.Contains(content)),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractMetadataAsync_WithContext_IncludesContextInPrompt()
    {
        // Arrange
        var content = "Test content";
        var context = "Additional context information";
        var response = """
        {
            "title": "Context Test",
            "summary": "Test with context",
            "keywords": ["context", "test"],
            "entities": [],
            "generated_questions": [],
            "quality_score": 0.7
        }
        """;

        _mockClient.Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        await _service.ExtractMetadataAsync(content, context);

        // Assert
        _mockClient.Verify(c => c.CompleteAsync(
            It.Is<string>(prompt => prompt.Contains(content) && prompt.Contains(context)),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task ExtractMetadataAsync_EmptyContent_ThrowsArgumentException(string content)
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ExtractMetadataAsync(content));

        Assert.Equal("Content cannot be empty", exception.Message);
        Assert.Equal("content", exception.ParamName);
    }

    [Fact]
    public async Task ExtractMetadataAsync_InvalidJsonResponse_RetriesAndThrows()
    {
        // Arrange
        var content = "Test content";
        var invalidResponse = "This is not valid JSON";

        _mockClient.Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(invalidResponse);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<MetadataExtractionException>(() =>
            _service.ExtractMetadataAsync(content));

        Assert.Equal(MetadataExtractionErrorType.InvalidResponse, exception.ErrorType);
        Assert.Contains("Failed to parse JSON", exception.Message);
        Assert.False(exception.IsRetryable);

        // Verify retries happened (maxRetries + 1 calls)
        _mockClient.Verify(c => c.CompleteAsync(
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Exactly(_options.MaxRetries + 1));
    }

    [Fact]
    public async Task ExtractMetadataAsync_LowQualityResponse_RetriesUntilAcceptable()
    {
        // Arrange
        var content = "Test content";
        var lowQualityResponse = """
        {
            "title": "",
            "summary": "",
            "keywords": [],
            "entities": [],
            "generated_questions": [],
            "quality_score": 0.1
        }
        """;
        var goodQualityResponse = """
        {
            "title": "Good Quality",
            "summary": "This is a good quality response",
            "keywords": ["good", "quality"],
            "entities": ["QualityEntity"],
            "generated_questions": ["What makes this good?"],
            "quality_score": 0.8
        }
        """;

        _mockClient.SetupSequence(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(lowQualityResponse)
            .ReturnsAsync(goodQualityResponse);

        // Act
        var result = await _service.ExtractMetadataAsync(content);

        // Assert
        Assert.Equal("Good Quality", result.Title);
        Assert.Equal(0.8f, result.QualityScore);

        // Verify two calls were made
        _mockClient.Verify(c => c.CompleteAsync(
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExtractMetadataAsync_CancellationRequested_ThrowsMetadataExtractionException()
    {
        // Arrange
        var content = "Test content";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockClient.Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<MetadataExtractionException>(() =>
            _service.ExtractMetadataAsync(content, cancellationToken: cts.Token));

        Assert.Equal(MetadataExtractionErrorType.TimeoutError, exception.ErrorType);
        Assert.True(exception.IsRetryable);
    }

    [Fact]
    public async Task ExtractBatchAsync_ValidBatch_ReturnsMetadataList()
    {
        // Arrange
        var contents = new List<string>
        {
            "First content",
            "Second content"
        };

        var batchResponse = """
        [
            {
                "title": "First Document",
                "summary": "First document summary",
                "keywords": ["first", "document"],
                "entities": ["FirstEntity"],
                "generated_questions": ["What is first?"],
                "quality_score": 0.8
            },
            {
                "title": "Second Document",
                "summary": "Second document summary",
                "keywords": ["second", "document"],
                "entities": ["SecondEntity"],
                "generated_questions": ["What is second?"],
                "quality_score": 0.9
            }
        ]
        """;

        _mockClient.Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResponse);

        // Act
        var results = await _service.ExtractBatchAsync(contents);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("First Document", results[0].Title);
        Assert.Equal("Second Document", results[1].Title);

        _mockClient.Verify(c => c.CompleteAsync(
            It.Is<string>(prompt => prompt.Contains("First content") && prompt.Contains("Second content")),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractBatchAsync_EmptyContents_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ExtractBatchAsync(new List<string>()));

        Assert.Equal("Contents cannot be null or empty", exception.Message);
        Assert.Equal("contents", exception.ParamName);
    }

    [Fact]
    public async Task ExtractBatchAsync_WithProgressCallback_CallsCallback()
    {
        // Arrange
        var contents = new List<string> { "Test content 1", "Test content 2" };
        var progressCalls = new List<(int processed, int total)>();

        var options = new BatchProcessingOptions
        {
            Size = 2,
            ProgressCallback = (processed, total) => progressCalls.Add((processed, total))
        };

        var batchResponse = """
        [
            {
                "title": "Doc 1",
                "summary": "Summary 1",
                "keywords": ["test1"],
                "entities": [],
                "generated_questions": [],
                "quality_score": 0.7
            },
            {
                "title": "Doc 2",
                "summary": "Summary 2",
                "keywords": ["test2"],
                "entities": [],
                "generated_questions": [],
                "quality_score": 0.8
            }
        ]
        """;

        _mockClient.Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResponse);

        // Act
        await _service.ExtractBatchAsync(contents, options);

        // Assert
        Assert.Single(progressCalls);
        Assert.Equal((2, 2), progressCalls[0]);
    }

    [Fact]
    public async Task ExtractWithSchemaAsync_CustomSchema_IncludesSchemaInPrompt()
    {
        // Arrange
        var content = "Technical document content";
        var schema = new { domain = "Software Engineering", complexity = "High" };
        var response = """
        {
            "title": "Technical Document",
            "summary": "Software engineering document",
            "keywords": ["software", "engineering"],
            "entities": ["SoftwareEntity"],
            "generated_questions": ["What are the technical requirements?"],
            "quality_score": 0.9,
            "domain_specific_fields": {
                "domain": "Software Engineering",
                "complexity": "High"
            }
        }
        """;

        _mockClient.Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _service.ExtractWithSchemaAsync(content, schema);

        // Assert
        Assert.Equal("Technical Document", result.Title);
        Assert.Contains("Software Engineering", result.CustomFields.Values);

        _mockClient.Verify(c => c.CompleteAsync(
            It.Is<string>(prompt => prompt.Contains(content) && prompt.Contains("Software Engineering")),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IsHealthyAsync_ServiceWorking_ReturnsTrue()
    {
        // Arrange
        var healthCheckResponse = """
        {
            "title": "Health Check",
            "summary": "Service health check",
            "keywords": ["health", "check"],
            "entities": [],
            "generated_questions": [],
            "quality_score": 0.8
        }
        """;

        _mockClient.Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthCheckResponse);

        // Act
        var isHealthy = await _service.IsHealthyAsync();

        // Assert
        Assert.True(isHealthy);
    }

    [Fact]
    public async Task IsHealthyAsync_ServiceFailing_ReturnsFalse()
    {
        // Arrange
        _mockClient.Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Service unavailable"));

        // Act
        var isHealthy = await _service.IsHealthyAsync();

        // Assert
        Assert.False(isHealthy);
    }

    [Fact]
    public async Task GetStatisticsAsync_ReturnsStatistics()
    {
        // Act
        var statistics = await _service.GetStatisticsAsync();

        // Assert
        Assert.NotNull(statistics);
        Assert.True(statistics.TotalProcessedChunks >= 0);
        Assert.True(statistics.SuccessfulExtractions >= 0);
        Assert.True(statistics.FailedExtractions >= 0);
    }

    [Fact]
    public void Constructor_InvalidOptions_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new MetadataExtractionOptions
        {
            MaxKeywords = -1, // Invalid
            MaxRetries = 10   // Invalid
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new OpenAIMetadataEnrichmentService(_mockClient.Object, invalidOptions, _logger));
    }

    [Fact]
    public void Constructor_NullClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new OpenAIMetadataEnrichmentService(null, _options, _logger));

        Assert.Equal("openAIClient", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new OpenAIMetadataEnrichmentService(_mockClient.Object, null, _logger));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new OpenAIMetadataEnrichmentService(_mockClient.Object, _options, null));

        Assert.Equal("logger", exception.ParamName);
    }
}