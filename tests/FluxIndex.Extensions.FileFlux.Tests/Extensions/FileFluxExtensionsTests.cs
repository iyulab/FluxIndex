using FluxIndex.Extensions.FileFlux;
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.Extensions.FileFlux.Tests.Extensions;

/// <summary>
/// Tests for FileFlux extension methods
/// </summary>
public class FileFluxExtensionsTests
{
    [Fact]
    public void UseFileFlux_ExtensionMethod_ShouldExist()
    {
        // Arrange
        var builder = new FluxIndexContextBuilder();

        // Act & Assert - Should not throw
        var result = builder.UseFileFlux();
        Assert.NotNull(result);
        Assert.IsType<FluxIndexContextBuilder>(result);
    }

    [Fact]
    public void UseFileFlux_WithOptions_ExtensionMethod_ShouldExist()
    {
        // Arrange
        var builder = new FluxIndexContextBuilder();

        // Act & Assert - Should not throw
        var result = builder.UseFileFlux(options =>
        {
            options.DefaultChunkingStrategy = "Fixed";
            options.DefaultMaxChunkSize = 1024;
            options.DefaultOverlapSize = 128;
        });

        Assert.NotNull(result);
        Assert.IsType<FluxIndexContextBuilder>(result);
    }

    [Fact]
    public void FileFluxOptions_ShouldHaveDefaults()
    {
        // Act
        var options = new FileFluxOptions();

        // Assert
        Assert.Equal("Auto", options.DefaultChunkingStrategy);
        Assert.Equal(512, options.DefaultMaxChunkSize);
        Assert.Equal(64, options.DefaultOverlapSize);
    }

    [Theory]
    [InlineData("Auto", 256, 32)]
    [InlineData("Fixed", 1024, 128)]
    [InlineData("Semantic", 2048, 256)]
    public void FileFluxOptions_ShouldAllowCustomization(string strategy, int maxSize, int overlap)
    {
        // Arrange & Act
        var options = new FileFluxOptions
        {
            DefaultChunkingStrategy = strategy,
            DefaultMaxChunkSize = maxSize,
            DefaultOverlapSize = overlap
        };

        // Assert
        Assert.Equal(strategy, options.DefaultChunkingStrategy);
        Assert.Equal(maxSize, options.DefaultMaxChunkSize);
        Assert.Equal(overlap, options.DefaultOverlapSize);
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

    [Fact]
    public void ChunkingOptions_ShouldHaveDefaults()
    {
        // Act
        var options = new ChunkingOptions();

        // Assert
        Assert.Equal("Auto", options.Strategy);
        Assert.Equal(512, options.MaxChunkSize);
        Assert.Equal(64, options.OverlapSize);
    }
}