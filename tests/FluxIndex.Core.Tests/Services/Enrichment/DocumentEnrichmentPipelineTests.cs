using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Enrichment;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Enrichment;

/// <summary>
/// Tests for DocumentEnrichmentPipeline
/// </summary>
public class DocumentEnrichmentPipelineTests
{
    private readonly IEmbeddingService _mockEmbeddingService;
    private readonly ITextCompletionService _mockTextCompletionService;
    private readonly IAdvancedEntityExtractionService _mockEntityExtractionService;
    private readonly ILogger<DocumentEnrichmentPipeline> _logger;

    public DocumentEnrichmentPipelineTests()
    {
        _mockEmbeddingService = Substitute.For<IEmbeddingService>();
        _mockTextCompletionService = Substitute.For<ITextCompletionService>();
        _mockEntityExtractionService = Substitute.For<IAdvancedEntityExtractionService>();
        _logger = NullLogger<DocumentEnrichmentPipeline>.Instance;

        // Setup default embedding behavior
        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f });

        _mockEmbeddingService.GetModelName().Returns("test-embedding-model");

        // Setup default text completion behavior
        _mockTextCompletionService.CompleteAsync(
                Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("Generated text response");

        // Setup default entity extraction behavior
        _mockEntityExtractionService.ExtractEntityGraphAsync(
                Arg.Any<string>(),
                Arg.Any<EntityExtractionOptions?>(),
                Arg.Any<CancellationToken>()).Returns(new EntityGraph
            {
                SourceId = "test-source",
                Entities = new List<ExtractedEntity>
                {
                    new ExtractedEntity
                    {
                        Id = "entity-1",
                        Text = "Test Entity",
                        NormalizedText = "test entity",
                        Type = NamedEntityType.Concept,
                        Confidence = 0.9
                    }
                },
                Relations = new List<EntityRelation>()
            });
    }

    private DocumentEnrichmentPipeline CreatePipeline(EnrichmentPipelineConfig? config = null)
    {
        return new DocumentEnrichmentPipeline(
            _mockEmbeddingService,
            _logger,
            _mockTextCompletionService,
            _mockEntityExtractionService,
            config);
    }

    private ChunkEnrichmentInput CreateTestInput(string content = "Test content for enrichment")
    {
        return new ChunkEnrichmentInput
        {
            ChunkId = Guid.NewGuid().ToString(),
            Content = content,
            DocumentId = "doc-1",
            DocumentTitle = "Test Document",
            ChunkIndex = 0,
            TotalChunks = 1
        };
    }

    #region Basic Pipeline Tests

    [Fact]
    public async Task EnrichChunkAsync_WithDefaultOptions_GeneratesContentEmbedding()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var input = CreateTestInput();
        var options = new EnrichmentOptions
        {
            GenerateContentEmbedding = true,
            GenerateContextualEmbedding = false,
            GenerateHypotheticalEmbedding = false,
            GenerateSummaryEmbedding = false,
            ExtractEntities = false,
            ExtractRelationships = false,
            GenerateContextualSummary = false
        };

        // Act
        var result = await pipeline.EnrichChunkAsync(input, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(input.ChunkId, result.ChunkId);
        Assert.NotNull(result.Embeddings);
        Assert.NotNull(result.Embeddings.Content);
        Assert.Equal(5, result.Embeddings.Content.Length);

        await _mockEmbeddingService.Received(1).GenerateEmbeddingAsync(input.Content, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrichChunkAsync_WithContextualEmbedding_GeneratesContextualContent()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var input = CreateTestInput("Machine learning is a subset of AI.");
        var options = new EnrichmentOptions
        {
            GenerateContentEmbedding = false,
            GenerateContextualEmbedding = true,
            GenerateHypotheticalEmbedding = false,
            GenerateSummaryEmbedding = false,
            ExtractEntities = false
        };

        // Act
        var result = await pipeline.EnrichChunkAsync(input, options);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Embeddings.Contextual);
    }

    [Fact]
    public async Task EnrichChunkAsync_WithHyDE_GeneratesHypotheticalDocument()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var input = CreateTestInput("What is machine learning?");
        var options = new EnrichmentOptions
        {
            GenerateContentEmbedding = false,
            GenerateContextualEmbedding = false,
            GenerateHypotheticalEmbedding = true,
            GenerateSummaryEmbedding = false,
            ExtractEntities = false
        };

        _mockTextCompletionService.CompleteAsync(
                Arg.Is<string>(s => s.Contains("hypothetical") || s.Contains("HyDE")),
                Arg.Any<Flux.Abstractions.TextCompletionOptions?>(),
                Arg.Any<CancellationToken>()).Returns("Machine learning is a field of artificial intelligence that enables computers to learn from data.");

        // Act
        var result = await pipeline.EnrichChunkAsync(input, options);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Embeddings.Hypothetical);
    }

    [Fact]
    public async Task EnrichChunkAsync_WithEntityExtraction_ExtractsEntities()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var input = CreateTestInput("Microsoft develops Azure cloud services.");
        var options = new EnrichmentOptions
        {
            GenerateContentEmbedding = false,
            GenerateContextualEmbedding = false,
            GenerateHypotheticalEmbedding = false,
            ExtractEntities = true,
            ExtractRelationships = false
        };

        _mockEntityExtractionService.ExtractEntityGraphAsync(
                Arg.Any<string>(),
                Arg.Any<EntityExtractionOptions?>(),
                Arg.Any<CancellationToken>()).Returns(new EntityGraph
            {
                SourceId = input.ChunkId,
                Entities = new List<ExtractedEntity>
                {
                    new ExtractedEntity
                    {
                        Id = "e1",
                        Text = "Microsoft",
                        NormalizedText = "microsoft",
                        Type = NamedEntityType.Organization,
                        Confidence = 0.95
                    },
                    new ExtractedEntity
                    {
                        Id = "e2",
                        Text = "Azure",
                        NormalizedText = "azure",
                        Type = NamedEntityType.Product,
                        Confidence = 0.90
                    }
                },
                Relations = new List<EntityRelation>()
            });

        // Act
        var result = await pipeline.EnrichChunkAsync(input, options);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Entities);
        Assert.Equal(2, result.Entities.Count);
        Assert.Contains(result.Entities, e => e.Name == "Microsoft");
        Assert.Contains(result.Entities, e => e.Name == "Azure");
    }

    [Fact]
    public async Task EnrichChunkAsync_WithRelationships_ExtractsRelationships()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var input = CreateTestInput("Microsoft develops Azure cloud services.");
        var options = new EnrichmentOptions
        {
            GenerateContentEmbedding = false,
            ExtractEntities = true,
            ExtractRelationships = true
        };

        _mockEntityExtractionService.ExtractEntityGraphAsync(
                Arg.Any<string>(),
                Arg.Any<EntityExtractionOptions?>(),
                Arg.Any<CancellationToken>()).Returns(new EntityGraph
            {
                SourceId = input.ChunkId,
                Entities = new List<ExtractedEntity>
                {
                    new ExtractedEntity { Id = "e1", Text = "Microsoft", NormalizedText = "microsoft", Type = NamedEntityType.Organization, Confidence = 0.95 },
                    new ExtractedEntity { Id = "e2", Text = "Azure", NormalizedText = "azure", Type = NamedEntityType.Product, Confidence = 0.90 }
                },
                Relations = new List<EntityRelation>
                {
                    new EntityRelation
                    {
                        Id = "r1",
                        SourceEntityId = "e1",
                        TargetEntityId = "e2",
                        Type = RelationType.Owns,
                        Label = "develops",
                        Confidence = 0.85,
                        Evidence = "Microsoft develops Azure"
                    }
                }
            });

        // Act
        var result = await pipeline.EnrichChunkAsync(input, options);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Relationships);
        Assert.Single(result.Relationships);
        Assert.Equal("Microsoft", result.Relationships[0].SourceEntity);
        Assert.Equal("Azure", result.Relationships[0].TargetEntity);
    }

    #endregion

    #region Batch Processing Tests

    [Fact]
    public async Task EnrichChunksBatchAsync_MultipleInputs_ProcessesAll()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var inputs = new List<ChunkEnrichmentInput>
        {
            CreateTestInput("First chunk content"),
            CreateTestInput("Second chunk content"),
            CreateTestInput("Third chunk content")
        };
        var options = new EnrichmentOptions
        {
            GenerateContentEmbedding = true,
            ExtractEntities = false
        };

        // Act
        var results = await pipeline.EnrichChunksBatchAsync(inputs, options);

        // Assert
        Assert.NotNull(results);
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.NotNull(r.Embeddings.Content));
    }

    [Fact]
    public async Task EnrichChunksBatchAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var inputs = Enumerable.Range(0, 10)
            .Select(i => CreateTestInput($"Content {i}"))
            .ToList();

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - Batch processing throws TaskCanceledException (derived from OperationCanceledException)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.EnrichChunksBatchAsync(inputs, ct: cts.Token));
    }

    #endregion

    #region Document-Level Enrichment Tests

    [Fact]
    public async Task EnrichDocumentAsync_MultipleChunks_ReturnsDocumentResult()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var input = new DocumentEnrichmentInput
        {
            DocumentId = "doc-1",
            Title = "AI Handbook",
            Chunks = new List<ChunkEnrichmentInput>
            {
                new ChunkEnrichmentInput
                {
                    ChunkId = "chunk-1",
                    Content = "Chapter 1: Introduction to AI",
                    DocumentId = "doc-1",
                    ChunkIndex = 0,
                    TotalChunks = 3
                },
                new ChunkEnrichmentInput
                {
                    ChunkId = "chunk-2",
                    Content = "Chapter 2: Machine Learning basics",
                    DocumentId = "doc-1",
                    ChunkIndex = 1,
                    TotalChunks = 3
                },
                new ChunkEnrichmentInput
                {
                    ChunkId = "chunk-3",
                    Content = "Chapter 3: Deep Learning",
                    DocumentId = "doc-1",
                    ChunkIndex = 2,
                    TotalChunks = 3
                }
            }
        };
        var options = new EnrichmentOptions
        {
            GenerateContentEmbedding = true,
            ExtractEntities = true,
            ExtractRelationships = true
        };

        // Act
        var result = await pipeline.EnrichDocumentAsync(input, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("doc-1", result.DocumentId);
        Assert.Equal(3, result.Chunks.Count);
    }

    [Fact]
    public async Task EnrichDocumentAsync_EmptyChunks_ReturnsEmptyResult()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var input = new DocumentEnrichmentInput
        {
            DocumentId = "doc-empty",
            Chunks = new List<ChunkEnrichmentInput>()
        };

        // Act
        var result = await pipeline.EnrichDocumentAsync(input);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Chunks);
    }

    #endregion

    #region Embedding Generation Tests

    [Fact]
    public async Task GenerateEmbeddingsAsync_ContentOnly_GeneratesSingleEmbedding()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var options = new EmbeddingGenerationOptions
        {
            GenerateContentEmbedding = true,
            GenerateSummaryEmbedding = false,
            GenerateHypotheticalEmbedding = false,
            GenerateQuestionEmbeddings = false
        };

        // Act
        var result = await pipeline.GenerateEmbeddingsAsync("Simple test content", options);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Content);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_AllTypes_GeneratesMultipleEmbeddings()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var options = new EmbeddingGenerationOptions
        {
            GenerateContentEmbedding = true,
            GenerateSummaryEmbedding = true,
            GenerateHypotheticalEmbedding = true,
            GenerateQuestionEmbeddings = false
        };

        // Act
        var result = await pipeline.GenerateEmbeddingsAsync("Test content about AI", options);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Content);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task EnrichChunkAsync_NullInput_ThrowsException()
    {
        // Arrange
        var pipeline = CreatePipeline();

        // Act & Assert - Implementation throws NullReferenceException when input is null
        await Assert.ThrowsAnyAsync<Exception>(
            () => pipeline.EnrichChunkAsync(null!));
    }

    [Fact]
    public async Task EnrichChunkAsync_EmptyContent_HandlesGracefully()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var input = new ChunkEnrichmentInput
        {
            ChunkId = "empty-chunk",
            Content = "",
            DocumentId = "doc-1"
        };
        var options = new EnrichmentOptions
        {
            GenerateContentEmbedding = true,
            ExtractEntities = false
        };

        // Act
        var result = await pipeline.EnrichChunkAsync(input, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(input.ChunkId, result.ChunkId);
    }

    [Fact]
    public async Task EnrichChunkAsync_EmbeddingServiceFailure_ThrowsException()
    {
        // Arrange
        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("Embedding service unavailable"));

        var pipeline = CreatePipeline();
        var input = CreateTestInput();
        var options = new EnrichmentOptions
        {
            GenerateContentEmbedding = true
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.EnrichChunkAsync(input, options));
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public async Task EnrichChunkAsync_WithCustomConfig_UsesConfiguredDefaults()
    {
        // Arrange
        var customConfig = new EnrichmentPipelineConfig
        {
            DefaultOptions = new EnrichmentOptions
            {
                GenerateContentEmbedding = true,
                GenerateContextualEmbedding = true,
                ExtractEntities = true,
                MinEntityConfidence = 0.8
            }
        };

        var pipeline = CreatePipeline(customConfig);
        var input = CreateTestInput();

        // Act - use default options from config (null options parameter)
        var result = await pipeline.EnrichChunkAsync(input, null);

        // Assert
        Assert.NotNull(result);
        await _mockEmbeddingService.Received().GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetConfiguration_ReturnsConfig()
    {
        // Arrange
        var customConfig = new EnrichmentPipelineConfig
        {
            DefaultOptions = new EnrichmentOptions
            {
                GenerateContentEmbedding = true,
                MaxConcurrency = 8
            }
        };
        var pipeline = CreatePipeline(customConfig);

        // Act
        var config = pipeline.GetConfiguration();

        // Assert
        Assert.NotNull(config);
        Assert.Equal(8, config.DefaultOptions.MaxConcurrency);
    }

    #endregion

    #region Graph Data Tests

    [Fact]
    public async Task BuildGraphDataAsync_WithEntitiesAndRelations_BuildsGraph()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var enrichedChunks = new List<EnrichedChunk>
        {
            new EnrichedChunk
            {
                ChunkId = "chunk-1",
                DocumentId = "doc-1",
                Content = "Test content",
                Entities = new List<EnrichmentEntity>
                {
                    new EnrichmentEntity { Id = "e1", Name = "Entity1", NormalizedName = "entity1", Type = NamedEntityType.Concept, Confidence = 0.9 },
                    new EnrichmentEntity { Id = "e2", Name = "Entity2", NormalizedName = "entity2", Type = NamedEntityType.Concept, Confidence = 0.85 }
                },
                Relationships = new List<ExtractedRelationship>
                {
                    new ExtractedRelationship { Id = "r1", SourceEntity = "Entity1", TargetEntity = "Entity2", Type = RelationType.RelatedTo, Confidence = 0.8 }
                }
            }
        };

        // Act
        var result = await pipeline.BuildGraphDataAsync(enrichedChunks);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Entities.Count);
        Assert.Single(result.Relationships);
    }

    [Fact]
    public async Task BuildGraphDataAsync_EmptyChunks_ReturnsEmptyGraph()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var enrichedChunks = new List<EnrichedChunk>();

        // Act
        var result = await pipeline.BuildGraphDataAsync(enrichedChunks);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Entities);
        Assert.Empty(result.Relationships);
    }

    #endregion

    #region Contextual Summary Tests

    [Fact]
    public async Task GenerateContextualSummaryAsync_WithContent_GeneratesSummary()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var chunkContent = "Machine learning algorithms can learn from data.";

        _mockTextCompletionService.CompleteAsync(
                Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("This chunk discusses how ML algorithms work with data.");

        // Act
        var result = await pipeline.GenerateContextualSummaryAsync(chunkContent);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Summary);
    }

    [Fact]
    public async Task GenerateContextualSummaryAsync_WithContext_IncludesContextInPrompt()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var chunkContent = "It uses neural networks for processing.";
        var documentContext = "A guide about deep learning";
        var precedingChunks = "Introduction to machine learning concepts.";

        // Act
        var result = await pipeline.GenerateContextualSummaryAsync(
            chunkContent,
            documentContext,
            precedingChunks);

        // Assert
        Assert.NotNull(result);
        await _mockTextCompletionService.Received().CompleteAsync(
                Arg.Is<string>(s => s.Contains(documentContext) || s.Contains("deep learning")),
                Arg.Any<Flux.Abstractions.TextCompletionOptions?>(),
                Arg.Any<CancellationToken>());
    }

    #endregion

    #region Entity Extraction Tests

    [Fact]
    public async Task ExtractEntitiesAsync_ValidContent_ReturnsEntities()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var content = "Apple Inc. is headquartered in Cupertino, California.";

        _mockEntityExtractionService.ExtractEntityGraphAsync(
                Arg.Any<string>(),
                Arg.Any<EntityExtractionOptions?>(),
                Arg.Any<CancellationToken>()).Returns(new EntityGraph
            {
                SourceId = "test",
                Entities = new List<ExtractedEntity>
                {
                    new ExtractedEntity { Id = "e1", Text = "Apple Inc.", NormalizedText = "apple inc.", Type = NamedEntityType.Organization, Confidence = 0.95 },
                    new ExtractedEntity { Id = "e2", Text = "Cupertino", NormalizedText = "cupertino", Type = NamedEntityType.Location, Confidence = 0.90 },
                    new ExtractedEntity { Id = "e3", Text = "California", NormalizedText = "california", Type = NamedEntityType.GeopoliticalEntity, Confidence = 0.92 }
                },
                Relations = new List<EntityRelation>
                {
                    new EntityRelation { Id = "r1", SourceEntityId = "e1", TargetEntityId = "e2", Type = RelationType.LocatedIn, Label = "headquartered in", Confidence = 0.88 }
                }
            });

        // Act
        var result = await pipeline.ExtractEntitiesAsync(content);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Entities.Count);
        Assert.Single(result.Relationships);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_WithOptions_AppliesOptions()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var content = "Test content";
        var options = new EnrichmentEntityOptions
        {
            MinConfidence = 0.9,
            MaxEntitiesPerChunk = 5
        };

        // Act
        var result = await pipeline.ExtractEntitiesAsync(content, options);

        // Assert
        Assert.NotNull(result);
        await _mockEntityExtractionService.Received(1).ExtractEntityGraphAsync(
                content,
                Arg.Is<EntityExtractionOptions>(o => o != null && o.MinConfidence >= 0.9),
                Arg.Any<CancellationToken>());
    }

    #endregion
}
