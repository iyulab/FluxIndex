using FluentAssertions;
using FluxIndex.Stack.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Stack.Api.Tests;

/// <summary>
/// Unit tests for OpenAIEmbeddingProvider construction and known dimensions.
/// Note: Actual API call tests require an API key (Category=RequiresApiKey).
/// </summary>
public class OpenAIEmbeddingProviderTests
{
    [Fact]
    public void Constructor_SetsModelNameAndDimension()
    {
        // Arrange & Act
        var logger = Substitute.For<ILogger>();
        var provider = new OpenAIEmbeddingProvider(
            apiKey: "test-key",
            modelName: "text-embedding-3-small",
            endpointUrl: null,
            logger: logger);

        // Assert
        provider.ModelName.Should().Be("text-embedding-3-small");
        provider.EmbeddingDimension.Should().Be(1536);
    }

    [Theory]
    [InlineData("text-embedding-3-small", 1536)]
    [InlineData("text-embedding-3-large", 3072)]
    [InlineData("text-embedding-ada-002", 1536)]
    [InlineData("unknown-model", 1536)] // default fallback
    public void Constructor_KnownDimensions_AreCorrect(string modelName, int expectedDim)
    {
        var logger = Substitute.For<ILogger>();
        var provider = new OpenAIEmbeddingProvider("test-key", modelName, null, logger);

        provider.EmbeddingDimension.Should().Be(expectedDim);
    }

    [Fact]
    public void Constructor_WithEndpointUrl_CreatesAzureClient()
    {
        // Arrange & Act — should not throw
        var logger = Substitute.For<ILogger>();
        var provider = new OpenAIEmbeddingProvider(
            apiKey: "test-key",
            modelName: "text-embedding-3-small",
            endpointUrl: "https://my-resource.openai.azure.com/",
            logger: logger);

        // Assert — provider created successfully with endpoint
        provider.ModelName.Should().Be("text-embedding-3-small");
    }

    [Fact]
    public void Constructor_ThrowsOnNullApiKey()
    {
        var logger = Substitute.For<ILogger>();
        var act = () => new OpenAIEmbeddingProvider(null!, "model", null, logger);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyApiKey()
    {
        var logger = Substitute.For<ILogger>();
        var act = () => new OpenAIEmbeddingProvider("", "model", null, logger);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ThrowsOnNullModelName()
    {
        var logger = Substitute.For<ILogger>();
        var act = () => new OpenAIEmbeddingProvider("test-key", null!, null, logger);
        act.Should().Throw<ArgumentException>();
    }
}
