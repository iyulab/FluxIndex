using FluxIndex.AI.OpenAI.Prompts;
using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxIndex.AI.OpenAI.Tests.Prompts;

/// <summary>
/// MetadataPrompts 단위 테스트
/// </summary>
public class MetadataPromptsTests
{
    [Fact]
    public void PromptBuilder_WithContent_BuildsValidPrompt()
    {
        // Arrange
        var content = "This is test content for metadata extraction.";

        // Act
        var prompt = new MetadataPrompts.PromptBuilder()
            .WithContent(content)
            .WithContext("")
            .Build();

        // Assert
        Assert.Contains(content, prompt);
        Assert.Contains("{content}", MetadataPrompts.ExtractionPrompt);
        Assert.DoesNotContain("{content}", prompt); // Should be replaced
    }

    [Fact]
    public void PromptBuilder_WithContext_IncludesContextInPrompt()
    {
        // Arrange
        var content = "Test content";
        var context = "Additional context information";

        // Act
        var prompt = new MetadataPrompts.PromptBuilder()
            .WithContent(content)
            .WithContext(context)
            .Build();

        // Assert
        Assert.Contains(content, prompt);
        Assert.Contains(context, prompt);
        Assert.Contains("Additional Context:", prompt);
    }

    [Fact]
    public void PromptBuilder_WithNullContext_HandlesGracefully()
    {
        // Arrange
        var content = "Test content";

        // Act
        var prompt = new MetadataPrompts.PromptBuilder()
            .WithContent(content)
            .WithContext(null)
            .Build();

        // Assert
        Assert.Contains(content, prompt);
        Assert.DoesNotContain("Additional Context:", prompt);
    }

    [Fact]
    public void PromptBuilder_WithEmptyContext_HandlesGracefully()
    {
        // Arrange
        var content = "Test content";

        // Act
        var prompt = new MetadataPrompts.PromptBuilder()
            .WithContent(content)
            .WithContext("")
            .Build();

        // Assert
        Assert.Contains(content, prompt);
        Assert.DoesNotContain("Additional Context:", prompt);
    }

    [Fact]
    public void PromptBuilder_WithBatchTemplate_BuildsBatchPrompt()
    {
        // Arrange
        var chunks = new List<string>
        {
            "First chunk content",
            "Second chunk content",
            "Third chunk content"
        };

        // Act
        var prompt = new MetadataPrompts.PromptBuilder()
            .WithTemplate(MetadataPrompts.BatchExtractionPrompt)
            .WithChunks(chunks)
            .Build();

        // Assert
        Assert.Contains("First chunk content", prompt);
        Assert.Contains("Second chunk content", prompt);
        Assert.Contains("Third chunk content", prompt);
        Assert.Contains("=== Chunk 1 ===", prompt);
        Assert.Contains("=== Chunk 2 ===", prompt);
        Assert.Contains("=== Chunk 3 ===", prompt);
        Assert.Contains("3", prompt); // chunk_count
    }

    [Fact]
    public void PromptBuilder_WithDomainSpecificTemplate_BuildsDomainPrompt()
    {
        // Arrange
        var content = "Technical documentation content";
        var domain = "Software Engineering";

        // Act
        var prompt = new MetadataPrompts.PromptBuilder()
            .WithTemplate(MetadataPrompts.DomainSpecificPrompt)
            .WithContent(content)
            .WithDomain(domain)
            .Build();

        // Assert
        Assert.Contains(content, prompt);
        Assert.Contains(domain, prompt);
        Assert.Contains("domain expert", prompt.ToLowerInvariant());
    }

    [Fact]
    public void PromptBuilder_WithCustomPlaceholder_ReplacesPlaceholder()
    {
        // Arrange
        var customTemplate = "Hello {name}, welcome to {place}!";
        var name = "John";
        var place = "FluxIndex";

        // Act
        var prompt = new MetadataPrompts.PromptBuilder()
            .WithTemplate(customTemplate)
            .WithPlaceholder("name", name)
            .WithPlaceholder("place", place)
            .Build();

        // Assert
        Assert.Equal($"Hello {name}, welcome to {place}!", prompt);
    }

    [Fact]
    public void PromptBuilder_WithUnresolvedPlaceholder_ThrowsInvalidOperationException()
    {
        // Arrange
        var templateWithPlaceholder = "Content: {content}, Missing: {missing_placeholder}";

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new MetadataPrompts.PromptBuilder()
                .WithTemplate(templateWithPlaceholder)
                .WithContent("test content")
                .Build());

        Assert.Contains("Unresolved placeholders: missing_placeholder", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void PromptBuilder_WithNullOrEmptyContent_ThrowsArgumentException(string content)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new MetadataPrompts.PromptBuilder()
                .WithContent(content));

        Assert.Equal("content", exception.ParamName);
    }

    [Fact]
    public void PromptBuilder_WithNullChunks_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new MetadataPrompts.PromptBuilder()
                .WithChunks(null));

        Assert.Equal("chunks", exception.ParamName);
    }

    [Fact]
    public void PromptBuilder_WithEmptyChunks_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new MetadataPrompts.PromptBuilder()
                .WithChunks(new List<string>()));

        Assert.Equal("chunks", exception.ParamName);
    }

    [Fact]
    public void PromptBuilder_CreateForTesting_ReturnsValidBuilder()
    {
        // Act
        var builder = MetadataPrompts.PromptBuilder.CreateForTesting();
        var prompt = builder.Build();

        // Assert
        Assert.NotEmpty(prompt);
        Assert.Contains("This is test content for metadata extraction", prompt);
        Assert.Contains("Test context information", prompt);
    }

    [Fact]
    public void EstimateTokenCount_ValidPrompt_ReturnsReasonableEstimate()
    {
        // Arrange
        var prompt = "This is a test prompt for token estimation.";

        // Act
        var tokenCount = MetadataPrompts.EstimateTokenCount(prompt);

        // Assert
        Assert.True(tokenCount > 0);
        Assert.True(tokenCount < 1000); // Should be reasonable for a short prompt
    }

    [Fact]
    public void EstimateTokenCount_LongPrompt_ReturnsHigherCount()
    {
        // Arrange
        var shortPrompt = "Short prompt";
        var longPrompt = new string('A', 1000) + " " + new string('B', 1000);

        // Act
        var shortCount = MetadataPrompts.EstimateTokenCount(shortPrompt);
        var longCount = MetadataPrompts.EstimateTokenCount(longPrompt);

        // Assert
        Assert.True(longCount > shortCount);
    }

    [Theory]
    [InlineData(MetadataPrompts.ExtractionPrompt)]
    [InlineData(MetadataPrompts.BatchExtractionPrompt)]
    [InlineData(MetadataPrompts.DomainSpecificPrompt)]
    [InlineData(MetadataPrompts.QualityValidationPrompt)]
    public void IsValidPrompt_BuiltInPrompts_ReturnsTrue(string prompt)
    {
        // Act
        var isValid = MetadataPrompts.IsValidPrompt(prompt);

        // Assert
        Assert.True(isValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Short")]
    [InlineData("No content placeholder")]
    public void IsValidPrompt_InvalidPrompts_ReturnsFalse(string prompt)
    {
        // Act
        var isValid = MetadataPrompts.IsValidPrompt(prompt);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void Versions_GetPrompt_ReturnsCorrectPrompt()
    {
        // Act
        var v1Prompt = MetadataPrompts.Versions.GetPrompt(MetadataPrompts.Versions.V1_Basic);
        var v2Prompt = MetadataPrompts.Versions.GetPrompt(MetadataPrompts.Versions.V2_Enhanced);
        var v3Prompt = MetadataPrompts.Versions.GetPrompt(MetadataPrompts.Versions.V3_Structured);
        var unknownPrompt = MetadataPrompts.Versions.GetPrompt("unknown");

        // Assert
        Assert.Equal(MetadataPrompts.ExtractionPrompt, v1Prompt);
        Assert.Equal(MetadataPrompts.BatchExtractionPrompt, v2Prompt);
        Assert.Equal(MetadataPrompts.DomainSpecificPrompt, v3Prompt);
        Assert.Equal(MetadataPrompts.ExtractionPrompt, unknownPrompt); // Default fallback
    }

    [Fact]
    public void ExtractionPrompt_ContainsRequiredElements()
    {
        // Act
        var prompt = MetadataPrompts.ExtractionPrompt;

        // Assert
        Assert.Contains("JSON", prompt);
        Assert.Contains("title", prompt);
        Assert.Contains("summary", prompt);
        Assert.Contains("keywords", prompt);
        Assert.Contains("entities", prompt);
        Assert.Contains("generated_questions", prompt);
        Assert.Contains("quality_score", prompt);
        Assert.Contains("{content}", prompt);
        Assert.Contains("{context_section}", prompt);
    }

    [Fact]
    public void BatchExtractionPrompt_ContainsArrayFormat()
    {
        // Act
        var prompt = MetadataPrompts.BatchExtractionPrompt;

        // Assert
        Assert.Contains("JSON array", prompt);
        Assert.Contains("{chunks}", prompt);
        Assert.Contains("{chunk_count}", prompt);
        Assert.Contains("same order", prompt);
    }

    [Fact]
    public void DomainSpecificPrompt_ContainsDomainPlaceholder()
    {
        // Act
        var prompt = MetadataPrompts.DomainSpecificPrompt;

        // Assert
        Assert.Contains("{domain}", prompt);
        Assert.Contains("domain expert", prompt);
        Assert.Contains("domain_specific_fields", prompt);
    }

    [Fact]
    public void QualityValidationPrompt_ContainsValidationFields()
    {
        // Act
        var prompt = MetadataPrompts.QualityValidationPrompt;

        // Assert
        Assert.Contains("title_accuracy", prompt);
        Assert.Contains("summary_completeness", prompt);
        Assert.Contains("keyword_relevance", prompt);
        Assert.Contains("entity_accuracy", prompt);
        Assert.Contains("question_answerability", prompt);
        Assert.Contains("overall_quality", prompt);
        Assert.Contains("issues", prompt);
        Assert.Contains("suggestions", prompt);
        Assert.Contains("{metadata}", prompt);
    }
}