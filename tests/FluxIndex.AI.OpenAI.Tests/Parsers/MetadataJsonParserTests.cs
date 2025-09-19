using FluxIndex.AI.OpenAI.Parsers;
using FluxIndex.Core.Domain.ValueObjects;
using Xunit;
using System;
using System.Text.Json;

namespace FluxIndex.AI.OpenAI.Tests.Parsers;

/// <summary>
/// MetadataJsonParser 단위 테스트
/// </summary>
public class MetadataJsonParserTests
{
    private readonly MetadataJsonParser _parser;

    public MetadataJsonParserTests()
    {
        _parser = MetadataJsonParser.CreateForTesting();
    }

    [Fact]
    public void ParseMetadata_ValidJson_ReturnsChunkMetadata()
    {
        // Arrange
        var validJson = """
        {
            "title": "Test Document",
            "summary": "This is a test summary",
            "keywords": ["test", "document", "metadata"],
            "entities": ["TestEntity"],
            "generated_questions": ["What is this about?"],
            "quality_score": 0.85
        }
        """;

        // Act
        var result = _parser.ParseMetadata(validJson, "TestService");

        // Assert
        Assert.Equal("Test Document", result.Title);
        Assert.Equal("This is a test summary", result.Summary);
        Assert.Contains("test", result.Keywords);
        Assert.Contains("TestEntity", result.Entities);
        Assert.Contains("What is this about?", result.GeneratedQuestions);
        Assert.Equal(0.85f, result.QualityScore);
        Assert.Equal("TestService", result.ExtractedBy);
    }

    [Fact]
    public void ParseMetadata_JsonWithMarkdownCodeBlock_ReturnsChunkMetadata()
    {
        // Arrange
        var jsonWithMarkdown = """
        ```json
        {
            "title": "Markdown Test",
            "summary": "Test with markdown",
            "keywords": ["markdown", "test"],
            "entities": [],
            "generated_questions": [],
            "quality_score": 0.7
        }
        ```
        """;

        // Act
        var result = _parser.ParseMetadata(jsonWithMarkdown);

        // Assert
        Assert.Equal("Markdown Test", result.Title);
        Assert.Equal("Test with markdown", result.Summary);
        Assert.Contains("markdown", result.Keywords);
    }

    [Fact]
    public void ParseMetadata_JsonWithExtraText_ReturnsChunkMetadata()
    {
        // Arrange
        var jsonWithExtraText = """
        Here's the extracted metadata:

        {
            "title": "Extra Text Test",
            "summary": "Test with extra text",
            "keywords": ["extraction", "test"],
            "entities": ["TextEntity"],
            "generated_questions": ["How does extraction work?"],
            "quality_score": 0.9
        }

        This completes the extraction.
        """;

        // Act
        var result = _parser.ParseMetadata(jsonWithExtraText);

        // Assert
        Assert.Equal("Extra Text Test", result.Title);
        Assert.Equal("Test with extra text", result.Summary);
        Assert.Equal(0.9f, result.QualityScore);
    }

    [Fact]
    public void ParseMetadata_EmptyResponse_ThrowsMetadataParsingException()
    {
        // Act & Assert
        var exception = Assert.Throws<MetadataParsingException>(() =>
            _parser.ParseMetadata(""));

        Assert.Equal("Response cannot be empty", exception.Message);
    }

    [Fact]
    public void ParseMetadata_InvalidJson_ThrowsMetadataParsingException()
    {
        // Arrange
        var invalidJson = "{ invalid json content }";

        // Act & Assert
        var exception = Assert.Throws<MetadataParsingException>(() =>
            _parser.ParseMetadata(invalidJson));

        Assert.Contains("Invalid JSON format", exception.Message);
        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public void ParseMetadata_LongStrings_TruncatesCorrectly()
    {
        // Arrange
        var longTitle = new string('A', 150); // Longer than 100 chars
        var longSummary = new string('B', 250); // Longer than 200 chars
        var longKeyword = new string('C', 80); // Longer than 50 chars

        var jsonWithLongStrings = $$"""
        {
            "title": "{{longTitle}}",
            "summary": "{{longSummary}}",
            "keywords": ["{{longKeyword}}", "short"],
            "entities": [],
            "generated_questions": [],
            "quality_score": 0.5
        }
        """;

        // Act
        var result = _parser.ParseMetadata(jsonWithLongStrings);

        // Assert
        Assert.True(result.Title.Length <= 100);
        Assert.True(result.Summary.Length <= 200);
        Assert.All(result.Keywords, keyword => Assert.True(keyword.Length <= 50));
    }

    [Fact]
    public void ParseBatchMetadata_ValidArray_ReturnsMetadataList()
    {
        // Arrange
        var batchJson = """
        [
            {
                "title": "First Document",
                "summary": "First summary",
                "keywords": ["first", "test"],
                "entities": ["FirstEntity"],
                "generated_questions": ["What is first?"],
                "quality_score": 0.8
            },
            {
                "title": "Second Document",
                "summary": "Second summary",
                "keywords": ["second", "test"],
                "entities": ["SecondEntity"],
                "generated_questions": ["What is second?"],
                "quality_score": 0.9
            }
        ]
        """;

        // Act
        var results = _parser.ParseBatchMetadata(batchJson, 2);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("First Document", results[0].Title);
        Assert.Equal("Second Document", results[1].Title);
    }

    [Fact]
    public void ParseBatchMetadata_WrongCount_ThrowsMetadataParsingException()
    {
        // Arrange
        var batchJson = """
        [
            {
                "title": "Only One",
                "summary": "Only one item",
                "keywords": ["single"],
                "entities": [],
                "generated_questions": [],
                "quality_score": 0.7
            }
        ]
        """;

        // Act & Assert
        var exception = Assert.Throws<MetadataParsingException>(() =>
            _parser.ParseBatchMetadata(batchJson, 2));

        Assert.Contains("Expected 2 items, but got 1", exception.Message);
    }

    [Fact]
    public void ParseBatchMetadata_NotArray_ThrowsMetadataParsingException()
    {
        // Arrange
        var notArrayJson = """
        {
            "title": "Not an array",
            "summary": "This is not an array",
            "keywords": ["error"],
            "entities": [],
            "generated_questions": [],
            "quality_score": 0.5
        }
        """;

        // Act & Assert
        var exception = Assert.Throws<MetadataParsingException>(() =>
            _parser.ParseBatchMetadata(notArrayJson, 1));

        Assert.Equal("Batch response must be a JSON array", exception.Message);
    }

    [Fact]
    public void ValidateMetadata_ValidData_ReturnsSuccessResult()
    {
        // Arrange
        var metadata = ChunkMetadata.CreateDefault("Valid Title");

        // Act
        var result = MetadataJsonParser.ValidateMetadata(metadata);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.True(result.QualityScore > 0.5f);
    }

    [Fact]
    public void ValidateMetadata_EmptyTitle_ReturnsFailureResult()
    {
        // Arrange
        var metadata = ChunkMetadata.Builder()
            .WithTitle("")
            .WithSummary("Valid summary")
            .WithQualityScore(0.8f)
            .Build();

        // Act
        var result = MetadataJsonParser.ValidateMetadata(metadata);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Title is missing or empty", result.Issues);
    }

    [Fact]
    public void ValidateMetadata_TooLongTitle_ReturnsFailureResult()
    {
        // Arrange
        var longTitle = new string('A', 101); // 101 characters
        var metadata = ChunkMetadata.Builder()
            .WithTitle(longTitle)
            .WithSummary("Valid summary")
            .WithQualityScore(0.8f)
            .Build();

        // Act
        var result = MetadataJsonParser.ValidateMetadata(metadata);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Title exceeds 100 characters", result.Issues);
    }

    [Fact]
    public void ValidateMetadata_InvalidQualityScore_ReturnsFailureResult()
    {
        // Arrange
        var metadata = ChunkMetadata.Builder()
            .WithTitle("Valid title")
            .WithSummary("Valid summary")
            .WithQualityScore(1.5f) // Invalid score > 1.0
            .Build();

        // Act
        var result = MetadataJsonParser.ValidateMetadata(metadata);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Quality score must be between 0.0 and 1.0", result.Issues);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseMetadata_NullOrEmptyInput_ThrowsMetadataParsingException(string input)
    {
        // Act & Assert
        var exception = Assert.Throws<MetadataParsingException>(() =>
            _parser.ParseMetadata(input));

        Assert.Equal("Response cannot be empty", exception.Message);
    }

    [Fact]
    public void ParseMetadata_MissingOptionalFields_CreatesValidMetadata()
    {
        // Arrange - JSON with only required fields
        var minimalJson = """
        {
            "title": "Minimal Test",
            "summary": "Minimal summary"
        }
        """;

        // Act
        var result = _parser.ParseMetadata(minimalJson);

        // Assert
        Assert.Equal("Minimal Test", result.Title);
        Assert.Equal("Minimal summary", result.Summary);
        Assert.Empty(result.Keywords);
        Assert.Empty(result.Entities);
        Assert.Empty(result.GeneratedQuestions);
        Assert.Equal(0.5f, result.QualityScore); // Default fallback value
    }

    [Fact]
    public void ParseMetadata_KeywordsLowercase_ConvertsToLowercase()
    {
        // Arrange
        var jsonWithUppercaseKeywords = """
        {
            "title": "Case Test",
            "summary": "Testing case conversion",
            "keywords": ["UPPERCASE", "MixedCase", "lowercase"],
            "entities": [],
            "generated_questions": [],
            "quality_score": 0.7
        }
        """;

        // Act
        var result = _parser.ParseMetadata(jsonWithUppercaseKeywords);

        // Assert
        Assert.Contains("uppercase", result.Keywords);
        Assert.Contains("mixedcase", result.Keywords);
        Assert.Contains("lowercase", result.Keywords);
    }

    [Fact]
    public void ParseMetadata_TooManyKeywords_LimitsToMaximum()
    {
        // Arrange
        var manyKeywords = string.Join("\", \"", Enumerable.Range(1, 20).Select(i => $"keyword{i}"));
        var jsonWithManyKeywords = $$"""
        {
            "title": "Many Keywords Test",
            "summary": "Testing keyword limits",
            "keywords": ["{{manyKeywords}}"],
            "entities": [],
            "generated_questions": [],
            "quality_score": 0.8
        }
        """;

        // Act
        var result = _parser.ParseMetadata(jsonWithManyKeywords);

        // Assert
        Assert.True(result.Keywords.Count <= 15); // Maximum 15 keywords
    }
}