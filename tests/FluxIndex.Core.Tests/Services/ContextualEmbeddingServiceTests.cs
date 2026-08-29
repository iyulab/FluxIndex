using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// Tests for ContextualEmbeddingService
/// </summary>
public class ContextualEmbeddingServiceTests
{
    private readonly IEmbeddingService _mockEmbeddingService;
    private readonly IContextualHeaderGenerator _mockHeaderGenerator;
    private readonly ILogger<ContextualEmbeddingService> _logger;

    public ContextualEmbeddingServiceTests()
    {
        _mockEmbeddingService = Substitute.For<IEmbeddingService>();
        _mockHeaderGenerator = Substitute.For<IContextualHeaderGenerator>();
        _logger = NullLogger<ContextualEmbeddingService>.Instance;

        _mockEmbeddingService.GetModelName().Returns("test-model");

        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f, 0.2f, 0.3f });
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
            _mockEmbeddingService,
            _mockHeaderGenerator,
            Microsoft.Extensions.Options.Options.Create(options),
            _logger);
    }

    private IEnrichedChunk CreateMockChunk(string content, double contextDependency = 0.5)
    {
        var mockChunk = Substitute.For<IEnrichedChunk>();
        mockChunk.ChunkId.Returns(Guid.NewGuid().ToString());
        mockChunk.Content.Returns(content);
        mockChunk.ContextDependency.Returns(contextDependency);
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

        _mockHeaderGenerator.GenerateAsync(Arg.Any<IEnrichedChunk>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(contextHeader);

        // Act
        var result = await service.GenerateContextualEmbeddingAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(chunk.ChunkId, result.ChunkId);
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

        _mockHeaderGenerator.GenerateAsync(Arg.Any<IEnrichedChunk>(), documentSummary, Arg.Any<CancellationToken>()).Returns(contextHeader);

        // Act
        var result = await service.GenerateContextualEmbeddingAsync(chunk, documentSummary, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        await _mockHeaderGenerator.Received(1).GenerateAsync(Arg.Any<IEnrichedChunk>(), documentSummary, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateContextualEmbeddingAsync_EmptyHeader_UsesOriginalContent()
    {
        // Arrange
        var service = CreateService();
        var chunk = CreateMockChunk("Original content.");

        _mockHeaderGenerator.GenerateAsync(Arg.Any<IEnrichedChunk>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(string.Empty);

        // Act
        var result = await service.GenerateContextualEmbeddingAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(chunk.Content, result.ContextualContent);
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
            CreateMockChunk("Content 1"),
            CreateMockChunk("Content 2"),
            CreateMockChunk("Content 3")
        };

        var headers = new Dictionary<string, string>
        {
            { chunks[0].ChunkId, "Header 1" },
            { chunks[1].ChunkId, "Header 2" },
            { chunks[2].ChunkId, "Header 3" }
        };

        _mockHeaderGenerator.GenerateBatchAsync(Arg.Any<IEnumerable<IEnrichedChunk>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(headers);

        _mockEmbeddingService.GenerateEmbeddingsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(new List<float[]>
            {
                new float[] { 0.1f, 0.2f },
                new float[] { 0.3f, 0.4f },
                new float[] { 0.5f, 0.6f }
            });

        // Act
        var results = await service.GenerateContextualEmbeddingsBatchAsync(chunks, cancellationToken: TestContext.Current.CancellationToken);

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
        var results = await service.GenerateContextualEmbeddingsBatchAsync(chunks, cancellationToken: TestContext.Current.CancellationToken);

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

        _mockHeaderGenerator.GenerateAsync(Arg.Any<IEnrichedChunk>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(contextHeader);

        // Act
        var result = await service.GenerateDualEmbeddingAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

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

        _mockHeaderGenerator.GenerateAsync(Arg.Any<IEnrichedChunk>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns("Header");

        // Act
        var result = await service.GenerateContextualEmbeddingAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContextSource.LlmGenerated, result.ContextSource);
    }

    [Fact]
    public async Task GenerateContextualEmbeddingAsync_LowContextDependency_ReturnsRuleBasedSource()
    {
        // Arrange
        var service = CreateService();
        var chunk = CreateMockChunk("Content", contextDependency: 0.3);

        _mockHeaderGenerator.GenerateAsync(Arg.Any<IEnrichedChunk>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns("Header");

        // Act
        var result = await service.GenerateContextualEmbeddingAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContextSource.RuleBased, result.ContextSource);
    }

    #endregion
}
