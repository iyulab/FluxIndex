using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests;

public class SearchServiceTests
{
    private readonly IVectorStore _mockVectorStore;
    private readonly IEmbeddingService _mockEmbeddingService;
    private readonly IDocumentRepository _mockDocumentRepository;
    private readonly IAdvancedRerankingService _mockRerankingService;
    private readonly IMetadataEnrichmentService _mockMetadataEnrichmentService;
    private readonly ILogger<SearchService> _logger;
    private readonly SearchService _service;

    public SearchServiceTests()
    {
        _mockVectorStore = Substitute.For<IVectorStore>();
        _mockEmbeddingService = Substitute.For<IEmbeddingService>();
        _mockDocumentRepository = Substitute.For<IDocumentRepository>();
        _mockRerankingService = Substitute.For<IAdvancedRerankingService>();
        _mockMetadataEnrichmentService = Substitute.For<IMetadataEnrichmentService>();
        _logger = NullLogger<SearchService>.Instance;

        _service = new SearchService(
            _mockVectorStore,
            _mockEmbeddingService,
            _mockDocumentRepository,
            _mockRerankingService,
            _mockMetadataEnrichmentService,
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

        _mockEmbeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>()).Returns(embedding);

        _mockVectorStore.SearchAsync(embedding, topK, 0.0f, Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>()).Returns(chunks);

        _mockDocumentRepository.GetByIdAsync("doc1", Arg.Any<CancellationToken>()).Returns(document);

        // Act
        var results = await _service.SearchAsync(query, topK);

        // Assert
        Assert.NotNull(results);
        Assert.Single(results);

        var result = results.First();
        Assert.Equal("chunk1", result.Chunk.Id);
        Assert.Equal(0.9f, result.Score);
        Assert.Equal("test.txt", result.FileName);

        await _mockEmbeddingService.Received(1).GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>());
        await _mockVectorStore.Received(1).SearchAsync(embedding, topK, 0.0f, Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_NoResults_ReturnsEmptyList()
    {
        // Arrange
        var query = "non-existent query";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        _mockEmbeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>()).Returns(embedding);

        _mockVectorStore.SearchAsync(embedding, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<DocumentChunk>());

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

        _mockEmbeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>()).Returns(embedding);

        _mockVectorStore.SearchAsync(embedding, topK, minScore, Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<DocumentChunk>());

        // Act
        await _service.SearchAsync(query, topK, minScore);

        // Assert
        await _mockVectorStore.Received(1).SearchAsync(embedding, topK, minScore, Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>());
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

        _mockVectorStore.GetByDocumentIdAsync(documentId, Arg.Any<CancellationToken>()).Returns(new[] { sourceChunk });

        _mockVectorStore.SearchAsync(embedding, topK + 1, Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>()).Returns(similarChunks);

        _mockDocumentRepository.GetByIdAsync("doc2", Arg.Any<CancellationToken>()).Returns(document2);

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

        _mockVectorStore.GetByDocumentIdAsync(documentId, Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<DocumentChunk>());

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

        _mockEmbeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>()).Returns(embedding);

        _mockVectorStore.SearchAsync(embedding, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>()).Returns(chunks);

        _mockDocumentRepository.GetByIdAsync("doc1", Arg.Any<CancellationToken>()).Returns(document);

        _mockRerankingService.RerankAsync(
                query,
                Arg.Any<IEnumerable<FluxIndex.Core.Application.Interfaces.SearchResult>>(),
                RerankingStrategy.Adaptive,
                Arg.Any<CancellationToken>()).Returns(new List<EnhancedSearchResult>(enhancedResults));

        // Act
        var results = await _service.AdvancedSearchAsync(query, topK);

        // Assert
        Assert.NotNull(results);
        Assert.Single(results);

        var result = results.First();
        Assert.Equal(0.95, result.RerankedScore);

        await _mockRerankingService.Received(1).RerankAsync(
            query,
            Arg.Any<IEnumerable<FluxIndex.Core.Application.Interfaces.SearchResult>>(),
            RerankingStrategy.Adaptive,
            Arg.Any<CancellationToken>());
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

        _mockEmbeddingService.GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>()).Returns(embedding);

        _mockVectorStore.SearchAsync(embedding, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>()).Returns(chunks);

        _mockDocumentRepository.GetByIdAsync("doc1", Arg.Any<CancellationToken>()).Returns(document);

        _mockRerankingService.RerankAsync(
                query,
                Arg.Any<IEnumerable<FluxIndex.Core.Application.Interfaces.SearchResult>>(),
                strategy,
                Arg.Any<CancellationToken>()).Returns(new List<EnhancedSearchResult>());

        // Act
        await _service.AdvancedSearchAsync(query, rerankingStrategy: strategy);

        // Assert
        await _mockRerankingService.Received(1).RerankAsync(
            query,
            Arg.Any<IEnumerable<FluxIndex.Core.Application.Interfaces.SearchResult>>(),
            strategy,
            Arg.Any<CancellationToken>());
    }
}
