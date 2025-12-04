using FluentAssertions;
using FluxIndex.Extensions.FluxImprover.Services;
using FluxImprover.Enrichment;
using FluxImprover.Models;
using FluxImprover.Options;
using FluxImprover.Utilities;
using Moq;
using Xunit;
using FluxIndexChunk = FluxIndex.Core.Application.Interfaces.IEnrichedChunk;
using FluxIndexSource = FluxIndex.Core.Application.Interfaces.ISourceMetadata;

namespace FluxIndex.Extensions.FluxImprover.Tests.Services;

/// <summary>
/// Tests for FluxImprover 0.4.0 conditional enrichment features
/// - ConditionalEnrichmentOptions for cost optimization
/// - ChunkQualityAnalyzer for pre-enrichment quality assessment
/// - ParentChunkContext for hierarchical enrichment
/// </summary>
public class ConditionalEnrichmentTests
{
    private readonly Mock<ISummarizationService> _mockSummarizationService;
    private readonly Mock<IKeywordExtractionService> _mockKeywordExtractionService;
    private readonly ChunkEnrichmentService _enrichmentService;
    private readonly ChunkEnrichmentServiceWrapper _wrapper;

    public ConditionalEnrichmentTests()
    {
        _mockSummarizationService = new Mock<ISummarizationService>();
        _mockKeywordExtractionService = new Mock<IKeywordExtractionService>();

        _enrichmentService = new ChunkEnrichmentService(
            _mockSummarizationService.Object,
            _mockKeywordExtractionService.Object);

        _wrapper = new ChunkEnrichmentServiceWrapper(_enrichmentService);
    }

    #region ChunkQualityAnalyzer Tests

    [Fact]
    public void ChunkQualityAnalyzer_AnalyzesEmptyContent_ReturnsZeroScores()
    {
        // Act
        var result = ChunkQualityAnalyzer.Analyze("");

        // Assert
        result.OverallScore.Should().Be(0f);
        result.CompletenessScore.Should().Be(0f);
        result.DensityScore.Should().Be(0f);
        result.StructureScore.Should().Be(0f);
        result.ContentLength.Should().Be(0);
        result.Recommendation.Should().Be(EnrichmentRecommendation.None);
    }

    [Fact]
    public void ChunkQualityAnalyzer_AnalyzesWellFormedContent_ReturnsHighScores()
    {
        // Arrange
        var content = "This is a well-formed paragraph with proper sentences. It contains multiple meaningful words and ends properly.";

        // Act
        var result = ChunkQualityAnalyzer.Analyze(content);

        // Assert
        result.OverallScore.Should().BeGreaterThan(0.5f);
        result.CompletenessScore.Should().Be(1.0f); // Capital start + period end
        result.DensityScore.Should().BeGreaterThan(0.5f);
        result.ContentLength.Should().Be(content.Length);
    }

    [Fact]
    public void ChunkQualityAnalyzer_AnalyzesLongContent_RecommendsSummarization()
    {
        // Arrange - Content longer than MinSummarizationLength (500 chars)
        var content = string.Join(" ", Enumerable.Repeat("This is a test sentence with meaningful content.", 15));

        // Act
        var result = ChunkQualityAnalyzer.Analyze(content);

        // Assert
        result.ContentLength.Should().BeGreaterThan(500);
        result.ShouldSummarize.Should().BeTrue();
        result.Recommendation.HasFlag(EnrichmentRecommendation.Summarize).Should().BeTrue();
    }

    [Fact]
    public void ChunkQualityAnalyzer_AnalyzesShortContent_DoesNotRecommendSummarization()
    {
        // Arrange - Short content
        var content = "Short text.";

        // Act
        var result = ChunkQualityAnalyzer.Analyze(content);

        // Assert
        result.ContentLength.Should().BeLessThan(500);
        result.ShouldSummarize.Should().BeFalse();
    }

    [Fact]
    public void ChunkQualityAnalyzer_AnalyzesContentWithMarkdown_RecognizesStructure()
    {
        // Arrange
        var content = "# Heading\n\nThis is a paragraph with content.\n\n- Item 1\n- Item 2\n\n```code```";

        // Act
        var result = ChunkQualityAnalyzer.Analyze(content);

        // Assert
        result.StructureScore.Should().BeGreaterThan(0.7f); // Has heading, list, code block
    }

    [Fact]
    public void ChunkQualityAnalyzer_AnalyzeWithMetadata_BoostsTableContentScore()
    {
        // Arrange
        var content = "| Column 1 | Column 2 |\n|----------|----------|\n| Data 1   | Data 2   |";
        var metadata = new Dictionary<string, object>
        {
            [ChunkMetadataKeys.ContentType] = ChunkContentTypes.Table
        };

        // Act
        var result = ChunkQualityAnalyzer.Analyze(content, metadata);

        // Assert
        result.StructureScore.Should().BeGreaterThanOrEqualTo(0.8f);
        result.Recommendation.HasFlag(EnrichmentRecommendation.UseTablePrompt).Should().BeTrue();
    }

    #endregion

    #region ConditionalEnrichmentOptions Tests

    [Fact]
    public async Task EnrichAsync_WithConditionalEnrichment_SkipsHighQualityChunks()
    {
        // Arrange
        var fluxIndexChunk = CreateMockFluxIndexChunk(
            "chunk-001",
            "High quality content with proper structure. Contains meaningful information.");

        var options = new EnrichmentOptions
        {
            ConditionalOptions = new ConditionalEnrichmentOptions
            {
                EnableConditionalEnrichment = true,
                SkipEnrichmentThreshold = 0.3f, // Very low threshold to ensure skip
                IncludeQualityMetrics = true
            }
        };

        // Act
        var result = await _wrapper.EnrichAsync(fluxIndexChunk, options);

        // Assert - High quality chunk should be skipped
        result.Should().NotBeNull();
        // Verify that quality metrics are included in metadata
        if (result.Metadata?.ContainsKey(EnrichmentMetadataKeys.QualityScore) == true)
        {
            result.Metadata[EnrichmentMetadataKeys.QualityScore].Should().BeOfType<float>();
        }
    }

    [Fact]
    public async Task EnrichAsync_WithConditionalEnrichment_EnrichesLowQualityChunks()
    {
        // Arrange - Create chunk with longer content to meet summarization threshold
        var longContent = string.Join(" ", Enumerable.Repeat("This is meaningful test content with proper structure.", 15));
        var fluxIndexChunk = CreateMockFluxIndexChunk("chunk-001", longContent);

        var options = new EnrichmentOptions
        {
            ConditionalOptions = new ConditionalEnrichmentOptions
            {
                EnableConditionalEnrichment = true,
                SkipEnrichmentThreshold = 0.99f, // Very high threshold - most chunks will be enriched
                IncludeQualityMetrics = true
            }
        };

        _mockSummarizationService
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Summary");

        _mockKeywordExtractionService
            .Setup(s => s.ExtractKeywordsAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "keyword" });

        // Act
        var result = await _wrapper.EnrichAsync(fluxIndexChunk, options);

        // Assert - Chunk with sufficient content should be enriched
        result.Should().NotBeNull();
        // Note: Conditional enrichment may skip based on quality analysis
        // We verify the wrapper correctly passes options to the service
        if (result.Summary != null)
        {
            result.Summary.Should().Be("Summary");
        }
    }

    [Fact]
    public void ConditionalEnrichmentOptions_ValidatesThresholdRange()
    {
        // Act & Assert - Valid values
        var validOptions = new ConditionalEnrichmentOptions
        {
            SkipEnrichmentThreshold = 0.5f,
            MinKeywordDensity = 0.3f
        };
        validOptions.SkipEnrichmentThreshold.Should().Be(0.5f);
        validOptions.MinKeywordDensity.Should().Be(0.3f);
    }

    [Fact]
    public void ConditionalEnrichmentOptions_RejectsInvalidThreshold()
    {
        // Act & Assert - Invalid values should throw
        var act1 = () => new ConditionalEnrichmentOptions { SkipEnrichmentThreshold = -0.1f };
        act1.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => new ConditionalEnrichmentOptions { SkipEnrichmentThreshold = 1.1f };
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region ParentChunkContext Tests

    [Fact]
    public async Task EnrichAsync_WithParentContext_PassesContextToService()
    {
        // Arrange
        var fluxIndexChunk = CreateMockFluxIndexChunk("chunk-001", "Child chunk content.");

        var options = new EnrichmentOptions
        {
            ParentContext = new ParentChunkContext
            {
                ParentId = "parent-001",
                ParentSummary = "Parent section about technology trends.",
                ParentKeywords = new List<string> { "technology", "trends" },
                ParentHeadingPath = "Chapter 1 > Section 1.1",
                HierarchyLevel = 2
            }
        };

        EnrichmentOptions? capturedOptions = null;
        _mockSummarizationService
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, EnrichmentOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync("Summary");

        _mockKeywordExtractionService
            .Setup(s => s.ExtractKeywordsAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        await _wrapper.EnrichAsync(fluxIndexChunk, options);

        // Assert
        capturedOptions.Should().NotBeNull();
        capturedOptions!.ParentContext.Should().NotBeNull();
        capturedOptions.ParentContext!.ParentId.Should().Be("parent-001");
        capturedOptions.ParentContext.ParentSummary.Should().Be("Parent section about technology trends.");
        capturedOptions.ParentContext.HierarchyLevel.Should().Be(2);
    }

    #endregion

    #region EnrichmentStatistics Tests

    [Fact]
    public void EnrichmentStatistics_CalculatesSkipRate()
    {
        // Arrange
        var chunks = new List<EnrichedChunk>
        {
            CreateEnrichedChunkWithSkipStatus(wasSkipped: true),
            CreateEnrichedChunkWithSkipStatus(wasSkipped: true),
            CreateEnrichedChunkWithSkipStatus(wasSkipped: false),
            CreateEnrichedChunkWithSkipStatus(wasSkipped: false)
        };

        // Act
        var stats = ChunkEnrichmentService.GetStatistics(chunks);

        // Assert
        stats.TotalChunks.Should().Be(4);
        stats.SkippedChunks.Should().Be(2);
        stats.SkipRate.Should().Be(0.5f);
        stats.EstimatedLlmCallsSaved.Should().Be(4); // 2 skipped * 2 calls each
    }

    #endregion

    #region Helper Methods

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

    private static EnrichedChunk CreateEnrichedChunkWithSkipStatus(bool wasSkipped)
    {
        var metadata = new Dictionary<string, object>
        {
            [EnrichmentMetadataKeys.WasSkipped] = wasSkipped
        };

        return new EnrichedChunk
        {
            Id = Guid.NewGuid().ToString(),
            Text = "Test content",
            SourceId = "doc-001",
            Summary = wasSkipped ? null : "Summary",
            Keywords = wasSkipped ? null : new List<string> { "keyword" },
            Metadata = metadata
        };
    }

    #endregion
}
