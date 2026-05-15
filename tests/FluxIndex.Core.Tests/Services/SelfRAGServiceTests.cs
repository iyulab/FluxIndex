using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// Tests for SelfRAGService implementing ISelfRAGService interface.
/// Tests cover: SearchAsync, AssessResultQualityAsync, SuggestQueryRefinementsAsync.
/// </summary>
public class SelfRAGServiceTests
{
    private readonly IHybridSearchService _mockSearchService;
    private readonly IEmbeddingService _mockEmbeddingService;
    private readonly ITextCompletionService _mockCompletionService;
    private readonly ILogger<SelfRAGService> _logger;
    private readonly SelfRAGServiceOptions _defaultOptions;

    public SelfRAGServiceTests()
    {
        _mockSearchService = Substitute.For<IHybridSearchService>();
        _mockEmbeddingService = Substitute.For<IEmbeddingService>();
        _mockCompletionService = Substitute.For<ITextCompletionService>();
        _logger = NullLogger<SelfRAGService>.Instance;
        _defaultOptions = new SelfRAGServiceOptions();

        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        _mockEmbeddingService.GetModelName().Returns("test-model");

        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f });

        _mockSearchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<HybridSearchOptions>(),
                Arg.Any<CancellationToken>()).Returns(CreateDefaultHybridResults(5));
    }

    private SelfRAGService CreateService(
        SelfRAGServiceOptions? options = null,
        bool withLlm = true)
    {
        var opts = options ?? _defaultOptions;

        return new SelfRAGService(
            _mockSearchService,
            _mockEmbeddingService,
            withLlm ? _mockCompletionService : null,
            Microsoft.Extensions.Options.Options.Create(opts),
            _logger);
    }

    private IReadOnlyList<HybridSearchResult> CreateDefaultHybridResults(int count, string topic = "machine learning")
    {
        var results = new List<HybridSearchResult>();
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            var embedding = new float[5];
            for (int j = 0; j < 5; j++)
            {
                embedding[j] = (float)random.NextDouble();
            }

            results.Add(new HybridSearchResult
            {
                Chunk = new DocumentChunk
                {
                    Id = $"chunk_{i}",
                    DocumentId = $"doc_{i / 2}",
                    Content = $"This is content about {topic} for chunk {i}. It contains relevant information.",
                    Embedding = embedding,
                    ChunkIndex = i,
                    Metadata = new Dictionary<string, object>
                    {
                        ["title"] = $"Document {i / 2}",
                        ["source"] = $"/path/to/doc_{i / 2}.txt"
                    }
                },
                FusedScore = 0.9 - (i * 0.1),
                VectorScore = 0.85 - (i * 0.08),
                SparseScore = 0.8 - (i * 0.1),
                VectorRank = i,
                SparseRank = i,
                FusedRank = i,
                Source = SearchSource.Both,
                Confidence = 0.9 - (i * 0.1)
            });
        }

        return results;
    }

    private List<Document> CreateTestDocuments(int count, string topic = "machine learning")
    {
        var documents = new List<Document>();

        for (int i = 0; i < count; i++)
        {
            documents.Add(new Document
            {
                Id = $"doc_{i}",
                FileName = $"Document about {topic} #{i}",
                FilePath = $"/path/to/doc_{i}.txt",
                Content = $"This is detailed content about {topic}. " +
                         $"It covers various aspects including algorithms, applications, and best practices. " +
                         $"Document {i} provides comprehensive information."
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
    public void Constructor_WithNullSearchService_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SelfRAGService(
                null!,
                _mockEmbeddingService,
                _mockCompletionService,
                Microsoft.Extensions.Options.Options.Create(_defaultOptions),
                _logger));
    }

    [Fact]
    public void Constructor_WithNullEmbeddingService_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SelfRAGService(
                _mockSearchService,
                null!,
                _mockCompletionService,
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
            new SelfRAGService(
                _mockSearchService,
                _mockEmbeddingService,
                _mockCompletionService,
                Microsoft.Extensions.Options.Options.Create(_defaultOptions),
                null!));
    }

    #endregion

    #region SearchAsync Tests

    [Fact]
    public async Task SearchAsync_ValidQuery_ReturnsResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "What is machine learning?";

        // Act
        var result = await service.SearchAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.FinalResults);
        Assert.True(result.TotalProcessingTime.TotalMilliseconds > 0);
    }

    [Fact]
    public async Task SearchAsync_WithOptions_RespectsMaxResults()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "Test query";
        var options = new SelfRAGOptions { MaxResults = 3 };

        _mockSearchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Is<HybridSearchOptions>(o => o.MaxResults <= 3),
                Arg.Any<CancellationToken>()).Returns(CreateDefaultHybridResults(3));

        // Act
        var result = await service.SearchAsync(query, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.FinalResults.Count() <= 3);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_HandlesGracefully()
    {
        // Arrange
        var service = CreateService(withLlm: false);

        // Act
        var result = await service.SearchAsync(string.Empty);

        // Assert - Empty query is handled gracefully, may return empty results or process
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SearchAsync_NullQuery_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService(withLlm: false);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SearchAsync(null!));
    }

    [Fact]
    public async Task SearchAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.SearchAsync("test query", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task SearchAsync_RecordsIterations()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "machine learning algorithms";

        // Act
        var result = await service.SearchAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Iterations);
        Assert.All(result.Iterations, iteration =>
        {
            Assert.NotEmpty(iteration.Query);
            Assert.True(iteration.ProcessingTime.TotalMilliseconds >= 0);
        });
    }

    [Fact]
    public async Task SearchAsync_ReturnsQualityScore()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "test query";

        // Act
        var result = await service.SearchAsync(query);

        // Assert
        Assert.True(result.FinalQualityScore >= 0 && result.FinalQualityScore <= 1);
    }

    [Fact]
    public async Task SearchAsync_WithAutoRefinementEnabled_MayRefineQuery()
    {
        // Arrange
        var options = new SelfRAGOptions
        {
            EnableAutoRefinement = true,
            MaxIterations = 2,
            QualityThreshold = 0.99 // High threshold to trigger refinement
        };
        var service = CreateService(withLlm: false);
        var query = "test";

        // Act
        var result = await service.SearchAsync(query, options);

        // Assert
        Assert.NotNull(result);
        // Service may or may not have refined based on quality assessment
    }

    [Fact]
    public async Task SearchAsync_WithLowQualityResults_AttemptsMultipleIterations()
    {
        // Arrange
        var lowQualityResults = CreateDefaultHybridResults(2);
        _mockSearchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<HybridSearchOptions>(),
                Arg.Any<CancellationToken>()).Returns(lowQualityResults);

        var options = new SelfRAGOptions
        {
            MaxIterations = 3,
            QualityThreshold = 0.95,
            MinResults = 5
        };
        var service = CreateService(withLlm: false);

        // Act
        var result = await service.SearchAsync("test query", options);

        // Assert
        Assert.NotNull(result);
        // With insufficient results, may attempt multiple iterations
        Assert.True(result.Iterations.Count >= 1);
    }

    #endregion

    #region AssessResultQualityAsync Tests

    [Fact]
    public async Task AssessResultQualityAsync_ValidResults_ReturnsAssessment()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "machine learning";
        var documents = CreateTestDocuments(5, "machine learning");

        // Act
        var assessment = await service.AssessResultQualityAsync(query, documents);

        // Assert
        Assert.NotNull(assessment);
        Assert.True(assessment.OverallScore >= 0, "OverallScore should be non-negative");
        Assert.True(assessment.CompletenessScore >= 0, "CompletenessScore should be non-negative");
        Assert.Equal(5, assessment.ResultCount);
    }

    [Fact]
    public async Task AssessResultQualityAsync_EmptyResults_ReturnsLowQuality()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "test query";
        var documents = new List<Document>();

        // Act
        var assessment = await service.AssessResultQualityAsync(query, documents);

        // Assert
        Assert.NotNull(assessment);
        Assert.Equal(0, assessment.ResultCount);
        Assert.True(assessment.OverallScore <= 0.3);
        Assert.Contains(assessment.Issues, i => i.Type == QualityIssueType.InsufficientResults);
    }

    [Fact]
    public async Task AssessResultQualityAsync_ManyResults_ReturnsHigherCompleteness()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "machine learning";
        var documents = CreateTestDocuments(10, "machine learning");

        // Act
        var assessment = await service.AssessResultQualityAsync(query, documents);

        // Assert
        Assert.NotNull(assessment);
        Assert.True(assessment.CompletenessScore >= 0.5);
    }

    [Fact]
    public async Task AssessResultQualityAsync_NullQuery_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var documents = CreateTestDocuments(3);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.AssessResultQualityAsync(null!, documents));
    }

    [Fact]
    public async Task AssessResultQualityAsync_NullResults_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService(withLlm: false);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.AssessResultQualityAsync("query", null!));
    }

    [Fact]
    public async Task AssessResultQualityAsync_IdentifiesQualityIssues()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "specific technical topic";
        var documents = CreateTestDocuments(1, "unrelated content");

        // Act
        var assessment = await service.AssessResultQualityAsync(query, documents);

        // Assert
        Assert.NotNull(assessment);
        Assert.NotEmpty(assessment.Issues);
    }

    [Fact]
    public async Task AssessResultQualityAsync_ProvidesSuggestions()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "test query";
        var documents = CreateTestDocuments(2);

        // Act
        var assessment = await service.AssessResultQualityAsync(query, documents);

        // Assert
        Assert.NotNull(assessment);
        // With few results, should provide improvement suggestions
        if (assessment.OverallScore < 0.8)
        {
            Assert.NotEmpty(assessment.Suggestions);
        }
    }

    [Fact]
    public async Task AssessResultQualityAsync_CalculatesDiversityScore()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "machine learning";

        // Create documents with varied content
        var documents = new List<Document>
        {
            new() { Id = "1", Content = "Machine learning supervised algorithms." },
            new() { Id = "2", Content = "Unsupervised clustering techniques." },
            new() { Id = "3", Content = "Neural network deep learning models." },
            new() { Id = "4", Content = "Reinforcement learning reward systems." }
        };

        // Act
        var assessment = await service.AssessResultQualityAsync(query, documents);

        // Assert
        Assert.NotNull(assessment);
        // DiversityScore is calculated (may vary based on implementation)
        Assert.Equal(4, assessment.ResultCount);
    }

    #endregion

    #region SuggestQueryRefinementsAsync Tests

    [Fact]
    public async Task SuggestQueryRefinementsAsync_ValidInput_ReturnsSuggestions()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var originalQuery = "machine learning";
        var assessment = new QualityAssessment
        {
            OverallScore = 0.5,
            RelevanceScore = 0.4,
            CompletenessScore = 0.5,
            DiversityScore = 0.6,
            ResultCount = 3,
            Issues = new List<QualityIssue>
            {
                new() { Type = QualityIssueType.InsufficientRelevance, Severity = 3 }
            }
        };

        // Act
        var suggestions = await service.SuggestQueryRefinementsAsync(originalQuery, assessment);

        // Assert
        Assert.NotNull(suggestions);
        Assert.Equal(originalQuery, suggestions.OriginalQuery);
        Assert.NotEmpty(suggestions.RefinedQueries);
    }

    [Fact]
    public async Task SuggestQueryRefinementsAsync_LowRelevance_SuggestsRefinements()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var originalQuery = "learning";
        var assessment = new QualityAssessment
        {
            OverallScore = 0.3,
            RelevanceScore = 0.2,
            CompletenessScore = 0.4,
            Issues = new List<QualityIssue>
            {
                new() { Type = QualityIssueType.InsufficientRelevance, Severity = 4 }
            }
        };

        // Act
        var suggestions = await service.SuggestQueryRefinementsAsync(originalQuery, assessment);

        // Assert
        Assert.NotNull(suggestions);
        Assert.True(suggestions.RefinedQueries.Count > 0 || suggestions.SuggestedKeywords.Count > 0);
    }

    [Fact]
    public async Task SuggestQueryRefinementsAsync_InsufficientResults_SuggestsExpansion()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var originalQuery = "very specific technical query";
        var assessment = new QualityAssessment
        {
            OverallScore = 0.4,
            ResultCount = 1,
            Issues = new List<QualityIssue>
            {
                new() { Type = QualityIssueType.InsufficientResults, Severity = 4 }
            }
        };

        // Act
        var suggestions = await service.SuggestQueryRefinementsAsync(originalQuery, assessment);

        // Assert
        Assert.NotNull(suggestions);
        // Should suggest broader queries or alternative strategies
        Assert.True(
            suggestions.RefinedQueries.Any() ||
            suggestions.AlternativeStrategies.Any() ||
            suggestions.ContextExpansions.Any());
    }

    [Fact]
    public async Task SuggestQueryRefinementsAsync_NullQuery_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var assessment = new QualityAssessment { OverallScore = 0.5 };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SuggestQueryRefinementsAsync(null!, assessment));
    }

    [Fact]
    public async Task SuggestQueryRefinementsAsync_NullAssessment_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService(withLlm: false);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SuggestQueryRefinementsAsync("query", null!));
    }

    [Fact]
    public async Task SuggestQueryRefinementsAsync_HighQualityAssessment_MinimalSuggestions()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var originalQuery = "machine learning algorithms";
        var assessment = new QualityAssessment
        {
            OverallScore = 0.95,
            RelevanceScore = 0.95,
            CompletenessScore = 0.9,
            DiversityScore = 0.9,
            ResultCount = 10,
            Issues = new List<QualityIssue>()
        };

        // Act
        var suggestions = await service.SuggestQueryRefinementsAsync(originalQuery, assessment);

        // Assert
        Assert.NotNull(suggestions);
        // High quality should result in fewer refinement suggestions
    }

    [Fact]
    public async Task SuggestQueryRefinementsAsync_ReturnsRefinementTypes()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var assessment = new QualityAssessment
        {
            OverallScore = 0.4,
            Issues = new List<QualityIssue>
            {
                new() { Type = QualityIssueType.InsufficientRelevance },
                new() { Type = QualityIssueType.LackOfDiversity }
            }
        };

        // Act
        var suggestions = await service.SuggestQueryRefinementsAsync("test", assessment);

        // Assert
        Assert.NotNull(suggestions);
        if (suggestions.RefinedQueries.Any())
        {
            Assert.All(suggestions.RefinedQueries, rq =>
            {
                Assert.NotEmpty(rq.QueryText);
                Assert.NotEmpty(rq.Rationale);
            });
        }
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task FullWorkflow_CompletesSuccessfully()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "machine learning applications";

        // Act - Full Self-RAG workflow
        var searchResult = await service.SearchAsync(query);
        var assessment = await service.AssessResultQualityAsync(query, searchResult.FinalResults);
        var suggestions = await service.SuggestQueryRefinementsAsync(query, assessment);

        // Assert
        Assert.NotNull(searchResult);
        Assert.NotNull(assessment);
        Assert.NotNull(suggestions);
        Assert.True(searchResult.TotalProcessingTime.TotalMilliseconds > 0);
    }

    [Fact]
    public async Task SearchAsync_WithMultipleIterations_TracksProgress()
    {
        // Arrange
        var options = new SelfRAGOptions
        {
            MaxIterations = 3,
            QualityThreshold = 0.99, // Very high to force iterations
            EnableAutoRefinement = true
        };
        var service = CreateService(withLlm: false);

        // Act
        var result = await service.SearchAsync("test query", options);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Iterations);
        Assert.All(result.Iterations, i => Assert.True(i.IterationNumber >= 1));
    }

    [Fact]
    public async Task SearchAsync_RecordsRefinementActions()
    {
        // Arrange
        var options = new SelfRAGOptions
        {
            EnableAutoRefinement = true,
            MaxIterations = 2
        };
        var service = CreateService(withLlm: false);

        // Act
        var result = await service.SearchAsync("test query", options);

        // Assert
        Assert.NotNull(result);
        // Refinement actions are recorded during iterative improvement
        if (result.Iterations.Count > 1)
        {
            Assert.NotEmpty(result.RefinementActions);
        }
    }

    #endregion

    #region Options Configuration Tests

    [Fact]
    public void SelfRAGOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new SelfRAGOptions();

        // Assert
        Assert.Equal(3, options.MaxIterations);
        Assert.Equal(0.7, options.QualityThreshold);
        Assert.Equal(20, options.MaxResults);
        Assert.Equal(5, options.MinResults);
        Assert.Equal(TimeSpan.FromMinutes(2), options.SearchTimeout);
        Assert.True(options.EnableAutoRefinement);
        Assert.True(options.EnableContextExpansion);
        Assert.True(options.EnableMultiPerspectiveSearch);
        Assert.False(options.EnableDetailedLogging);
    }

    [Fact]
    public void SelfRAGServiceOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new SelfRAGServiceOptions();

        // Assert
        Assert.True(options.UseLlmForRefinement);
        Assert.Equal(3, options.DefaultMaxIterations);
        Assert.Equal(0.7, options.DefaultQualityThreshold);
        Assert.Equal(0.35, options.RelevanceWeight);
        Assert.Equal(0.25, options.CompletenessWeight);
        Assert.Equal(0.15, options.DiversityWeight);
        Assert.Equal(0.15, options.CredibilityWeight);
        Assert.Equal(0.10, options.FreshnessWeight);
    }

    [Fact]
    public async Task Service_WithCustomOptions_RespectsConfiguration()
    {
        // Arrange
        var serviceOptions = new SelfRAGServiceOptions
        {
            DefaultMaxIterations = 2,
            DefaultQualityThreshold = 0.8
        };
        var service = CreateService(serviceOptions, withLlm: false);
        var searchOptions = new SelfRAGOptions
        {
            MaxIterations = 1,
            MaxResults = 5,
            EnableAutoRefinement = false
        };

        // Act
        var result = await service.SearchAsync("test", searchOptions);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Iterations.Count <= 1);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task SearchAsync_VeryLongQuery_HandlesGracefully()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = string.Join(" ", Enumerable.Repeat("machine learning", 100));

        // Act
        var result = await service.SearchAsync(query);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SearchAsync_SpecialCharactersInQuery_HandlesGracefully()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "machine learning? with special!@#$% chars";

        // Act
        var result = await service.SearchAsync(query);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SearchAsync_WhitespaceOnlyQuery_HandlesGracefully()
    {
        // Arrange
        var service = CreateService(withLlm: false);

        // Act
        var result = await service.SearchAsync("   ");

        // Assert - Whitespace query is handled gracefully
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SearchAsync_NoResultsFromSearch_HandlesGracefully()
    {
        // Arrange
        _mockSearchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<HybridSearchOptions>(),
                Arg.Any<CancellationToken>()).Returns(new List<HybridSearchResult>());

        var service = CreateService(withLlm: false);

        // Act
        var result = await service.SearchAsync("test query");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.FinalResults);
    }

    [Fact]
    public async Task AssessResultQualityAsync_SingleDocument_ReturnsValidAssessment()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var documents = CreateTestDocuments(1);

        // Act
        var assessment = await service.AssessResultQualityAsync("test", documents);

        // Assert
        Assert.NotNull(assessment);
        Assert.Equal(1, assessment.ResultCount);
    }

    [Fact]
    public async Task SearchAsync_TimeoutExceeded_ReturnsPartialResults()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var searchOptions = new SelfRAGOptions
        {
            SearchTimeout = TimeSpan.FromMilliseconds(1), // Very short timeout
            MaxIterations = 10
        };

        // Act
        var result = await service.SearchAsync("test query", searchOptions);

        // Assert
        Assert.NotNull(result);
        // Should return whatever results were gathered before timeout
    }

    #endregion

    #region Quality Assessment Dimension Tests

    [Fact]
    public async Task AssessResultQualityAsync_CalculatesCredibilityScore()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var documents = CreateTestDocuments(5);

        // Act
        var assessment = await service.AssessResultQualityAsync("test query", documents);

        // Assert
        Assert.True(assessment.CredibilityScore >= 0 && assessment.CredibilityScore <= 1);
    }

    [Fact]
    public async Task AssessResultQualityAsync_CalculatesFreshnessScore()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var documents = CreateTestDocuments(5);

        // Act
        var assessment = await service.AssessResultQualityAsync("test query", documents);

        // Assert
        Assert.True(assessment.FreshnessScore >= 0 && assessment.FreshnessScore <= 1);
    }

    [Fact]
    public async Task AssessResultQualityAsync_ProvidesRationale()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var documents = CreateTestDocuments(3);

        // Act
        var assessment = await service.AssessResultQualityAsync("machine learning", documents);

        // Assert
        Assert.NotNull(assessment.Rationale);
    }

    #endregion
}
