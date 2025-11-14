using FluxIndex.AI.OpenAI.Services;
using FluxIndex.AI.OpenAI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FluxIndex.AI.OpenAI.Tests.Services;

/// <summary>
/// Tests for OpenAI Text Completion Service
/// </summary>
public class OpenAITextCompletionServiceTests
{
    private readonly Mock<IOptions<OpenAIOptions>> _mockOptions;
    private readonly Mock<ILogger<OpenAITextCompletionService>> _mockLogger;
    private readonly IMemoryCache _cache;

    public OpenAITextCompletionServiceTests()
    {
        _mockOptions = new Mock<IOptions<OpenAIOptions>>();
        _mockLogger = new Mock<ILogger<OpenAITextCompletionService>>();
        _cache = new MemoryCache(new MemoryCacheOptions());

        // Setup mock options
        _mockOptions.Setup(x => x.Value).Returns(new OpenAIOptions
        {
            ApiKey = "test-key",
            ModelName = "gpt-5-nano",
            MaxRetries = 3,
            TimeoutSeconds = 30
        });
    }

    [Fact]
    public void Constructor_WithValidOptions_ShouldInitialize()
    {
        // Act & Assert - Should not throw
        var service = new OpenAITextCompletionService(_mockOptions.Object, _mockLogger.Object, _cache);

        Assert.NotNull(service);
    }

    [Fact]
    public void CountTokens_WithValidText_ShouldReturnPositiveCount()
    {
        // Arrange
        var service = new OpenAITextCompletionService(_mockOptions.Object, _mockLogger.Object, _cache);
        var text = "This is a test text for token counting.";

        // Act
        var tokenCount = service.CountTokens(text);

        // Assert
        Assert.True(tokenCount > 0);
    }

    [Fact]
    public void CountTokens_WithEmptyText_ShouldReturnZero()
    {
        // Arrange
        var service = new OpenAITextCompletionService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var tokenCount = service.CountTokens(string.Empty);

        // Assert
        Assert.Equal(0, tokenCount);
    }

    [Fact]
    public void CountTokens_WithNullText_ShouldReturnZero()
    {
        // Arrange
        var service = new OpenAITextCompletionService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var tokenCount = service.CountTokens(null!);

        // Assert
        Assert.Equal(0, tokenCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateCompletionAsync_WithInvalidInput_ShouldReturnEmptyString(string? input)
    {
        // Arrange
        var service = new OpenAITextCompletionService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var result = await service.GenerateCompletionAsync(input!, maxTokens: 500, cancellationToken: CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateJsonCompletionAsync_WithInvalidInput_ShouldReturnEmptyJson(string? input)
    {
        // Arrange
        var service = new OpenAITextCompletionService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var result = await service.GenerateJsonCompletionAsync(input!, maxTokens: 500, cancellationToken: CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("{}", result);
    }

    [Fact]
    public async Task GenerateCompletionAsync_WithCancelledToken_ShouldThrowOperationCancelledException()
    {
        // Arrange
        var service = new OpenAITextCompletionService(_mockOptions.Object, _mockLogger.Object, _cache);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        // TaskCanceledException derives from OperationCanceledException, so we check for the base type
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await service.GenerateCompletionAsync("test prompt", cancellationToken: cts.Token);
        });
    }

    [Fact]
    public async Task GenerateJsonCompletionAsync_WithCancelledToken_ShouldThrowOperationCancelledException()
    {
        // Arrange
        var service = new OpenAITextCompletionService(_mockOptions.Object, _mockLogger.Object, _cache);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        // TaskCanceledException derives from OperationCanceledException, so we check for the base type
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await service.GenerateJsonCompletionAsync("test prompt", cancellationToken: cts.Token);
        });
    }

    [Fact]
    public void Constructor_WithAzureEndpoint_ShouldInitialize()
    {
        // Arrange
        var azureOptions = new Mock<IOptions<OpenAIOptions>>();
        azureOptions.Setup(x => x.Value).Returns(new OpenAIOptions
        {
            ApiKey = "test-key",
            ModelName = "gpt-5-nano",
            Endpoint = "https://test.openai.azure.com",
            MaxRetries = 3,
            TimeoutSeconds = 30
        });

        // Act & Assert - Should not throw
        var service = new OpenAITextCompletionService(azureOptions.Object, _mockLogger.Object, _cache);

        Assert.NotNull(service);
    }

    [Fact]
    public void CountTokens_WithLongText_ShouldReturnReasonableCount()
    {
        // Arrange
        var service = new OpenAITextCompletionService(_mockOptions.Object, _mockLogger.Object, _cache);
        var text = new string('a', 1000); // 1000 characters

        // Act
        var tokenCount = service.CountTokens(text);

        // Assert
        // Approximation is ~4 chars per token, so 1000 chars ≈ 250 tokens
        Assert.True(tokenCount > 200 && tokenCount < 300);
    }

    [Fact]
    public void CountTokens_WithShortText_ShouldReturnLowCount()
    {
        // Arrange
        var service = new OpenAITextCompletionService(_mockOptions.Object, _mockLogger.Object, _cache);
        var text = "Hello";

        // Act
        var tokenCount = service.CountTokens(text);

        // Assert
        Assert.True(tokenCount >= 1 && tokenCount <= 2);
    }
}
