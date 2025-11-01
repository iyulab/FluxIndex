using FluxIndex.Core.Models;
using FluxIndex.Core.Services;
using FluentAssertions;
using Xunit;

namespace FluxIndex.Tests.Core.Services;

public class RuleBasedMetadataExtractorTests
{
    private readonly RuleBasedMetadataExtractor _extractor;

    public RuleBasedMetadataExtractorTests()
    {
        _extractor = new RuleBasedMetadataExtractor();
    }

    [Fact]
    public async Task ExtractAsync_WithSimpleContent_ShouldExtractBasicMetadata()
    {
        // Arrange
        var content = "This is a test document about machine learning and artificial intelligence.";

        // Act
        var metadata = await _extractor.ExtractAsync(content, MetadataSchema.General);

        // Assert
        metadata.Should().NotBeNull();
        metadata.Keywords.Should().NotBeEmpty();
        metadata.ExtractionMethod.Should().Be("RuleBased");
        metadata.ExtractedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ExtractAsync_WithEmptyContent_ShouldReturnEmptyMetadata()
    {
        // Arrange
        var content = string.Empty;

        // Act
        var metadata = await _extractor.ExtractAsync(content, MetadataSchema.General);

        // Assert
        metadata.Should().NotBeNull();
        metadata.Keywords.Should().BeEmpty();
        metadata.Topics.Should().BeEmpty();
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
    public async Task ExtractAsync_WithLongContent_ShouldExtractTopKeywords()
    {
        // Arrange
        var content = @"
            Machine learning is a subset of artificial intelligence.
            Machine learning algorithms build models based on sample data.
            Artificial intelligence and machine learning are transforming technology.
            Deep learning is a subset of machine learning.
            Neural networks are fundamental to deep learning.
        ";

        // Act
        var metadata = await _extractor.ExtractAsync(content, MetadataSchema.General);

        // Assert
        metadata.Keywords.Should().Contain(k => k.Contains("machine") || k.Contains("learning"));
        metadata.Keywords.Count.Should().BeLessOrEqualTo(20); // Top 20 keywords limit
    }

    [Fact]
    public async Task ExtractAsync_GeneralSchema_ShouldUseGeneralPatterns()
    {
        // Arrange
        var content = "This is a general document about technology.";

        // Act
        var metadata = await _extractor.ExtractAsync(content, MetadataSchema.General);

        // Assert
        metadata.Should().NotBeNull();
        metadata.ExtractionMethod.Should().Be("RuleBased");
    }

    [Fact]
    public async Task ExtractAsync_ProductManualSchema_ShouldExtractProductInfo()
    {
        // Arrange
        var content = @"
            iPhone 15 Pro User Manual
            Manufacturer: Apple Inc.
            Model: iPhone 15 Pro
            Version: iOS 17
        ";

        // Act
        var metadata = await _extractor.ExtractAsync(content, MetadataSchema.ProductManual);

        // Assert
        metadata.Should().NotBeNull();
        metadata.Keywords.Should().Contain(k => k.ToLower().Contains("iphone") || k.ToLower().Contains("apple"));
    }

    [Fact]
    public async Task ExtractAsync_ArticleSchema_ShouldExtractArticleMetadata()
    {
        // Arrange
        var content = @"
            The Future of AI
            By John Doe
            Published: 2024-01-15

            This article discusses the future trends in artificial intelligence.
        ";

        // Act
        var metadata = await _extractor.ExtractAsync(content, MetadataSchema.Article);

        // Assert
        metadata.Should().NotBeNull();
        metadata.Keywords.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_TechnicalDocSchema_ShouldExtractTechnicalInfo()
    {
        // Arrange
        var content = @"
            REST API Documentation v2.0
            Framework: .NET 9.0
            Language: C#

            This documentation covers the REST API endpoints.
        ";

        // Act
        var metadata = await _extractor.ExtractAsync(content, MetadataSchema.TechnicalDoc);

        // Assert
        metadata.Should().NotBeNull();
        metadata.Keywords.Should().Contain(k => k.ToLower().Contains("api") || k.ToLower().Contains("net"));
    }

    [Fact]
    public async Task ExtractAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        var content = "Test content";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await _extractor.ExtractAsync(content, MetadataSchema.General, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void MergeMetadata_WithBothMetadata_ShouldMergeProperly()
    {
        // Arrange
        var primary = new ExtractedMetadata
        {
            Title = "Primary Title",
            Summary = "Primary Summary",
            Keywords = new List<string> { "keyword1", "keyword2" },
            Topics = new List<string> { "topic1" },
            OverallConfidence = 0.9f
        };

        var fallback = new ExtractedMetadata
        {
            Title = "Fallback Title",
            Summary = "Fallback Summary",
            Keywords = new List<string> { "keyword3", "keyword4" },
            Topics = new List<string> { "topic2" },
            Language = "en",
            DocumentType = "article"
        };

        // Act
        var merged = _extractor.MergeMetadata(primary, fallback);

        // Assert
        merged.Title.Should().Be("Primary Title"); // Primary takes precedence
        merged.Summary.Should().Be("Primary Summary");
        merged.Keywords.Should().Contain("keyword1");
        merged.Keywords.Should().Contain("keyword3"); // Merged from both
        merged.Topics.Should().Contain("topic1");
        merged.Topics.Should().Contain("topic2");
        merged.Language.Should().Be("en"); // From fallback when primary is null
    }

    [Fact]
    public void MergeMetadata_WithNullPrimaryTitle_ShouldUseFallbackTitle()
    {
        // Arrange
        var primary = new ExtractedMetadata
        {
            Title = null,
            OverallConfidence = 0.9f
        };

        var fallback = new ExtractedMetadata
        {
            Title = "Fallback Title"
        };

        // Act
        var merged = _extractor.MergeMetadata(primary, fallback);

        // Assert
        merged.Title.Should().Be("Fallback Title");
    }

    [Fact]
    public void MergeMetadata_WithEmptyPrimarySummary_ShouldUseFallbackSummary()
    {
        // Arrange
        var primary = new ExtractedMetadata
        {
            Summary = ""
        };

        var fallback = new ExtractedMetadata
        {
            Summary = "Fallback Summary"
        };

        // Act
        var merged = _extractor.MergeMetadata(primary, fallback);

        // Assert
        merged.Summary.Should().Be("Fallback Summary");
    }

    [Fact]
    public void MergeMetadata_ShouldCombineKeywordsWithoutDuplicates()
    {
        // Arrange
        var primary = new ExtractedMetadata
        {
            Keywords = new List<string> { "keyword1", "keyword2", "shared" }
        };

        var fallback = new ExtractedMetadata
        {
            Keywords = new List<string> { "keyword3", "shared" }
        };

        // Act
        var merged = _extractor.MergeMetadata(primary, fallback);

        // Assert
        merged.Keywords.Should().HaveCount(4); // keyword1, keyword2, shared, keyword3
        merged.Keywords.Should().Contain("keyword1");
        merged.Keywords.Should().Contain("keyword2");
        merged.Keywords.Should().Contain("keyword3");
        merged.Keywords.Should().Contain("shared");
        merged.Keywords.Where(k => k == "shared").Should().HaveCount(1); // No duplicates
    }

    [Fact]
    public void MergeMetadata_ShouldUsePrimaryConfidence()
    {
        // Arrange
        var primary = new ExtractedMetadata { OverallConfidence = 0.95f };
        var fallback = new ExtractedMetadata { OverallConfidence = 0.5f };

        // Act
        var merged = _extractor.MergeMetadata(primary, fallback);

        // Assert
        merged.OverallConfidence.Should().Be(0.95f);
    }

    [Fact]
    public void MergeMetadata_WithNullPrimary_ShouldThrowArgumentNullException()
    {
        // Arrange
        ExtractedMetadata? primary = null;
        var fallback = new ExtractedMetadata();

        // Act
        Action act = () => _extractor.MergeMetadata(primary!, fallback);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("primary");
    }

    [Fact]
    public void MergeMetadata_WithNullFallback_ShouldThrowArgumentNullException()
    {
        // Arrange
        var primary = new ExtractedMetadata();
        ExtractedMetadata? fallback = null;

        // Act
        Action act = () => _extractor.MergeMetadata(primary, fallback!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("fallback");
    }
}
