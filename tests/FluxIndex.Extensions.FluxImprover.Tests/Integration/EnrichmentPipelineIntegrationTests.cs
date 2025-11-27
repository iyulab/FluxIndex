using FluentAssertions;
using FluxIndex.Extensions.FluxImprover.Adapters;
using FluxIndex.Extensions.FluxImprover.Services;
using FluxImprover.Enrichment;
using FluxImprover.Abstractions.Options;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using FluxIndexChunk = FluxIndex.Core.Application.Interfaces.IEnrichedChunk;
using FluxIndexSource = FluxIndex.Core.Application.Interfaces.ISourceMetadata;
using FluxIndexCompletion = FluxIndex.Core.Application.Interfaces.ITextCompletionService;
using FluxImproverCompletion = FluxImprover.Abstractions.Services.ITextCompletionService;

namespace FluxIndex.Extensions.FluxImprover.Tests.Integration;

/// <summary>
/// Integration tests for the complete enrichment pipeline from FluxIndex chunks to enriched results.
/// Tests the full flow: FluxIndex chunk → Adapter → FluxImprover service → Enriched result.
/// </summary>
public class EnrichmentPipelineIntegrationTests
{
    private readonly Mock<ISummarizationService> _mockSummarizationService;
    private readonly Mock<IKeywordExtractionService> _mockKeywordExtractionService;
    private readonly Mock<FluxIndexCompletion> _mockFluxIndexCompletion;

    public EnrichmentPipelineIntegrationTests()
    {
        _mockSummarizationService = new Mock<ISummarizationService>();
        _mockKeywordExtractionService = new Mock<IKeywordExtractionService>();
        _mockFluxIndexCompletion = new Mock<FluxIndexCompletion>();
    }

    [Fact]
    public async Task FullPipeline_EnrichesFluxIndexChunk_WithDIContainer()
    {
        // Arrange - Setup DI container with all services
        var services = new ServiceCollection();

        // Register FluxIndex core service
        services.AddSingleton(_mockFluxIndexCompletion.Object);

        // Register FluxImprover underlying services
        _mockSummarizationService
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("AI-generated summary of the content.");

        _mockKeywordExtractionService
            .Setup(s => s.ExtractKeywordsAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "integration", "testing", "pipeline" });

        services.AddSingleton(_mockSummarizationService.Object);
        services.AddSingleton(_mockKeywordExtractionService.Object);
        services.AddSingleton<ChunkEnrichmentService>();

        // Register FluxImprover integration
        services.AddFluxImproverTextCompletion();
        services.AddChunkEnrichmentWrapper();

        var provider = services.BuildServiceProvider();

        // Get the wrapper from DI
        var wrapper = provider.GetRequiredService<ChunkEnrichmentServiceWrapper>();

        // Create a FluxIndex chunk
        var fluxIndexChunk = CreateMockFluxIndexChunk("integration-001", "This is test content for the integration pipeline.");

        // Act
        var result = await wrapper.EnrichAsync(fluxIndexChunk);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("integration-001");
        result.Text.Should().Be("This is test content for the integration pipeline.");
        result.Summary.Should().Be("AI-generated summary of the content.");
        result.Keywords.Should().BeEquivalentTo(new[] { "integration", "testing", "pipeline" });
    }

    [Fact]
    public async Task FullPipeline_PreservesMetadataThroughEnrichment()
    {
        // Arrange
        var enrichmentService = new ChunkEnrichmentService(
            _mockSummarizationService.Object,
            _mockKeywordExtractionService.Object);

        var wrapper = new ChunkEnrichmentServiceWrapper(enrichmentService);

        _mockSummarizationService
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Summary");

        _mockKeywordExtractionService
            .Setup(s => s.ExtractKeywordsAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var fluxIndexChunk = CreateMockFluxIndexChunk("metadata-001", "Content with metadata.");

        // Act
        var result = await wrapper.EnrichAsync(fluxIndexChunk);

        // Assert - metadata should be preserved
        result.Metadata.Should().NotBeNull();
        result.Metadata!["Quality"].Should().Be(0.92);
        result.Metadata["ContextDependency"].Should().Be(0.15);
        result.SourceId.Should().Be("doc-456");
        result.HeadingPath.Should().Be("Chapter 1 > Section A");
    }

    [Fact]
    public async Task FullPipeline_BatchEnrichment_ProcessesAllChunks()
    {
        // Arrange
        var enrichmentService = new ChunkEnrichmentService(
            _mockSummarizationService.Object,
            _mockKeywordExtractionService.Object);

        var wrapper = new ChunkEnrichmentServiceWrapper(enrichmentService);

        var callCount = 0;
        _mockSummarizationService
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"Summary {++callCount}");

        _mockKeywordExtractionService
            .Setup(s => s.ExtractKeywordsAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "keyword" });

        var chunks = new List<FluxIndexChunk>
        {
            CreateMockFluxIndexChunk("batch-001", "Content 1"),
            CreateMockFluxIndexChunk("batch-002", "Content 2"),
            CreateMockFluxIndexChunk("batch-003", "Content 3"),
            CreateMockFluxIndexChunk("batch-004", "Content 4"),
            CreateMockFluxIndexChunk("batch-005", "Content 5")
        };

        // Act
        var results = await wrapper.EnrichBatchAsync(chunks);

        // Assert
        results.Should().HaveCount(5);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { "batch-001", "batch-002", "batch-003", "batch-004", "batch-005" });

        // Verify all were summarized
        _mockSummarizationService.Verify(
            s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()),
            Times.Exactly(5));
    }

    [Fact]
    public async Task FullPipeline_WithCustomOptions_AppliesOptionsCorrectly()
    {
        // Arrange
        var enrichmentService = new ChunkEnrichmentService(
            _mockSummarizationService.Object,
            _mockKeywordExtractionService.Object);

        var wrapper = new ChunkEnrichmentServiceWrapper(enrichmentService);

        EnrichmentOptions? capturedOptions = null;
        _mockSummarizationService
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, EnrichmentOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync("Summarized");

        _mockKeywordExtractionService
            .Setup(s => s.ExtractKeywordsAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var fluxIndexChunk = CreateMockFluxIndexChunk("options-001", "Content to summarize.");
        var options = new EnrichmentOptions
        {
            MaxKeywords = 10,
            MaxSummaryLength = 200
        };

        // Act
        await wrapper.EnrichAsync(fluxIndexChunk, options);

        // Assert
        capturedOptions.Should().NotBeNull();
        capturedOptions!.MaxKeywords.Should().Be(10);
        capturedOptions.MaxSummaryLength.Should().Be(200);
    }

    [Fact]
    public async Task FullPipeline_CancellationSupported()
    {
        // Arrange
        var enrichmentService = new ChunkEnrichmentService(
            _mockSummarizationService.Object,
            _mockKeywordExtractionService.Object);

        var wrapper = new ChunkEnrichmentServiceWrapper(enrichmentService);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockSummarizationService
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var fluxIndexChunk = CreateMockFluxIndexChunk("cancel-001", "Content");

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => wrapper.EnrichAsync(fluxIndexChunk, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task FullPipeline_TextCompletionAdapter_WorksWithFluxImproverServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Setup FluxIndex text completion service (uses GenerateCompletionAsync)
        _mockFluxIndexCompletion
            .Setup(s => s.GenerateCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Generated text completion response.");

        services.AddSingleton(_mockFluxIndexCompletion.Object);
        services.AddFluxImproverTextCompletion();

        var provider = services.BuildServiceProvider();
        var fluxImproverCompletion = provider.GetRequiredService<FluxImproverCompletion>();

        // Act - FluxImprover uses CompleteAsync which is adapted to GenerateCompletionAsync
        var result = await fluxImproverCompletion.CompleteAsync("Test prompt");

        // Assert
        result.Should().Be("Generated text completion response.");
        _mockFluxIndexCompletion.Verify(
            s => s.GenerateCompletionAsync(
                "Test prompt",
                It.IsAny<int>(),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void AdapterChain_EnrichedChunkAdapter_CorrectlyMapsAllProperties()
    {
        // Arrange - Create a detailed FluxIndex chunk
        var mockSource = new Mock<FluxIndexSource>();
        mockSource.Setup(s => s.SourceId).Returns("source-detailed-001");
        mockSource.Setup(s => s.Title).Returns("Detailed Test Document");
        mockSource.Setup(s => s.Language).Returns("ko");
        mockSource.Setup(s => s.SourceType).Returns("docx");
        mockSource.Setup(s => s.WordCount).Returns(500);

        var mockChunk = new Mock<FluxIndexChunk>();
        mockChunk.Setup(c => c.ChunkId).Returns("chunk-detailed-001");
        mockChunk.Setup(c => c.Content).Returns("This is detailed content for testing adapter chain.");
        mockChunk.Setup(c => c.ChunkIndex).Returns(5);
        mockChunk.Setup(c => c.HeadingPath).Returns(new List<string> { "Part I", "Chapter 2", "Section 3" });
        mockChunk.Setup(c => c.SectionTitle).Returns("Section 3");
        mockChunk.Setup(c => c.Quality).Returns(0.95);
        mockChunk.Setup(c => c.ContextDependency).Returns(0.1);
        mockChunk.Setup(c => c.TokenCount).Returns(150);
        mockChunk.Setup(c => c.Source).Returns(mockSource.Object);

        // Act
        var adapter = new EnrichedChunkAdapter(mockChunk.Object);

        // Assert - verify all mappings
        adapter.Id.Should().Be("chunk-detailed-001");
        adapter.Text.Should().Be("This is detailed content for testing adapter chain.");
        adapter.SourceId.Should().Be("source-detailed-001");
        adapter.HeadingPath.Should().Be("Part I > Chapter 2 > Section 3");
        adapter.Summary.Should().BeNull(); // Not yet enriched
        adapter.Keywords.Should().BeNull(); // Not yet enriched

        // Verify metadata preservation
        adapter.Metadata.Should().NotBeNull();
        adapter.Metadata!["Quality"].Should().Be(0.95);
        adapter.Metadata["ContextDependency"].Should().Be(0.1);
        adapter.Metadata["TokenCount"].Should().Be(150);
        adapter.Metadata["ChunkIndex"].Should().Be(5);
        adapter.Metadata["SectionTitle"].Should().Be("Section 3");

        // Verify underlying chunk access
        adapter.UnderlyingChunk.Should().BeSameAs(mockChunk.Object);
    }

    private static FluxIndexChunk CreateMockFluxIndexChunk(string chunkId, string content)
    {
        var mockSource = new Mock<FluxIndexSource>();
        mockSource.Setup(s => s.SourceId).Returns("doc-456");
        mockSource.Setup(s => s.Title).Returns("Test Document");
        mockSource.Setup(s => s.Language).Returns("en");
        mockSource.Setup(s => s.SourceType).Returns("pdf");
        mockSource.Setup(s => s.WordCount).Returns(200);

        var mockChunk = new Mock<FluxIndexChunk>();
        mockChunk.Setup(c => c.ChunkId).Returns(chunkId);
        mockChunk.Setup(c => c.Content).Returns(content);
        mockChunk.Setup(c => c.ChunkIndex).Returns(0);
        mockChunk.Setup(c => c.HeadingPath).Returns(new List<string> { "Chapter 1", "Section A" });
        mockChunk.Setup(c => c.SectionTitle).Returns("Section A");
        mockChunk.Setup(c => c.Quality).Returns(0.92);
        mockChunk.Setup(c => c.ContextDependency).Returns(0.15);
        mockChunk.Setup(c => c.TokenCount).Returns(75);
        mockChunk.Setup(c => c.Source).Returns(mockSource.Object);

        return mockChunk.Object;
    }
}
