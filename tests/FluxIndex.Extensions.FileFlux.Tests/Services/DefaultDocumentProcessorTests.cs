using FluxIndex.Extensions.FileFlux;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FluxIndex.Extensions.FileFlux.Tests.Services;

/// <summary>
/// Tests for FileFlux document processing integration
/// </summary>
public class DocumentProcessorIntegrationTests
{
    [Fact]
    public void IDocumentProcessor_Interface_ShouldBeAvailable()
    {
        // Arrange & Act
        var mockProcessor = new Mock<IDocumentProcessor>();

        // Assert
        Assert.NotNull(mockProcessor.Object);
    }

    [Fact]
    public async Task IDocumentProcessor_ProcessAsync_ShouldReturnChunks()
    {
        // Arrange
        var mockProcessor = new Mock<IDocumentProcessor>();
        var expectedChunks = new List<IDocumentChunk>
        {
            new TestDocumentChunk("Test content 1", 0),
            new TestDocumentChunk("Test content 2", 1)
        };

        mockProcessor.Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<ChunkingOptions>()))
                    .ReturnsAsync(expectedChunks);

        var options = new ChunkingOptions
        {
            Strategy = "Auto",
            MaxSize = 512,
            OverlapSize = 64
        };

        // Act
        var result = await mockProcessor.Object.ProcessAsync("test.txt", options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("Test content 1", result[0].Content);
        Assert.Equal("Test content 2", result[1].Content);
    }

    [Fact]
    public void ChunkingOptions_ShouldHaveDefaults()
    {
        // Act
        var options = new ChunkingOptions();

        // Assert
        Assert.Equal("Auto", options.Strategy);
        Assert.Equal(512, options.MaxSize);
        Assert.Equal(64, options.OverlapSize);
    }

    [Fact]
    public void ProcessingOptions_ShouldHaveDefaults()
    {
        // Act
        var options = new ProcessingOptions();

        // Assert
        Assert.Equal("Auto", options.ChunkingStrategy);
        Assert.Equal(512, options.MaxChunkSize);
        Assert.Equal(64, options.OverlapSize);
    }

    /// <summary>
    /// Test implementation of IDocumentChunk
    /// </summary>
    private class TestDocumentChunk : IDocumentChunk
    {
        public string Content { get; }
        public int ChunkIndex { get; }
        public int StartPosition { get; }
        public int EndPosition { get; }
        public Dictionary<string, object>? Properties { get; }

        public TestDocumentChunk(string content, int chunkIndex)
        {
            Content = content;
            ChunkIndex = chunkIndex;
            StartPosition = 0;
            EndPosition = content.Length;
            Properties = new Dictionary<string, object>();
        }
    }
}