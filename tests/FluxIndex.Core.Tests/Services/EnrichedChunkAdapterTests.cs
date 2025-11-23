using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

public class EnrichedChunkAdapterTests
{
    [Fact]
    public void ToAugmentedChunk_ConvertsAllProperties()
    {
        // Arrange
        var enrichedChunk = CreateTestEnrichedChunk();

        // Act
        var augmented = EnrichedChunkAdapter.ToAugmentedChunk(enrichedChunk);

        // Assert
        Assert.Equal(enrichedChunk.Content, augmented.Content);
        Assert.Equal(enrichedChunk.ChunkId, augmented.ChunkId);
        Assert.Equal(enrichedChunk.ChunkIndex, augmented.ChunkIndex);
        Assert.Equal(enrichedChunk.HeadingPath.Count, augmented.HeadingPath.Count);
        Assert.Equal(enrichedChunk.SectionTitle, augmented.SectionTitle);
        Assert.Equal(enrichedChunk.StartPage, augmented.StartPage);
        Assert.Equal(enrichedChunk.EndPage, augmented.EndPage);
        Assert.Equal(enrichedChunk.Quality, augmented.Quality);
        Assert.Equal(enrichedChunk.ContextDependency, augmented.ContextDependency);
        Assert.Equal(enrichedChunk.TokenCount, augmented.TokenCount);
    }

    [Fact]
    public void ToAugmentedChunk_ConvertsSourceMetadata()
    {
        // Arrange
        var enrichedChunk = CreateTestEnrichedChunk();

        // Act
        var augmented = EnrichedChunkAdapter.ToAugmentedChunk(enrichedChunk);

        // Assert
        Assert.NotNull(augmented.Source);
        Assert.Equal(enrichedChunk.Source.SourceId, augmented.Source.SourceId);
        Assert.Equal(enrichedChunk.Source.SourceType, augmented.Source.SourceType);
        Assert.Equal(enrichedChunk.Source.Title, augmented.Source.Title);
        Assert.Equal(enrichedChunk.Source.Language, augmented.Source.Language);
        Assert.Equal(enrichedChunk.Source.WordCount, augmented.Source.WordCount);
        Assert.Equal(enrichedChunk.Source.Author, augmented.Source.Author);
        Assert.Equal(enrichedChunk.Source.Keywords, augmented.Source.Keywords);
    }

    [Fact]
    public void ToAugmentedChunks_ConvertsList()
    {
        // Arrange
        var chunks = new[]
        {
            CreateTestEnrichedChunk("chunk1"),
            CreateTestEnrichedChunk("chunk2"),
            CreateTestEnrichedChunk("chunk3")
        };

        // Act
        var augmented = EnrichedChunkAdapter.ToAugmentedChunks(chunks);

        // Assert
        Assert.Equal(3, augmented.Count);
        Assert.All(augmented, a => Assert.NotNull(a.Source));
    }

    [Fact]
    public void WithContextualHeader_AddsHeader()
    {
        // Arrange
        var enrichedChunk = CreateTestEnrichedChunk();
        var header = "[Test Document] [Chapter 1 > Section 1.1]";

        // Act
        var augmented = EnrichedChunkAdapter.WithContextualHeader(enrichedChunk, header);

        // Assert
        Assert.Equal(header, augmented.ContextualHeader);
        Assert.Equal(enrichedChunk.Content, augmented.Content);
    }

    [Fact]
    public void SearchableContent_CombinesHeaderAndContent()
    {
        // Arrange
        var enrichedChunk = CreateTestEnrichedChunk();
        var header = "[Test Document]";

        // Act
        var augmented = EnrichedChunkAdapter.WithContextualHeader(enrichedChunk, header);

        // Assert
        Assert.Contains(header, augmented.SearchableContent);
        Assert.Contains(enrichedChunk.Content, augmented.SearchableContent);
    }

    [Fact]
    public void SearchableContent_WithoutHeader_ReturnsContent()
    {
        // Arrange
        var enrichedChunk = CreateTestEnrichedChunk();

        // Act
        var augmented = EnrichedChunkAdapter.ToAugmentedChunk(enrichedChunk);

        // Assert
        Assert.Equal(enrichedChunk.Content, augmented.SearchableContent);
    }

    [Fact]
    public void PageRange_FormatsCorrectly()
    {
        // Arrange
        var chunk = CreateTestEnrichedChunk();
        chunk.StartPage = 10;
        chunk.EndPage = 15;

        // Act
        var augmented = EnrichedChunkAdapter.ToAugmentedChunk(chunk);

        // Assert
        Assert.Equal("pp.10-15", augmented.PageRange);
    }

    [Fact]
    public void PageRange_SinglePage_FormatsSinglePage()
    {
        // Arrange
        var chunk = CreateTestEnrichedChunk();
        chunk.StartPage = 10;
        chunk.EndPage = null;

        // Act
        var augmented = EnrichedChunkAdapter.ToAugmentedChunk(chunk);

        // Assert
        Assert.Equal("p.10", augmented.PageRange);
    }

    [Fact]
    public void PageRange_NoPage_ReturnsNull()
    {
        // Arrange
        var chunk = CreateTestEnrichedChunk();
        chunk.StartPage = null;

        // Act
        var augmented = EnrichedChunkAdapter.ToAugmentedChunk(chunk);

        // Assert
        Assert.Null(augmented.PageRange);
    }

    [Fact]
    public void WithAugmentation_AddsMultipleProperties()
    {
        // Arrange
        var chunk = EnrichedChunkAdapter.ToAugmentedChunk(CreateTestEnrichedChunk());
        var topics = new List<string> { "Security", "Authentication" };
        var questions = new List<string> { "How does auth work?", "What are the security risks?" };

        // Act
        EnrichedChunkAdapter.WithAugmentation(
            chunk,
            contextualHeader: "Test header",
            summary: "Test summary",
            topics: topics,
            potentialQuestions: questions);

        // Assert
        Assert.Equal("Test header", chunk.ContextualHeader);
        Assert.Equal("Test summary", chunk.Summary);
        Assert.Equal(topics, chunk.Topics);
        Assert.Equal(questions, chunk.PotentialQuestions);
    }

    [Fact]
    public void ToAugmentedChunk_NullChunk_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            EnrichedChunkAdapter.ToAugmentedChunk(null!));
    }

    [Fact]
    public void ToAugmentedChunks_NullList_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            EnrichedChunkAdapter.ToAugmentedChunks(null!));
    }

    [Fact]
    public void ExtensionMethod_ToAugmentedChunk_Works()
    {
        // Arrange
        var enrichedChunk = CreateTestEnrichedChunk();

        // Act
        var augmented = enrichedChunk.ToAugmentedChunk();

        // Assert
        Assert.Equal(enrichedChunk.ChunkId, augmented.ChunkId);
    }

    [Fact]
    public void ExtensionMethod_ToAugmentedChunks_Works()
    {
        // Arrange
        var chunks = new[]
        {
            CreateTestEnrichedChunk("chunk1"),
            CreateTestEnrichedChunk("chunk2")
        };

        // Act
        var augmented = chunks.ToAugmentedChunks();

        // Assert
        Assert.Equal(2, augmented.Count);
    }

    [Fact]
    public void ExtensionMethod_WithContextualHeader_Works()
    {
        // Arrange
        var enrichedChunk = CreateTestEnrichedChunk();

        // Act
        var augmented = enrichedChunk.WithContextualHeader("Test header");

        // Assert
        Assert.Equal("Test header", augmented.ContextualHeader);
    }

    private static TestEnrichedChunk CreateTestEnrichedChunk(string? chunkId = null)
    {
        return new TestEnrichedChunk
        {
            Content = "Test content for the chunk.",
            ChunkId = chunkId ?? Guid.NewGuid().ToString(),
            ChunkIndex = 0,
            HeadingPath = new List<string> { "Chapter 1", "Section 1.1" },
            SectionTitle = "Section 1.1",
            StartPage = 10,
            EndPage = 12,
            Quality = 0.85,
            ContextDependency = 0.6,
            TokenCount = 50,
            Source = new TestSourceMetadata
            {
                SourceId = "test-doc-1",
                SourceType = "pdf",
                Title = "Test Document",
                FilePath = "/path/to/file.pdf",
                Language = "en",
                LanguageConfidence = 0.95,
                WordCount = 1000,
                ChunkCount = 10,
                PageCount = 20,
                Author = "Test Author",
                Keywords = new List<string> { "test", "document" }
            }
        };
    }
}
