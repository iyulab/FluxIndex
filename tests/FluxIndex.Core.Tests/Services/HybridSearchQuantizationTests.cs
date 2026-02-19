using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// HybridSearchService의 양자화 검색 통합 테스트
/// </summary>
public class HybridSearchQuantizationTests
{
    private readonly IVectorStore _mockVectorStore;
    private readonly ISparseRetriever _mockSparseRetriever;
    private readonly IEmbeddingService _mockEmbeddingService;
    private readonly IVectorQuantizer _mockQuantizer;
    private readonly ILogger<HybridSearchService> _logger;

    public HybridSearchQuantizationTests()
    {
        _mockVectorStore = Substitute.For<IVectorStore>();
        _mockSparseRetriever = Substitute.For<ISparseRetriever>();
        _mockEmbeddingService = Substitute.For<IEmbeddingService>();
        _mockQuantizer = Substitute.For<IVectorQuantizer>();
        _logger = NullLogger<HybridSearchService>.Instance;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithQuantizer_SetsQuantizer()
    {
        // Arrange & Act
        var service = new HybridSearchService(
            _mockVectorStore,
            _mockSparseRetriever,
            _mockEmbeddingService,
            _mockQuantizer,
            _logger);

        // Assert - 양자화 지원 확인 (VectorStore가 IQuantizedVectorStore가 아니므로 false)
        Assert.False(service.SupportsQuantizedSearch);
    }

    [Fact]
    public void Constructor_WithNullQuantizer_WorksCorrectly()
    {
        // Arrange & Act
        var service = new HybridSearchService(
            _mockVectorStore,
            _mockSparseRetriever,
            _mockEmbeddingService,
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
            _mockVectorStore,
            _mockSparseRetriever,
            _mockEmbeddingService,
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
        var mockQuantizedStore = Substitute.For<IQuantizedVectorStore, IVectorStore>();

        var service = new HybridSearchService(
            (IVectorStore)mockQuantizedStore,
            _mockSparseRetriever,
            _mockEmbeddingService,
            _mockQuantizer,
            _logger);

        // Act & Assert
        Assert.True(service.SupportsQuantizedSearch);
    }

    [Fact]
    public void SupportsQuantizedSearch_WithQuantizerButRegularStore_ReturnsFalse()
    {
        // Arrange
        var service = new HybridSearchService(
            _mockVectorStore, // regular IVectorStore, not IQuantizedVectorStore
            _mockSparseRetriever,
            _mockEmbeddingService,
            _mockQuantizer,
            _logger);

        // Act & Assert
        Assert.False(service.SupportsQuantizedSearch);
    }

    [Fact]
    public void SupportsQuantizedSearch_WithoutQuantizer_ReturnsFalse()
    {
        // Arrange
        var mockQuantizedStore = Substitute.For<IQuantizedVectorStore, IVectorStore>();

        var service = new HybridSearchService(
            (IVectorStore)mockQuantizedStore,
            _mockSparseRetriever,
            _mockEmbeddingService,
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
        var mockQuantizedStore = Substitute.For<IQuantizedVectorStore, IVectorStore>();

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

        _mockEmbeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>()).Returns(embedding);

        _mockQuantizer.QuantizeAsync(embedding, Arg.Any<CancellationToken>()).Returns(quantizedVector);

        mockQuantizedStore.SearchWithRerankAsync(
                embedding,
                quantizedVector,
                10,
                3,
                0.0f,
                Arg.Any<CancellationToken>()).Returns(searchResults);

        _mockSparseRetriever.SearchAsync(query, Arg.Any<SparseSearchOptions>(), Arg.Any<CancellationToken>()).Returns(new List<SparseSearchResult>());

        var service = new HybridSearchService(
            (IVectorStore)mockQuantizedStore,
            _mockSparseRetriever,
            _mockEmbeddingService,
            _mockQuantizer,
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
        await mockQuantizedStore.Received(1).SearchWithRerankAsync(
            embedding,
            quantizedVector,
            Arg.Any<int>(),
            3,
            0.0f,
            Arg.Any<CancellationToken>());

        Assert.Equal(2, results.Count);
        Assert.Equal("chunk1", results[0].Chunk.Id);
    }

    [Fact]
    public async Task SearchAsync_WithUseQuantizedSearchFalse_UsesRegularSearch()
    {
        // Arrange
        var mockQuantizedStore = Substitute.For<IQuantizedVectorStore, IVectorStore>();

        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        var vectorResults = new List<DocumentChunk>
        {
            CreateTestChunk("chunk1", "Content 1", 0.9f),
            CreateTestChunk("chunk2", "Content 2", 0.8f)
        };

        _mockEmbeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>()).Returns(embedding);

        ((IVectorStore)mockQuantizedStore).SearchAsync(embedding, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>())
            .Returns(vectorResults);

        _mockSparseRetriever.SearchAsync(query, Arg.Any<SparseSearchOptions>(), Arg.Any<CancellationToken>()).Returns(new List<SparseSearchResult>());

        var service = new HybridSearchService(
            (IVectorStore)mockQuantizedStore,
            _mockSparseRetriever,
            _mockEmbeddingService,
            _mockQuantizer,
            _logger);

        var options = new HybridSearchOptions
        {
            UseQuantizedSearch = false, // Disabled
            EnableAutoStrategy = false
        };

        // Act
        var results = await service.SearchAsync(query, options);

        // Assert
        await mockQuantizedStore.DidNotReceive().SearchWithRerankAsync(
            Arg.Any<float[]>(),
            Arg.Any<QuantizedVector>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<float>(),
            Arg.Any<CancellationToken>());

        await ((IVectorStore)mockQuantizedStore).Received(1).SearchAsync(
            embedding,
            Arg.Any<int>(),
            Arg.Any<float>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_WhenQuantizedSearchFails_FallsBackToRegularSearch()
    {
        // Arrange
        var mockQuantizedStore = Substitute.For<IQuantizedVectorStore, IVectorStore>();

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

        _mockEmbeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>()).Returns(embedding);

        _mockQuantizer.QuantizeAsync(embedding, Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("Quantization failed"));

        ((IVectorStore)mockQuantizedStore).SearchAsync(embedding, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>())
            .Returns(regularResults);

        _mockSparseRetriever.SearchAsync(query, Arg.Any<SparseSearchOptions>(), Arg.Any<CancellationToken>()).Returns(new List<SparseSearchResult>());

        var service = new HybridSearchService(
            (IVectorStore)mockQuantizedStore,
            _mockSparseRetriever,
            _mockEmbeddingService,
            _mockQuantizer,
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

        await ((IVectorStore)mockQuantizedStore).Received(1).SearchAsync(
            embedding,
            Arg.Any<int>(),
            Arg.Any<float>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_WithQuantizedSearchAndCustomMultiplier_UsesCorrectMultiplier()
    {
        // Arrange
        var mockQuantizedStore = Substitute.For<IQuantizedVectorStore, IVectorStore>();

        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var quantizedVector = new QuantizedVector
        {
            Type = QuantizationType.Binary,
            Data = new byte[] { 0xFF },
            OriginalDimension = 3
        };

        _mockEmbeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>()).Returns(embedding);

        _mockQuantizer.QuantizeAsync(embedding, Arg.Any<CancellationToken>()).Returns(quantizedVector);

        mockQuantizedStore.SearchWithRerankAsync(
                Arg.Any<float[]>(),
                Arg.Any<QuantizedVector>(),
                Arg.Any<int>(),
                5, // Custom multiplier
                Arg.Any<float>(),
                Arg.Any<CancellationToken>()).Returns(new List<(DocumentChunk, float)>());

        _mockSparseRetriever.SearchAsync(query, Arg.Any<SparseSearchOptions>(), Arg.Any<CancellationToken>()).Returns(new List<SparseSearchResult>());

        var service = new HybridSearchService(
            (IVectorStore)mockQuantizedStore,
            _mockSparseRetriever,
            _mockEmbeddingService,
            _mockQuantizer,
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
        await mockQuantizedStore.Received(1).SearchWithRerankAsync(
            embedding,
            quantizedVector,
            Arg.Any<int>(),
            5, // Verify custom multiplier was used
            Arg.Any<float>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_WithQuantizedSearchAndMinScore_UsesCorrectMinScore()
    {
        // Arrange
        var mockQuantizedStore = Substitute.For<IQuantizedVectorStore, IVectorStore>();

        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var quantizedVector = new QuantizedVector
        {
            Type = QuantizationType.ScalarInt8,
            Data = new byte[] { 128, 64, 192 },
            OriginalDimension = 3
        };

        _mockEmbeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>()).Returns(embedding);

        _mockQuantizer.QuantizeAsync(embedding, Arg.Any<CancellationToken>()).Returns(quantizedVector);

        mockQuantizedStore.SearchWithRerankAsync(
                Arg.Any<float[]>(),
                Arg.Any<QuantizedVector>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                0.5f, // Custom min score
                Arg.Any<CancellationToken>()).Returns(new List<(DocumentChunk, float)>());

        _mockSparseRetriever.SearchAsync(query, Arg.Any<SparseSearchOptions>(), Arg.Any<CancellationToken>()).Returns(new List<SparseSearchResult>());

        var service = new HybridSearchService(
            (IVectorStore)mockQuantizedStore,
            _mockSparseRetriever,
            _mockEmbeddingService,
            _mockQuantizer,
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
        await mockQuantizedStore.Received(1).SearchWithRerankAsync(
            embedding,
            quantizedVector,
            Arg.Any<int>(),
            Arg.Any<int>(),
            0.5f, // Verify custom min score was used
            Arg.Any<CancellationToken>());
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
