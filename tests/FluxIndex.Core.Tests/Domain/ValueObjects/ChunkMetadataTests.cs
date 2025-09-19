using FluxIndex.Core.Domain.ValueObjects;
using System;
using System.Linq;
using Xunit;

namespace FluxIndex.Core.Tests.Domain.ValueObjects;

/// <summary>
/// ChunkMetadata 도메인 모델 단위 테스트
/// </summary>
public class ChunkMetadataTests
{
    [Fact]
    public void CreateDefault_ValidTitle_ReturnsValidMetadata()
    {
        // Arrange
        var title = "Test Document Title";

        // Act
        var metadata = ChunkMetadata.CreateDefault(title);

        // Assert
        Assert.Equal(title, metadata.Title);
        Assert.Equal("Test summary for validation", metadata.Summary);
        Assert.Contains("test", metadata.Keywords);
        Assert.Contains("metadata", metadata.Keywords);
        Assert.Contains("chunk", metadata.Keywords);
        Assert.Equal(0.85f, metadata.QualityScore);
        Assert.Equal("TestService", metadata.ExtractedBy);
        Assert.Equal(ConfidenceLevel.High, metadata.Confidence);
        Assert.True(metadata.IsValid);
    }

    [Fact]
    public void Builder_FluentInterface_BuildsCorrectMetadata()
    {
        // Arrange & Act
        var metadata = ChunkMetadata.Builder()
            .WithTitle("Builder Test Title")
            .WithSummary("Builder test summary")
            .WithKeywords("builder", "test", "fluent")
            .WithEntities("BuilderEntity", "TestEntity")
            .WithQuestions("How does the builder work?", "What are the benefits?")
            .WithQualityScore(0.9f)
            .WithConfidence(ConfidenceLevel.VeryHigh)
            .WithCustomField("category", "technical")
            .WithCustomField("priority", "high")
            .Build();

        // Assert
        Assert.Equal("Builder Test Title", metadata.Title);
        Assert.Equal("Builder test summary", metadata.Summary);
        Assert.Contains("builder", metadata.Keywords);
        Assert.Contains("test", metadata.Keywords);
        Assert.Contains("fluent", metadata.Keywords);
        Assert.Contains("BuilderEntity", metadata.Entities);
        Assert.Contains("TestEntity", metadata.Entities);
        Assert.Contains("How does the builder work?", metadata.GeneratedQuestions);
        Assert.Contains("What are the benefits?", metadata.GeneratedQuestions);
        Assert.Equal(0.9f, metadata.QualityScore);
        Assert.Equal(ConfidenceLevel.VeryHigh, metadata.Confidence);
        Assert.Equal("technical", metadata.CustomFields["category"]);
        Assert.Equal("high", metadata.CustomFields["priority"]);
    }

    [Fact]
    public void Builder_DuplicateKeywords_RemovesDuplicates()
    {
        // Arrange & Act
        var metadata = ChunkMetadata.Builder()
            .WithTitle("Duplicate Test")
            .WithSummary("Testing duplicate removal")
            .WithKeywords("test", "duplicate", "test", "keyword", "duplicate")
            .Build();

        // Assert
        Assert.Equal(3, metadata.Keywords.Count);
        Assert.Contains("test", metadata.Keywords);
        Assert.Contains("duplicate", metadata.Keywords);
        Assert.Contains("keyword", metadata.Keywords);
    }

    [Fact]
    public void Builder_DuplicateEntities_RemovesDuplicates()
    {
        // Arrange & Act
        var metadata = ChunkMetadata.Builder()
            .WithTitle("Entity Test")
            .WithSummary("Testing entity duplicates")
            .WithEntities("Entity1", "Entity2", "Entity1", "Entity3", "Entity2")
            .Build();

        // Assert
        Assert.Equal(3, metadata.Entities.Count);
        Assert.Contains("Entity1", metadata.Entities);
        Assert.Contains("Entity2", metadata.Entities);
        Assert.Contains("Entity3", metadata.Entities);
    }

    [Fact]
    public void IsValid_ValidMetadata_ReturnsTrue()
    {
        // Arrange
        var metadata = ChunkMetadata.Builder()
            .WithTitle("Valid Title")
            .WithSummary("Valid summary within limits")
            .WithQualityScore(0.75f)
            .Build();

        // Act & Assert
        Assert.True(metadata.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValid_EmptyTitle_ReturnsFalse(string title)
    {
        // Arrange
        var metadata = ChunkMetadata.Builder()
            .WithTitle(title)
            .WithSummary("Valid summary")
            .WithQualityScore(0.75f)
            .Build();

        // Act & Assert
        Assert.False(metadata.IsValid);
    }

    [Fact]
    public void IsValid_TitleTooLong_ReturnsFalse()
    {
        // Arrange
        var longTitle = new string('A', 101); // 101 characters
        var metadata = ChunkMetadata.Builder()
            .WithTitle(longTitle)
            .WithSummary("Valid summary")
            .WithQualityScore(0.75f)
            .Build();

        // Act & Assert
        Assert.False(metadata.IsValid);
    }

    [Fact]
    public void IsValid_SummaryTooLong_ReturnsFalse()
    {
        // Arrange
        var longSummary = new string('B', 201); // 201 characters
        var metadata = ChunkMetadata.Builder()
            .WithTitle("Valid title")
            .WithSummary(longSummary)
            .WithQualityScore(0.75f)
            .Build();

        // Act & Assert
        Assert.False(metadata.IsValid);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void IsValid_InvalidQualityScore_ReturnsFalse(float score)
    {
        // Arrange
        var metadata = ChunkMetadata.Builder()
            .WithTitle("Valid title")
            .WithSummary("Valid summary")
            .WithQualityScore(score)
            .Build();

        // Act & Assert
        Assert.False(metadata.IsValid);
    }

    [Fact]
    public void GetSearchableTerms_ValidMetadata_ReturnsFilteredTerms()
    {
        // Arrange
        var metadata = ChunkMetadata.Builder()
            .WithTitle("Test Document Title With Multiple Words")
            .WithKeywords("keyword1", "keyword2", "a", "ab") // Include short keywords
            .WithEntities("Entity1", "Entity2")
            .Build();

        // Act
        var searchableTerms = metadata.GetSearchableTerms().ToList();

        // Assert
        Assert.Contains("test", searchableTerms);
        Assert.Contains("document", searchableTerms);
        Assert.Contains("title", searchableTerms);
        Assert.Contains("keyword1", searchableTerms);
        Assert.Contains("keyword2", searchableTerms);
        Assert.Contains("entity1", searchableTerms);
        Assert.Contains("entity2", searchableTerms);

        // Short terms should be filtered out
        Assert.DoesNotContain("a", searchableTerms);
        Assert.DoesNotContain("ab", searchableTerms);

        // Should be lowercase
        Assert.All(searchableTerms, term => Assert.Equal(term, term.ToLowerInvariant()));

        // Should be distinct
        Assert.Equal(searchableTerms.Count, searchableTerms.Distinct().Count());
    }

    [Fact]
    public void GetSearchableTerms_EmptyFields_ReturnsEmptyList()
    {
        // Arrange
        var metadata = ChunkMetadata.Builder()
            .WithTitle("")
            .WithSummary("Summary is not included in searchable terms")
            .Build();

        // Act
        var searchableTerms = metadata.GetSearchableTerms().ToList();

        // Assert
        Assert.Empty(searchableTerms);
    }

    [Fact]
    public void ToString_ValidMetadata_ReturnsFormattedString()
    {
        // Arrange
        var metadata = ChunkMetadata.Builder()
            .WithTitle("Test Title")
            .WithKeywords("test", "metadata")
            .WithQualityScore(0.85f)
            .Build();

        // Act
        var stringRepresentation = metadata.ToString();

        // Assert
        Assert.Contains("Test Title", stringRepresentation);
        Assert.Contains("0.85", stringRepresentation);
        Assert.Contains("2", stringRepresentation); // Keyword count
    }

    [Fact]
    public void ExtractedAt_NewMetadata_IsRecentTimestamp()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var metadata = ChunkMetadata.CreateDefault();
        var afterCreation = DateTime.UtcNow;

        // Assert
        Assert.True(metadata.ExtractedAt >= beforeCreation);
        Assert.True(metadata.ExtractedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, metadata.ExtractedAt.Kind);
    }

    [Fact]
    public void Record_Equality_WorksCorrectly()
    {
        // Arrange
        var metadata1 = ChunkMetadata.Builder()
            .WithTitle("Same Title")
            .WithSummary("Same Summary")
            .WithKeywords("same", "keywords")
            .WithQualityScore(0.8f)
            .Build();

        var metadata2 = ChunkMetadata.Builder()
            .WithTitle("Same Title")
            .WithSummary("Same Summary")
            .WithKeywords("same", "keywords")
            .WithQualityScore(0.8f)
            .Build();

        var metadata3 = ChunkMetadata.Builder()
            .WithTitle("Different Title")
            .WithSummary("Same Summary")
            .WithKeywords("same", "keywords")
            .WithQualityScore(0.8f)
            .Build();

        // Act & Assert
        Assert.Equal(metadata1, metadata2);
        Assert.NotEqual(metadata1, metadata3);
        Assert.Equal(metadata1.GetHashCode(), metadata2.GetHashCode());
        Assert.NotEqual(metadata1.GetHashCode(), metadata3.GetHashCode());
    }

    [Fact]
    public void CustomFields_ImmutableDictionary_CannotBeModified()
    {
        // Arrange
        var metadata = ChunkMetadata.Builder()
            .WithTitle("Immutable Test")
            .WithCustomField("field1", "value1")
            .Build();

        // Act & Assert
        Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyDictionary<string, object>>(metadata.CustomFields);
        Assert.Equal("value1", metadata.CustomFields["field1"]);
    }

    [Theory]
    [InlineData(ConfidenceLevel.Low)]
    [InlineData(ConfidenceLevel.Medium)]
    [InlineData(ConfidenceLevel.High)]
    [InlineData(ConfidenceLevel.VeryHigh)]
    public void ConfidenceLevel_AllValues_AreValid(ConfidenceLevel confidence)
    {
        // Arrange & Act
        var metadata = ChunkMetadata.Builder()
            .WithTitle("Confidence Test")
            .WithConfidence(confidence)
            .Build();

        // Assert
        Assert.Equal(confidence, metadata.Confidence);
        Assert.True(Enum.IsDefined(typeof(ConfidenceLevel), confidence));
    }
}