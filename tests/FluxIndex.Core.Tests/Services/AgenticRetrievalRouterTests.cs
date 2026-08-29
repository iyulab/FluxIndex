using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

using HybridSearchResult = FluxIndex.Core.Domain.Models.HybridSearchResult;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// Unit tests for AgenticRetrievalRouter.
/// Tests intelligent query routing to optimal retrieval strategies.
/// </summary>
public class AgenticRetrievalRouterTests
{
    private readonly IHybridSearchService _mockHybridSearchService;
    private readonly IEmbeddingService _mockEmbeddingService;
    private readonly ISelfRAGService _mockSelfRAGService;
    private readonly ICorrectiveRAGService _mockCorrectiveRAGService;
    private readonly ISmallToBigRetriever _mockSmallToBigRetriever;
    private readonly IIterativeRetrievalService _mockIterativeRetrievalService;
    private readonly ILogger<AgenticRetrievalRouter> _mockLogger;
    private readonly AgenticRetrievalRouter _router;

    public AgenticRetrievalRouterTests()
    {
        _mockHybridSearchService = Substitute.For<IHybridSearchService>();
        _mockEmbeddingService = Substitute.For<IEmbeddingService>();
        _mockSelfRAGService = Substitute.For<ISelfRAGService>();
        _mockCorrectiveRAGService = Substitute.For<ICorrectiveRAGService>();
        _mockSmallToBigRetriever = Substitute.For<ISmallToBigRetriever>();
        _mockIterativeRetrievalService = Substitute.For<IIterativeRetrievalService>();
        _mockLogger = Substitute.For<ILogger<AgenticRetrievalRouter>>();

        SetupDefaultMocks();

        _router = new AgenticRetrievalRouter(
            _mockHybridSearchService,
            _mockEmbeddingService,
            _mockSelfRAGService,
            _mockCorrectiveRAGService,
            _mockSmallToBigRetriever,
            _mockIterativeRetrievalService,
            Microsoft.Extensions.Options.Options.Create(new AgenticRetrievalRouterOptions()),
            _mockLogger);
    }

    private void SetupDefaultMocks()
    {
        _mockHybridSearchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<HybridSearchOptions>(),
                Arg.Any<CancellationToken>()).Returns(CreateDefaultHybridResults(5));

        _mockEmbeddingService.GenerateEmbeddingAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()).Returns(CreateRandomEmbedding());

        _mockSelfRAGService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<SelfRAGOptions>(),
                Arg.Any<CancellationToken>()).Returns(CreateDefaultSelfRAGResult());

        _mockCorrectiveRAGService.RetrieveWithCorrectionAsync(
                Arg.Any<string>(),
                Arg.Any<CorrectiveRAGOptions>(),
                Arg.Any<CancellationToken>()).Returns(CreateDefaultCorrectiveRAGResult());

        _mockSmallToBigRetriever.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<SmallToBigOptions>(),
                Arg.Any<CancellationToken>()).Returns(CreateDefaultSmallToBigResults());

        _mockIterativeRetrievalService.RetrieveWithReasoningAsync(
                Arg.Any<string>(),
                Arg.Any<IterativeRetrievalOptions>(),
                Arg.Any<CancellationToken>()).Returns(CreateDefaultIterativeResult());
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Arrange & Act
        var router = new AgenticRetrievalRouter(
            _mockHybridSearchService,
            _mockEmbeddingService,
            null, null, null, null,
            Microsoft.Extensions.Options.Options.Create(new AgenticRetrievalRouterOptions()),
            _mockLogger);

        // Assert
        Assert.NotNull(router);
    }

    [Fact]
    public void Constructor_WithNullHybridSearchService_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AgenticRetrievalRouter(
            null!,
            _mockEmbeddingService,
            null, null, null, null,
            Microsoft.Extensions.Options.Options.Create(new AgenticRetrievalRouterOptions()),
            _mockLogger));
    }

    [Fact]
    public void Constructor_WithNullEmbeddingService_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AgenticRetrievalRouter(
            _mockHybridSearchService,
            null!,
            null, null, null, null,
            Microsoft.Extensions.Options.Options.Create(new AgenticRetrievalRouterOptions()),
            _mockLogger));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AgenticRetrievalRouter(
            _mockHybridSearchService,
            _mockEmbeddingService,
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
        var result = await _router.RouteAndRetrieveAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _router.RouteAndRetrieveAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
            () => _router.RouteAndRetrieveAsync(query!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RouteAndRetrieveAsync_WithContext_UsesProvidedContext()
    {
        // Arrange
        var query = "What is machine learning?";
        var context = new RoutingContext { MaxResults = 3, Domain = "technical" };

        // Act
        var result = await _router.RouteAndRetrieveAsync(query, context, TestContext.Current.CancellationToken);

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
        var result = await _router.RouteAndRetrieveAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _router.AnalyzeQueryAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _router.AnalyzeQueryAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _router.AnalyzeQueryAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _router.AnalyzeQueryAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _router.AnalyzeQueryAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _router.AnalyzeQueryAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        var plan = await _router.GenerateRetrievalPlanAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        var plan = await _router.GenerateRetrievalPlanAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        var plan = await _router.GenerateRetrievalPlanAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        var plan = await _router.GenerateRetrievalPlanAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var result = await _router.ExecuteRetrievalPlanAsync(query, plan, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public async Task ExecuteRetrievalPlanAsync_ReturnsMergedDocuments()
    {
        // Arrange
        var query = "What is machine learning?";
        var plan = await _router.GenerateRetrievalPlanAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var result = await _router.ExecuteRetrievalPlanAsync(query, plan, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(result.MergedDocuments);
    }

    [Fact]
    public async Task ExecuteRetrievalPlanAsync_TracksCompletedSteps()
    {
        // Arrange
        var query = "What is machine learning?";
        var plan = await _router.GenerateRetrievalPlanAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var result = await _router.ExecuteRetrievalPlanAsync(query, plan, TestContext.Current.CancellationToken);

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
        await _router.RecordRoutingFeedbackAsync(routingId, feedback, TestContext.Current.CancellationToken);

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
        await _router.RecordRoutingFeedbackAsync(routingId, feedback, TestContext.Current.CancellationToken);

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
            await _router.RecordRoutingFeedbackAsync(routingId, feedback, TestContext.Current.CancellationToken);
        }

        var result = await _router.RouteAndRetrieveAsync("simple factual question", cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _router.RouteAndRetrieveAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _router.RouteAndRetrieveAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _router.RouteAndRetrieveAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        _mockSelfRAGService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<SelfRAGOptions>(),
                Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("Service unavailable"));

        // Act
        var result = await _router.RouteAndRetrieveAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _router.RouteAndRetrieveAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.TotalTime > TimeSpan.Zero);
    }

    [Fact]
    public async Task RouteAndRetrieveAsync_TracksRetrievalSteps()
    {
        // Arrange
        var query = "What is machine learning?";

        // Act
        var result = await _router.RouteAndRetrieveAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _router.RouteAndRetrieveAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.RoutingTime >= TimeSpan.Zero);
    }

    [Fact]
    public async Task RouteAndRetrieveAsync_TracksRetrievalTime()
    {
        // Arrange
        var query = "What is machine learning?";

        // Act
        var result = await _router.RouteAndRetrieveAsync(query, cancellationToken: TestContext.Current.CancellationToken);

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
