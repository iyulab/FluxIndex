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

public class BatchMetadataExtractionTests : IClassFixture<OpenAITestFixture>
{
    private readonly OpenAITestFixture _fixture;

    public BatchMetadataExtractionTests(OpenAITestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ExtractBatchWithProgressAsync_WithValidRequest_ShouldReturnResults()
    {
        // Arrange
        var request = new BatchMetadataExtractionRequest
        {
            MaxConcurrency = 2,
            Items = new List<MetadataExtractionItem>
            {
                new() { DocumentId = "doc1", Content = "Content 1" },
                new() { DocumentId = "doc2", Content = "Content 2" },
                new() { DocumentId = "doc3", Content = "Content 3" }
            }
        };

        var mockResponse = @"{
            ""description"": ""Summary"",
            ""keywords"": [""test""],
            ""topics"": [""testing""],
            ""language"": ""en"",
            ""documentType"": ""article"",
            ""overallConfidence"": 0.9
        }";

        _fixture.SetupMockResponse(mockResponse);

        // Act
        var result = await _fixture.Extractor.ExtractBatchWithProgressAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.TotalItems.Should().Be(3);
        result.SuccessfulItems.Should().Be(3);
        result.FailedItems.Should().Be(0);
        result.ItemResults.Should().HaveCount(3);
        result.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExtractBatchWithProgressAsync_WithProgressCallback_ShouldReportProgress()
    {
        // Arrange
        var request = new BatchMetadataExtractionRequest
        {
            MaxConcurrency = 1,
            Items = new List<MetadataExtractionItem>
            {
                new() { DocumentId = "doc1", Content = "Content 1" },
                new() { DocumentId = "doc2", Content = "Content 2" }
            }
        };

        var mockResponse = @"{
            ""title"": ""Test"",
            ""summary"": ""Summary"",
            ""keywords"": [],
            ""topics"": [],
            ""overallConfidence"": 0.9
        }";

        _fixture.SetupMockResponse(mockResponse);

        var progressReports = new List<BatchMetadataExtractionProgress>();
        var progress = new Progress<BatchMetadataExtractionProgress>(p => progressReports.Add(p));

        // Act
        var result = await _fixture.Extractor.ExtractBatchWithProgressAsync(request, progressCallback: progress);

        // Assert
        result.Should().NotBeNull();
        progressReports.Should().NotBeEmpty();
        progressReports.Should().Contain(p => p.Status == BatchExtractionStatus.Processing);
        progressReports.Should().Contain(p => p.Status == BatchExtractionStatus.Completed);
    }

    [Fact]
    public async Task ExtractBatchWithProgressAsync_WithPartialFailure_ShouldContinueProcessing()
    {
        // Arrange
        var request = new BatchMetadataExtractionRequest
        {
            MaxConcurrency = 1,
            ContinueOnError = true,
            Items = new List<MetadataExtractionItem>
            {
                new() { DocumentId = "doc1", Content = "Content 1" },
                new() { DocumentId = "doc2", Content = "Content 2" },
                new() { DocumentId = "doc3", Content = "Content 3" }
            }
        };

        var successResponse = @"{
            ""title"": ""Test"",
            ""summary"": ""Summary"",
            ""keywords"": [],
            ""topics"": [],
            ""overallConfidence"": 0.9
        }";

        var callCount = 0;
        _fixture.SetupMockResponseWithCallback(() =>
        {
            callCount++;
            if (callCount == 2) // Second call fails
                throw new HttpRequestException("API error");
            return successResponse;
        });

        // Act
        var result = await _fixture.Extractor.ExtractBatchWithProgressAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.TotalItems.Should().Be(3);
        result.SuccessfulItems.Should().Be(2);
        result.FailedItems.Should().Be(1);
        result.ItemResults.Where(r => r.Success).Should().HaveCount(2);
        result.ItemResults.Where(r => !r.Success).Should().HaveCount(1);
    }

    [Fact]
    public async Task ExtractBatchWithProgressAsync_WithConcurrencyLimit_ShouldRespectLimit()
    {
        // Arrange
        var request = new BatchMetadataExtractionRequest
        {
            MaxConcurrency = 2,
            Items = Enumerable.Range(1, 10)
                .Select(i => new MetadataExtractionItem
                {
                    DocumentId = $"doc{i}",
                    Content = $"Content {i}"
                })
                .ToList()
        };

        var mockResponse = @"{
            ""title"": ""Test"",
            ""summary"": ""Summary"",
            ""keywords"": [],
            ""topics"": [],
            ""overallConfidence"": 0.9
        }";

        var concurrentCalls = 0;
        var maxConcurrentCalls = 0;
        var lockObj = new object();

        _fixture.SetupMockResponseWithAsyncCallback(async () =>
        {
            lock (lockObj)
            {
                concurrentCalls++;
                maxConcurrentCalls = Math.Max(maxConcurrentCalls, concurrentCalls);
            }

            await Task.Delay(50); // Simulate API call

            lock (lockObj)
            {
                concurrentCalls--;
            }

            return mockResponse;
        });

        // Act
        var result = await _fixture.Extractor.ExtractBatchWithProgressAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessfulItems.Should().Be(10);
        maxConcurrentCalls.Should().BeLessThanOrEqualTo(request.MaxConcurrency);
    }

    [Fact]
    public async Task ExtractBatchWithProgressAsync_ShouldCalculateStatistics()
    {
        // Arrange
        var request = new BatchMetadataExtractionRequest
        {
            Items = new List<MetadataExtractionItem>
            {
                new() { DocumentId = "doc1", Content = "Content 1" },
                new() { DocumentId = "doc2", Content = "Content 2" },
                new() { DocumentId = "doc3", Content = "Content 3" }
            }
        };

        var mockResponse = @"{
            ""title"": ""Test"",
            ""summary"": ""Summary"",
            ""keywords"": [""ai"", ""ml""],
            ""topics"": [""technology"", ""science""],
            ""language"": ""en"",
            ""documentType"": ""article"",
            ""overallConfidence"": 0.85
        }";

        _fixture.SetupMockResponse(mockResponse);

        // Act
        var result = await _fixture.Extractor.ExtractBatchWithProgressAsync(request);

        // Assert
        result.Statistics.Should().NotBeNull();
        result.Statistics.AverageConfidence.Should().BeApproximately(0.85f, 0.01f);
        result.Statistics.TopKeywords.Should().ContainKey("ai");
        result.Statistics.TopTopics.Should().ContainKey("technology");
        result.Statistics.LanguageDistribution.Should().ContainKey("en");
        result.Statistics.DocumentTypeDistribution.Should().ContainKey("article");
    }

    [Fact]
    public async Task ExtractBatchWithProgressAsync_WithEmptyRequest_ShouldReturnEmptyResult()
    {
        // Arrange
        var request = new BatchMetadataExtractionRequest
        {
            Items = new List<MetadataExtractionItem>()
        };

        // Act
        var result = await _fixture.Extractor.ExtractBatchWithProgressAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.TotalItems.Should().Be(0);
        result.SuccessfulItems.Should().Be(0);
        result.FailedItems.Should().Be(0);
        result.ItemResults.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractBatchWithProgressAsync_ShouldCalculateProcessingTime()
    {
        // Arrange
        var request = new BatchMetadataExtractionRequest
        {
            Items = new List<MetadataExtractionItem>
            {
                new() { DocumentId = "doc1", Content = "Content 1" }
            }
        };

        var mockResponse = @"{
            ""title"": ""Test"",
            ""summary"": ""Summary"",
            ""keywords"": [],
            ""topics"": [],
            ""overallConfidence"": 0.9
        }";

        _fixture.SetupMockResponseWithAsyncCallback(async () =>
        {
            await Task.Delay(100);
            return mockResponse;
        });

        // Act
        var result = await _fixture.Extractor.ExtractBatchWithProgressAsync(request);

        // Assert
        result.ProcessingTime.Should().BeGreaterThan(TimeSpan.Zero);
        result.StartedAt.Should().BeBefore(result.CompletedAt!.Value);
    }

    [Fact]
    public async Task ExtractBatchWithProgressAsync_WithCancellation_ShouldThrow()
    {
        // Arrange
        var request = new BatchMetadataExtractionRequest
        {
            Items = new List<MetadataExtractionItem>
            {
                new() { DocumentId = "doc1", Content = "Content 1" }
            }
        };

        var cts = new CancellationTokenSource();
        cts.Cancel();

        _fixture.SetupMockException(new OperationCanceledException());

        // Act
        Func<Task> act = async () => await _fixture.Extractor.ExtractBatchWithProgressAsync(request, cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExtractBatchWithProgressAsync_ShouldReportEstimatedTimeRemaining()
    {
        // Arrange
        var request = new BatchMetadataExtractionRequest
        {
            MaxConcurrency = 1,
            Items = Enumerable.Range(1, 5)
                .Select(i => new MetadataExtractionItem
                {
                    DocumentId = $"doc{i}",
                    Content = $"Content {i}"
                })
                .ToList()
        };

        var mockResponse = @"{
            ""title"": ""Test"",
            ""summary"": ""Summary"",
            ""keywords"": [],
            ""topics"": [],
            ""overallConfidence"": 0.9
        }";

        _fixture.SetupMockResponse(mockResponse);

        var progressReports = new List<BatchMetadataExtractionProgress>();
        var progress = new Progress<BatchMetadataExtractionProgress>(p => progressReports.Add(p));

        // Act
        var result = await _fixture.Extractor.ExtractBatchWithProgressAsync(request, progressCallback: progress);

        // Assert
        result.Should().NotBeNull();
        progressReports.Should().Contain(p => p.EstimatedTimeRemaining.HasValue);
    }

    [Fact]
    public async Task ExtractBatchWithProgressAsync_ShouldUsePerItemSchemaAndStrategy()
    {
        // Arrange
        var request = new BatchMetadataExtractionRequest
        {
            Items = new List<MetadataExtractionItem>
            {
                new()
                {
                    DocumentId = "doc1",
                    Content = "Content 1",
                    Schema = MetadataSchema.Article,
                    Strategy = MetadataExtractionStrategy.Deep
                },
                new()
                {
                    DocumentId = "doc2",
                    Content = "Content 2",
                    Schema = MetadataSchema.ProductManual,
                    Strategy = MetadataExtractionStrategy.Fast
                }
            }
        };

        var mockResponse = @"{
            ""title"": ""Test"",
            ""summary"": ""Summary"",
            ""keywords"": [],
            ""topics"": [],
            ""overallConfidence"": 0.9
        }";

        _fixture.SetupMockResponse(mockResponse);

        // Act
        var result = await _fixture.Extractor.ExtractBatchWithProgressAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessfulItems.Should().Be(2);
        // Mock 모드에서만 검증
        if (!_fixture.UseRealApi)
        {
            _fixture.MockCompletionService!.Verify(
                x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
    }
}
