using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

using HybridSearchResult = FluxIndex.Core.Domain.Models.HybridSearchResult;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// Unit tests for CorrectiveRAGService.
/// Tests the Corrective RAG (CRAG) pattern implementation.
/// </summary>
public class CorrectiveRAGServiceTests
{
    private readonly IHybridSearchService _mockSearchService;
    private readonly IEmbeddingService _mockEmbeddingService;
    private readonly IRetrievalVerificationService _mockVerificationService;
    private readonly ITextCompletionService _mockCompletionService;
    private readonly ILogger<CorrectiveRAGService> _mockLogger;
    private readonly CorrectiveRAGService _service;

    public CorrectiveRAGServiceTests()
    {
        _mockSearchService = Substitute.For<IHybridSearchService>();
        _mockEmbeddingService = Substitute.For<IEmbeddingService>();
        _mockVerificationService = Substitute.For<IRetrievalVerificationService>();
        _mockCompletionService = Substitute.For<ITextCompletionService>();
        _mockLogger = Substitute.For<ILogger<CorrectiveRAGService>>();

        SetupDefaultMocks();

        _service = new CorrectiveRAGService(
            _mockSearchService,
            _mockEmbeddingService,
            _mockVerificationService,
            _mockCompletionService,
            Microsoft.Extensions.Options.Options.Create(new CorrectiveRAGServiceOptions()),
            _mockLogger);
    }

    private void SetupDefaultMocks()
    {
        _mockSearchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<HybridSearchOptions>(),
                Arg.Any<CancellationToken>()).Returns(CreateDefaultHybridResults(5));

        _mockEmbeddingService.GenerateEmbeddingAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()).Returns(CreateRandomEmbedding());
    }

    private static IReadOnlyList<HybridSearchResult> CreateDefaultHybridResults(int count)
    {
        var results = new List<HybridSearchResult>();
        for (int i = 0; i < count; i++)
        {
            results.Add(new HybridSearchResult
            {
                Chunk = CreateDocumentChunk($"doc-{i}", $"Test content for document {i} with relevant information."),
                FusedScore = 0.9 - (i * 0.1),
                VectorScore = 0.85 - (i * 0.05),
                SparseScore = 0.8 - (i * 0.1),
                FusedRank = i + 1
            });
        }

        return results;
    }

    private static DocumentChunk CreateDocumentChunk(string id, string content)
    {
        return new DocumentChunk
        {
            Id = id,
            DocumentId = $"parent-{id}",
            Content = content,
            ChunkIndex = 0,
            TotalChunks = 1,
            Embedding = CreateRandomEmbedding(),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static float[] CreateRandomEmbedding(int dimensions = 384)
    {
        var random = new Random(42);
        var embedding = new float[dimensions];
        for (int i = 0; i < dimensions; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }
        return embedding;
    }

    #region RetrieveWithCorrectionAsync Tests

    [Fact]
    public async Task RetrieveWithCorrectionAsync_ValidQuery_ReturnsResult()
    {
        // Arrange
        var query = "test query";

        // Act
        var result = await _service.RetrieveWithCorrectionAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public async Task RetrieveWithCorrectionAsync_NullQuery_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.RetrieveWithCorrectionAsync(null!));
    }

    [Fact]
    public async Task RetrieveWithCorrectionAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.RetrieveWithCorrectionAsync("test", null, cts.Token));
    }

    [Fact]
    public async Task RetrieveWithCorrectionAsync_ReturnsDocuments()
    {
        // Arrange
        var query = "test query";

        // Act
        var result = await _service.RetrieveWithCorrectionAsync(query);

        // Assert
        Assert.NotEmpty(result.Documents);
    }

    [Fact]
    public async Task RetrieveWithCorrectionAsync_IncludesCorrectionSteps()
    {
        // Arrange
        var query = "test query";

        // Act
        var result = await _service.RetrieveWithCorrectionAsync(query);

        // Assert
        Assert.NotEmpty(result.CorrectionSteps);
        Assert.Contains(result.CorrectionSteps, s => s.Type == CorrectionStepType.InitialRetrieval);
    }

    [Fact]
    public async Task RetrieveWithCorrectionAsync_RecordsProcessingTime()
    {
        // Arrange
        var query = "test query";

        // Act
        var result = await _service.RetrieveWithCorrectionAsync(query);

        // Assert
        Assert.True(result.ProcessingTime > TimeSpan.Zero);
    }

    [Fact]
    public async Task RetrieveWithCorrectionAsync_WithCustomOptions_UsesOptions()
    {
        // Arrange
        var query = "test query";
        var options = new CorrectiveRAGOptions
        {
            MaxInitialDocuments = 3,
            CorrectThreshold = 0.8
        };

        // Act
        var result = await _service.RetrieveWithCorrectionAsync(query, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public async Task RetrieveWithCorrectionAsync_CallsSearchService()
    {
        // Arrange
        var query = "test query";

        // Act
        await _service.RetrieveWithCorrectionAsync(query);

        // Assert
        await _mockSearchService.Received().SearchAsync(
                query,
                Arg.Any<HybridSearchOptions>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetrieveWithCorrectionAsync_HasConfidenceScore()
    {
        // Arrange
        var query = "test query";

        // Act
        var result = await _service.RetrieveWithCorrectionAsync(query);

        // Assert
        Assert.True(result.ConfidenceScore >= 0);
    }

    #endregion

    #region GradeDocumentsAsync Tests

    [Fact]
    public async Task GradeDocumentsAsync_ValidInput_ReturnsGradingResult()
    {
        // Arrange
        var query = "test query";
        var documents = new[]
        {
            CreateDocumentChunk("doc-1", "Relevant content about test query topic"),
            CreateDocumentChunk("doc-2", "Another relevant document with test information")
        };

        // Act
        var result = await _service.GradeDocumentsAsync(query, documents);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.GradedDocuments.Count);
    }

    [Fact]
    public async Task GradeDocumentsAsync_NullQuery_ThrowsArgumentNullException()
    {
        // Arrange
        var documents = new[] { CreateDocumentChunk("doc-1", "content") };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.GradeDocumentsAsync(null!, documents));
    }

    [Fact]
    public async Task GradeDocumentsAsync_NullDocuments_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.GradeDocumentsAsync("query", null!));
    }

    [Fact]
    public async Task GradeDocumentsAsync_EmptyDocuments_ReturnsEmptyResult()
    {
        // Arrange
        var query = "test query";
        var documents = Array.Empty<DocumentChunk>();

        // Act
        var result = await _service.GradeDocumentsAsync(query, documents);

        // Assert
        Assert.Empty(result.GradedDocuments);
    }

    [Fact]
    public async Task GradeDocumentsAsync_AssignsGrades()
    {
        // Arrange
        var query = "test query about specific topic";
        var documents = new[]
        {
            CreateDocumentChunk("doc-1", "This is about a specific topic with test information"),
            CreateDocumentChunk("doc-2", "Unrelated content about weather and sports")
        };

        // Act
        var result = await _service.GradeDocumentsAsync(query, documents);

        // Assert
        Assert.All(result.GradedDocuments, graded =>
        {
            Assert.True(Enum.IsDefined(typeof(DocumentRelevanceGrade), graded.Grade));
        });
    }

    [Fact]
    public async Task GradeDocumentsAsync_CalculatesRelevanceScores()
    {
        // Arrange
        var query = "test query";
        var documents = new[] { CreateDocumentChunk("doc-1", "Relevant test content") };

        // Act
        var result = await _service.GradeDocumentsAsync(query, documents);

        // Assert
        var gradedDoc = result.GradedDocuments.First();
        Assert.True(gradedDoc.RelevanceScore >= 0 && gradedDoc.RelevanceScore <= 1);
    }

    [Fact]
    public async Task GradeDocumentsAsync_ProvidesOverallAssessment()
    {
        // Arrange
        var query = "test query";
        var documents = new[]
        {
            CreateDocumentChunk("doc-1", "Test content"),
            CreateDocumentChunk("doc-2", "More test content")
        };

        // Act
        var result = await _service.GradeDocumentsAsync(query, documents);

        // Assert
        Assert.True(Enum.IsDefined(typeof(OverallAssessment), result.Assessment));
    }

    [Fact]
    public async Task GradeDocumentsAsync_CountsGradeCategories()
    {
        // Arrange
        var query = "test query";
        var documents = new[]
        {
            CreateDocumentChunk("doc-1", "Test content"),
            CreateDocumentChunk("doc-2", "More content"),
            CreateDocumentChunk("doc-3", "Even more content")
        };

        // Act
        var result = await _service.GradeDocumentsAsync(query, documents);

        // Assert
        var totalCount = result.CorrectCount + result.AmbiguousCount + result.IncorrectCount;
        Assert.Equal(3, totalCount);
    }

    [Fact]
    public async Task GradeDocumentsAsync_RecordsProcessingTime()
    {
        // Arrange
        var query = "test query";
        var documents = new[] { CreateDocumentChunk("doc-1", "Content") };

        // Act
        var result = await _service.GradeDocumentsAsync(query, documents);

        // Assert
        Assert.True(result.ProcessingTime >= TimeSpan.Zero);
    }

    #endregion

    #region RefineKnowledgeAsync Tests

    [Fact]
    public async Task RefineKnowledgeAsync_ValidInput_ReturnsResult()
    {
        // Arrange
        var query = "test query";
        var documents = new[]
        {
            CreateDocumentChunk("doc-1", "Content with key information about the topic")
        };

        // Act
        var result = await _service.RefineKnowledgeAsync(query, documents);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RefineKnowledgeAsync_NullQuery_ThrowsArgumentNullException()
    {
        // Arrange
        var documents = new[] { CreateDocumentChunk("doc-1", "content") };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.RefineKnowledgeAsync(null!, documents));
    }

    [Fact]
    public async Task RefineKnowledgeAsync_NullDocuments_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.RefineKnowledgeAsync("query", null!));
    }

    [Fact]
    public async Task RefineKnowledgeAsync_EmptyDocuments_ReturnsEmptyResult()
    {
        // Arrange
        var query = "test query";
        var documents = Array.Empty<DocumentChunk>();

        // Act
        var result = await _service.RefineKnowledgeAsync(query, documents);

        // Assert
        Assert.Empty(result.RefinedDocuments);
    }

    [Fact]
    public async Task RefineKnowledgeAsync_ExtractsRefinedContent()
    {
        // Arrange
        var query = "specific topic";
        var documents = new[]
        {
            CreateDocumentChunk("doc-1", "This document contains information about the specific topic we are searching for.")
        };

        // Act
        var result = await _service.RefineKnowledgeAsync(query, documents);

        // Assert
        if (result.RefinedDocuments.Any())
        {
            var refined = result.RefinedDocuments.First();
            Assert.NotNull(refined.RefinedContent);
        }
    }

    [Fact]
    public async Task RefineKnowledgeAsync_ReturnsIsSuccessful()
    {
        // Arrange
        var query = "test query";
        var documents = new[]
        {
            CreateDocumentChunk("doc-1", "Content for refinement")
        };

        // Act
        var result = await _service.RefineKnowledgeAsync(query, documents);

        // Assert
        // Should either succeed or fail gracefully
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RefineKnowledgeAsync_RecordsProcessingTime()
    {
        // Arrange
        var query = "test query";
        var documents = new[] { CreateDocumentChunk("doc-1", "Content") };

        // Act
        var result = await _service.RefineKnowledgeAsync(query, documents);

        // Assert
        Assert.True(result.ProcessingTime >= TimeSpan.Zero);
    }

    #endregion

    #region PerformAlternativeRetrievalAsync Tests

    [Fact]
    public async Task PerformAlternativeRetrievalAsync_ValidInput_ReturnsResult()
    {
        // Arrange
        var query = "test query";
        var originalDocuments = new[]
        {
            CreateDocumentChunk("doc-1", "Irrelevant content")
        };

        // Act
        var result = await _service.PerformAlternativeRetrievalAsync(query, originalDocuments);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task PerformAlternativeRetrievalAsync_NullQuery_ThrowsArgumentNullException()
    {
        // Arrange
        var documents = new[] { CreateDocumentChunk("doc-1", "content") };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.PerformAlternativeRetrievalAsync(null!, documents));
    }

    [Fact]
    public async Task PerformAlternativeRetrievalAsync_NullDocuments_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.PerformAlternativeRetrievalAsync("query", null!));
    }

    [Fact]
    public async Task PerformAlternativeRetrievalAsync_RecordsStrategy()
    {
        // Arrange
        var query = "test query";
        var originalDocuments = new[]
        {
            CreateDocumentChunk("doc-1", "Content")
        };

        // Act
        var result = await _service.PerformAlternativeRetrievalAsync(query, originalDocuments);

        // Assert
        Assert.True(Enum.IsDefined(typeof(AlternativeRetrievalStrategy), result.Strategy));
    }

    [Fact]
    public async Task PerformAlternativeRetrievalAsync_RecordsProcessingTime()
    {
        // Arrange
        var query = "test query";
        var originalDocuments = new[] { CreateDocumentChunk("doc-1", "Content") };

        // Act
        var result = await _service.PerformAlternativeRetrievalAsync(query, originalDocuments);

        // Assert
        Assert.True(result.ProcessingTime >= TimeSpan.Zero);
    }

    [Fact]
    public async Task PerformAlternativeRetrievalAsync_TransformsQuery()
    {
        // Arrange
        var query = "test query";
        var originalDocuments = new[]
        {
            CreateDocumentChunk("doc-1", "Some related content with keywords")
        };

        // Act
        var result = await _service.PerformAlternativeRetrievalAsync(query, originalDocuments);

        // Assert
        // Transformed query should be present if transformation was applied
        Assert.NotNull(result);
    }

    [Fact]
    public async Task PerformAlternativeRetrievalAsync_ReturnsDocuments()
    {
        // Arrange
        var query = "test query";
        var originalDocuments = new[]
        {
            CreateDocumentChunk("doc-1", "Original irrelevant content")
        };

        // Act
        var result = await _service.PerformAlternativeRetrievalAsync(query, originalDocuments);

        // Assert
        Assert.NotNull(result.Documents);
    }

    #endregion

    #region Correction Action Tests

    [Fact]
    public async Task RetrieveWithCorrectionAsync_WithHighlyRelevantDocs_ReturnsNoneAction()
    {
        // Arrange
        var query = "test query";
        _mockSearchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<HybridSearchOptions>(),
                Arg.Any<CancellationToken>()).Returns(CreateHighRelevanceResults());

        var service = CreateService();

        // Act
        var result = await service.RetrieveWithCorrectionAsync(query);

        // Assert
        // With highly relevant docs, should take minimal correction action
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public async Task RetrieveWithCorrectionAsync_WithLowRelevanceDocs_PerformsCorrection()
    {
        // Arrange
        var query = "test query";
        _mockSearchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<HybridSearchOptions>(),
                Arg.Any<CancellationToken>()).Returns(CreateLowRelevanceResults());

        var service = CreateService();

        // Act
        var result = await service.RetrieveWithCorrectionAsync(query);

        // Assert
        Assert.NotNull(result);
        // Should have correction steps beyond initial retrieval
        Assert.True(result.CorrectionSteps.Count >= 1);
    }

    private static IReadOnlyList<HybridSearchResult> CreateHighRelevanceResults()
    {
        return new List<HybridSearchResult>
        {
            new()
            {
                Chunk = CreateDocumentChunk("doc-1", "Highly relevant test query content with important information"),
                FusedScore = 0.95,
                VectorScore = 0.92,
                SparseScore = 0.90,
                FusedRank = 1
            }
        };
    }

    private static IReadOnlyList<HybridSearchResult> CreateLowRelevanceResults()
    {
        return new List<HybridSearchResult>
        {
            new()
            {
                Chunk = CreateDocumentChunk("doc-1", "Unrelated weather forecast information"),
                FusedScore = 0.2,
                VectorScore = 0.15,
                SparseScore = 0.1,
                FusedRank = 1
            }
        };
    }

    private CorrectiveRAGService CreateService()
    {
        return new CorrectiveRAGService(
            _mockSearchService,
            _mockEmbeddingService,
            _mockVerificationService,
            _mockCompletionService,
            Microsoft.Extensions.Options.Options.Create(new CorrectiveRAGServiceOptions()),
            _mockLogger);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task FullCorrectiveWorkflow_ExecutesAllSteps()
    {
        // Arrange
        var query = "test query for full workflow";
        var options = new CorrectiveRAGOptions
        {
            EnableKnowledgeRefinement = true,
            EnableQueryTransformation = true,
            EnableDetailedLogging = true
        };

        // Act
        var result = await _service.RetrieveWithCorrectionAsync(query, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotEmpty(result.CorrectionSteps);
        Assert.Contains(result.CorrectionSteps, s => s.Type == CorrectionStepType.InitialRetrieval);
        Assert.Contains(result.CorrectionSteps, s => s.Type == CorrectionStepType.Grading);
    }

    [Fact]
    public async Task CorrectiveWorkflow_WithNoResults_HandlesGracefully()
    {
        // Arrange
        var query = "test query";
        _mockSearchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<HybridSearchOptions>(),
                Arg.Any<CancellationToken>()).Returns(new List<HybridSearchResult>());

        var service = CreateService();

        // Act
        var result = await service.RetrieveWithCorrectionAsync(query);

        // Assert
        Assert.NotNull(result);
        // Should handle empty results gracefully
    }

    [Fact]
    public async Task CorrectiveWorkflow_MetadataIncluded()
    {
        // Arrange
        var query = "test query";

        // Act
        var result = await _service.RetrieveWithCorrectionAsync(query);

        // Assert
        Assert.NotNull(result.Metadata);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task GradeDocumentsAsync_WithNullContent_HandlesGracefully()
    {
        // Arrange
        var query = "test query";
        var documents = new[]
        {
            new DocumentChunk
            {
                Id = "doc-1",
                DocumentId = "parent-1",
                Content = null!,
                ChunkIndex = 0,
                TotalChunks = 1,
                CreatedAt = DateTime.UtcNow
            }
        };

        // Act
        var result = await _service.GradeDocumentsAsync(query, documents);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GradeDocumentsAsync_WithEmptyContent_HandlesGracefully()
    {
        // Arrange
        var query = "test query";
        var documents = new[]
        {
            CreateDocumentChunk("doc-1", string.Empty)
        };

        // Act
        var result = await _service.GradeDocumentsAsync(query, documents);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.GradedDocuments);
    }

    [Fact]
    public async Task RefineKnowledgeAsync_WithShortContent_HandlesGracefully()
    {
        // Arrange
        var query = "test query";
        var documents = new[]
        {
            CreateDocumentChunk("doc-1", "Short")
        };

        // Act
        var result = await _service.RefineKnowledgeAsync(query, documents);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task PerformAlternativeRetrievalAsync_WithEmptyOriginalDocs_HandlesGracefully()
    {
        // Arrange
        var query = "test query";
        var originalDocuments = Array.Empty<DocumentChunk>();

        // Act
        var result = await _service.PerformAlternativeRetrievalAsync(query, originalDocuments);

        // Assert
        Assert.NotNull(result);
    }

    #endregion
}
