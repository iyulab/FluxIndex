using FluxIndex.AI.OpenAI;
using Xunit;

namespace FluxIndex.AI.OpenAI.Tests;

/// <summary>
/// Tests for OpenAI Options configuration
/// </summary>
public class OpenAIOptionsTests
{
    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        // Act
        var options = new OpenAIOptions();

        // Assert
        Assert.NotNull(options.ModelName);
        Assert.True(options.MaxRetries > 0);
        Assert.True(options.TimeoutSeconds > 0);
        Assert.True(options.MaxTokens > 0);
    }

    [Fact]
    public void SetApiKey_ShouldUpdateProperty()
    {
        // Arrange
        var options = new OpenAIOptions();
        var apiKey = "test-api-key";

        // Act
        options.ApiKey = apiKey;

        // Assert
        Assert.Equal(apiKey, options.ApiKey);
    }

    [Fact]
    public void SetModelName_ShouldUpdateProperty()
    {
        // Arrange
        var options = new OpenAIOptions();
        var model = "text-embedding-3-large";

        // Act
        options.ModelName = model;

        // Assert
        Assert.Equal(model, options.ModelName);
    }

    [Fact]
    public void SetEndpoint_ShouldUpdateProperty()
    {
        // Arrange
        var options = new OpenAIOptions();
        var endpoint = "https://test.openai.azure.com";

        // Act
        options.Endpoint = endpoint;

        // Assert
        Assert.Equal(endpoint, options.Endpoint);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void SetMaxRetries_ShouldUpdateProperty(int maxRetries)
    {
        // Arrange
        var options = new OpenAIOptions();

        // Act
        options.MaxRetries = maxRetries;

        // Assert
        Assert.Equal(maxRetries, options.MaxRetries);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void SetTimeoutSeconds_ShouldUpdateProperty(int timeoutSeconds)
    {
        // Arrange
        var options = new OpenAIOptions();

        // Act
        options.TimeoutSeconds = timeoutSeconds;

        // Assert
        Assert.Equal(timeoutSeconds, options.TimeoutSeconds);
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(8192)]
    public void SetMaxTokens_ShouldUpdateProperty(int maxTokens)
    {
        // Arrange
        var options = new OpenAIOptions();

        // Act
        options.MaxTokens = maxTokens;

        // Assert
        Assert.Equal(maxTokens, options.MaxTokens);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(1536)]
    [InlineData(3072)]
    public void SetDimensions_ShouldUpdateProperty(int dimensions)
    {
        // Arrange
        var options = new OpenAIOptions();

        // Act
        options.Dimensions = dimensions;

        // Assert
        Assert.Equal(dimensions, options.Dimensions);
    }
}