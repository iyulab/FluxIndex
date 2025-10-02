using FluxIndex.Extensions.WebFlux;
using WebFlux.Core.Options;
using Xunit;

namespace FluxIndex.Extensions.WebFlux.Tests;

/// <summary>
/// Tests for WebFlux options configuration
/// </summary>
public class WebFluxOptionsTests
{
    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        // Act
        var options = new WebFluxOptions();

        // Assert
        Assert.Equal(ChunkingStrategyType.Auto, options.DefaultChunkingStrategy);
        Assert.Equal(512, options.DefaultMaxChunkSize);
        Assert.Equal(50, options.DefaultChunkOverlap);
        Assert.False(options.DefaultIncludeImages);
        Assert.True(options.UseStreamingApi);
    }

    [Theory]
    [InlineData(ChunkingStrategyType.Smart, 1024, 128, true, false)]
    [InlineData(ChunkingStrategyType.Intelligent, 512, 64, false, true)]
    [InlineData(ChunkingStrategyType.Semantic, 2048, 256, true, true)]
    public void SetProperties_ShouldUpdateValues(
        ChunkingStrategyType strategy,
        int maxChunk,
        int overlap,
        bool includeImages,
        bool useStreaming)
    {
        // Arrange
        var options = new WebFluxOptions();

        // Act
        options.DefaultChunkingStrategy = strategy;
        options.DefaultMaxChunkSize = maxChunk;
        options.DefaultChunkOverlap = overlap;
        options.DefaultIncludeImages = includeImages;
        options.UseStreamingApi = useStreaming;

        // Assert
        Assert.Equal(strategy, options.DefaultChunkingStrategy);
        Assert.Equal(maxChunk, options.DefaultMaxChunkSize);
        Assert.Equal(overlap, options.DefaultChunkOverlap);
        Assert.Equal(includeImages, options.DefaultIncludeImages);
        Assert.Equal(useStreaming, options.UseStreamingApi);
    }

    [Fact]
    public void WebFluxProcessingOptions_ShouldSetDefaults()
    {
        // Act
        var options = new WebFluxProcessingOptions();

        // Assert
        Assert.Equal(ChunkingStrategyType.Auto, options.ChunkingStrategy);
        Assert.Equal(512, options.MaxChunkSize);
        Assert.Equal(50, options.ChunkOverlap);
        Assert.False(options.IncludeImages);
    }
}
