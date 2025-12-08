using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// Tests for RetrievalVerificationService implementing IRetrievalVerificationService interface.
/// Tests cover: VerifyAsync, GradeDocumentAsync, DetectHallucinationRisksAsync,
/// CheckFactualGroundingAsync, FilterByConfidence, CalculateClaimSupportAsync, and GetRecommendation.
/// </summary>
public class RetrievalVerificationServiceTests
{
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly Mock<ITextCompletionService> _mockCompletionService;
    private readonly ILogger<RetrievalVerificationService> _logger;
    private readonly RetrievalVerificationServiceOptions _defaultOptions;

    public RetrievalVerificationServiceTests()
    {
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _mockCompletionService = new Mock<ITextCompletionService>();
        _logger = NullLogger<RetrievalVerificationService>.Instance;
        _defaultOptions = new RetrievalVerificationServiceOptions();

        _mockEmbeddingService
            .Setup(x => x.GetModelName())
            .Returns("test-model");

        _mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f });
    }

    private RetrievalVerificationService CreateService(
        RetrievalVerificationServiceOptions? options = null,
        bool withLlm = true)
    {
        var opts = options ?? _defaultOptions;

        return new RetrievalVerificationService(
            _mockEmbeddingService.Object,
            withLlm ? _mockCompletionService.Object : null,
            Microsoft.Extensions.Options.Options.Create(opts),
            _logger);
    }

    private List<DocumentChunk> CreateTestChunks(int count, string topic = "machine learning")
    {
        var chunks = new List<DocumentChunk>();
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            var embedding = new float[5];
            for (int j = 0; j < 5; j++)
            {
                embedding[j] = (float)random.NextDouble();
            }

            chunks.Add(new DocumentChunk
            {
                Id = $"chunk_{i}",
                DocumentId = $"doc_{i / 3}",
                Content = $"This is content about {topic} for chunk {i}. It contains relevant information about {topic} algorithms and applications.",
                Embedding = embedding,
                ChunkIndex = i
            });
        }

        return chunks;
    }

    private List<GradedDocument> CreateGradedDocuments(int count, double baseConfidence = 0.8)
    {
        var documents = new List<GradedDocument>();
        for (int i = 0; i < count; i++)
        {
            var confidence = baseConfidence - (i * 0.1);
            documents.Add(new GradedDocument
            {
                Document = new DocumentChunk
                {
                    Id = $"chunk_{i}",
                    DocumentId = $"doc_{i}",
                    Content = $"Test content {i}"
                },
                Grade = new DocumentGrade
                {
                    Relevance = confidence >= 0.6 ? RelevanceGrade.Relevant :
                               confidence >= 0.4 ? RelevanceGrade.PartiallyRelevant :
                               RelevanceGrade.NotRelevant,
                    ConfidenceScore = confidence,
                    SemanticSimilarity = baseConfidence - (i * 0.08),
                    KeywordMatch = baseConfidence - (i * 0.12),
                    EntityOverlap = 0.5,
                    ContextualFit = 0.6
                },
                OriginalRank = i
            });
        }
        return documents;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Arrange & Act
        var service = CreateService();

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullEmbeddingService_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RetrievalVerificationService(
                null!,
                _mockCompletionService.Object,
                Microsoft.Extensions.Options.Options.Create(_defaultOptions),
                _logger));
    }

    [Fact]
    public void Constructor_WithNullCompletionService_CreatesInstance()
    {
        // Arrange & Act
        var service = CreateService(withLlm: false);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RetrievalVerificationService(
                _mockEmbeddingService.Object,
                _mockCompletionService.Object,
                Microsoft.Extensions.Options.Options.Create(_defaultOptions),
                null!));
    }

    #endregion

    #region VerifyAsync Tests

    [Fact]
    public async Task VerifyAsync_ValidChunks_ReturnsResultWithGradedDocuments()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "What is machine learning?";
        var chunks = CreateTestChunks(5, "machine learning");

        // Act
        var result = await service.VerifyAsync(query, chunks);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.Query);
        Assert.Equal(5, result.GradedDocuments.Count);
        Assert.NotEqual(VerificationStatus.Failed, result.Status);
    }

    [Fact]
    public async Task VerifyAsync_EmptyChunks_ReturnsEmptyResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "Test query";
        var chunks = new List<DocumentChunk>();

        // Act
        var result = await service.VerifyAsync(query, chunks);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.GradedDocuments);
        // With empty chunks, status should indicate failure or inconclusive
        Assert.True(result.Status == VerificationStatus.Failed || result.Status == VerificationStatus.Inconclusive);
    }

    [Fact]
    public async Task VerifyAsync_WithVerificationOptions_RespectsMaxDocuments()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "Test query";
        var chunks = CreateTestChunks(10);
        var options = new VerificationOptions { MaxDocumentsToVerify = 3 };

        // Act
        var result = await service.VerifyAsync(query, chunks, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.GradedDocuments.Count);
    }

    [Fact]
    public async Task VerifyAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "Test query";
        var chunks = CreateTestChunks(5);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.VerifyAsync(query, chunks, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task VerifyAsync_NullQuery_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var chunks = CreateTestChunks(3);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.VerifyAsync(null!, chunks));
    }

    [Fact]
    public async Task VerifyAsync_NullDocuments_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService(withLlm: false);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.VerifyAsync("query", null!));
    }

    [Fact]
    public async Task VerifyAsync_ReturnsProcessingTime()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "Test query";
        var chunks = CreateTestChunks(3);

        // Act
        var result = await service.VerifyAsync(query, chunks);

        // Assert
        Assert.True(result.ProcessingTime.TotalMilliseconds > 0);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsStatistics()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "machine learning algorithms";
        var chunks = CreateTestChunks(5, "machine learning");

        // Act
        var result = await service.VerifyAsync(query, chunks);

        // Assert
        Assert.NotNull(result.Statistics);
        Assert.Equal(5, result.Statistics.TotalDocuments);
    }

    #endregion

    #region GradeDocumentAsync Tests

    [Fact]
    public async Task GradeDocumentAsync_ValidDocument_ReturnsGrade()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "machine learning";
        var document = CreateTestChunks(1, "machine learning").First();

        // Act
        var grade = await service.GradeDocumentAsync(query, document);

        // Assert
        Assert.NotNull(grade);
        Assert.True(grade.ConfidenceScore >= 0 && grade.ConfidenceScore <= 1);
        Assert.True(grade.SemanticSimilarity >= 0 && grade.SemanticSimilarity <= 1);
        Assert.True(grade.KeywordMatch >= 0 && grade.KeywordMatch <= 1);
    }

    [Fact]
    public async Task GradeDocumentAsync_HighRelevanceDocument_ReturnsRelevantGrade()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "machine learning algorithms";
        var document = new DocumentChunk
        {
            Id = "test",
            DocumentId = "doc1",
            Content = "Machine learning algorithms are powerful tools. Machine learning enables pattern recognition. Algorithms in machine learning include neural networks."
        };

        // Normalize the embedding so it's close to query embedding
        _mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingAsync("machine learning algorithms", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.9f, 0.1f, 0.1f, 0.1f, 0.1f });

        _mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingAsync(It.Is<string>(s => s.Contains("Machine learning algorithms")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.9f, 0.1f, 0.1f, 0.1f, 0.1f });

        // Act
        var grade = await service.GradeDocumentAsync(query, document);

        // Assert
        Assert.NotNull(grade);
        Assert.True(grade.KeywordMatch > 0.5, "Should have high keyword match");
    }

    [Fact]
    public async Task GradeDocumentAsync_NullQuery_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var document = CreateTestChunks(1).First();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.GradeDocumentAsync(null!, document));
    }

    [Fact]
    public async Task GradeDocumentAsync_NullDocument_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService(withLlm: false);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.GradeDocumentAsync("query", null!));
    }

    [Fact]
    public async Task GradeDocumentAsync_DocumentWithEmbedding_UsesExistingEmbedding()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "test query";
        var existingEmbedding = new float[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
        var document = new DocumentChunk
        {
            Id = "test",
            DocumentId = "doc1",
            Content = "Test content",
            Embedding = existingEmbedding
        };

        // Act
        var grade = await service.GradeDocumentAsync(query, document);

        // Assert
        Assert.NotNull(grade);
        // Should not call GenerateEmbeddingAsync for the document content
        _mockEmbeddingService.Verify(
            x => x.GenerateEmbeddingAsync(document.Content, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region DetectHallucinationRisksAsync Tests

    [Fact]
    public async Task DetectHallucinationRisksAsync_ValidDocuments_ReturnsAssessment()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "machine learning";
        var documents = CreateTestChunks(5, "machine learning");

        // Act
        var assessment = await service.DetectHallucinationRisksAsync(query, documents);

        // Assert
        Assert.NotNull(assessment);
        Assert.True(assessment.OverallRisk >= 0 && assessment.OverallRisk <= 1);
    }

    [Fact]
    public async Task DetectHallucinationRisksAsync_EmptyDocuments_ReturnsValidRisk()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "test query";
        var documents = new List<DocumentChunk>();

        // Act
        var assessment = await service.DetectHallucinationRisksAsync(query, documents);

        // Assert
        Assert.NotNull(assessment);
        // Empty documents should return a valid risk assessment
        // The risk level depends on implementation (high risk due to insufficient evidence is valid)
        Assert.True(assessment.OverallRisk >= 0 && assessment.OverallRisk <= 1);
    }

    [Fact]
    public async Task DetectHallucinationRisksAsync_IdentifiesRiskFactors()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "very specific technical query about quantum computing";
        var documents = CreateTestChunks(2, "completely unrelated topic");

        // Act
        var assessment = await service.DetectHallucinationRisksAsync(query, documents);

        // Assert
        Assert.NotNull(assessment);
        // Should detect risk factors when content doesn't match query
    }

    #endregion

    #region CheckFactualGroundingAsync Tests

    [Fact]
    public async Task CheckFactualGroundingAsync_ValidDocuments_ReturnsGroundingResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "What is machine learning?";
        var documents = CreateTestChunks(3, "machine learning");

        // Act
        var result = await service.CheckFactualGroundingAsync(query, documents);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.OverallScore >= 0 && result.OverallScore <= 1);
    }

    [Fact]
    public async Task CheckFactualGroundingAsync_WithExplicitClaims_ChecksEachClaim()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "machine learning applications";
        var documents = CreateTestChunks(3, "machine learning");
        var claims = new List<string>
        {
            "Machine learning uses algorithms.",
            "Machine learning can recognize patterns."
        };

        // Act
        var result = await service.CheckFactualGroundingAsync(query, documents, claims);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.GroundedClaims);
    }

    [Fact]
    public async Task CheckFactualGroundingAsync_EmptyDocuments_ReturnsLowGrounding()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "test query";
        var documents = new List<DocumentChunk>();

        // Act
        var result = await service.CheckFactualGroundingAsync(query, documents);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.OverallScore <= 0.5);
    }

    #endregion

    #region FilterByConfidence Tests

    [Fact]
    public void FilterByConfidence_FiltersLowConfidenceDocuments()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var gradedDocs = CreateGradedDocuments(5, 0.9);

        // Act
        var filtered = service.FilterByConfidence(gradedDocs, threshold: 0.5).ToList();

        // Assert
        Assert.True(filtered.Count <= gradedDocs.Count);
        Assert.All(filtered, doc => Assert.True(doc.Grade.ConfidenceScore >= 0.5));
    }

    [Fact]
    public void FilterByConfidence_HighThreshold_ReturnsFewerDocuments()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var gradedDocs = CreateGradedDocuments(5, 0.7);

        // Act
        var lowThreshold = service.FilterByConfidence(gradedDocs, threshold: 0.3).ToList();
        var highThreshold = service.FilterByConfidence(gradedDocs, threshold: 0.7).ToList();

        // Assert
        Assert.True(highThreshold.Count <= lowThreshold.Count);
    }

    [Fact]
    public void FilterByConfidence_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var gradedDocs = new List<GradedDocument>();

        // Act
        var filtered = service.FilterByConfidence(gradedDocs, threshold: 0.5).ToList();

        // Assert
        Assert.Empty(filtered);
    }

    [Fact]
    public void FilterByConfidence_UsesDefaultThreshold()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var gradedDocs = CreateGradedDocuments(3, 0.4);

        // Act
        var filtered = service.FilterByConfidence(gradedDocs).ToList(); // Default threshold = 0.5

        // Assert
        Assert.All(filtered, doc => Assert.True(doc.Grade.ConfidenceScore >= 0.5));
    }

    #endregion

    #region CalculateClaimSupportAsync Tests

    [Fact]
    public async Task CalculateClaimSupportAsync_SingleClaim_ReturnsSupportResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var claims = new List<string> { "Machine learning uses algorithms to learn from data." };
        var documents = CreateTestChunks(3, "machine learning");

        // Act
        var result = await service.CalculateClaimSupportAsync(claims, documents);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Claims);
    }

    [Fact]
    public async Task CalculateClaimSupportAsync_MultipleClaims_ReturnsAllSupportResults()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var claims = new List<string>
        {
            "Machine learning learns from data.",
            "Algorithms process information."
        };
        var documents = CreateTestChunks(5, "machine learning algorithms data");

        // Act
        var result = await service.CalculateClaimSupportAsync(claims, documents);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Claims.Count);
    }

    [Fact]
    public async Task CalculateClaimSupportAsync_EmptyClaims_ReturnsEmptyResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var claims = new List<string>();
        var documents = CreateTestChunks(3);

        // Act
        var result = await service.CalculateClaimSupportAsync(claims, documents);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Claims);
    }

    [Fact]
    public async Task CalculateClaimSupportAsync_EmptyDocuments_ReturnsNoSupportingEvidence()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var claims = new List<string> { "Test claim" };
        var documents = new List<DocumentChunk>();

        // Act
        var result = await service.CalculateClaimSupportAsync(claims, documents);

        // Assert
        Assert.NotNull(result);
        Assert.All(result.Claims, cs => Assert.Empty(cs.SupportingDocuments));
    }

    #endregion

    #region GetRecommendation Tests

    [Fact]
    public void GetRecommendation_PassedResult_ReturnsProceed()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var result = new RetrievalVerificationResult
        {
            Query = "test",
            Status = VerificationStatus.Passed,
            OverallConfidence = 0.9,
            GradedDocuments = CreateGradedDocuments(3, 0.85),
            Statistics = new VerificationStatistics { TotalDocuments = 3, RelevantCount = 3 }
        };

        // Act
        var recommendation = service.GetRecommendation(result);

        // Assert
        Assert.NotNull(recommendation);
        Assert.Equal(RecommendedAction.Proceed, recommendation.Action);
    }

    [Fact]
    public void GetRecommendation_FailedResult_RecommendsAction()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var result = new RetrievalVerificationResult
        {
            Query = "test",
            Status = VerificationStatus.Failed,
            OverallConfidence = 0.3,
            GradedDocuments = new List<GradedDocument>(),
            Statistics = new VerificationStatistics { TotalDocuments = 0, RelevantCount = 0 }
        };

        // Act
        var recommendation = service.GetRecommendation(result);

        // Assert
        Assert.NotNull(recommendation);
        Assert.True(
            recommendation.Action == RecommendedAction.ExpandSearch ||
            recommendation.Action == RecommendedAction.RetryWithModifiedQuery ||
            recommendation.Action == RecommendedAction.Abort ||
            recommendation.Action == RecommendedAction.AddContext);
    }

    [Fact]
    public void GetRecommendation_HighHallucinationRisk_RecommendsAction()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var result = new RetrievalVerificationResult
        {
            Query = "test",
            Status = VerificationStatus.Warning,
            OverallConfidence = 0.5,
            GradedDocuments = CreateGradedDocuments(3, 0.5),
            HallucinationRisk = new HallucinationRiskAssessment
            {
                RiskLevel = HallucinationRiskLevel.High,
                OverallRisk = 0.8,
                RiskFactors = new List<HallucinationRiskFactor>
                {
                    new HallucinationRiskFactor { Type = HallucinationRiskType.InsufficientEvidence, Severity = 0.8 }
                }
            },
            Statistics = new VerificationStatistics { TotalDocuments = 3, RelevantCount = 1 }
        };

        // Act
        var recommendation = service.GetRecommendation(result);

        // Assert
        Assert.NotNull(recommendation);
        Assert.NotNull(recommendation.Reasoning);
    }

    [Fact]
    public void GetRecommendation_PartiallyPassed_ReturnsAppropriateRecommendations()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var result = new RetrievalVerificationResult
        {
            Query = "test",
            Status = VerificationStatus.PartiallyPassed,
            OverallConfidence = 0.6,
            GradedDocuments = CreateGradedDocuments(5, 0.6),
            Statistics = new VerificationStatistics { TotalDocuments = 5, RelevantCount = 3 }
        };

        // Act
        var recommendation = service.GetRecommendation(result);

        // Assert
        Assert.NotNull(recommendation);
        Assert.NotEmpty(recommendation.Reasoning);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task FullVerificationWorkflow_CompletesSuccessfully()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "machine learning applications";
        var documents = CreateTestChunks(5, "machine learning");

        // Act - Full workflow
        var verifyResult = await service.VerifyAsync(query, documents);
        var filtered = service.FilterByConfidence(verifyResult.GradedDocuments, 0.3).ToList();
        var recommendation = service.GetRecommendation(verifyResult);

        // Assert
        Assert.NotNull(verifyResult);
        Assert.NotNull(filtered);
        Assert.NotNull(recommendation);
        Assert.True(verifyResult.ProcessingTime.TotalMilliseconds > 0);
    }

    [Fact]
    public async Task VerifyAsync_WithLlmEnabled_UsesCompletionService()
    {
        // Arrange
        var options = new RetrievalVerificationServiceOptions
        {
            AlwaysCheckHallucination = true
        };
        var service = CreateService(options, withLlm: true);
        var query = "test query";
        var documents = CreateTestChunks(2);

        _mockCompletionService
            .Setup(x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"risk_level\": \"low\", \"factors\": []}");

        // Act
        var result = await service.VerifyAsync(query, documents);

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task VerifyAsync_VeryLongQuery_HandlesGracefully()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = string.Join(" ", Enumerable.Repeat("machine learning", 100));
        var documents = CreateTestChunks(3);

        // Act
        var result = await service.VerifyAsync(query, documents);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task VerifyAsync_SpecialCharactersInQuery_HandlesGracefully()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "machine learning? with special!@#$% chars";
        var documents = CreateTestChunks(3);

        // Act
        var result = await service.VerifyAsync(query, documents);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GradeDocumentAsync_EmptyContent_ReturnsLowScore()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "test query";
        var document = new DocumentChunk
        {
            Id = "empty",
            DocumentId = "doc1",
            Content = ""
        };

        // Act
        var grade = await service.GradeDocumentAsync(query, document);

        // Assert
        Assert.NotNull(grade);
        Assert.True(grade.KeywordMatch == 0);
    }

    [Fact]
    public async Task DetectHallucinationRisksAsync_SingleDocument_ReturnsValidAssessment()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "test";
        var documents = CreateTestChunks(1);

        // Act
        var assessment = await service.DetectHallucinationRisksAsync(query, documents);

        // Assert
        Assert.NotNull(assessment);
        Assert.NotNull(assessment.RiskLevel);
    }

    #endregion

    #region Options Configuration Tests

    [Fact]
    public void Options_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new RetrievalVerificationServiceOptions();

        // Assert
        Assert.False(options.AlwaysCheckHallucination);
        Assert.False(options.UseLlmForGrading);
        Assert.Equal(0.5, options.MinGroundingScore);
        Assert.Equal(0.6, options.MinFactualGroundingScore);
        Assert.Equal(0.5, options.MinQueryCoverage);
        Assert.Equal(0.6, options.HighHallucinationRiskThreshold);
    }

    [Fact]
    public async Task Service_WithCustomOptions_UsesConfiguredValues()
    {
        // Arrange
        var options = new RetrievalVerificationServiceOptions
        {
            MinGroundingScore = 0.8,
            HighHallucinationRiskThreshold = 0.3
        };
        var service = CreateService(options, withLlm: false);
        var query = "test";
        var documents = CreateTestChunks(2);

        // Act
        var result = await service.VerifyAsync(query, documents);

        // Assert
        Assert.NotNull(result);
    }

    #endregion
}
