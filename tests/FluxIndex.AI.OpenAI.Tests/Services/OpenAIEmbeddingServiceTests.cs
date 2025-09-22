using FluxIndex.AI.OpenAI.Services;
using FluxIndex.AI.OpenAI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FluxIndex.AI.OpenAI.Tests.Services;

/// <summary>
/// Tests for OpenAI Embedding Service
/// </summary>
public class OpenAIEmbeddingServiceTests
{
    private readonly Mock<IOptions<OpenAIOptions>> _mockOptions;
    private readonly Mock<ILogger<OpenAIEmbeddingService>> _mockLogger;
    private readonly IMemoryCache _cache;

    public OpenAIEmbeddingServiceTests()
    {
        _mockOptions = new Mock<IOptions<OpenAIOptions>>();
        _mockLogger = new Mock<ILogger<OpenAIEmbeddingService>>();
        _cache = new MemoryCache(new MemoryCacheOptions());

        // Setup mock options
        _mockOptions.Setup(x => x.Value).Returns(new OpenAIOptions
        {
            ApiKey = "test-key",
            ModelName = "text-embedding-3-small",
            MaxRetries = 3,
            TimeoutSeconds = 30
        });
    }

    [Fact]
    public void Constructor_WithValidOptions_ShouldInitialize()
    {
        // Act & Assert - Should not throw
        var service = new OpenAIEmbeddingService(_mockOptions.Object, _mockLogger.Object, _cache);

        Assert.NotNull(service);
    }

    [Fact]
    public void GetModelName_ShouldReturnConfiguredModel()
    {
        // Arrange
        var service = new OpenAIEmbeddingService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var modelName = service.GetModelName();

        // Assert
        Assert.Equal("text-embedding-3-small", modelName);
    }

    [Fact]
    public void GetEmbeddingDimension_ShouldReturnExpectedDimension()
    {
        // Arrange
        var service = new OpenAIEmbeddingService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var dimension = service.GetEmbeddingDimension();

        // Assert
        Assert.True(dimension > 0);
    }

    [Fact]
    public void GetMaxTokens_ShouldReturnValidValue()
    {
        // Arrange
        var service = new OpenAIEmbeddingService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var maxTokens = service.GetMaxTokens();

        // Assert
        Assert.True(maxTokens > 0);
    }

    [Fact]
    public async Task CountTokensAsync_WithValidText_ShouldReturnPositiveCount()
    {
        // Arrange
        var service = new OpenAIEmbeddingService(_mockOptions.Object, _mockLogger.Object, _cache);
        var text = "This is a test text for token counting.";

        // Act
        var tokenCount = await service.CountTokensAsync(text);

        // Assert
        Assert.True(tokenCount > 0);
    }

    [Fact]
    public async Task CountTokensAsync_WithEmptyText_ShouldReturnZero()
    {
        // Arrange
        var service = new OpenAIEmbeddingService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var tokenCount = await service.CountTokensAsync(string.Empty);

        // Assert
        Assert.Equal(0, tokenCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateEmbeddingAsync_WithInvalidInput_ShouldReturnEmptyArray(string? input)
    {
        // Arrange
        var service = new OpenAIEmbeddingService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var result = await service.GenerateEmbeddingAsync(input!, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    // Note: OpenAIEmbeddingService doesn't implement IDisposable in current implementation
}