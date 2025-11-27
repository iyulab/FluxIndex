using FluentAssertions;
using FluxIndex.AI.LocalEmbedder;
using Xunit;

namespace FluxIndex.AI.LocalEmbedder.Tests;

public class LocalEmbedderOptionsTests
{
    [Fact]
    public void DefaultOptions_ShouldHaveExpectedValues()
    {
        // Arrange & Act
        var options = new LocalEmbedderOptions();

        // Assert
        options.ModelId.Should().Be("all-MiniLM-L6-v2");
        options.ExecutionProvider.Should().Be(LocalEmbedderExecutionProvider.CPU);
        options.PoolingMode.Should().Be(LocalEmbedderPoolingMode.Mean);
        options.MaxSequenceLength.Should().Be(512);
        options.NormalizeEmbeddings.Should().BeTrue();
        options.MaxTokens.Should().Be(8192);
        options.Dimensions.Should().BeNull();
    }

    [Theory]
    [InlineData("all-MiniLM-L6-v2")]
    [InlineData("all-mpnet-base-v2")]
    [InlineData("bge-small-en-v1.5")]
    [InlineData("bge-base-en-v1.5")]
    [InlineData("multilingual-e5-small")]
    [InlineData("multilingual-e5-base")]
    public void ModelId_ShouldAcceptValidModels(string modelId)
    {
        // Arrange
        var options = new LocalEmbedderOptions();

        // Act
        options.ModelId = modelId;

        // Assert
        options.ModelId.Should().Be(modelId);
    }

    [Fact]
    public void Options_ShouldBeConfigurable()
    {
        // Arrange & Act
        var options = new LocalEmbedderOptions
        {
            ModelId = "bge-base-en-v1.5",
            ExecutionProvider = LocalEmbedderExecutionProvider.CUDA,
            PoolingMode = LocalEmbedderPoolingMode.Cls,
            MaxSequenceLength = 256,
            NormalizeEmbeddings = false,
            MaxTokens = 4096,
            Dimensions = 768
        };

        // Assert
        options.ModelId.Should().Be("bge-base-en-v1.5");
        options.ExecutionProvider.Should().Be(LocalEmbedderExecutionProvider.CUDA);
        options.PoolingMode.Should().Be(LocalEmbedderPoolingMode.Cls);
        options.MaxSequenceLength.Should().Be(256);
        options.NormalizeEmbeddings.Should().BeFalse();
        options.MaxTokens.Should().Be(4096);
        options.Dimensions.Should().Be(768);
    }

    [Theory]
    [InlineData("all-MiniLM-L6-v2", 384)]
    [InlineData("all-minilm-l6-v2", 384)]
    [InlineData("bge-small-en-v1.5", 384)]
    [InlineData("multilingual-e5-small", 384)]
    [InlineData("all-mpnet-base-v2", 768)]
    [InlineData("bge-base-en-v1.5", 768)]
    [InlineData("multilingual-e5-base", 768)]
    [InlineData("unknown-model", 384)] // Default fallback
    public void GetEffectiveDimensions_ShouldReturnCorrectDimensions(string modelId, int expectedDimensions)
    {
        // Arrange
        var options = new LocalEmbedderOptions { ModelId = modelId };

        // Act
        var dimensions = options.GetEffectiveDimensions();

        // Assert
        dimensions.Should().Be(expectedDimensions);
    }

    [Fact]
    public void GetEffectiveDimensions_WithExplicitDimensions_ShouldReturnOverride()
    {
        // Arrange
        var options = new LocalEmbedderOptions
        {
            ModelId = "all-MiniLM-L6-v2",
            Dimensions = 512  // Override the default 384
        };

        // Act
        var dimensions = options.GetEffectiveDimensions();

        // Assert
        dimensions.Should().Be(512);
    }

    [Fact]
    public void Validate_WithValidOptions_ShouldNotThrow()
    {
        // Arrange
        var options = new LocalEmbedderOptions();

        // Act & Assert
        var action = () => options.Validate();
        action.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithEmptyModelId_ShouldThrow()
    {
        // Arrange
        var options = new LocalEmbedderOptions { ModelId = "" };

        // Act & Assert
        var action = () => options.Validate();
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*ModelId*");
    }

    [Fact]
    public void Validate_WithWhitespaceModelId_ShouldThrow()
    {
        // Arrange
        var options = new LocalEmbedderOptions { ModelId = "   " };

        // Act & Assert
        var action = () => options.Validate();
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*ModelId*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_WithInvalidMaxSequenceLength_ShouldThrow(int maxSequenceLength)
    {
        // Arrange
        var options = new LocalEmbedderOptions { MaxSequenceLength = maxSequenceLength };

        // Act & Assert
        var action = () => options.Validate();
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxSequenceLength*");
    }

    [Theory]
    [InlineData(LocalEmbedderExecutionProvider.CPU)]
    [InlineData(LocalEmbedderExecutionProvider.CUDA)]
    [InlineData(LocalEmbedderExecutionProvider.DirectML)]
    public void ExecutionProvider_ShouldAcceptAllValidValues(LocalEmbedderExecutionProvider provider)
    {
        // Arrange
        var options = new LocalEmbedderOptions();

        // Act
        options.ExecutionProvider = provider;

        // Assert
        options.ExecutionProvider.Should().Be(provider);
    }

    [Theory]
    [InlineData(LocalEmbedderPoolingMode.Cls)]
    [InlineData(LocalEmbedderPoolingMode.Mean)]
    [InlineData(LocalEmbedderPoolingMode.LastToken)]
    public void PoolingMode_ShouldAcceptAllValidValues(LocalEmbedderPoolingMode mode)
    {
        // Arrange
        var options = new LocalEmbedderOptions();

        // Act
        options.PoolingMode = mode;

        // Assert
        options.PoolingMode.Should().Be(mode);
    }
}
