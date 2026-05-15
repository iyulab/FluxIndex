using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// Tests for IterativeRetrievalService
/// </summary>
public class IterativeRetrievalServiceTests
{
    private readonly IHybridSearchService _mockSearchService;
    private readonly ITextCompletionService _mockLlmService;
    private readonly IAdvancedEntityExtractionService _mockEntityService;
    private readonly ILogger<IterativeRetrievalService> _logger;

    public IterativeRetrievalServiceTests()
    {
        _mockSearchService = Substitute.For<IHybridSearchService>();
        _mockLlmService = Substitute.For<ITextCompletionService>();
        _mockEntityService = Substitute.For<IAdvancedEntityExtractionService>();
        _logger = NullLogger<IterativeRetrievalService>.Instance;
    }

    private IterativeRetrievalService CreateService(
        bool withLlm = true,
        bool withEntityService = true)
    {
        return new IterativeRetrievalService(
            _mockSearchService,
            withLlm ? _mockLlmService : null,
            withEntityService ? _mockEntityService : null,
            _logger);
    }

    private HybridSearchResult CreateMockResult(string id, string content, double score)
    {
        return new HybridSearchResult
        {
            Chunk = new DocumentChunk
            {
                Id = id,
                DocumentId = $"doc_{id}",
                Content = content,
                ChunkIndex = 0
            },
            FusedScore = score,
            VectorScore = score * 0.7,
            SparseScore = score * 0.3,
            FusionMethod = FusionMethod.RelativeScoreFusion
        };
    }

    #region RetrieveWithReasoningAsync Tests

    [Fact]
    public async Task RetrieveWithReasoningAsync_WithoutLLM_ReturnsSearchResults()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "What is machine learning?";
        var mockResults = new List<HybridSearchResult>
        {
            CreateMockResult("1", "ML is a branch of AI...", 0.9),
            CreateMockResult("2", "Supervised learning uses labeled data...", 0.8)
        };

        _mockSearchService.SearchAsync(query, Arg.Any<HybridSearchOptions>(), Arg.Any<CancellationToken>()).Returns(mockResults);

        // Act
        var result = await service.RetrieveWithReasoningAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Documents.Count);
        Assert.NotEmpty(result.Iterations); // Multiple iterations without LLM (rule-based continues until confidence threshold)
    }

    [Fact]
    public async Task RetrieveWithReasoningAsync_WithLLM_PerformsIterativeReasoning()
    {
        // Arrange
        var service = CreateService(withLlm: true);
        var query = "What are the applications of machine learning?";
        var mockResults = new List<HybridSearchResult>
        {
            CreateMockResult("1", "ML applications include healthcare, finance...", 0.95),
            CreateMockResult("2", "Natural language processing uses ML...", 0.85)
        };

        _mockSearchService.SearchAsync(Arg.Any<string>(), Arg.Any<HybridSearchOptions>(), Arg.Any<CancellationToken>()).Returns(mockResults);

        // LLM returns thought indicating more retrieval needed, then a final answer
        var callCount = 0;
        _mockLlmService.CompleteAsync(Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns(callInfo => {
                callCount++;
                if (callCount == 1)
                    return "[Retrieval] healthcare applications of ML";
                return "[Finish] Machine learning has many applications including healthcare diagnostics...";
            });

        // Act
        var result = await service.RetrieveWithReasoningAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Iterations);
    }

    [Fact]
    public async Task RetrieveWithReasoningAsync_WithOptions_RespectsMaxIterations()
    {
        // Arrange
        var service = CreateService(withLlm: true);
        var query = "Complex query requiring multiple iterations";
        var options = new IterativeRetrievalOptions { MaxIterations = 2, MaxDocsPerIteration = 5 };
        var mockResults = new List<HybridSearchResult>
        {
            CreateMockResult("1", "Some relevant content...", 0.9)
        };

        _mockSearchService.SearchAsync(Arg.Any<string>(), Arg.Any<HybridSearchOptions>(), Arg.Any<CancellationToken>()).Returns(mockResults);

        // Always return retrieval action to test max iterations
        _mockLlmService.CompleteAsync(Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("[Retrieval] more data needed");

        // Act
        var result = await service.RetrieveWithReasoningAsync(query, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Stats.TotalIterations <= options.MaxIterations);
    }

    #endregion

    #region DecomposeAndRetrieveAsync Tests

    [Fact]
    public async Task DecomposeAndRetrieveAsync_SimpleQuery_ReturnsDirectResults()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "What is Python?";
        var mockResults = new List<HybridSearchResult>
        {
            CreateMockResult("1", "Python is a programming language...", 0.95)
        };

        _mockSearchService.SearchAsync(query, Arg.Any<HybridSearchOptions>(), Arg.Any<CancellationToken>()).Returns(mockResults);

        // Act
        var result = await service.DecomposeAndRetrieveAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
        Assert.NotEmpty(result.AllDocuments);
    }

    [Fact]
    public async Task DecomposeAndRetrieveAsync_WithLLM_DecomposesComplexQuery()
    {
        // Arrange
        var service = CreateService(withLlm: true);
        var query = "Compare Python and Java for web development and machine learning";
        var mockResults = new List<HybridSearchResult>
        {
            CreateMockResult("1", "Python for web development uses Django...", 0.9),
            CreateMockResult("2", "Java enterprise development...", 0.85)
        };

        _mockSearchService.SearchAsync(Arg.Any<string>(), Arg.Any<HybridSearchOptions>(), Arg.Any<CancellationToken>()).Returns(mockResults);

        // LLM decomposes and generates sub-questions
        _mockLlmService.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("""
                {
                    "sub_questions": [
                        "What are Python's strengths for web development?",
                        "What are Java's strengths for web development?",
                        "Which is better for machine learning?"
                    ],
                    "decomposition_type": "comparison"
                }
            """);

        _mockLlmService.CompleteAsync(Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("Based on the retrieved documents, Python excels in ML while Java is preferred for enterprise web apps...");

        // Act
        var result = await service.DecomposeAndRetrieveAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
    }

    #endregion

    #region MultiHopRetrieveAsync Tests

    [Fact]
    public async Task MultiHopRetrieveAsync_WithoutEntityService_ReturnsBasicResults()
    {
        // Arrange
        var service = CreateService(withLlm: false, withEntityService: false);
        var query = "Who founded Microsoft and what is their background?";
        var mockResults = new List<HybridSearchResult>
        {
            CreateMockResult("1", "Bill Gates founded Microsoft in 1975...", 0.95)
        };

        _mockSearchService.SearchAsync(Arg.Any<string>(), Arg.Any<HybridSearchOptions>(), Arg.Any<CancellationToken>()).Returns(mockResults);

        // Act
        var result = await service.MultiHopRetrieveAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.FinalDocuments);
    }

    [Fact]
    public async Task MultiHopRetrieveAsync_WithEntityService_FollowsEntityLinks()
    {
        // Arrange
        var service = CreateService(withLlm: true, withEntityService: true);
        var query = "Who founded Microsoft and where did they study?";
        var mockResults1 = new List<HybridSearchResult>
        {
            CreateMockResult("1", "Microsoft was founded by Bill Gates and Paul Allen...", 0.95)
        };
        var mockResults2 = new List<HybridSearchResult>
        {
            CreateMockResult("2", "Bill Gates attended Harvard University...", 0.9)
        };

        var callCount = 0;
        _mockSearchService.SearchAsync(Arg.Any<string>(), Arg.Any<HybridSearchOptions>(), Arg.Any<CancellationToken>()).Returns(callInfo => {
                callCount++;
                return callCount == 1 ? mockResults1 : mockResults2;
            });

        var extractedEntities = new List<ExtractedEntity>
        {
            new() { Text = "Bill Gates", Type = NamedEntityType.Person, Confidence = 0.95f },
            new() { Text = "Microsoft", Type = NamedEntityType.Organization, Confidence = 0.98f }
        };

        _mockEntityService.ExtractEntitiesAsync(Arg.Any<string>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>()).Returns(extractedEntities);

        // Act
        var result = await service.MultiHopRetrieveAsync(query);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task MultiHopRetrieveAsync_RespectsMaxHops()
    {
        // Arrange
        var service = CreateService(withLlm: true, withEntityService: true);
        var query = "Multi-hop query";
        var options = new MultiHopOptions { MaxHops = 2, MaxEntitiesPerHop = 3 };
        var mockResults = new List<HybridSearchResult>
        {
            CreateMockResult("1", "Entity A is related to Entity B...", 0.9)
        };

        _mockSearchService.SearchAsync(Arg.Any<string>(), Arg.Any<HybridSearchOptions>(), Arg.Any<CancellationToken>()).Returns(mockResults);

        var extractedEntities = new List<ExtractedEntity>
        {
            new() { Text = "Entity A", Type = NamedEntityType.Organization, Confidence = 0.9f }
        };

        _mockEntityService.ExtractEntitiesAsync(Arg.Any<string>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>()).Returns(extractedEntities);

        // Act
        var result = await service.MultiHopRetrieveAsync(query, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Hops.Count <= options.MaxHops);
    }

    #endregion

    #region AgenticRetrieveAsync Tests

    [Fact]
    public async Task AgenticRetrieveAsync_WithoutLLM_ReturnsFallbackResults()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "What are the best practices for software testing?";
        var mockResults = new List<HybridSearchResult>
        {
            CreateMockResult("1", "Unit testing best practices include...", 0.9)
        };

        _mockSearchService.SearchAsync(query, Arg.Any<HybridSearchOptions>(), Arg.Any<CancellationToken>()).Returns(mockResults);

        // Act
        var result = await service.AgenticRetrieveAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Documents);
    }

    [Fact]
    public async Task AgenticRetrieveAsync_WithLLM_ExecutesPlanningLoop()
    {
        // Arrange
        var service = CreateService(withLlm: true);
        var query = "Comprehensive analysis of cloud computing trends";
        var mockResults = new List<HybridSearchResult>
        {
            CreateMockResult("1", "Cloud computing trends show growth in serverless...", 0.95),
            CreateMockResult("2", "Multi-cloud strategies are becoming popular...", 0.88)
        };

        _mockSearchService.SearchAsync(Arg.Any<string>(), Arg.Any<HybridSearchOptions>(), Arg.Any<CancellationToken>()).Returns(mockResults);

        // LLM returns actions
        var actionCallCount = 0;
        _mockLlmService.CompleteAsync(Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns(callInfo => {
                actionCallCount++;
                if (actionCallCount == 1)
                    return "Action: Search\nInput: serverless computing trends 2024";
                return "Action: Finish\nFinal Answer: Cloud computing is evolving with serverless and multi-cloud strategies...";
            });

        // Act
        var result = await service.AgenticRetrieveAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.ExecutionTrace);
    }

    [Fact]
    public async Task AgenticRetrieveAsync_RespectsMaxIterations()
    {
        // Arrange
        var service = CreateService(withLlm: true);
        var query = "Complex agentic query";
        var options = new AgenticRetrievalOptions { MaxIterations = 3 };
        var mockResults = new List<HybridSearchResult>
        {
            CreateMockResult("1", "Some content...", 0.8)
        };

        _mockSearchService.SearchAsync(Arg.Any<string>(), Arg.Any<HybridSearchOptions>(), Arg.Any<CancellationToken>()).Returns(mockResults);

        // Always return search action to test max iterations limit
        _mockLlmService.CompleteAsync(Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("Action: Search\nInput: keep searching");

        // Act
        var result = await service.AgenticRetrieveAsync(query, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ExecutionTrace.Count <= options.MaxIterations);
    }

    #endregion

    #region DI Registration Tests

    [Fact]
    public void ServiceCanBeCreatedWithMinimalDependencies()
    {
        // Arrange & Act - Only search service is required
        var service = new IterativeRetrievalService(
            _mockSearchService,
            llmService: null,
            entityService: null,
            logger: null);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void ServiceCanBeCreatedWithAllDependencies()
    {
        // Arrange & Act
        var service = CreateService(withLlm: true, withEntityService: true);

        // Assert
        Assert.NotNull(service);
    }

    #endregion
}
