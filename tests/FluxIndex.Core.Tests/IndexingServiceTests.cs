using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace FluxIndex.Core.Tests;

public class IndexingServiceTests
{
    private readonly IDocumentRepository _mockDocumentRepository;
    private readonly IVectorStore _mockVectorStore;
    private readonly IEmbeddingService _mockEmbeddingService;
    private readonly IMetadataEnrichmentService _mockMetadataEnrichmentService;
    private readonly ILogger<IndexingService> _logger;
    private readonly IndexingService _service;

    public IndexingServiceTests()
    {
        _mockDocumentRepository = Substitute.For<IDocumentRepository>();
        _mockVectorStore = Substitute.For<IVectorStore>();
        _mockEmbeddingService = Substitute.For<IEmbeddingService>();
        _mockMetadataEnrichmentService = Substitute.For<IMetadataEnrichmentService>();
        _logger = NullLogger<IndexingService>.Instance;

        _service = new IndexingService(
            _mockDocumentRepository,
            _mockVectorStore,
            _mockEmbeddingService,
            _mockMetadataEnrichmentService,
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

        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(embedding);

        _mockMetadataEnrichmentService.EnrichMetadataAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>()).Returns(enrichedMetadata);

        _mockMetadataEnrichmentService.EvaluateQualityAsync(
                Arg.Any<DocumentChunk>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()).Returns(quality);

        _mockMetadataEnrichmentService.AnalyzeRelationshipsAsync(
                Arg.Any<DocumentChunk>(),
                Arg.Any<IEnumerable<DocumentChunk>>(),
                Arg.Any<CancellationToken>()).Returns(new List<ChunkRelationship>());

        _mockDocumentRepository.AddAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(documentId);

        _mockDocumentRepository.UpdateAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _mockVectorStore.StoreAsync(Arg.Any<DocumentChunk>(), Arg.Any<CancellationToken>()).Returns("chunk-id");

        _mockVectorStore.UpdateAsync(Arg.Any<DocumentChunk>(), Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await _service.IndexDocumentAsync(documentId, chunks, metadata, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(documentId, result.Id);
        Assert.Equal(DocumentStatus.Indexed, result.Status);
        Assert.Equal(2, result.Chunks.Count);

        // Verify all chunks were processed
        await _mockEmbeddingService.Received(2).GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _mockVectorStore.Received(2).StoreAsync(Arg.Any<DocumentChunk>(), Arg.Any<CancellationToken>());
        await _mockDocumentRepository.Received(1).AddAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>());
        await _mockDocumentRepository.Received(1).UpdateAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>());
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

        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f, 0.2f, 0.3f });

        _mockMetadataEnrichmentService.EnrichMetadataAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>()).Returns(new ChunkMetadata());

        _mockMetadataEnrichmentService.EvaluateQualityAsync(
                Arg.Any<DocumentChunk>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()).Returns(new ChunkQuality
            {
                ContentCompleteness = 0.9,
                InformationDensity = 0.85,
                Coherence = 0.88
            });

        _mockMetadataEnrichmentService.AnalyzeRelationshipsAsync(
                Arg.Any<DocumentChunk>(),
                Arg.Any<IEnumerable<DocumentChunk>>(),
                Arg.Any<CancellationToken>()).Returns(new List<ChunkRelationship>());

        _mockDocumentRepository.AddAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(documentId);

        _mockDocumentRepository.UpdateAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _mockVectorStore.StoreAsync(Arg.Any<DocumentChunk>(), Arg.Any<CancellationToken>()).Returns("chunk-id");

        _mockVectorStore.UpdateAsync(Arg.Any<DocumentChunk>(), Arg.Any<CancellationToken>()).Returns(true);

        // Act
        await _service.IndexDocumentAsync(documentId, chunks, metadata, TestContext.Current.CancellationToken);

        // Assert
        // Verify enrichment called for each chunk with correct context
        await _mockMetadataEnrichmentService.Received(1).EnrichMetadataAsync(
            "First chunk",  // First chunk has no previous
            0,
            null,
            "Middle chunk",
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<CancellationToken>());

        await _mockMetadataEnrichmentService.Received(1).EnrichMetadataAsync(
            "Middle chunk",  // Middle chunk has both previous and next
            1,
            "First chunk",
            "Last chunk",
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<CancellationToken>());

        await _mockMetadataEnrichmentService.Received(1).EnrichMetadataAsync(
            "Last chunk",  // Last chunk has no next
            2,
            "Middle chunk",
            null,
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<CancellationToken>());
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

        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f, 0.2f, 0.3f });

        _mockMetadataEnrichmentService.EnrichMetadataAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>()).Returns(new ChunkMetadata());

        _mockMetadataEnrichmentService.EvaluateQualityAsync(
                Arg.Any<DocumentChunk>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()).Returns(quality);

        _mockMetadataEnrichmentService.AnalyzeRelationshipsAsync(
                Arg.Any<DocumentChunk>(),
                Arg.Any<IEnumerable<DocumentChunk>>(),
                Arg.Any<CancellationToken>()).Returns(new List<ChunkRelationship>());

        _mockDocumentRepository.AddAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(documentId);

        _mockDocumentRepository.UpdateAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _mockVectorStore.StoreAsync(Arg.Any<DocumentChunk>(), Arg.Any<CancellationToken>()).Returns("chunk-id");

        _mockVectorStore.UpdateAsync(Arg.Any<DocumentChunk>(), Arg.Any<CancellationToken>()).Returns(true);

        // Act
        await _service.IndexDocumentAsync(documentId, chunks, metadata, TestContext.Current.CancellationToken);

        // Assert
        await _mockMetadataEnrichmentService.Received(1).EvaluateQualityAsync(
            Arg.Any<DocumentChunk>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
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

        _mockDocumentRepository.AddAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(documentId);

        // Setup metadata enrichment to return valid metadata
        _mockMetadataEnrichmentService.EnrichMetadataAsync(
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<CancellationToken>()).Returns(new ChunkMetadata());

        // Setup quality evaluation to return valid quality
        _mockMetadataEnrichmentService.EvaluateQualityAsync(
            Arg.Any<DocumentChunk>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(new ChunkQuality());

        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Throws(new Exception("Embedding service error"));

        _mockDocumentRepository.UpdateAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() =>
            _service.IndexDocumentAsync(documentId, chunks, metadata, TestContext.Current.CancellationToken));

        // Verify document was marked as failed
        await _mockDocumentRepository.Received(1).UpdateAsync(
            Arg.Is<Document>(d => d.Id == documentId && d.Status == DocumentStatus.Failed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteDocumentAsync_ValidDocumentId_DeletesSuccessfully()
    {
        // Arrange
        var documentId = "doc1";

        _mockVectorStore.DeleteByDocumentIdAsync(documentId, Arg.Any<CancellationToken>()).Returns(true);

        _mockDocumentRepository.DeleteAsync(documentId, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await _service.DeleteDocumentAsync(documentId, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);

        await _mockVectorStore.Received(1).DeleteByDocumentIdAsync(documentId, Arg.Any<CancellationToken>());
        await _mockDocumentRepository.Received(1).DeleteAsync(documentId, Arg.Any<CancellationToken>());
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

        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f, 0.2f, 0.3f });

        _mockMetadataEnrichmentService.EnrichMetadataAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>()).Returns(new ChunkMetadata());

        _mockMetadataEnrichmentService.EvaluateQualityAsync(
                Arg.Any<DocumentChunk>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()).Returns(new ChunkQuality
            {
                ContentCompleteness = 0.9,
                InformationDensity = 0.85,
                Coherence = 0.88
            });

        _mockMetadataEnrichmentService.AnalyzeRelationshipsAsync(
                Arg.Any<DocumentChunk>(),
                Arg.Any<IEnumerable<DocumentChunk>>(),
                Arg.Any<CancellationToken>()).Returns(relationships);

        _mockDocumentRepository.AddAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(documentId);

        _mockDocumentRepository.UpdateAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _mockVectorStore.StoreAsync(Arg.Any<DocumentChunk>(), Arg.Any<CancellationToken>()).Returns("chunk-id");

        _mockVectorStore.UpdateAsync(Arg.Any<DocumentChunk>(), Arg.Any<CancellationToken>()).Returns(true);

        // Act
        await _service.IndexDocumentAsync(documentId, chunks, metadata, TestContext.Current.CancellationToken);

        // Assert
        // Verify relationship analysis was called for each chunk
        await _mockMetadataEnrichmentService.Received(2).AnalyzeRelationshipsAsync(
            Arg.Any<DocumentChunk>(),
            Arg.Any<IEnumerable<DocumentChunk>>(),
            Arg.Any<CancellationToken>());

        // Verify chunks were updated with relationships
        await _mockVectorStore.Received(2).UpdateAsync(
            Arg.Any<DocumentChunk>(),
            Arg.Any<CancellationToken>());
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

        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f, 0.2f, 0.3f });

        _mockMetadataEnrichmentService.EnrichMetadataAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>()).Returns(new ChunkMetadata());

        _mockMetadataEnrichmentService.EvaluateQualityAsync(
                Arg.Any<DocumentChunk>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()).Returns(new ChunkQuality
            {
                ContentCompleteness = 0.9,
                InformationDensity = 0.85,
                Coherence = 0.88
            });

        _mockMetadataEnrichmentService.AnalyzeRelationshipsAsync(
                Arg.Any<DocumentChunk>(),
                Arg.Any<IEnumerable<DocumentChunk>>(),
                Arg.Any<CancellationToken>()).Returns(new List<ChunkRelationship>());

        _mockDocumentRepository.AddAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(documentId);

        _mockDocumentRepository.UpdateAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _mockVectorStore.StoreAsync(Arg.Any<DocumentChunk>(), Arg.Any<CancellationToken>()).Returns("chunk-id");

        _mockVectorStore.UpdateAsync(Arg.Any<DocumentChunk>(), Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await _service.IndexDocumentAsync(documentId, chunks, metadata, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(chunkCount, result.Chunks.Count);
        await _mockEmbeddingService.Received().GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _mockVectorStore.Received().StoreAsync(Arg.Any<DocumentChunk>(), Arg.Any<CancellationToken>());
    }
}
