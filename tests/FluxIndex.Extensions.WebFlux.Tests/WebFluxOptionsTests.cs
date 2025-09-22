using FluxIndex.Extensions.WebFlux;
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
        Assert.Equal(1, options.MaxDepth);
        Assert.False(options.FollowExternalLinks);
        Assert.Equal("Smart", options.ChunkingStrategy);
        Assert.Equal(512, options.MaxChunkSize);
        Assert.Equal(64, options.ChunkOverlap);
        Assert.False(options.IncludeImages);
    }

    [Fact]
    public void Default_ShouldReturnDefaultConfiguration()
    {
        // Act
        var defaultOptions = WebFluxOptions.Default;

        // Assert
        Assert.Equal(1, defaultOptions.MaxDepth);
        Assert.False(defaultOptions.FollowExternalLinks);
        Assert.Equal("Smart", defaultOptions.ChunkingStrategy);
        Assert.Equal(512, defaultOptions.MaxChunkSize);
        Assert.Equal(64, defaultOptions.ChunkOverlap);
        Assert.False(defaultOptions.IncludeImages);
    }

    [Fact]
    public void DeepCrawl_ShouldReturnDeepCrawlConfiguration()
    {
        // Act
        var deepCrawlOptions = WebFluxOptions.DeepCrawl;

        // Assert
        Assert.Equal(3, deepCrawlOptions.MaxDepth);
        Assert.False(deepCrawlOptions.FollowExternalLinks);
        Assert.Equal("Intelligent", deepCrawlOptions.ChunkingStrategy);
        Assert.Equal(1024, deepCrawlOptions.MaxChunkSize);
        Assert.Equal(128, deepCrawlOptions.ChunkOverlap);
    }

    [Fact]
    public void LargeContent_ShouldReturnLargeContentConfiguration()
    {
        // Act
        var largeContentOptions = WebFluxOptions.LargeContent;

        // Assert
        Assert.Equal(1, largeContentOptions.MaxDepth);
        Assert.Equal("Auto", largeContentOptions.ChunkingStrategy);
        Assert.Equal(2048, largeContentOptions.MaxChunkSize);
        Assert.Equal(256, largeContentOptions.ChunkOverlap);
        Assert.True(largeContentOptions.IncludeImages);
    }

    [Theory]
    [InlineData(1, false, "Smart", 512, 64, false)]
    [InlineData(3, true, "Intelligent", 1024, 128, true)]
    [InlineData(5, false, "Auto", 2048, 256, false)]
    public void SetProperties_ShouldUpdateValues(int maxDepth, bool followExternal, string strategy, int maxChunk, int overlap, bool includeImages)
    {
        // Arrange
        var options = new WebFluxOptions();

        // Act
        options.MaxDepth = maxDepth;
        options.FollowExternalLinks = followExternal;
        options.ChunkingStrategy = strategy;
        options.MaxChunkSize = maxChunk;
        options.ChunkOverlap = overlap;
        options.IncludeImages = includeImages;

        // Assert
        Assert.Equal(maxDepth, options.MaxDepth);
        Assert.Equal(followExternal, options.FollowExternalLinks);
        Assert.Equal(strategy, options.ChunkingStrategy);
        Assert.Equal(maxChunk, options.MaxChunkSize);
        Assert.Equal(overlap, options.ChunkOverlap);
        Assert.Equal(includeImages, options.IncludeImages);
    }
}