using FluentAssertions;
using FluxIndex.AI.LocalEmbedder;
using FluxIndex.AI.LocalEmbedder.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FluxIndex.AI.LocalEmbedder.Tests;

/// <summary>
/// Tests for LocalEmbedder Service
/// Note: Full integration tests with actual model loading are skipped by default
/// as they require model download and can be slow
/// </summary>
public class LocalEmbedderServiceTests
{
    private readonly Mock<IOptions<LocalEmbedderOptions>> _mockOptions;
    private readonly Mock<ILogger<LocalEmbedderService>> _mockLogger;
    private readonly IMemoryCache _cache;

    public LocalEmbedderServiceTests()
    {
        _mockOptions = new Mock<IOptions<LocalEmbedderOptions>>();
        _mockLogger = new Mock<ILogger<LocalEmbedderService>>();
        _cache = new MemoryCache(new MemoryCacheOptions());

        // Setup default mock options
        _mockOptions.Setup(x => x.Value).Returns(new LocalEmbedderOptions
        {
            ModelId = "all-MiniLM-L6-v2",
            ExecutionProvider = LocalEmbedderExecutionProvider.CPU,
            PoolingMode = LocalEmbedderPoolingMode.Mean,
            MaxSequenceLength = 512,
            MaxTokens = 8192
        });
    }

    [Fact]
    public void Constructor_WithValidOptions_ShouldInitialize()
    {
        // Act & Assert - Should not throw
        var service = new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, _cache);
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithInvalidOptions_ShouldThrow()
    {
        // Arrange
        _mockOptions.Setup(x => x.Value).Returns(new LocalEmbedderOptions
        {
            ModelId = "" // Invalid
        });

        // Act & Assert
        var action = () => new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, _cache);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetModelName_ShouldReturnConfiguredModel()
    {
        // Arrange
        var service = new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var modelName = service.GetModelName();

        // Assert
        modelName.Should().Be("all-MiniLM-L6-v2");
    }

    [Fact]
    public void GetEmbeddingDimension_ShouldReturnExpectedDimension()
    {
        // Arrange
        var service = new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var dimension = service.GetEmbeddingDimension();

        // Assert
        dimension.Should().Be(384); // all-MiniLM-L6-v2 has 384 dimensions
    }

    [Fact]
    public void GetEmbeddingDimension_WithDifferentModel_ShouldReturnCorrectDimension()
    {
        // Arrange
        _mockOptions.Setup(x => x.Value).Returns(new LocalEmbedderOptions
        {
            ModelId = "bge-base-en-v1.5" // 768 dimensions
        });
        var service = new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var dimension = service.GetEmbeddingDimension();

        // Assert
        dimension.Should().Be(768);
    }

    [Fact]
    public void GetMaxTokens_ShouldReturnValidValue()
    {
        // Arrange
        var service = new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var maxTokens = service.GetMaxTokens();

        // Assert
        maxTokens.Should().Be(8192);
    }

    [Fact]
    public async Task CountTokensAsync_WithValidText_ShouldReturnPositiveCount()
    {
        // Arrange
        var service = new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, _cache);
        var text = "This is a test text for token counting.";

        // Act
        var tokenCount = await service.CountTokensAsync(text);

        // Assert
        tokenCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CountTokensAsync_WithEmptyText_ShouldReturnZero()
    {
        // Arrange
        var service = new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var tokenCount = await service.CountTokensAsync(string.Empty);

        // Assert
        tokenCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateEmbeddingAsync_WithInvalidInput_ShouldReturnEmptyArray(string? input)
    {
        // Arrange
        var service = new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var result = await service.GenerateEmbeddingAsync(input!, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateEmbeddingsBatchAsync_WithEmptyList_ShouldReturnEmptyResult()
    {
        // Arrange
        var service = new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var result = await service.GenerateEmbeddingsBatchAsync(Array.Empty<string>());

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var service = new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act & Assert
        var action = () => service.Dispose();
        action.Should().NotThrow();
    }

    [Fact]
    public async Task DisposeAsync_ShouldNotThrow()
    {
        // Arrange
        var service = new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act & Assert
        var action = async () => await service.DisposeAsync();
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public void Constructor_WithNullCache_ShouldInitialize()
    {
        // Act & Assert - Should not throw even without cache
        var service = new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, null);
        service.Should().NotBeNull();
    }

    [Theory]
    [InlineData("all-MiniLM-L6-v2", 384)]
    [InlineData("all-mpnet-base-v2", 768)]
    [InlineData("bge-small-en-v1.5", 384)]
    [InlineData("bge-base-en-v1.5", 768)]
    [InlineData("multilingual-e5-small", 384)]
    [InlineData("multilingual-e5-base", 768)]
    public void GetEmbeddingDimension_ShouldMatchModelDimensions(string modelId, int expectedDimension)
    {
        // Arrange
        _mockOptions.Setup(x => x.Value).Returns(new LocalEmbedderOptions
        {
            ModelId = modelId
        });
        var service = new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, _cache);

        // Act
        var dimension = service.GetEmbeddingDimension();

        // Assert
        dimension.Should().Be(expectedDimension);
    }

    [Fact]
    public void Constructor_WithAllExecutionProviders_ShouldInitialize()
    {
        // Test all execution providers can be configured
        foreach (LocalEmbedderExecutionProvider provider in Enum.GetValues<LocalEmbedderExecutionProvider>())
        {
            // Arrange
            _mockOptions.Setup(x => x.Value).Returns(new LocalEmbedderOptions
            {
                ModelId = "all-MiniLM-L6-v2",
                ExecutionProvider = provider
            });

            // Act & Assert - Should not throw
            var service = new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, _cache);
            service.Should().NotBeNull();
        }
    }

    [Fact]
    public void Constructor_WithAllPoolingModes_ShouldInitialize()
    {
        // Test all pooling modes can be configured
        foreach (LocalEmbedderPoolingMode mode in Enum.GetValues<LocalEmbedderPoolingMode>())
        {
            // Arrange
            _mockOptions.Setup(x => x.Value).Returns(new LocalEmbedderOptions
            {
                ModelId = "all-MiniLM-L6-v2",
                PoolingMode = mode
            });

            // Act & Assert - Should not throw
            var service = new LocalEmbedderService(_mockOptions.Object, _mockLogger.Object, _cache);
            service.Should().NotBeNull();
        }
    }
}
