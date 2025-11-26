using FluentAssertions;
using FluxIndex.AI.LocalReranker;
using Xunit;

namespace FluxIndex.AI.LocalReranker.Tests;

public class LocalRerankerOptionsTests
{
    [Fact]
    public void DefaultOptions_ShouldHaveExpectedValues()
    {
        // Arrange & Act
        var options = new LocalRerankerOptions();

        // Assert
        options.ModelId.Should().Be("default");
        options.MaxSequenceLength.Should().BeNull();
        options.UseGpu.Should().BeFalse();
        options.BatchSize.Should().Be(32);
        options.CacheDirectory.Should().BeNull();
        options.WarmupOnStartup.Should().BeTrue();
        options.ThreadCount.Should().BeNull();
    }

    [Theory]
    [InlineData("default")]
    [InlineData("quality")]
    [InlineData("fast")]
    public void ModelId_ShouldAcceptValidAliases(string modelId)
    {
        // Arrange
        var options = new LocalRerankerOptions();

        // Act
        options.ModelId = modelId;

        // Assert
        options.ModelId.Should().Be(modelId);
    }

    [Fact]
    public void Options_ShouldBeConfigurable()
    {
        // Arrange & Act
        var options = new LocalRerankerOptions
        {
            ModelId = "quality",
            MaxSequenceLength = 256,
            UseGpu = true,
            BatchSize = 64,
            CacheDirectory = "/tmp/models",
            WarmupOnStartup = false,
            ThreadCount = 4
        };

        // Assert
        options.ModelId.Should().Be("quality");
        options.MaxSequenceLength.Should().Be(256);
        options.UseGpu.Should().BeTrue();
        options.BatchSize.Should().Be(64);
        options.CacheDirectory.Should().Be("/tmp/models");
        options.WarmupOnStartup.Should().BeFalse();
        options.ThreadCount.Should().Be(4);
    }
}
