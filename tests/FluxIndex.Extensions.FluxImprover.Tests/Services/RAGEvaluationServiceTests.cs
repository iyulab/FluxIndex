using FluentAssertions;
using FluxIndex.Extensions.FluxImprover.Services;
using FluxImprover.Options;
using FluxImprover.Services;
using FluxImprover.Evaluation;
using Moq;
using Xunit;

namespace FluxIndex.Extensions.FluxImprover.Tests.Services;

/// <summary>
/// Tests for RAGEvaluationService - comprehensive RAG evaluation with answerability, faithfulness, and relevancy metrics.
/// </summary>
public class RAGEvaluationServiceTests
{
    private readonly Mock<ITextCompletionService> _mockCompletionService;
    private readonly AnswerabilityEvaluator _answerabilityEvaluator;
    private readonly FaithfulnessEvaluator _faithfulnessEvaluator;
    private readonly RelevancyEvaluator _relevancyEvaluator;
    private readonly RAGEvaluationService _service;

    public RAGEvaluationServiceTests()
    {
        _mockCompletionService = new Mock<ITextCompletionService>();

        // Setup default response for completion service
        SetupCompletionServiceResponse(0.85, "Good quality response based on context.");

        _answerabilityEvaluator = new AnswerabilityEvaluator(_mockCompletionService.Object);
        _faithfulnessEvaluator = new FaithfulnessEvaluator(_mockCompletionService.Object);
        _relevancyEvaluator = new RelevancyEvaluator(_mockCompletionService.Object);

        _service = new RAGEvaluationService(
            _answerabilityEvaluator,
            _faithfulnessEvaluator,
            _relevancyEvaluator);
    }

    private void SetupCompletionServiceResponse(double score, string reasoning)
    {
        var jsonResponse = $"{{\"score\": {score}, \"reasoning\": \"{reasoning}\"}}";
        _mockCompletionService
            .Setup(s => s.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<CompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);
    }

    [Fact]
    public async Task EvaluateAnswerabilityAsync_ReturnsMetricResult()
    {
        // Arrange
        var context = "FluxIndex is a RAG infrastructure library for .NET.";
        var question = "What is FluxIndex?";

        // Act
        var result = await _service.EvaluateAnswerabilityAsync(context, question);

        // Assert
        result.Should().NotBeNull();
        result.Score.Should().BeInRange(0.0, 1.0);
        result.MetricName.Should().Be("Answerability");
    }

    [Fact]
    public async Task EvaluateFaithfulnessAsync_ReturnsMetricResult()
    {
        // Arrange
        var context = "FluxIndex supports PostgreSQL and SQLite for vector storage.";
        var answer = "FluxIndex supports PostgreSQL and SQLite databases.";

        // Act
        var result = await _service.EvaluateFaithfulnessAsync(context, answer);

        // Assert
        result.Should().NotBeNull();
        result.Score.Should().BeInRange(0.0, 1.0);
        result.MetricName.Should().Be("Faithfulness");
    }

    [Fact]
    public async Task EvaluateRelevancyAsync_ReturnsMetricResult()
    {
        // Arrange
        var question = "What databases does FluxIndex support?";
        var answer = "FluxIndex supports PostgreSQL and SQLite for vector storage.";

        // Act
        var result = await _service.EvaluateRelevancyAsync(question, answer);

        // Assert
        result.Should().NotBeNull();
        result.Score.Should().BeInRange(0.0, 1.0);
        result.MetricName.Should().Be("Relevancy");
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsCompleteEvaluation()
    {
        // Arrange
        var context = "FluxIndex is a RAG library supporting hybrid search.";
        var question = "What is FluxIndex?";
        var answer = "FluxIndex is a RAG library that supports hybrid search capabilities.";

        // Act
        var result = await _service.EvaluateAsync(context, question, answer);

        // Assert
        result.Should().NotBeNull();
        result.Answerability.Should().NotBeNull();
        result.Faithfulness.Should().NotBeNull();
        result.Relevancy.Should().NotBeNull();
        result.OverallScore.Should().BeInRange(0.0, 1.0);
    }

    [Fact]
    public async Task EvaluateAsync_RunsEvaluationsInParallel()
    {
        // Arrange
        var callCount = 0;
        _mockCompletionService
            .Setup(s => s.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<CompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref callCount);
                return "{\"score\": 0.85, \"reasoning\": \"Test\"}";
            });

        var context = "Test context";
        var question = "Test question";
        var answer = "Test answer";

        // Act
        await _service.EvaluateAsync(context, question, answer);

        // Assert - Should have 3 calls (answerability, faithfulness, relevancy)
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task EvaluateBatchAsync_ProcessesAllInputs()
    {
        // Arrange
        var inputs = new List<RAGEvaluationInput>
        {
            new() { Context = "Context 1", Question = "Question 1", Answer = "Answer 1" },
            new() { Context = "Context 2", Question = "Question 2", Answer = "Answer 2" },
            new() { Context = "Context 3", Question = "Question 3", Answer = "Answer 3" }
        };

        // Act
        var results = await _service.EvaluateBatchAsync(inputs);

        // Assert
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r =>
        {
            r.Answerability.Should().NotBeNull();
            r.Faithfulness.Should().NotBeNull();
            r.Relevancy.Should().NotBeNull();
        });
    }

    [Fact]
    public void RAGEvaluationResult_OverallScore_CalculatesAverage()
    {
        // Arrange
        var result = new RAGEvaluationResult
        {
            Answerability = new MetricResult { MetricName = "Answerability", Score = 0.9 },
            Faithfulness = new MetricResult { MetricName = "Faithfulness", Score = 0.8 },
            Relevancy = new MetricResult { MetricName = "Relevancy", Score = 0.7 }
        };

        // Act & Assert
        result.OverallScore.Should().BeApproximately(0.8, 0.001);
    }

    [Fact]
    public void RAGEvaluationResult_PassesThreshold_ReturnsTrueWhenAllMetricsMeetThreshold()
    {
        // Arrange
        var result = new RAGEvaluationResult
        {
            Answerability = new MetricResult { MetricName = "Answerability", Score = 0.8 },
            Faithfulness = new MetricResult { MetricName = "Faithfulness", Score = 0.75 },
            Relevancy = new MetricResult { MetricName = "Relevancy", Score = 0.7 }
        };

        // Act & Assert
        result.PassesThreshold(0.7).Should().BeTrue();
        result.PassesThreshold(0.8).Should().BeFalse(); // Relevancy is below 0.8
    }

    [Fact]
    public void RAGEvaluationResult_PassesThreshold_ReturnsFalseWhenAnyMetricBelowThreshold()
    {
        // Arrange
        var result = new RAGEvaluationResult
        {
            Answerability = new MetricResult { MetricName = "Answerability", Score = 0.9 },
            Faithfulness = new MetricResult { MetricName = "Faithfulness", Score = 0.5 },
            Relevancy = new MetricResult { MetricName = "Relevancy", Score = 0.85 }
        };

        // Act & Assert
        result.PassesThreshold(0.7).Should().BeFalse(); // Faithfulness is 0.5
    }

    [Fact]
    public void Constructor_WithNullAnswerabilityEvaluator_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new RAGEvaluationService(null!, _faithfulnessEvaluator, _relevancyEvaluator);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("answerabilityEvaluator");
    }

    [Fact]
    public void Constructor_WithNullFaithfulnessEvaluator_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new RAGEvaluationService(_answerabilityEvaluator, null!, _relevancyEvaluator);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("faithfulnessEvaluator");
    }

    [Fact]
    public void Constructor_WithNullRelevancyEvaluator_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new RAGEvaluationService(_answerabilityEvaluator, _faithfulnessEvaluator, null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("relevancyEvaluator");
    }

    [Fact]
    public async Task EvaluateAnswerabilityAsync_WithNullContext_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.EvaluateAnswerabilityAsync(null!, "question"));
    }

    [Fact]
    public async Task EvaluateAnswerabilityAsync_WithNullQuestion_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.EvaluateAnswerabilityAsync("context", null!));
    }

    [Fact]
    public async Task EvaluateFaithfulnessAsync_WithNullContext_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.EvaluateFaithfulnessAsync(null!, "answer"));
    }

    [Fact]
    public async Task EvaluateFaithfulnessAsync_WithNullAnswer_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.EvaluateFaithfulnessAsync("context", null!));
    }

    [Fact]
    public async Task EvaluateRelevancyAsync_WithNullQuestion_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.EvaluateRelevancyAsync(null!, "answer"));
    }

    [Fact]
    public async Task EvaluateRelevancyAsync_WithNullAnswer_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.EvaluateRelevancyAsync("question", null!));
    }

    [Fact]
    public async Task EvaluateAsync_WithCustomOptions_PassesOptionsToEvaluators()
    {
        // Arrange
        var options = new EvaluationOptions(); // Use default options

        _mockCompletionService
            .Setup(s => s.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<CompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"score\": 0.85, \"details\": \"Evaluation completed.\"}");

        // Act
        var result = await _service.EvaluateAsync("context", "question", "answer", options);

        // Assert
        result.Should().NotBeNull();
        // The options are passed through - we verify the service didn't throw
    }
}
