using FluxIndex.Extensions.FileFlux;
using FileFlux;
using FileFlux.Domain;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq;
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
        var expectedChunks = new[]
        {
            new DocumentChunk
            {
                Content = "Test content 1",
                Index = 0,
                Location = new SourceLocation { StartChar = 0, EndChar = 13 }
            },
            new DocumentChunk
            {
                Content = "Test content 2",
                Index = 1,
                Location = new SourceLocation { StartChar = 0, EndChar = 13 }
            }
        };

        mockProcessor.Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<ChunkingOptions>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(expectedChunks);

        var options = new ChunkingOptions
        {
            Strategy = ChunkingStrategies.Auto,
            MaxChunkSize = 1024,
            OverlapSize = 128
        };

        // Act
        var result = await mockProcessor.Object.ProcessAsync("test.txt", options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal("Test content 1", result[0].Content);
        Assert.Equal("Test content 2", result[1].Content);
    }

    [Fact]
    public void ChunkingOptions_ShouldHaveDefaults()
    {
        // Act
        var options = new ChunkingOptions();

        // Assert
        Assert.Equal(ChunkingStrategies.Auto, options.Strategy);
        Assert.Equal(1024, options.MaxChunkSize);
        Assert.Equal(128, options.OverlapSize);
    }

    [Fact]
    public void ProcessingOptions_ShouldHaveDefaults()
    {
        // Act
        var options = new ProcessingOptions();

        // Assert
        Assert.Equal(ChunkingStrategies.Auto, options.ChunkingStrategy);
        Assert.Equal(1024, options.MaxChunkSize);
        Assert.Equal(128, options.OverlapSize);
    }

}