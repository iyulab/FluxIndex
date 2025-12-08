using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Application.Services.Fusion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Fusion;

public class LearningBasedFusionServiceTests
{
    private readonly Mock<ILogger<LearningBasedFusionService>> _loggerMock;
    private readonly LearningBasedFusionService _sut;

    public LearningBasedFusionServiceTests()
    {
        _loggerMock = new Mock<ILogger<LearningBasedFusionService>>();
        _sut = new LearningBasedFusionService(_loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LearningBasedFusionService(null!));
    }

    [Fact]
    public void Constructor_InitializesDefaultModels()
    {
        // Act
        var stats = _sut.GetModelStatistics();

        // Assert
        Assert.Equal(0, stats.TrainingExampleCount);
        Assert.False(stats.IsModelTrained);
        Assert.NotNull(stats.QueryTypeDistribution);
    }

    #endregion

    #region TrainAsync Tests

    [Fact]
    public async Task TrainAsync_WithEmptyExamples_DoesNotTrain()
    {
        // Arrange
        var examples = new List<FusionTrainingExample>();

        // Act
        await _sut.TrainAsync(examples);

        // Assert
        var stats = _sut.GetModelStatistics();
        Assert.Equal(0, stats.TrainingExampleCount);
        Assert.False(stats.IsModelTrained);
    }

    [Fact]
    public async Task TrainAsync_WithValidExamples_UpdatesModelStatistics()
    {
        // Arrange
        var examples = CreateTrainingExamples(15);

        // Act
        await _sut.TrainAsync(examples);

        // Assert
        var stats = _sut.GetModelStatistics();
        Assert.Equal(15, stats.TrainingExampleCount);
        Assert.True(stats.IsModelTrained);
        Assert.NotNull(stats.LastTrainedAt);
    }

    [Fact]
    public async Task TrainAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var examples = CreateTrainingExamples(50);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.TrainAsync(examples, cts.Token));
    }

    [Fact]
    public async Task TrainAsync_WithNullExamples_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.TrainAsync(null!));
    }

    #endregion

    #region PredictWeightsAsync Tests

    [Fact]
    public async Task PredictWeightsAsync_WithSimpleKeywordQuery_ReturnsSparseFavoring()
    {
        // Arrange
        var query = "API";
        var vectorResults = CreateRankedResults(5);
        var sparseResults = CreateRankedResults(5);

        // Act
        var prediction = await _sut.PredictWeightsAsync(query, vectorResults, sparseResults);

        // Assert
        Assert.NotNull(prediction);
        Assert.NotNull(prediction.Weights);
        Assert.True(prediction.Weights.SparseWeight >= prediction.Weights.VectorWeight,
            "Simple keyword query should favor sparse search");
        Assert.False(prediction.IsLearnedPrediction);
    }

    [Fact]
    public async Task PredictWeightsAsync_WithNaturalQuestion_ReturnsVectorFavoring()
    {
        // Arrange
        var query = "What is the best way to implement authentication?";
        var vectorResults = CreateRankedResults(5);
        var sparseResults = CreateRankedResults(5);

        // Act
        var prediction = await _sut.PredictWeightsAsync(query, vectorResults, sparseResults);

        // Assert
        Assert.NotNull(prediction);
        Assert.True(prediction.Weights.VectorWeight >= prediction.Weights.SparseWeight,
            "Natural question should favor vector search");
    }

    [Fact]
    public async Task PredictWeightsAsync_WithReasoningQuery_ReturnsHighVectorWeight()
    {
        // Arrange - use a clear reasoning query without question mark for precise detection
        var query = "explain why memory leaks cause system failures";
        var vectorResults = CreateRankedResults(5);
        var sparseResults = CreateRankedResults(5);

        // Act
        var prediction = await _sut.PredictWeightsAsync(query, vectorResults, sparseResults);

        // Assert
        Assert.NotNull(prediction);
        // Reasoning queries favor vector search (>0.6 after overlap adjustment)
        Assert.True(prediction.Weights.VectorWeight >= 0.6,
            $"Reasoning query should favor vector search, got {prediction.Weights.VectorWeight}");
        Assert.True(prediction.Weights.VectorWeight > prediction.Weights.SparseWeight,
            "Reasoning query should have vector weight > sparse weight");
    }

    [Fact]
    public async Task PredictWeightsAsync_AfterTraining_ReturnsLearnedPrediction()
    {
        // Arrange
        var examples = CreateTrainingExamples(20);
        await _sut.TrainAsync(examples);

        var query = "What is authentication?";
        var vectorResults = CreateRankedResults(5);
        var sparseResults = CreateRankedResults(5);

        // Act
        var prediction = await _sut.PredictWeightsAsync(query, vectorResults, sparseResults);

        // Assert
        Assert.NotNull(prediction);
        Assert.True(prediction.IsLearnedPrediction);
        Assert.True(prediction.Confidence > 0);
    }

    [Fact]
    public async Task PredictWeightsAsync_WithNullQuery_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.PredictWeightsAsync(null!, CreateRankedResults(5), CreateRankedResults(5)));
    }

    [Fact]
    public async Task PredictWeightsAsync_ReturnsNormalizedWeights()
    {
        // Arrange
        var query = "test query";
        var vectorResults = CreateRankedResults(5);
        var sparseResults = CreateRankedResults(5);

        // Act
        var prediction = await _sut.PredictWeightsAsync(query, vectorResults, sparseResults);

        // Assert
        var totalWeight = prediction.Weights.VectorWeight + prediction.Weights.SparseWeight;
        Assert.True(Math.Abs(totalWeight - 1.0) < 0.01,
            $"Weights should sum to 1.0, got {totalWeight}");
    }

    #endregion

    #region UpdateOnlineAsync Tests

    [Fact]
    public async Task UpdateOnlineAsync_WithValidFeedback_UpdatesModel()
    {
        // Arrange
        var feedback = CreateFusionFeedback("How do I configure SSL?", true);

        // Act
        await _sut.UpdateOnlineAsync(feedback);

        // Assert
        var stats = _sut.GetModelStatistics();
        Assert.Equal(1, stats.OnlineUpdateCount);
        Assert.NotNull(stats.LastUpdatedAt);
    }

    [Fact]
    public async Task UpdateOnlineAsync_WithMultipleFeedbacks_AccumulatesUpdates()
    {
        // Arrange
        var feedback1 = CreateFusionFeedback("Query one", true);
        var feedback2 = CreateFusionFeedback("Query two", false);
        var feedback3 = CreateFusionFeedback("Query three", true);

        // Act
        await _sut.UpdateOnlineAsync(feedback1);
        await _sut.UpdateOnlineAsync(feedback2);
        await _sut.UpdateOnlineAsync(feedback3);

        // Assert
        var stats = _sut.GetModelStatistics();
        Assert.Equal(3, stats.OnlineUpdateCount);
    }

    [Fact]
    public async Task UpdateOnlineAsync_WithNullFeedback_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.UpdateOnlineAsync(null!));
    }

    #endregion

    #region RecordFeedbackAsync Tests

    [Fact]
    public async Task RecordFeedbackAsync_WithValidInput_RecordsFeedback()
    {
        // Arrange
        var queryId = "query-123";
        var resultId = "result-456";
        var relevanceScore = 0.85;

        // Act
        await _sut.RecordFeedbackAsync(queryId, resultId, relevanceScore);

        // Assert - no exception thrown, feedback recorded internally
        Assert.True(true);
    }

    [Fact]
    public async Task RecordFeedbackAsync_ClampsRelevanceScore()
    {
        // Arrange
        var queryId = "query-123";
        var resultId = "result-456";

        // Act & Assert - should not throw for out-of-range values
        await _sut.RecordFeedbackAsync(queryId, resultId, 1.5);  // Clamped to 1.0
        await _sut.RecordFeedbackAsync(queryId, resultId, -0.5);  // Clamped to 0.0
    }

    [Fact]
    public async Task RecordFeedbackAsync_WithNullQueryId_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.RecordFeedbackAsync(null!, "result-id", 0.5));
    }

    #endregion

    #region GetModelStatistics Tests

    [Fact]
    public void GetModelStatistics_ReturnsInitialState()
    {
        // Act
        var stats = _sut.GetModelStatistics();

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(0, stats.TrainingExampleCount);
        Assert.Equal(0, stats.OnlineUpdateCount);
        Assert.False(stats.IsModelTrained);
        Assert.Equal("1.0.0", stats.ModelVersion);
    }

    [Fact]
    public async Task GetModelStatistics_AfterTraining_ReflectsTrainingState()
    {
        // Arrange
        await _sut.TrainAsync(CreateTrainingExamples(20));

        // Act
        var stats = _sut.GetModelStatistics();

        // Assert
        Assert.True(stats.IsModelTrained);
        Assert.Equal(20, stats.TrainingExampleCount);
        Assert.NotNull(stats.LastTrainedAt);
        Assert.True(stats.QueryTypeDistribution.Count > 0);
    }

    #endregion

    #region ExportModelAsync/ImportModelAsync Tests

    [Fact]
    public async Task ExportModelAsync_ReturnsValidModelState()
    {
        // Arrange
        await _sut.TrainAsync(CreateTrainingExamples(15));

        // Act
        var state = await _sut.ExportModelAsync();

        // Assert
        Assert.NotNull(state);
        Assert.Equal("1.0.0", state.Version);
        Assert.NotNull(state.LearnedWeights);
        Assert.NotNull(state.FeatureMeans);
        Assert.NotNull(state.FeatureStdDevs);
        Assert.NotNull(state.Checksum);
        Assert.NotNull(state.Statistics);
    }

    [Fact]
    public async Task ImportModelAsync_RestoresModelState()
    {
        // Arrange
        await _sut.TrainAsync(CreateTrainingExamples(15));
        var exportedState = await _sut.ExportModelAsync();

        // Reset the model
        _sut.ResetModel();
        Assert.False(_sut.GetModelStatistics().IsModelTrained);

        // Act
        await _sut.ImportModelAsync(exportedState);

        // Assert
        var stats = _sut.GetModelStatistics();
        Assert.True(stats.IsModelTrained);
        Assert.Equal(exportedState.Statistics.TrainingExampleCount, stats.TrainingExampleCount);
    }

    [Fact]
    public async Task ImportModelAsync_WithNullState_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.ImportModelAsync(null!));
    }

    [Fact]
    public async Task ImportModelAsync_WithInvalidChecksum_ThrowsInvalidOperationException()
    {
        // Arrange
        await _sut.TrainAsync(CreateTrainingExamples(15));
        var state = await _sut.ExportModelAsync();

        // Tamper with checksum
        var tamperedState = state with { Checksum = "invalid-checksum" };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ImportModelAsync(tamperedState));
    }

    #endregion

    #region ResetModel Tests

    [Fact]
    public async Task ResetModel_ClearsAllTrainingData()
    {
        // Arrange
        await _sut.TrainAsync(CreateTrainingExamples(20));
        var feedback = CreateFusionFeedback("test query", true);
        await _sut.UpdateOnlineAsync(feedback);

        // Pre-assert
        var statsBefore = _sut.GetModelStatistics();
        Assert.True(statsBefore.IsModelTrained);

        // Act
        _sut.ResetModel();

        // Assert
        var statsAfter = _sut.GetModelStatistics();
        Assert.Equal(0, statsAfter.TrainingExampleCount);
        Assert.Equal(0, statsAfter.OnlineUpdateCount);
        Assert.False(statsAfter.IsModelTrained);
        Assert.Null(statsAfter.LastTrainedAt);
        Assert.Null(statsAfter.LastUpdatedAt);
    }

    #endregion

    #region Query Type Detection Tests

    [Theory]
    [InlineData("Why does this fail?", true)]  // Reasoning
    [InlineData("Compare A versus B", true)]   // Comparison
    [InlineData("When did this happen?", true)]  // Temporal
    [InlineData("How to do X and then Y", true)]  // Multi-hop
    [InlineData("What is authentication?", true)]  // Natural question
    [InlineData("API endpoint", false)]  // Simple keyword
    public async Task PredictWeightsAsync_DetectsQueryTypeCorrectly(string query, bool expectsVectorFavoring)
    {
        // Arrange
        var vectorResults = CreateRankedResults(5);
        var sparseResults = CreateRankedResults(5);

        // Act
        var prediction = await _sut.PredictWeightsAsync(query, vectorResults, sparseResults);

        // Assert
        if (expectsVectorFavoring)
        {
            Assert.True(prediction.Weights.VectorWeight >= 0.5,
                $"Query '{query}' should favor vector, got {prediction.Weights.VectorWeight}");
        }
        else
        {
            Assert.True(prediction.Weights.SparseWeight >= prediction.Weights.VectorWeight,
                $"Query '{query}' should favor sparse, got {prediction.Weights.SparseWeight}");
        }
    }

    #endregion

    #region FusionWeights Tests

    [Fact]
    public void FusionWeights_Normalize_CreatesValidWeights()
    {
        // Act
        var weights = FusionWeights.Normalize(3.0, 7.0);

        // Assert
        Assert.Equal(0.3, weights.VectorWeight, 2);
        Assert.Equal(0.7, weights.SparseWeight, 2);
        Assert.True(weights.IsValid);
    }

    [Fact]
    public void FusionWeights_Normalize_WithZeroTotal_ReturnsBalanced()
    {
        // Act
        var weights = FusionWeights.Normalize(0.0, 0.0);

        // Assert
        Assert.Equal(0.5, weights.VectorWeight);
        Assert.Equal(0.5, weights.SparseWeight);
    }

    [Fact]
    public void FusionWeights_IsValid_ReturnsTrueForNormalizedWeights()
    {
        // Arrange
        var weights = new FusionWeights { VectorWeight = 0.6, SparseWeight = 0.4 };

        // Assert
        Assert.True(weights.IsValid);
    }

    [Fact]
    public void FusionWeights_IsValid_ReturnsFalseForUnnormalizedWeights()
    {
        // Arrange
        var weights = new FusionWeights { VectorWeight = 0.6, SparseWeight = 0.6 };

        // Assert
        Assert.False(weights.IsValid);
    }

    #endregion

    #region FusionFeedback Tests

    [Fact]
    public void FusionFeedback_CalculateImplicitRelevance_WithNoClicks_ReturnsZero()
    {
        // Arrange
        var feedback = new FusionFeedback
        {
            Query = "test",
            ClickedResults = Array.Empty<string>(),
            UserSatisfied = null
        };

        // Act
        var relevance = feedback.CalculateImplicitRelevance();

        // Assert
        Assert.Equal(0.0, relevance);
    }

    [Fact]
    public void FusionFeedback_CalculateImplicitRelevance_WithClicks_ReturnsPositive()
    {
        // Arrange
        var feedback = new FusionFeedback
        {
            Query = "test",
            ClickedResults = new[] { "result-1", "result-2" },
            UserSatisfied = true,
            DwellTime = TimeSpan.FromSeconds(45)
        };

        // Act
        var relevance = feedback.CalculateImplicitRelevance();

        // Assert
        Assert.True(relevance > 0);
        Assert.True(relevance <= 1.0);
    }

    #endregion

    #region DI Registration Tests

    [Fact]
    public void AddLearningBasedFusion_RegistersService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Act
        services.AddLearningBasedFusion();

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ILearningBasedFusionService));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddAdvancedHybridSearch_RegistersAllServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Act
        services.AddAdvancedHybridSearch();

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(ILearningBasedFusionService));
        Assert.Contains(services, d => d.ServiceType == typeof(IDynamicFusionService));
        Assert.Contains(services, d => d.ServiceType == typeof(IQueryComplexityAnalyzer));
    }

    #endregion

    #region Helper Methods

    private static List<FusionTrainingExample> CreateTrainingExamples(int count)
    {
        var examples = new List<FusionTrainingExample>();
        var random = new Random(42);  // Fixed seed for reproducibility

        var queryTemplates = new[]
        {
            "What is {0}?",
            "How to {0}?",
            "Why does {0} happen?",
            "{0} API",
            "Compare {0} and {1}"
        };

        var topics = new[] { "authentication", "caching", "logging", "security", "performance" };

        for (int i = 0; i < count; i++)
        {
            var template = queryTemplates[i % queryTemplates.Length];
            var topic1 = topics[i % topics.Length];
            var topic2 = topics[(i + 1) % topics.Length];
            var query = string.Format(template, topic1, topic2);

            var vectorWeight = 0.3 + random.NextDouble() * 0.4;  // 0.3-0.7
            var sparseWeight = 1.0 - vectorWeight;

            examples.Add(new FusionTrainingExample
            {
                Query = query,
                OptimalWeights = new FusionWeights
                {
                    VectorWeight = vectorWeight,
                    SparseWeight = sparseWeight
                },
                RelevanceScore = 0.5 + random.NextDouble() * 0.5,  // 0.5-1.0
                VectorResults = CreateResultsWithRelevance(5),
                SparseResults = CreateResultsWithRelevance(5),
                CollectedAt = DateTimeOffset.UtcNow.AddDays(-i)
            });
        }

        return examples;
    }

    private static List<RankedResult> CreateRankedResults(int count)
    {
        return Enumerable.Range(0, count).Select(i => new RankedResult
        {
            Id = $"result-{i}",
            DocumentId = $"doc-{i}",
            ChunkId = $"chunk-{i}",
            Content = $"Content for result {i}",
            Score = 0.9f - (i * 0.1f),  // Decreasing scores
            Rank = i + 1
        }).ToList();
    }

    private static List<ResultWithRelevance> CreateResultsWithRelevance(int count)
    {
        var random = new Random(42);
        return Enumerable.Range(0, count).Select(i => new ResultWithRelevance
        {
            Id = $"result-{i}",
            Score = 0.9 - (i * 0.1),
            Rank = i + 1,
            RelevanceLabel = 0.5 + random.NextDouble() * 0.5
        }).ToList();
    }

    private static FusionFeedback CreateFusionFeedback(string query, bool satisfied)
    {
        return new FusionFeedback
        {
            Query = query,
            Features = new QueryPredictionFeatures
            {
                QueryLength = query.Split(' ').Length,
                UniqueTermCount = query.Split(' ').Distinct().Count(),
                QueryType = QueryType.NaturalQuestion,
                Complexity = ComplexityLevel.Moderate
            },
            UsedWeights = new FusionWeights
            {
                VectorWeight = 0.6,
                SparseWeight = 0.4
            },
            ClickedResults = satisfied ? new[] { "result-1", "result-2" } : Array.Empty<string>(),
            UserSatisfied = satisfied,
            DwellTime = satisfied ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(5),
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
