using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// HybridSearchService의 양자화 검색 통합 테스트
/// </summary>
public class HybridSearchQuantizationTests
{
    private readonly Mock<IVectorStore> _mockVectorStore;
    private readonly Mock<ISparseRetriever> _mockSparseRetriever;
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly Mock<IVectorQuantizer> _mockQuantizer;
    private readonly ILogger<HybridSearchService> _logger;

    public HybridSearchQuantizationTests()
    {
        _mockVectorStore = new Mock<IVectorStore>();
        _mockSparseRetriever = new Mock<ISparseRetriever>();
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _mockQuantizer = new Mock<IVectorQuantizer>();
        _logger = NullLogger<HybridSearchService>.Instance;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithQuantizer_SetsQuantizer()
    {
        // Arrange & Act
        var service = new HybridSearchService(
            _mockVectorStore.Object,
            _mockSparseRetriever.Object,
            _mockEmbeddingService.Object,
            _mockQuantizer.Object,
            _logger);

        // Assert - 양자화 지원 확인 (VectorStore가 IQuantizedVectorStore가 아니므로 false)
        Assert.False(service.SupportsQuantizedSearch);
    }

    [Fact]
    public void Constructor_WithNullQuantizer_WorksCorrectly()
    {
        // Arrange & Act
        var service = new HybridSearchService(
            _mockVectorStore.Object,
            _mockSparseRetriever.Object,
            _mockEmbeddingService.Object,
            null,
            _logger);

        // Assert
        Assert.False(service.SupportsQuantizedSearch);
    }

    [Fact]
    public void Constructor_BasicConstructor_WorksCorrectly()
    {
        // Arrange & Act
        var service = new HybridSearchService(
            _mockVectorStore.Object,
            _mockSparseRetriever.Object,
            _mockEmbeddingService.Object,
            _logger);

        // Assert
        Assert.False(service.SupportsQuantizedSearch);
    }

    #endregion

    #region SupportsQuantizedSearch Tests

    [Fact]
    public void SupportsQuantizedSearch_WithQuantizerAndQuantizedStore_ReturnsTrue()
    {
        // Arrange
        var mockQuantizedStore = new Mock<IQuantizedVectorStore>();
        mockQuantizedStore.As<IVectorStore>();

        var service = new HybridSearchService(
            mockQuantizedStore.Object,
            _mockSparseRetriever.Object,
            _mockEmbeddingService.Object,
            _mockQuantizer.Object,
            _logger);

        // Act & Assert
        Assert.True(service.SupportsQuantizedSearch);
    }

    [Fact]
    public void SupportsQuantizedSearch_WithQuantizerButRegularStore_ReturnsFalse()
    {
        // Arrange
        var service = new HybridSearchService(
            _mockVectorStore.Object, // regular IVectorStore, not IQuantizedVectorStore
            _mockSparseRetriever.Object,
            _mockEmbeddingService.Object,
            _mockQuantizer.Object,
            _logger);

        // Act & Assert
        Assert.False(service.SupportsQuantizedSearch);
    }

    [Fact]
    public void SupportsQuantizedSearch_WithoutQuantizer_ReturnsFalse()
    {
        // Arrange
        var mockQuantizedStore = new Mock<IQuantizedVectorStore>();
        mockQuantizedStore.As<IVectorStore>();

        var service = new HybridSearchService(
            mockQuantizedStore.Object,
            _mockSparseRetriever.Object,
            _mockEmbeddingService.Object,
            null, // no quantizer
            _logger);

        // Act & Assert
        Assert.False(service.SupportsQuantizedSearch);
    }

    #endregion

    #region Quantized Search Integration Tests

    [Fact]
    public async Task SearchAsync_WithUseQuantizedSearchTrue_UsesQuantizedSearch()
    {
        // Arrange
        var mockQuantizedStore = new Mock<IQuantizedVectorStore>();
        mockQuantizedStore.As<IVectorStore>();

        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var quantizedVector = new QuantizedVector
        {
            Type = QuantizationType.ScalarInt8,
            Data = new byte[] { 128, 64, 192 },
            OriginalDimension = 3
        };

        var searchResults = new List<(DocumentChunk Chunk, float Score)>
        {
            (CreateTestChunk("chunk1", "Content 1"), 0.95f),
            (CreateTestChunk("chunk2", "Content 2"), 0.85f)
        };

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockQuantizer.Setup(x => x.QuantizeAsync(embedding, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quantizedVector);

        mockQuantizedStore.Setup(x => x.SearchWithRerankAsync(
                embedding,
                quantizedVector,
                10,
                3,
                0.0f,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        _mockSparseRetriever.Setup(x => x.SearchAsync(query, It.IsAny<SparseSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SparseSearchResult>());

        var service = new HybridSearchService(
            mockQuantizedStore.Object,
            _mockSparseRetriever.Object,
            _mockEmbeddingService.Object,
            _mockQuantizer.Object,
            _logger);

        var options = new HybridSearchOptions
        {
            UseQuantizedSearch = true,
            QuantizedCandidateMultiplier = 3,
            QuantizedMinScore = 0.0f,
            EnableAutoStrategy = false
        };

        // Act
        var results = await service.SearchAsync(query, options);

        // Assert
        mockQuantizedStore.Verify(x => x.SearchWithRerankAsync(
            embedding,
            quantizedVector,
            It.IsAny<int>(),
            3,
            0.0f,
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(2, results.Count);
        Assert.Equal("chunk1", results[0].Chunk.Id);
    }

    [Fact]
    public async Task SearchAsync_WithUseQuantizedSearchFalse_UsesRegularSearch()
    {
        // Arrange
        var mockQuantizedStore = new Mock<IQuantizedVectorStore>();
        mockQuantizedStore.As<IVectorStore>();

        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        var vectorResults = new List<DocumentChunk>
        {
            CreateTestChunk("chunk1", "Content 1", 0.9f),
            CreateTestChunk("chunk2", "Content 2", 0.8f)
        };

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        mockQuantizedStore.As<IVectorStore>()
            .Setup(x => x.SearchAsync(embedding, It.IsAny<int>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        _mockSparseRetriever.Setup(x => x.SearchAsync(query, It.IsAny<SparseSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SparseSearchResult>());

        var service = new HybridSearchService(
            mockQuantizedStore.Object,
            _mockSparseRetriever.Object,
            _mockEmbeddingService.Object,
            _mockQuantizer.Object,
            _logger);

        var options = new HybridSearchOptions
        {
            UseQuantizedSearch = false, // Disabled
            EnableAutoStrategy = false
        };

        // Act
        var results = await service.SearchAsync(query, options);

        // Assert
        mockQuantizedStore.Verify(x => x.SearchWithRerankAsync(
            It.IsAny<float[]>(),
            It.IsAny<QuantizedVector>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<CancellationToken>()), Times.Never);

        mockQuantizedStore.As<IVectorStore>().Verify(x => x.SearchAsync(
            embedding,
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WhenQuantizedSearchFails_FallsBackToRegularSearch()
    {
        // Arrange
        var mockQuantizedStore = new Mock<IQuantizedVectorStore>();
        mockQuantizedStore.As<IVectorStore>();

        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var quantizedVector = new QuantizedVector
        {
            Type = QuantizationType.ScalarInt8,
            Data = new byte[] { 128, 64, 192 },
            OriginalDimension = 3
        };

        var regularResults = new List<DocumentChunk>
        {
            CreateTestChunk("chunk1", "Content 1", 0.9f)
        };

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockQuantizer.Setup(x => x.QuantizeAsync(embedding, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Quantization failed"));

        mockQuantizedStore.As<IVectorStore>()
            .Setup(x => x.SearchAsync(embedding, It.IsAny<int>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(regularResults);

        _mockSparseRetriever.Setup(x => x.SearchAsync(query, It.IsAny<SparseSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SparseSearchResult>());

        var service = new HybridSearchService(
            mockQuantizedStore.Object,
            _mockSparseRetriever.Object,
            _mockEmbeddingService.Object,
            _mockQuantizer.Object,
            _logger);

        var options = new HybridSearchOptions
        {
            UseQuantizedSearch = true,
            EnableAutoStrategy = false
        };

        // Act
        var results = await service.SearchAsync(query, options);

        // Assert - Should fallback to regular search
        Assert.Single(results);
        Assert.Equal("chunk1", results[0].Chunk.Id);

        mockQuantizedStore.As<IVectorStore>().Verify(x => x.SearchAsync(
            embedding,
            It.IsAny<int>(),
            It.IsAny<float>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithQuantizedSearchAndCustomMultiplier_UsesCorrectMultiplier()
    {
        // Arrange
        var mockQuantizedStore = new Mock<IQuantizedVectorStore>();
        mockQuantizedStore.As<IVectorStore>();

        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var quantizedVector = new QuantizedVector
        {
            Type = QuantizationType.Binary,
            Data = new byte[] { 0xFF },
            OriginalDimension = 3
        };

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockQuantizer.Setup(x => x.QuantizeAsync(embedding, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quantizedVector);

        mockQuantizedStore.Setup(x => x.SearchWithRerankAsync(
                It.IsAny<float[]>(),
                It.IsAny<QuantizedVector>(),
                It.IsAny<int>(),
                5, // Custom multiplier
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(DocumentChunk, float)>());

        _mockSparseRetriever.Setup(x => x.SearchAsync(query, It.IsAny<SparseSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SparseSearchResult>());

        var service = new HybridSearchService(
            mockQuantizedStore.Object,
            _mockSparseRetriever.Object,
            _mockEmbeddingService.Object,
            _mockQuantizer.Object,
            _logger);

        var options = new HybridSearchOptions
        {
            UseQuantizedSearch = true,
            QuantizedCandidateMultiplier = 5, // Custom multiplier
            EnableAutoStrategy = false
        };

        // Act
        await service.SearchAsync(query, options);

        // Assert
        mockQuantizedStore.Verify(x => x.SearchWithRerankAsync(
            embedding,
            quantizedVector,
            It.IsAny<int>(),
            5, // Verify custom multiplier was used
            It.IsAny<float>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithQuantizedSearchAndMinScore_UsesCorrectMinScore()
    {
        // Arrange
        var mockQuantizedStore = new Mock<IQuantizedVectorStore>();
        mockQuantizedStore.As<IVectorStore>();

        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var quantizedVector = new QuantizedVector
        {
            Type = QuantizationType.ScalarInt8,
            Data = new byte[] { 128, 64, 192 },
            OriginalDimension = 3
        };

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockQuantizer.Setup(x => x.QuantizeAsync(embedding, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quantizedVector);

        mockQuantizedStore.Setup(x => x.SearchWithRerankAsync(
                It.IsAny<float[]>(),
                It.IsAny<QuantizedVector>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                0.5f, // Custom min score
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(DocumentChunk, float)>());

        _mockSparseRetriever.Setup(x => x.SearchAsync(query, It.IsAny<SparseSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SparseSearchResult>());

        var service = new HybridSearchService(
            mockQuantizedStore.Object,
            _mockSparseRetriever.Object,
            _mockEmbeddingService.Object,
            _mockQuantizer.Object,
            _logger);

        var options = new HybridSearchOptions
        {
            UseQuantizedSearch = true,
            QuantizedMinScore = 0.5f, // Custom min score
            EnableAutoStrategy = false
        };

        // Act
        await service.SearchAsync(query, options);

        // Assert
        mockQuantizedStore.Verify(x => x.SearchWithRerankAsync(
            embedding,
            quantizedVector,
            It.IsAny<int>(),
            It.IsAny<int>(),
            0.5f, // Verify custom min score was used
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Helper Methods

    private static DocumentChunk CreateTestChunk(string id, string content, float? score = null)
    {
        return new DocumentChunk
        {
            Id = id,
            Content = content,
            DocumentId = $"doc_{id}",
            Score = score,
            Metadata = new Dictionary<string, object>()
        };
    }

    #endregion
}
