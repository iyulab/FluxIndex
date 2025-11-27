using FluentAssertions;
using FluxIndex.Extensions.FluxImprover.Adapters;
using FluxIndex.Extensions.FluxImprover.Services;
using FluxImprover.Enrichment;
using FluxImprover.Abstractions.Models;
using FluxImprover.Abstractions.Options;
using Moq;
using Xunit;
using FluxIndexChunk = FluxIndex.Core.Application.Interfaces.IEnrichedChunk;
using FluxIndexSource = FluxIndex.Core.Application.Interfaces.ISourceMetadata;
using FluxImproverEnrichedChunk = FluxImprover.Abstractions.Models.IEnrichedChunk;

namespace FluxIndex.Extensions.FluxImprover.Tests.Services;

/// <summary>
/// Tests for ChunkEnrichmentServiceWrapper - wraps FluxImprover's ChunkEnrichmentService for use with FluxIndex chunks
/// </summary>
public class ChunkEnrichmentServiceWrapperTests
{
    private readonly Mock<ISummarizationService> _mockSummarizationService;
    private readonly Mock<IKeywordExtractionService> _mockKeywordExtractionService;
    private readonly ChunkEnrichmentService _enrichmentService;
    private readonly ChunkEnrichmentServiceWrapper _wrapper;

    public ChunkEnrichmentServiceWrapperTests()
    {
        _mockSummarizationService = new Mock<ISummarizationService>();
        _mockKeywordExtractionService = new Mock<IKeywordExtractionService>();

        _enrichmentService = new ChunkEnrichmentService(
            _mockSummarizationService.Object,
            _mockKeywordExtractionService.Object);

        _wrapper = new ChunkEnrichmentServiceWrapper(_enrichmentService);
    }

    [Fact]
    public async Task EnrichAsync_ReturnsEnrichedChunkWithSummaryAndKeywords()
    {
        // Arrange
        var fluxIndexChunk = CreateMockFluxIndexChunk("chunk-001", "Test content for enrichment.");

        _mockSummarizationService
            .Setup(s => s.SummarizeAsync(
                It.IsAny<string>(),
                It.IsAny<EnrichmentOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("This is a summary of the test content.");

        _mockKeywordExtractionService
            .Setup(s => s.ExtractKeywordsAsync(
                It.IsAny<string>(),
                It.IsAny<EnrichmentOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "test", "content", "enrichment" });

        // Act
        var result = await _wrapper.EnrichAsync(fluxIndexChunk);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("chunk-001");
        result.Text.Should().Be("Test content for enrichment.");
        result.Summary.Should().Be("This is a summary of the test content.");
        result.Keywords.Should().BeEquivalentTo(new[] { "test", "content", "enrichment" });
    }

    [Fact]
    public async Task EnrichAsync_WithOptions_PassesOptionsToUnderlyingService()
    {
        // Arrange
        var fluxIndexChunk = CreateMockFluxIndexChunk("chunk-001", "Test content.");
        var options = new EnrichmentOptions
        {
            MaxKeywords = 5,
            MaxSummaryLength = 100
        };

        EnrichmentOptions? capturedOptions = null;
        _mockSummarizationService
            .Setup(s => s.SummarizeAsync(
                It.IsAny<string>(),
                It.IsAny<EnrichmentOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, EnrichmentOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync("Summary");

        _mockKeywordExtractionService
            .Setup(s => s.ExtractKeywordsAsync(
                It.IsAny<string>(),
                It.IsAny<EnrichmentOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        await _wrapper.EnrichAsync(fluxIndexChunk, options);

        // Assert
        capturedOptions.Should().NotBeNull();
        capturedOptions!.MaxKeywords.Should().Be(5);
        capturedOptions.MaxSummaryLength.Should().Be(100);
    }

    [Fact]
    public async Task EnrichAsync_PreservesFluxIndexMetadataInResult()
    {
        // Arrange
        var fluxIndexChunk = CreateMockFluxIndexChunk("chunk-001", "Test content.");

        _mockSummarizationService
            .Setup(s => s.SummarizeAsync(
                It.IsAny<string>(),
                It.IsAny<EnrichmentOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Summary");

        _mockKeywordExtractionService
            .Setup(s => s.ExtractKeywordsAsync(
                It.IsAny<string>(),
                It.IsAny<EnrichmentOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _wrapper.EnrichAsync(fluxIndexChunk);

        // Assert
        result.Metadata.Should().NotBeNull();
        result.Metadata!["Quality"].Should().Be(0.85);
        result.Metadata["ContextDependency"].Should().Be(0.3);
    }

    [Fact]
    public async Task EnrichBatchAsync_EnrichesAllChunks()
    {
        // Arrange
        var chunks = new List<FluxIndexChunk>
        {
            CreateMockFluxIndexChunk("chunk-001", "Content 1"),
            CreateMockFluxIndexChunk("chunk-002", "Content 2"),
            CreateMockFluxIndexChunk("chunk-003", "Content 3")
        };

        _mockSummarizationService
            .Setup(s => s.SummarizeAsync(
                It.IsAny<string>(),
                It.IsAny<EnrichmentOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Summary");

        _mockKeywordExtractionService
            .Setup(s => s.ExtractKeywordsAsync(
                It.IsAny<string>(),
                It.IsAny<EnrichmentOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "keyword" });

        // Act
        var results = await _wrapper.EnrichBatchAsync(chunks);

        // Assert
        results.Should().HaveCount(3);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { "chunk-001", "chunk-002", "chunk-003" });
    }

    [Fact]
    public void Constructor_WithNullService_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new ChunkEnrichmentServiceWrapper(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("enrichmentService");
    }

    private static FluxIndexChunk CreateMockFluxIndexChunk(string chunkId, string content)
    {
        var mockSource = new Mock<FluxIndexSource>();
        mockSource.Setup(s => s.SourceId).Returns("doc-123");
        mockSource.Setup(s => s.Title).Returns("Test Document");
        mockSource.Setup(s => s.Language).Returns("en");
        mockSource.Setup(s => s.SourceType).Returns("pdf");
        mockSource.Setup(s => s.WordCount).Returns(100);

        var mockChunk = new Mock<FluxIndexChunk>();
        mockChunk.Setup(c => c.ChunkId).Returns(chunkId);
        mockChunk.Setup(c => c.Content).Returns(content);
        mockChunk.Setup(c => c.ChunkIndex).Returns(0);
        mockChunk.Setup(c => c.HeadingPath).Returns(new List<string> { "Section 1" });
        mockChunk.Setup(c => c.SectionTitle).Returns("Section 1");
        mockChunk.Setup(c => c.Quality).Returns(0.85);
        mockChunk.Setup(c => c.ContextDependency).Returns(0.3);
        mockChunk.Setup(c => c.TokenCount).Returns(50);
        mockChunk.Setup(c => c.Source).Returns(mockSource.Object);

        return mockChunk.Object;
    }
}
