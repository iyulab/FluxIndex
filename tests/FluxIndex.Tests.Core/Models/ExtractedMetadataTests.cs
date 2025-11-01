using FluxIndex.Core.Models;
using FluentAssertions;
using Xunit;

namespace FluxIndex.Tests.Core.Models;

public class ExtractedMetadataTests
{
    [Fact]
    public void Create_WithValidData_ShouldSucceed()
    {
        // Arrange
        var description = "This is a test summary";
        var keywords = new[] { "test", "document" };
        var topics = new[] { "testing", "documentation" };

        // Act
        var metadata = new ExtractedMetadata
        {
            Description = description,
            Keywords = keywords,
            Topics = topics,
            Language = "en",
            DocumentType = "article",
            OverallConfidence = 0.95f
        };

        // Assert
        metadata.Description.Should().Be(description);
        metadata.Keywords.Should().BeEquivalentTo(keywords);
        metadata.Topics.Should().BeEquivalentTo(topics);
        metadata.Language.Should().Be("en");
        metadata.DocumentType.Should().Be("article");
        metadata.OverallConfidence.Should().Be(0.95f);
    }

    [Fact]
    public void ExtractedAt_ShouldBeInitializedToUtcNow()
    {
        // Arrange & Act
        var before = DateTimeOffset.UtcNow;
        var metadata = new ExtractedMetadata();
        var after = DateTimeOffset.UtcNow;

        // Assert
        metadata.ExtractedAt.Should().BeOnOrAfter(before);
        metadata.ExtractedAt.Should().BeOnOrBefore(after);
        metadata.ExtractedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Keywords_ShouldInitializeAsEmptyArray()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata();

        // Assert
        metadata.Keywords.Should().NotBeNull();
        metadata.Keywords.Should().BeEmpty();
    }

    [Fact]
    public void Topics_ShouldInitializeAsEmptyArray()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata();

        // Assert
        metadata.Topics.Should().NotBeNull();
        metadata.Topics.Should().BeEmpty();
    }

    [Fact]
    public void Categories_ShouldInitializeAsEmptyArray()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata();

        // Assert
        metadata.Categories.Should().NotBeNull();
        metadata.Categories.Should().BeEmpty();
    }

    [Fact]
    public void SchemaSpecificData_ShouldInitializeAsEmptyDictionary()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata();

        // Assert
        metadata.SchemaSpecificData.Should().NotBeNull();
        metadata.SchemaSpecificData.Should().BeEmpty();
    }

    [Fact]
    public void OverallConfidence_ShouldDefaultToZero()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata();

        // Assert
        metadata.OverallConfidence.Should().Be(0f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(0.95f)]
    [InlineData(1.0f)]
    public void OverallConfidence_ShouldAcceptValidValues(float confidence)
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata { OverallConfidence = confidence };

        // Assert
        metadata.OverallConfidence.Should().Be(confidence);
    }

    [Fact]
    public void SchemaSpecificData_ShouldStoreComplexObjects()
    {
        // Arrange
        var metadata = new ExtractedMetadata();
        var customData = new Dictionary<string, object>
        {
            { "author", "John Doe" },
            { "publishedDate", DateTime.Parse("2024-01-15") },
            { "tags", new[] { "tech", "ai" } }
        };

        // Act
        metadata.SchemaSpecificData = customData;

        // Assert
        metadata.SchemaSpecificData.Should().ContainKey("author");
        metadata.SchemaSpecificData["author"].Should().Be("John Doe");
        metadata.SchemaSpecificData.Should().ContainKey("publishedDate");
        metadata.SchemaSpecificData.Should().ContainKey("tags");
    }

    [Fact]
    public void FieldSources_ShouldInitializeAsEmptyDictionary()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata();

        // Assert
        metadata.FieldSources.Should().NotBeNull();
        metadata.FieldSources.Should().BeEmpty();
    }

    [Fact]
    public void FieldConfidence_ShouldInitializeAsEmptyDictionary()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata();

        // Assert
        metadata.FieldConfidence.Should().NotBeNull();
        metadata.FieldConfidence.Should().BeEmpty();
    }

    [Fact]
    public void UserCorrections_ShouldInitializeAsEmptyDictionary()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata();

        // Assert
        metadata.UserCorrections.Should().NotBeNull();
        metadata.UserCorrections.Should().BeEmpty();
    }

    [Fact]
    public void ExtractionMethod_ShouldDefaultToAI()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata();

        // Assert
        metadata.ExtractionMethod.Should().Be("AI");
    }

    [Fact]
    public void ExtractionMethod_ShouldStoreProviderInformation()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata
        {
            ExtractionMethod = "OpenAI-gpt-4o"
        };

        // Assert
        metadata.ExtractionMethod.Should().Be("OpenAI-gpt-4o");
    }

    [Fact]
    public void Source_ShouldDefaultToAI()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata();

        // Assert
        metadata.Source.Should().Be(MetadataSource.AI);
    }

    [Fact]
    public void UserVerified_ShouldDefaultToFalse()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata();

        // Assert
        metadata.UserVerified.Should().BeFalse();
    }

    [Fact]
    public void Language_ShouldDefaultToEn()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata();

        // Assert
        metadata.Language.Should().Be("en");
    }

    [Fact]
    public void Description_ShouldDefaultToEmptyString()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata();

        // Assert
        metadata.Description.Should().BeEmpty();
    }

    [Fact]
    public void DocumentType_ShouldDefaultToEmptyString()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata();

        // Assert
        metadata.DocumentType.Should().BeEmpty();
    }

    [Fact]
    public void DocumentId_ShouldDefaultToEmptyString()
    {
        // Arrange & Act
        var metadata = new ExtractedMetadata();

        // Assert
        metadata.DocumentId.Should().BeEmpty();
    }

    [Fact]
    public void FieldSources_ShouldStoreSourceTracking()
    {
        // Arrange
        var metadata = new ExtractedMetadata
        {
            FieldSources = new Dictionary<string, MetadataSource>
            {
                { "topics", MetadataSource.AI },
                { "keywords", MetadataSource.Merged },
                { "description", MetadataSource.RuleBased }
            }
        };

        // Assert
        metadata.FieldSources.Should().HaveCount(3);
        metadata.FieldSources["topics"].Should().Be(MetadataSource.AI);
        metadata.FieldSources["keywords"].Should().Be(MetadataSource.Merged);
        metadata.FieldSources["description"].Should().Be(MetadataSource.RuleBased);
    }

    [Fact]
    public void FieldConfidence_ShouldStoreConfidenceScores()
    {
        // Arrange
        var metadata = new ExtractedMetadata
        {
            FieldConfidence = new Dictionary<string, float>
            {
                { "topics", 0.96f },
                { "keywords", 0.92f },
                { "documentType", 0.88f }
            }
        };

        // Assert
        metadata.FieldConfidence.Should().HaveCount(3);
        metadata.FieldConfidence["topics"].Should().Be(0.96f);
        metadata.FieldConfidence["keywords"].Should().Be(0.92f);
        metadata.FieldConfidence["documentType"].Should().Be(0.88f);
    }
}
