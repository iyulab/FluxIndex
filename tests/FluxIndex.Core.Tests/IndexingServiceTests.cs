using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests;

public class IndexingServiceTests
{
    private readonly Mock<IDocumentRepository> _mockDocumentRepository;
    private readonly Mock<IVectorStore> _mockVectorStore;
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly Mock<IMetadataEnrichmentService> _mockMetadataEnrichmentService;
    private readonly ILogger<IndexingService> _logger;
    private readonly IndexingService _service;

    public IndexingServiceTests()
    {
        _mockDocumentRepository = new Mock<IDocumentRepository>();
        _mockVectorStore = new Mock<IVectorStore>();
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _mockMetadataEnrichmentService = new Mock<IMetadataEnrichmentService>();
        _logger = NullLogger<IndexingService>.Instance;

        _service = new IndexingService(
            _mockDocumentRepository.Object,
            _mockVectorStore.Object,
            _mockEmbeddingService.Object,
            _mockMetadataEnrichmentService.Object,
            _logger);
    }

    [Fact]
    public async Task IndexDocumentAsync_ValidDocument_IndexesSuccessfully()
    {
        // Arrange
        var documentId = "doc1";
        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = "chunk1",
                Content = "Test content 1",
                DocumentId = documentId,
                ChunkIndex = 0,
                TotalChunks = 2
            },
            new DocumentChunk
            {
                Id = "chunk2",
                Content = "Test content 2",
                DocumentId = documentId,
                ChunkIndex = 1,
                TotalChunks = 2
            }
        };

        var metadata = new DocumentMetadata();
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var enrichedMetadata = new ChunkMetadata();
        var quality = new ChunkQuality
        {
            ContentCompleteness = 0.9,
            InformationDensity = 0.85,
            Coherence = 0.88
        };

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        _mockMetadataEnrichmentService.Setup(x => x.EnrichMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(enrichedMetadata);

        _mockMetadataEnrichmentService.Setup(x => x.EvaluateQualityAsync(
                It.IsAny<DocumentChunk>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(quality);

        _mockMetadataEnrichmentService.Setup(x => x.AnalyzeRelationshipsAsync(
                It.IsAny<DocumentChunk>(),
                It.IsAny<IEnumerable<DocumentChunk>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChunkRelationship>());

        _mockDocumentRepository.Setup(x => x.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentId);

        _mockDocumentRepository.Setup(x => x.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockVectorStore.Setup(x => x.StoreAsync(It.IsAny<DocumentChunk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("chunk-id");

        _mockVectorStore.Setup(x => x.UpdateAsync(It.IsAny<DocumentChunk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.IndexDocumentAsync(documentId, chunks, metadata);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(documentId, result.Id);
        Assert.Equal(DocumentStatus.Indexed, result.Status);
        Assert.Equal(2, result.Chunks.Count);

        // Verify all chunks were processed
        _mockEmbeddingService.Verify(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _mockVectorStore.Verify(x => x.StoreAsync(It.IsAny<DocumentChunk>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _mockDocumentRepository.Verify(x => x.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockDocumentRepository.Verify(x => x.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IndexDocumentAsync_MetadataEnrichment_EnrichesAllChunks()
    {
        // Arrange
        var documentId = "doc1";
        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = "chunk1",
                Content = "First chunk",
                DocumentId = documentId,
                ChunkIndex = 0,
                TotalChunks = 3
            },
            new DocumentChunk
            {
                Id = "chunk2",
                Content = "Middle chunk",
                DocumentId = documentId,
                ChunkIndex = 1,
                TotalChunks = 3
            },
            new DocumentChunk
            {
                Id = "chunk3",
                Content = "Last chunk",
                DocumentId = documentId,
                ChunkIndex = 2,
                TotalChunks = 3
            }
        };

        var metadata = new DocumentMetadata();

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        _mockMetadataEnrichmentService.Setup(x => x.EnrichMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkMetadata());

        _mockMetadataEnrichmentService.Setup(x => x.EvaluateQualityAsync(
                It.IsAny<DocumentChunk>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkQuality
            {
                ContentCompleteness = 0.9,
                InformationDensity = 0.85,
                Coherence = 0.88
            });

        _mockMetadataEnrichmentService.Setup(x => x.AnalyzeRelationshipsAsync(
                It.IsAny<DocumentChunk>(),
                It.IsAny<IEnumerable<DocumentChunk>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChunkRelationship>());

        _mockDocumentRepository.Setup(x => x.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentId);

        _mockDocumentRepository.Setup(x => x.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockVectorStore.Setup(x => x.StoreAsync(It.IsAny<DocumentChunk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("chunk-id");

        _mockVectorStore.Setup(x => x.UpdateAsync(It.IsAny<DocumentChunk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.IndexDocumentAsync(documentId, chunks, metadata);

        // Assert
        // Verify enrichment called for each chunk with correct context
        _mockMetadataEnrichmentService.Verify(x => x.EnrichMetadataAsync(
            "First chunk",  // First chunk has no previous
            0,
            null,
            "Middle chunk",
            It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        _mockMetadataEnrichmentService.Verify(x => x.EnrichMetadataAsync(
            "Middle chunk",  // Middle chunk has both previous and next
            1,
            "First chunk",
            "Last chunk",
            It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        _mockMetadataEnrichmentService.Verify(x => x.EnrichMetadataAsync(
            "Last chunk",  // Last chunk has no next
            2,
            "Middle chunk",
            null,
            It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IndexDocumentAsync_QualityEvaluation_EvaluatesAllChunks()
    {
        // Arrange
        var documentId = "doc1";
        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = "chunk1",
                Content = "Test content",
                DocumentId = documentId,
                ChunkIndex = 0,
                TotalChunks = 1
            }
        };

        var metadata = new DocumentMetadata();
        var quality = new ChunkQuality
        {
            ContentCompleteness = 0.8,
            InformationDensity = 0.85,
            Coherence = 0.9,
            Uniqueness = 0.75
        };

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        _mockMetadataEnrichmentService.Setup(x => x.EnrichMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkMetadata());

        _mockMetadataEnrichmentService.Setup(x => x.EvaluateQualityAsync(
                It.IsAny<DocumentChunk>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(quality);

        _mockMetadataEnrichmentService.Setup(x => x.AnalyzeRelationshipsAsync(
                It.IsAny<DocumentChunk>(),
                It.IsAny<IEnumerable<DocumentChunk>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChunkRelationship>());

        _mockDocumentRepository.Setup(x => x.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentId);

        _mockDocumentRepository.Setup(x => x.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockVectorStore.Setup(x => x.StoreAsync(It.IsAny<DocumentChunk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("chunk-id");

        _mockVectorStore.Setup(x => x.UpdateAsync(It.IsAny<DocumentChunk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.IndexDocumentAsync(documentId, chunks, metadata);

        // Assert
        _mockMetadataEnrichmentService.Verify(x => x.EvaluateQualityAsync(
            It.IsAny<DocumentChunk>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IndexDocumentAsync_Exception_MarksDocumentAsFailed()
    {
        // Arrange
        var documentId = "doc1";
        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = "chunk1",
                Content = "Test content",
                DocumentId = documentId,
                ChunkIndex = 0,
                TotalChunks = 1
            }
        };

        var metadata = new DocumentMetadata();

        _mockDocumentRepository.Setup(x => x.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentId);

        // Setup metadata enrichment to return valid metadata
        _mockMetadataEnrichmentService.Setup(x => x.EnrichMetadataAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkMetadata());

        // Setup quality evaluation to return valid quality
        _mockMetadataEnrichmentService.Setup(x => x.EvaluateQualityAsync(
            It.IsAny<DocumentChunk>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkQuality());

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Embedding service error"));

        _mockDocumentRepository.Setup(x => x.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() =>
            _service.IndexDocumentAsync(documentId, chunks, metadata));

        // Verify document was marked as failed
        _mockDocumentRepository.Verify(x => x.UpdateAsync(
            It.Is<Document>(d => d.Id == documentId && d.Status == DocumentStatus.Failed),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteDocumentAsync_ValidDocumentId_DeletesSuccessfully()
    {
        // Arrange
        var documentId = "doc1";

        _mockVectorStore.Setup(x => x.DeleteByDocumentIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockDocumentRepository.Setup(x => x.DeleteAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.DeleteDocumentAsync(documentId);

        // Assert
        Assert.True(result);

        _mockVectorStore.Verify(x => x.DeleteByDocumentIdAsync(documentId, It.IsAny<CancellationToken>()), Times.Once);
        _mockDocumentRepository.Verify(x => x.DeleteAsync(documentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IndexDocumentAsync_AnalyzeRelationships_UpdatesChunksWithRelationships()
    {
        // Arrange
        var documentId = "doc1";
        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = "chunk1",
                Content = "First chunk",
                DocumentId = documentId,
                ChunkIndex = 0,
                TotalChunks = 2
            },
            new DocumentChunk
            {
                Id = "chunk2",
                Content = "Second chunk",
                DocumentId = documentId,
                ChunkIndex = 1,
                TotalChunks = 2
            }
        };

        var metadata = new DocumentMetadata();
        var relationships = new List<ChunkRelationship>
        {
            new ChunkRelationship
            {
                SourceChunkId = "chunk1",
                TargetChunkId = "chunk2",
                Type = RelationshipType.Sequential,
                Strength = 0.9
            }
        };

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        _mockMetadataEnrichmentService.Setup(x => x.EnrichMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkMetadata());

        _mockMetadataEnrichmentService.Setup(x => x.EvaluateQualityAsync(
                It.IsAny<DocumentChunk>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkQuality
            {
                ContentCompleteness = 0.9,
                InformationDensity = 0.85,
                Coherence = 0.88
            });

        _mockMetadataEnrichmentService.Setup(x => x.AnalyzeRelationshipsAsync(
                It.IsAny<DocumentChunk>(),
                It.IsAny<IEnumerable<DocumentChunk>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(relationships);

        _mockDocumentRepository.Setup(x => x.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentId);

        _mockDocumentRepository.Setup(x => x.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockVectorStore.Setup(x => x.StoreAsync(It.IsAny<DocumentChunk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("chunk-id");

        _mockVectorStore.Setup(x => x.UpdateAsync(It.IsAny<DocumentChunk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.IndexDocumentAsync(documentId, chunks, metadata);

        // Assert
        // Verify relationship analysis was called for each chunk
        _mockMetadataEnrichmentService.Verify(x => x.AnalyzeRelationshipsAsync(
            It.IsAny<DocumentChunk>(),
            It.IsAny<IEnumerable<DocumentChunk>>(),
            It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Verify chunks were updated with relationships
        _mockVectorStore.Verify(x => x.UpdateAsync(
            It.IsAny<DocumentChunk>(),
            It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task IndexDocumentAsync_MultipleChunks_ProcessesAllChunks(int chunkCount)
    {
        // Arrange
        var documentId = "doc1";
        var chunks = Enumerable.Range(0, chunkCount)
            .Select(i => new DocumentChunk
            {
                Id = $"chunk{i}",
                Content = $"Content {i}",
                DocumentId = documentId,
                ChunkIndex = i,
                TotalChunks = chunkCount
            })
            .ToList();

        var metadata = new DocumentMetadata();

        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        _mockMetadataEnrichmentService.Setup(x => x.EnrichMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkMetadata());

        _mockMetadataEnrichmentService.Setup(x => x.EvaluateQualityAsync(
                It.IsAny<DocumentChunk>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkQuality
            {
                ContentCompleteness = 0.9,
                InformationDensity = 0.85,
                Coherence = 0.88
            });

        _mockMetadataEnrichmentService.Setup(x => x.AnalyzeRelationshipsAsync(
                It.IsAny<DocumentChunk>(),
                It.IsAny<IEnumerable<DocumentChunk>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChunkRelationship>());

        _mockDocumentRepository.Setup(x => x.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentId);

        _mockDocumentRepository.Setup(x => x.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockVectorStore.Setup(x => x.StoreAsync(It.IsAny<DocumentChunk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("chunk-id");

        _mockVectorStore.Setup(x => x.UpdateAsync(It.IsAny<DocumentChunk>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.IndexDocumentAsync(documentId, chunks, metadata);

        // Assert
        Assert.Equal(chunkCount, result.Chunks.Count);
        _mockEmbeddingService.Verify(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(chunkCount));
        _mockVectorStore.Verify(x => x.StoreAsync(It.IsAny<DocumentChunk>(), It.IsAny<CancellationToken>()), Times.Exactly(chunkCount));
    }
}
