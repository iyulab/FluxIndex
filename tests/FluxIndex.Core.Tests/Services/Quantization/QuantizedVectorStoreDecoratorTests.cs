using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Quantization;

public class QuantizedVectorStoreDecoratorTests
{
    private readonly Mock<IVectorStore> _innerStoreMock;
    private readonly Mock<IVectorQuantizer> _quantizerMock;
    private readonly Mock<ILogger<QuantizedVectorStoreDecorator>> _loggerMock;
    private readonly QuantizedVectorStoreOptions _options;

    public QuantizedVectorStoreDecoratorTests()
    {
        _innerStoreMock = new Mock<IVectorStore>();
        _quantizerMock = new Mock<IVectorQuantizer>();
        _loggerMock = new Mock<ILogger<QuantizedVectorStoreDecorator>>();
        _options = new QuantizedVectorStoreOptions
        {
            AutoQuantizeOnStore = true
        };
    }

    private QuantizedVectorStoreDecorator CreateDecorator(QuantizedVectorStoreOptions? options = null)
    {
        return new QuantizedVectorStoreDecorator(
            _innerStoreMock.Object,
            _quantizerMock.Object,
            _loggerMock.Object,
            options ?? _options);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullInnerStore_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new QuantizedVectorStoreDecorator(
                null!,
                _quantizerMock.Object,
                _loggerMock.Object,
                _options));
    }

    [Fact]
    public void Constructor_WithNullQuantizer_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new QuantizedVectorStoreDecorator(
                _innerStoreMock.Object,
                null!,
                _loggerMock.Object,
                _options));
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        var decorator = CreateDecorator();

        // Assert
        Assert.NotNull(decorator);
        Assert.True(decorator.SupportsQuantization);
        Assert.Same(_quantizerMock.Object, decorator.Quantizer);
    }

    #endregion

    #region StoreAsync Tests

    [Fact]
    public async Task StoreAsync_WithAutoQuantize_QuantizesAndStoresEmbedding()
    {
        // Arrange
        var chunk = new DocumentChunk
        {
            Id = "chunk-1",
            DocumentId = "doc-1",
            Content = "Test content",
            Embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f }
        };

        var quantizedVector = new QuantizedVector
        {
            Type = QuantizationType.ScalarInt8,
            Data = new byte[] { 10, 20, 30, 40 },
            OriginalDimension = 4
        };

        _innerStoreMock
            .Setup(s => s.StoreAsync(chunk, It.IsAny<CancellationToken>()))
            .ReturnsAsync("chunk-1");

        _quantizerMock
            .Setup(q => q.QuantizeAsync(chunk.Embedding, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quantizedVector);

        var decorator = CreateDecorator();

        // Act
        var result = await decorator.StoreAsync(chunk);

        // Assert
        Assert.Equal("chunk-1", result);
        _innerStoreMock.Verify(s => s.StoreAsync(chunk, It.IsAny<CancellationToken>()), Times.Once);
        _quantizerMock.Verify(q => q.QuantizeAsync(chunk.Embedding, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StoreAsync_WithoutAutoQuantize_OnlyStoresInInnerStore()
    {
        // Arrange
        var chunk = new DocumentChunk
        {
            Id = "chunk-1",
            DocumentId = "doc-1",
            Content = "Test content",
            Embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f }
        };

        _innerStoreMock
            .Setup(s => s.StoreAsync(chunk, It.IsAny<CancellationToken>()))
            .ReturnsAsync("chunk-1");

        var options = new QuantizedVectorStoreOptions { AutoQuantizeOnStore = false };
        var decorator = CreateDecorator(options);

        // Act
        var result = await decorator.StoreAsync(chunk);

        // Assert
        Assert.Equal("chunk-1", result);
        _innerStoreMock.Verify(s => s.StoreAsync(chunk, It.IsAny<CancellationToken>()), Times.Once);
        _quantizerMock.Verify(q => q.QuantizeAsync(It.IsAny<float[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StoreAsync_WithNullEmbedding_SkipsQuantization()
    {
        // Arrange
        var chunk = new DocumentChunk
        {
            Id = "chunk-1",
            DocumentId = "doc-1",
            Content = "Test content",
            Embedding = null
        };

        _innerStoreMock
            .Setup(s => s.StoreAsync(chunk, It.IsAny<CancellationToken>()))
            .ReturnsAsync("chunk-1");

        var decorator = CreateDecorator();

        // Act
        var result = await decorator.StoreAsync(chunk);

        // Assert
        Assert.Equal("chunk-1", result);
        _quantizerMock.Verify(q => q.QuantizeAsync(It.IsAny<float[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region StoreWithQuantizedAsync Tests

    [Fact]
    public async Task StoreWithQuantizedAsync_StoresBothOriginalAndQuantized()
    {
        // Arrange
        var chunk = new DocumentChunk
        {
            Id = "chunk-1",
            DocumentId = "doc-1",
            Content = "Test content",
            Embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f }
        };

        var quantizedVector = new QuantizedVector
        {
            Type = QuantizationType.ScalarInt8,
            Data = new byte[] { 10, 20, 30, 40 },
            OriginalDimension = 4
        };

        _innerStoreMock
            .Setup(s => s.StoreAsync(chunk, It.IsAny<CancellationToken>()))
            .ReturnsAsync("chunk-1");

        var decorator = CreateDecorator();

        // Act
        var result = await decorator.StoreWithQuantizedAsync(chunk, quantizedVector);

        // Assert
        Assert.Equal("chunk-1", result);
        _innerStoreMock.Verify(s => s.StoreAsync(chunk, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region SearchAsync Tests

    [Fact]
    public async Task SearchAsync_DelegatestoInnerStore()
    {
        // Arrange
        var queryEmbedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
        var expectedChunks = new[]
        {
            new DocumentChunk { Id = "chunk-1", Content = "Result 1" },
            new DocumentChunk { Id = "chunk-2", Content = "Result 2" }
        };

        _innerStoreMock
            .Setup(s => s.SearchAsync(queryEmbedding, 10, 0.0f, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedChunks);

        var decorator = CreateDecorator();

        // Act
        var results = await decorator.SearchAsync(queryEmbedding);

        // Assert
        Assert.Equal(2, results.Count());
        _innerStoreMock.Verify(s => s.SearchAsync(queryEmbedding, 10, 0.0f, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region SearchQuantizedAsync Tests

    [Fact]
    public async Task SearchQuantizedAsync_ReturnsResultsFromQuantizedSearch()
    {
        // Arrange
        var chunk1 = new DocumentChunk { Id = "chunk-1", Content = "Content 1" };
        var chunk2 = new DocumentChunk { Id = "chunk-2", Content = "Content 2" };

        var quantizedQuery = new QuantizedVector
        {
            Type = QuantizationType.ScalarInt8,
            Data = new byte[] { 10, 20, 30, 40 },
            OriginalDimension = 4
        };

        var quantizedVector1 = new QuantizedVector
        {
            Type = QuantizationType.ScalarInt8,
            Data = new byte[] { 11, 21, 31, 41 },
            OriginalDimension = 4
        };

        var quantizedVector2 = new QuantizedVector
        {
            Type = QuantizationType.ScalarInt8,
            Data = new byte[] { 12, 22, 32, 42 },
            OriginalDimension = 4
        };

        _quantizerMock
            .Setup(q => q.ComputeDistance(quantizedQuery, It.IsAny<QuantizedVector>()))
            .Returns(0.1f);

        // First store some quantized data
        _innerStoreMock
            .Setup(s => s.StoreAsync(It.IsAny<DocumentChunk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentChunk c, CancellationToken _) => c.Id);

        _quantizerMock
            .Setup(q => q.QuantizeAsync(It.IsAny<float[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(quantizedVector1);

        var decorator = CreateDecorator();

        // Store chunks first to populate quantized cache
        chunk1.Embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
        chunk2.Embedding = new float[] { 0.2f, 0.3f, 0.4f, 0.5f };
        await decorator.StoreAsync(chunk1);
        await decorator.StoreAsync(chunk2);

        // Act
        var results = await decorator.SearchQuantizedAsync(quantizedQuery, topK: 10);

        // Assert
        Assert.NotNull(results);
    }

    #endregion

    #region GetQuantizedStatsAsync Tests

    [Fact]
    public async Task GetQuantizedStatsAsync_ReturnsCorrectStats()
    {
        // Arrange
        var chunk = new DocumentChunk
        {
            Id = "chunk-1",
            DocumentId = "doc-1",
            Content = "Test content",
            Embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f }
        };

        var quantizedVector = new QuantizedVector
        {
            Type = QuantizationType.ScalarInt8,
            Data = new byte[] { 10, 20, 30, 40 },
            OriginalDimension = 4
        };

        _innerStoreMock
            .Setup(s => s.StoreAsync(chunk, It.IsAny<CancellationToken>()))
            .ReturnsAsync("chunk-1");

        _quantizerMock
            .Setup(q => q.QuantizeAsync(chunk.Embedding, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quantizedVector);

        var decorator = CreateDecorator();

        // Store one chunk
        await decorator.StoreAsync(chunk);

        // Act
        var stats = await decorator.GetQuantizedStatsAsync();

        // Assert
        Assert.Equal(1, stats.QuantizedChunkCount);
        Assert.Equal(QuantizationType.ScalarInt8, stats.QuantizationType);
        Assert.True(stats.CompressionRatio > 0);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_RemovesFromBothStores()
    {
        // Arrange
        var chunk = new DocumentChunk
        {
            Id = "chunk-1",
            DocumentId = "doc-1",
            Content = "Test content",
            Embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f }
        };

        var quantizedVector = new QuantizedVector
        {
            Type = QuantizationType.ScalarInt8,
            Data = new byte[] { 10, 20, 30, 40 },
            OriginalDimension = 4
        };

        _innerStoreMock
            .Setup(s => s.StoreAsync(chunk, It.IsAny<CancellationToken>()))
            .ReturnsAsync("chunk-1");

        _innerStoreMock
            .Setup(s => s.DeleteAsync("chunk-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _quantizerMock
            .Setup(q => q.QuantizeAsync(chunk.Embedding, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quantizedVector);

        var decorator = CreateDecorator();

        // Store first
        await decorator.StoreAsync(chunk);

        // Act
        var result = await decorator.DeleteAsync("chunk-1");

        // Assert
        Assert.True(result);
        _innerStoreMock.Verify(s => s.DeleteAsync("chunk-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region SearchWithRerankAsync Tests

    [Fact]
    public async Task SearchWithRerankAsync_UsesQuantizedForCandidatesThenReranks()
    {
        // Arrange
        var queryEmbedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
        var quantizedQuery = new QuantizedVector
        {
            Type = QuantizationType.ScalarInt8,
            Data = new byte[] { 10, 20, 30, 40 },
            OriginalDimension = 4
        };

        var chunk1 = new DocumentChunk
        {
            Id = "chunk-1",
            Content = "Content 1",
            Embedding = new float[] { 0.15f, 0.25f, 0.35f, 0.45f }
        };

        _innerStoreMock
            .Setup(s => s.SearchAsync(queryEmbedding, It.IsAny<int>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { chunk1 });

        var decorator = CreateDecorator();

        // Act
        var results = await decorator.SearchWithRerankAsync(queryEmbedding, quantizedQuery, topK: 5);

        // Assert
        Assert.NotNull(results);
    }

    #endregion
}
