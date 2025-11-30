using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// Tests for ContextualEmbeddingService
/// </summary>
public class ContextualEmbeddingServiceTests
{
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly Mock<IContextualHeaderGenerator> _mockHeaderGenerator;
    private readonly ILogger<ContextualEmbeddingService> _logger;

    public ContextualEmbeddingServiceTests()
    {
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _mockHeaderGenerator = new Mock<IContextualHeaderGenerator>();
        _logger = NullLogger<ContextualEmbeddingService>.Instance;

        _mockEmbeddingService
            .Setup(x => x.GetModelName())
            .Returns("test-model");

        _mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });
    }

    private ContextualEmbeddingService CreateService()
    {
        var options = new ContextualEmbeddingOptions
        {
            LlmThreshold = 0.7,
            ContextPosition = ContextPosition.Prepend,
            GenerateDualEmbeddings = false,
            MaxCombinedLength = 8192
        };

        return new ContextualEmbeddingService(
            _mockEmbeddingService.Object,
            _mockHeaderGenerator.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            _logger);
    }

    private Mock<IEnrichedChunk> CreateMockChunk(string content, double contextDependency = 0.5)
    {
        var mockChunk = new Mock<IEnrichedChunk>();
        mockChunk.Setup(x => x.ChunkId).Returns(Guid.NewGuid().ToString());
        mockChunk.Setup(x => x.Content).Returns(content);
        mockChunk.Setup(x => x.ContextDependency).Returns(contextDependency);
        return mockChunk;
    }

    #region Single Chunk Embedding Tests

    [Fact]
    public async Task GenerateContextualEmbeddingAsync_ValidChunk_ReturnsEmbedding()
    {
        // Arrange
        var service = CreateService();
        var chunk = CreateMockChunk("Test content about machine learning.");
        var contextHeader = "This chunk discusses ML concepts.";

        _mockHeaderGenerator
            .Setup(x => x.GenerateAsync(It.IsAny<IEnrichedChunk>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contextHeader);

        // Act
        var result = await service.GenerateContextualEmbeddingAsync(chunk.Object);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(chunk.Object.ChunkId, result.ChunkId);
        Assert.Equal(contextHeader, result.ContextualHeader);
        Assert.NotNull(result.Embedding);
    }

    [Fact]
    public async Task GenerateContextualEmbeddingAsync_WithDocumentSummary_PassesSummary()
    {
        // Arrange
        var service = CreateService();
        var chunk = CreateMockChunk("Chunk content.");
        var documentSummary = "This is a document about AI.";
        var contextHeader = "Context header";

        _mockHeaderGenerator
            .Setup(x => x.GenerateAsync(It.IsAny<IEnrichedChunk>(), documentSummary, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contextHeader);

        // Act
        var result = await service.GenerateContextualEmbeddingAsync(chunk.Object, documentSummary);

        // Assert
        Assert.NotNull(result);
        _mockHeaderGenerator.Verify(
            x => x.GenerateAsync(It.IsAny<IEnrichedChunk>(), documentSummary, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateContextualEmbeddingAsync_EmptyHeader_UsesOriginalContent()
    {
        // Arrange
        var service = CreateService();
        var chunk = CreateMockChunk("Original content.");

        _mockHeaderGenerator
            .Setup(x => x.GenerateAsync(It.IsAny<IEnrichedChunk>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        // Act
        var result = await service.GenerateContextualEmbeddingAsync(chunk.Object);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(chunk.Object.Content, result.ContextualContent);
    }

    #endregion

    #region Batch Embedding Tests

    [Fact]
    public async Task GenerateContextualEmbeddingsBatchAsync_MultipleChunks_ReturnsAllEmbeddings()
    {
        // Arrange
        var service = CreateService();
        var chunks = new List<IEnrichedChunk>
        {
            CreateMockChunk("Content 1").Object,
            CreateMockChunk("Content 2").Object,
            CreateMockChunk("Content 3").Object
        };

        var headers = new Dictionary<string, string>
        {
            { chunks[0].ChunkId, "Header 1" },
            { chunks[1].ChunkId, "Header 2" },
            { chunks[2].ChunkId, "Header 3" }
        };

        _mockHeaderGenerator
            .Setup(x => x.GenerateBatchAsync(It.IsAny<IEnumerable<IEnrichedChunk>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(headers);

        _mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingsBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]>
            {
                new float[] { 0.1f, 0.2f },
                new float[] { 0.3f, 0.4f },
                new float[] { 0.5f, 0.6f }
            });

        // Act
        var results = await service.GenerateContextualEmbeddingsBatchAsync(chunks);

        // Assert
        Assert.NotNull(results);
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task GenerateContextualEmbeddingsBatchAsync_EmptyChunks_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var chunks = new List<IEnrichedChunk>();

        // Act
        var results = await service.GenerateContextualEmbeddingsBatchAsync(chunks);

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    #endregion

    #region Dual Embedding Tests

    [Fact]
    public async Task GenerateDualEmbeddingAsync_ValidChunk_ReturnsBothEmbeddings()
    {
        // Arrange
        var service = CreateService();
        var chunk = CreateMockChunk("Test content.");
        var contextHeader = "Contextual header";

        _mockHeaderGenerator
            .Setup(x => x.GenerateAsync(It.IsAny<IEnrichedChunk>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contextHeader);

        // Act
        var result = await service.GenerateDualEmbeddingAsync(chunk.Object);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ContextualEmbedding);
        Assert.NotNull(result.StandardEmbedding);
        Assert.Equal(contextHeader, result.ContextualHeader);
    }

    #endregion

    #region Context Source Detection Tests

    [Fact]
    public async Task GenerateContextualEmbeddingAsync_HighContextDependency_ReturnsLlmSource()
    {
        // Arrange
        var service = CreateService();
        var chunk = CreateMockChunk("Content", contextDependency: 0.9);

        _mockHeaderGenerator
            .Setup(x => x.GenerateAsync(It.IsAny<IEnrichedChunk>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Header");

        // Act
        var result = await service.GenerateContextualEmbeddingAsync(chunk.Object);

        // Assert
        Assert.Equal(ContextSource.LlmGenerated, result.ContextSource);
    }

    [Fact]
    public async Task GenerateContextualEmbeddingAsync_LowContextDependency_ReturnsRuleBasedSource()
    {
        // Arrange
        var service = CreateService();
        var chunk = CreateMockChunk("Content", contextDependency: 0.3);

        _mockHeaderGenerator
            .Setup(x => x.GenerateAsync(It.IsAny<IEnrichedChunk>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Header");

        // Act
        var result = await service.GenerateContextualEmbeddingAsync(chunk.Object);

        // Assert
        Assert.Equal(ContextSource.RuleBased, result.ContextSource);
    }

    #endregion
}
