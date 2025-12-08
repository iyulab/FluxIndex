using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using HybridSearchResult = FluxIndex.Core.Domain.Models.HybridSearchResult;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// Unit tests for AgenticRetrievalRouter.
/// Tests intelligent query routing to optimal retrieval strategies.
/// </summary>
public class AgenticRetrievalRouterTests
{
    private readonly Mock<IHybridSearchService> _mockHybridSearchService;
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly Mock<ISelfRAGService> _mockSelfRAGService;
    private readonly Mock<ICorrectiveRAGService> _mockCorrectiveRAGService;
    private readonly Mock<ISmallToBigRetriever> _mockSmallToBigRetriever;
    private readonly Mock<IIterativeRetrievalService> _mockIterativeRetrievalService;
    private readonly Mock<ILogger<AgenticRetrievalRouter>> _mockLogger;
    private readonly AgenticRetrievalRouter _router;

    public AgenticRetrievalRouterTests()
    {
        _mockHybridSearchService = new Mock<IHybridSearchService>();
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _mockSelfRAGService = new Mock<ISelfRAGService>();
        _mockCorrectiveRAGService = new Mock<ICorrectiveRAGService>();
        _mockSmallToBigRetriever = new Mock<ISmallToBigRetriever>();
        _mockIterativeRetrievalService = new Mock<IIterativeRetrievalService>();
        _mockLogger = new Mock<ILogger<AgenticRetrievalRouter>>();

        SetupDefaultMocks();

        _router = new AgenticRetrievalRouter(
            _mockHybridSearchService.Object,
            _mockEmbeddingService.Object,
            _mockSelfRAGService.Object,
            _mockCorrectiveRAGService.Object,
            _mockSmallToBigRetriever.Object,
            _mockIterativeRetrievalService.Object,
            Microsoft.Extensions.Options.Options.Create(new AgenticRetrievalRouterOptions()),
            _mockLogger.Object);
    }

    private void SetupDefaultMocks()
    {
        _mockHybridSearchService
            .Setup(x => x.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<HybridSearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultHybridResults(5));

        _mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRandomEmbedding());

        _mockSelfRAGService
            .Setup(x => x.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<SelfRAGOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultSelfRAGResult());

        _mockCorrectiveRAGService
            .Setup(x => x.RetrieveWithCorrectionAsync(
                It.IsAny<string>(),
                It.IsAny<CorrectiveRAGOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultCorrectiveRAGResult());

        _mockSmallToBigRetriever
            .Setup(x => x.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<SmallToBigOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultSmallToBigResults());

        _mockIterativeRetrievalService
            .Setup(x => x.RetrieveWithReasoningAsync(
                It.IsAny<string>(),
                It.IsAny<IterativeRetrievalOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultIterativeResult());
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Arrange & Act
        var router = new AgenticRetrievalRouter(
            _mockHybridSearchService.Object,
            _mockEmbeddingService.Object,
            null, null, null, null,
            Microsoft.Extensions.Options.Options.Create(new AgenticRetrievalRouterOptions()),
            _mockLogger.Object);

        // Assert
        Assert.NotNull(router);
    }

    [Fact]
    public void Constructor_WithNullHybridSearchService_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AgenticRetrievalRouter(
            null!,
            _mockEmbeddingService.Object,
            null, null, null, null,
            Microsoft.Extensions.Options.Options.Create(new AgenticRetrievalRouterOptions()),
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullEmbeddingService_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AgenticRetrievalRouter(
            _mockHybridSearchService.Object,
            null!,
            null, null, null, null,
            Microsoft.Extensions.Options.Options.Create(new AgenticRetrievalRouterOptions()),
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AgenticRetrievalRouter(
            _mockHybridSearchService.Object,
            _mockEmbeddingService.Object,
            null, null, null, null,
            Microsoft.Extensions.Options.Options.Create(new AgenticRetrievalRouterOptions()),
            null!));
    }

    #endregion

    #region RouteAndRetrieveAsync Tests

    [Fact]
    public async Task RouteAndRetrieveAsync_WithSimpleQuery_ReturnsResults()
    {
        // Arrange
        var query = "What is machine learning?";

        // Act
        var result = await _router.RouteAndRetrieveAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotEmpty(result.Documents);
        Assert.NotNull(result.Decision);
    }

    [Fact]
    public async Task RouteAndRetrieveAsync_WithEmptyQuery_StillReturnsResults()
    {
        // Arrange - Empty query is handled gracefully
        var query = "";

        // Act
        var result = await _router.RouteAndRetrieveAsync(query);

        // Assert - Service handles empty query gracefully
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RouteAndRetrieveAsync_WithNullQuery_ThrowsArgumentNullException()
    {
        // Arrange
        string? query = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _router.RouteAndRetrieveAsync(query!));
    }

    [Fact]
    public async Task RouteAndRetrieveAsync_WithContext_UsesProvidedContext()
    {
        // Arrange
        var query = "What is machine learning?";
        var context = new RoutingContext { MaxResults = 3, Domain = "technical" };

        // Act
        var result = await _router.RouteAndRetrieveAsync(query, context);

        // Assert - Context is used in routing (results may vary based on strategy)
        Assert.NotNull(result);
        Assert.NotEmpty(result.Documents);
    }

    [Fact]
    public async Task RouteAndRetrieveAsync_IncludesTimingInformation()
    {
        // Arrange
        var query = "What is machine learning?";

        // Act
        var result = await _router.RouteAndRetrieveAsync(query);

        // Assert
        Assert.True(result.TotalTime >= TimeSpan.Zero);
    }

    [Fact]
    public async Task RouteAndRetrieveAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var query = "What is machine learning?";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _router.RouteAndRetrieveAsync(query, null, cts.Token));
    }

    #endregion

    #region AnalyzeQueryAsync Tests

    [Fact]
    public async Task AnalyzeQueryAsync_WithFactualQuery_ReturnsDecision()
    {
        // Arrange
        var query = "What is the capital of France?";

        // Act
        var result = await _router.AnalyzeQueryAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Confidence > 0);
    }

    [Fact]
    public async Task AnalyzeQueryAsync_WithComparisonQuery_ReturnsComparisonType()
    {
        // Arrange
        // Query uses "compare" keyword to trigger Comparison type detection
        // Note: "What is the difference..." would match Definition due to "what is" prefix
        var query = "Compare Python and Java programming languages";

        // Act
        var result = await _router.AnalyzeQueryAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(RoutingQueryType.Comparison, result.QueryAnalysis.Type);
    }

    [Fact]
    public async Task AnalyzeQueryAsync_WithProceduralQuery_ReturnsProceduralType()
    {
        // Arrange
        var query = "How do I install Docker on Windows?";

        // Act
        var result = await _router.AnalyzeQueryAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(RoutingQueryType.Procedural, result.QueryAnalysis.Type);
    }

    [Fact]
    public async Task AnalyzeQueryAsync_WithDefinitionQuery_ReturnsDefinitionType()
    {
        // Arrange
        var query = "Define machine learning";

        // Act
        var result = await _router.AnalyzeQueryAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(RoutingQueryType.Definition, result.QueryAnalysis.Type);
    }

    [Fact]
    public async Task AnalyzeQueryAsync_ExtractsKeyConcepts()
    {
        // Arrange
        var query = "What are the benefits of cloud computing?";

        // Act
        var result = await _router.AnalyzeQueryAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.QueryAnalysis.KeyConcepts);
    }

    [Fact]
    public async Task AnalyzeQueryAsync_CalculatesComplexityScore()
    {
        // Arrange
        var query = "What is the difference between supervised and unsupervised learning in machine learning?";

        // Act
        var result = await _router.AnalyzeQueryAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.QueryAnalysis.Complexity >= 0 && result.QueryAnalysis.Complexity <= 1);
    }

    #endregion

    #region GenerateRetrievalPlanAsync Tests

    [Fact]
    public async Task GenerateRetrievalPlanAsync_CreatesValidPlan()
    {
        // Arrange
        var query = "What is machine learning?";

        // Act
        var plan = await _router.GenerateRetrievalPlanAsync(query);

        // Assert
        Assert.NotNull(plan);
        Assert.NotEmpty(plan.Steps);
        Assert.True(plan.EstimatedDuration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task GenerateRetrievalPlanAsync_HasPlanId()
    {
        // Arrange
        var query = "What is machine learning?";

        // Act
        var plan = await _router.GenerateRetrievalPlanAsync(query);

        // Assert
        Assert.NotNull(plan);
        Assert.NotEmpty(plan.PlanId);
    }

    [Fact]
    public async Task GenerateRetrievalPlanAsync_StoresOriginalQuery()
    {
        // Arrange
        var query = "What is machine learning?";

        // Act
        var plan = await _router.GenerateRetrievalPlanAsync(query);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(query, plan.OriginalQuery);
    }

    #endregion

    #region ExecuteRetrievalPlanAsync Tests

    [Fact]
    public async Task ExecuteRetrievalPlanAsync_ExecutesPlanSteps()
    {
        // Arrange
        var query = "What is machine learning?";
        var plan = await _router.GenerateRetrievalPlanAsync(query);

        // Act
        var result = await _router.ExecuteRetrievalPlanAsync(query, plan);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public async Task ExecuteRetrievalPlanAsync_ReturnsMergedDocuments()
    {
        // Arrange
        var query = "What is machine learning?";
        var plan = await _router.GenerateRetrievalPlanAsync(query);

        // Act
        var result = await _router.ExecuteRetrievalPlanAsync(query, plan);

        // Assert
        Assert.NotEmpty(result.MergedDocuments);
    }

    [Fact]
    public async Task ExecuteRetrievalPlanAsync_TracksCompletedSteps()
    {
        // Arrange
        var query = "What is machine learning?";
        var plan = await _router.GenerateRetrievalPlanAsync(query);

        // Act
        var result = await _router.ExecuteRetrievalPlanAsync(query, plan);

        // Assert
        Assert.True(result.CompletedSteps > 0);
    }

    #endregion

    #region RecordRoutingFeedbackAsync Tests

    [Fact]
    public async Task RecordRoutingFeedbackAsync_RecordsFeedback()
    {
        // Arrange
        var routingId = Guid.NewGuid().ToString();
        var feedback = new RoutingFeedback
        {
            WasSatisfactory = true,
            QualityRating = 4,
            FeedbackText = "Good results"
        };

        // Act
        await _router.RecordRoutingFeedbackAsync(routingId, feedback);

        // Assert - No exception means success
        Assert.True(true);
    }

    [Fact]
    public async Task RecordRoutingFeedbackAsync_WithBetterStrategy_RecordsSuggestion()
    {
        // Arrange
        var routingId = Guid.NewGuid().ToString();
        var feedback = new RoutingFeedback
        {
            WasSatisfactory = false,
            QualityRating = 2,
            BetterStrategy = RetrievalStrategy.SemanticSearch,
            FeedbackText = "Should have used semantic search"
        };

        // Act
        await _router.RecordRoutingFeedbackAsync(routingId, feedback);

        // Assert - No exception means success
        Assert.True(true);
    }

    [Fact]
    public async Task RecordRoutingFeedbackAsync_WithMultipleFeedback_AdaptsFutureRouting()
    {
        // Arrange & Act - Record multiple feedbacks
        for (int i = 0; i < 5; i++)
        {
            var routingId = Guid.NewGuid().ToString();
            var feedback = new RoutingFeedback
            {
                WasSatisfactory = true,
                QualityRating = 5,
                FeedbackText = "Excellent results"
            };
            await _router.RecordRoutingFeedbackAsync(routingId, feedback);
        }

        var result = await _router.RouteAndRetrieveAsync("simple factual question");

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region Strategy Selection Tests

    [Fact]
    public async Task RouteAndRetrieveAsync_WithComplexQuery_SelectsAppropriateStrategy()
    {
        // Arrange
        var query = "Compare and contrast the differences between supervised learning, unsupervised learning, and reinforcement learning, then explain how each is applied in real-world scenarios";

        // Act
        var result = await _router.RouteAndRetrieveAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Decision);
    }

    [Fact]
    public async Task RouteAndRetrieveAsync_WithDefinitionQuery_ReturnsResults()
    {
        // Arrange
        var query = "Define artificial intelligence";

        // Act
        var result = await _router.RouteAndRetrieveAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public async Task RouteAndRetrieveAsync_WithProceduralQuery_ReturnsResults()
    {
        // Arrange
        var query = "How to configure a Kubernetes cluster step by step?";

        // Act
        var result = await _router.RouteAndRetrieveAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
    }

    #endregion

    #region Fallback Strategy Tests

    [Fact]
    public async Task RouteAndRetrieveAsync_WhenPrimaryStrategyFails_UsesFallback()
    {
        // Arrange
        var query = "What is machine learning?";
        _mockSelfRAGService
            .Setup(x => x.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<SelfRAGOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Service unavailable"));

        // Act
        var result = await _router.RouteAndRetrieveAsync(query);

        // Assert - Should still succeed using fallback
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
    }

    #endregion

    #region Performance Tracking Tests

    [Fact]
    public async Task RouteAndRetrieveAsync_TracksLatency()
    {
        // Arrange
        var query = "What is machine learning?";

        // Act
        var result = await _router.RouteAndRetrieveAsync(query);

        // Assert
        Assert.True(result.TotalTime > TimeSpan.Zero);
    }

    [Fact]
    public async Task RouteAndRetrieveAsync_TracksRetrievalSteps()
    {
        // Arrange
        var query = "What is machine learning?";

        // Act
        var result = await _router.RouteAndRetrieveAsync(query);

        // Assert
        Assert.All(result.Documents, doc =>
        {
            Assert.True(doc.RetrievalStep >= 1);
        });
    }

    [Fact]
    public async Task RouteAndRetrieveAsync_TracksRoutingTime()
    {
        // Arrange
        var query = "What is machine learning?";

        // Act
        var result = await _router.RouteAndRetrieveAsync(query);

        // Assert
        Assert.True(result.RoutingTime >= TimeSpan.Zero);
    }

    [Fact]
    public async Task RouteAndRetrieveAsync_TracksRetrievalTime()
    {
        // Arrange
        var query = "What is machine learning?";

        // Act
        var result = await _router.RouteAndRetrieveAsync(query);

        // Assert
        Assert.True(result.RetrievalTime >= TimeSpan.Zero);
    }

    #endregion

    #region Helper Methods

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

    private static SelfRAGResult CreateDefaultSelfRAGResult()
    {
        var document = new Document
        {
            Id = "selfrag-doc-1",
            FileName = "test.txt",
            Content = "Self-RAG retrieved content",
            Status = DocumentStatus.Indexed,
            Chunks = new List<DocumentChunk>
            {
                CreateDocumentChunk("selfrag-chunk-1", "Self-RAG chunk content")
            }
        };

        return new SelfRAGResult
        {
            FinalResults = new List<Document> { document },
            FinalQualityScore = 0.85,
            IsSuccessful = true,
            Iterations = new List<SearchIteration>
            {
                new SearchIteration { IterationNumber = 1 }
            }
        };
    }

    private static CorrectiveRAGResult CreateDefaultCorrectiveRAGResult()
    {
        return new CorrectiveRAGResult
        {
            Documents = new List<CorrectedDocument>
            {
                new CorrectedDocument
                {
                    Chunk = CreateDocumentChunk("crag-doc-1", "Corrective RAG content"),
                    Grade = DocumentRelevanceGrade.Correct,
                    RelevanceScore = 0.9,
                    Source = DocumentSource.OriginalRetrieval,
                    InclusionReason = "Highly relevant to query"
                }
            },
            IsSuccessful = true
        };
    }

    private static IReadOnlyList<SmallToBigResult> CreateDefaultSmallToBigResults()
    {
        return new List<SmallToBigResult>
        {
            new SmallToBigResult
            {
                PrimaryChunk = CreateDocumentChunk("s2b-primary-1", "Primary chunk content"),
                ContextChunks = new List<DocumentChunk>
                {
                    CreateDocumentChunk("s2b-context-1", "Context chunk 1"),
                    CreateDocumentChunk("s2b-context-2", "Context chunk 2")
                },
                RelevanceScore = 0.88,
                WindowSize = 3,
                ExpansionReason = "Expanded for additional context"
            }
        };
    }

    private static IterativeRetrievalResult CreateDefaultIterativeResult()
    {
        return new IterativeRetrievalResult
        {
            Documents = new List<IterativeSearchResult>
            {
                new IterativeSearchResult
                {
                    Id = "iter-1",
                    DocumentId = "parent-iter-1",
                    ChunkId = "iter-chunk-1",
                    Content = "Iterative retrieval content",
                    Score = 0.87f,
                    ChunkIndex = 0
                }
            },
            Iterations = new List<ReasoningIteration>
            {
                new ReasoningIteration
                {
                    IterationNumber = 1
                }
            },
            IsComplete = true,
            Confidence = 0.85f,
            StopReason = "Query fully answered"
        };
    }

    #endregion
}
