using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services;
using FluxIndex.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests;

public class SearchServiceTests
{
    private readonly Mock<IVectorStore> _mockVectorStore;
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly Mock<IDocumentRepository> _mockDocumentRepository;
    private readonly Mock<IAdvancedRerankingService> _mockRerankingService;
    private readonly Mock<IMetadataEnrichmentService> _mockMetadataEnrichmentService;
    private readonly ILogger<SearchService> _logger;
    private readonly SearchService _service;

    public SearchServiceTests()
    {
        _mockVectorStore = new Mock<IVectorStore>();
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _mockDocumentRepository = new Mock<IDocumentRepository>();
        _mockRerankingService = new Mock<IAdvancedRerankingService>();
        _mockMetadataEnrichmentService = new Mock<IMetadataEnrichmentService>();
        _logger = NullLogger<SearchService>.Instance;

        _service = new SearchService(
            _mockVectorStore.Object,
            _mockEmbeddingService.Object,
            _mockDocumentRepository.Object,
            _mockRerankingService.Object,
            _mockMetadataEnrichmentService.Object,
            _logger);
    }

    [Fact]
    public async Task SearchAsync_ValidQuery_ReturnsResults()
    {
        // Arrange
        var query = "test query";
        var topK = 5;
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = "chunk1",
                Content = "Test content 1",
                DocumentId = "doc1",
                Score = 0.9f,
                Embedding = embedding
            }
        };

        var document = Document.Create("doc1");
        document.FileName = "test.txt";
        document.SetMetadata("title", "Test Document");

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockVectorStore.Setup(x => x.SearchAsync(embedding, topK, 0.0f, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);

        _mockDocumentRepository.Setup(x => x.GetByIdAsync("doc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        // Act
        var results = await _service.SearchAsync(query, topK);

        // Assert
        Assert.NotNull(results);
        Assert.Single(results);

        var result = results.First();
        Assert.Equal("chunk1", result.Chunk.Id);
        Assert.Equal(0.9f, result.Score);
        Assert.Equal("test.txt", result.FileName);

        _mockEmbeddingService.Verify(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()), Times.Once);
        _mockVectorStore.Verify(x => x.SearchAsync(embedding, topK, 0.0f, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_NoResults_ReturnsEmptyList()
    {
        // Arrange
        var query = "non-existent query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockVectorStore.Setup(x => x.SearchAsync(embedding, It.IsAny<int>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<DocumentChunk>());

        // Act
        var results = await _service.SearchAsync(query);

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(5, 0.0f)]
    [InlineData(10, 0.5f)]
    [InlineData(20, 0.8f)]
    public async Task SearchAsync_DifferentParameters_CallsCorrectServices(int topK, float minScore)
    {
        // Arrange
        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockVectorStore.Setup(x => x.SearchAsync(embedding, topK, minScore, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<DocumentChunk>());

        // Act
        await _service.SearchAsync(query, topK, minScore);

        // Assert
        _mockVectorStore.Verify(x => x.SearchAsync(embedding, topK, minScore, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FindSimilarAsync_ValidDocumentId_ReturnsSimilarDocuments()
    {
        // Arrange
        var documentId = "doc1";
        var topK = 5;
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        var sourceChunk = new DocumentChunk
        {
            Id = "chunk1",
            DocumentId = "doc1",
            Embedding = embedding
        };

        var similarChunks = new List<DocumentChunk>
        {
            sourceChunk,  // Self (will be excluded)
            new DocumentChunk
            {
                Id = "chunk2",
                Content = "Similar content",
                DocumentId = "doc2",
                Score = 0.85f,
                Embedding = new float[] { 0.12f, 0.21f, 0.32f }
            }
        };

        var document2 = Document.Create("doc2");
        document2.FileName = "similar.txt";

        _mockVectorStore.Setup(x => x.GetByDocumentIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { sourceChunk });

        _mockVectorStore.Setup(x => x.SearchAsync(embedding, topK + 1, It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(similarChunks);

        _mockDocumentRepository.Setup(x => x.GetByIdAsync("doc2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(document2);

        // Act
        var results = await _service.FindSimilarAsync(documentId, topK);

        // Assert
        Assert.NotNull(results);
        Assert.Single(results);  // Self excluded

        var result = results.First();
        Assert.Equal("doc2", result.Chunk.DocumentId);
        Assert.NotEqual(documentId, result.Chunk.DocumentId);
    }

    [Fact]
    public async Task FindSimilarAsync_DocumentNotFound_ReturnsEmpty()
    {
        // Arrange
        var documentId = "non-existent";

        _mockVectorStore.Setup(x => x.GetByDocumentIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<DocumentChunk>());

        // Act
        var results = await _service.FindSimilarAsync(documentId);

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public async Task AdvancedSearchAsync_WithReranking_ReturnsRerankedResults()
    {
        // Arrange
        var query = "advanced query";
        var topK = 5;
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = "chunk1",
                Content = "Test content",
                DocumentId = "doc1",
                Score = 0.7f,
                Embedding = embedding
            }
        };

        var document = Document.Create("doc1");
        document.FileName = "test.txt";

        var enhancedResults = new List<EnhancedSearchResult>
        {
            new EnhancedSearchResult
            {
                Chunk = chunks[0],
                SimilarityScore = 0.7,
                RerankedScore = 0.95,
                HybridScore = 0.85,
                ExplanationMetadata = new Dictionary<string, object>()
            }
        };

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockVectorStore.Setup(x => x.SearchAsync(embedding, It.IsAny<int>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);

        _mockDocumentRepository.Setup(x => x.GetByIdAsync("doc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        _mockRerankingService.Setup(x => x.RerankAsync(
                query,
                It.IsAny<IEnumerable<FluxIndex.Core.Application.Interfaces.SearchResult>>(),
                RerankingStrategy.Adaptive,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EnhancedSearchResult>(enhancedResults));

        // Act
        var results = await _service.AdvancedSearchAsync(query, topK);

        // Assert
        Assert.NotNull(results);
        Assert.Single(results);

        var result = results.First();
        Assert.Equal(0.95, result.RerankedScore);

        _mockRerankingService.Verify(x => x.RerankAsync(
            query,
            It.IsAny<IEnumerable<FluxIndex.Core.Application.Interfaces.SearchResult>>(),
            RerankingStrategy.Adaptive,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(RerankingStrategy.Adaptive)]
    [InlineData(RerankingStrategy.Contextual)]
    [InlineData(RerankingStrategy.Semantic)]
    public async Task AdvancedSearchAsync_DifferentStrategies_UsesCorrectStrategy(RerankingStrategy strategy)
    {
        // Arrange
        var query = "test query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = "chunk1",
                Content = "Test content",
                DocumentId = "doc1",
                Score = 0.8f,
                Embedding = embedding
            }
        };

        var document = Document.Create("doc1");
        document.FileName = "test.txt";

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockVectorStore.Setup(x => x.SearchAsync(embedding, It.IsAny<int>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);

        _mockDocumentRepository.Setup(x => x.GetByIdAsync("doc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        _mockRerankingService.Setup(x => x.RerankAsync(
                query,
                It.IsAny<IEnumerable<FluxIndex.Core.Application.Interfaces.SearchResult>>(),
                strategy,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EnhancedSearchResult>());

        // Act
        await _service.AdvancedSearchAsync(query, rerankingStrategy: strategy);

        // Assert
        _mockRerankingService.Verify(x => x.RerankAsync(
            query,
            It.IsAny<IEnumerable<FluxIndex.Core.Application.Interfaces.SearchResult>>(),
            strategy,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
