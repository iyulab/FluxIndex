using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FluxIndex.Tests.Evaluation;

/// <summary>
/// RAG 평가 서비스 테스트
/// </summary>
public class RAGEvaluationServiceTests
{
    private readonly Mock<ITextCompletionService> _mockTextCompletionService;
    private readonly Mock<ILogger<RAGEvaluationService>> _mockLogger;
    private readonly RAGEvaluationService _service;

    public RAGEvaluationServiceTests()
    {
        _mockTextCompletionService = new Mock<ITextCompletionService>();
        _mockLogger = new Mock<ILogger<RAGEvaluationService>>();
        _service = new RAGEvaluationService(_mockTextCompletionService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task EvaluateQueryAsync_ValidInput_ReturnsEvaluationResult()
    {
        // Arrange
        var query = "머신러닝이란 무엇인가요?";
        var retrievedChunks = CreateMockDocumentChunks();
        var generatedAnswer = "머신러닝은 인공지능의 한 분야입니다.";
        var goldenItem = CreateMockGoldenDatasetItem();

        _mockTextCompletionService
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("0.8");

        // Act
        var result = await _service.EvaluateQueryAsync(
            query, retrievedChunks, generatedAnswer, goldenItem);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.Query);
        Assert.Equal(goldenItem.Id, result.QueryId);
        Assert.True(result.Precision >= 0 && result.Precision <= 1);
        Assert.True(result.Recall >= 0 && result.Recall <= 1);
        Assert.True(result.F1Score >= 0 && result.F1Score <= 1);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task CalculateRetrievalMetricsAsync_PerfectMatch_ReturnsMaxScores()
    {
        // Arrange
        var retrievedChunks = CreateMockDocumentChunks();
        var relevantChunkIds = retrievedChunks.Select(c => c.Id).ToList();

        // Act
        var metrics = await _service.CalculateRetrievalMetricsAsync(
            retrievedChunks, relevantChunkIds, relevantChunkIds.Count);

        // Assert
        Assert.Equal(1.0, metrics["precision"], 2);
        Assert.Equal(1.0, metrics["recall"], 2);
        Assert.Equal(1.0, metrics["f1_score"], 2);
        Assert.Equal(1.0, metrics["hit_rate"], 2);
        Assert.True(metrics["mrr"] > 0);
        Assert.True(metrics["ndcg"] > 0);
    }

    [Fact]
    public async Task CalculateRetrievalMetricsAsync_NoRelevantRetrieved_ReturnsZeroScores()
    {
        // Arrange
        var retrievedChunks = CreateMockDocumentChunks();
        var relevantChunkIds = new[] { "chunk_100", "chunk_101", "chunk_102" };

        // Act
        var metrics = await _service.CalculateRetrievalMetricsAsync(
            retrievedChunks, relevantChunkIds, relevantChunkIds.Length);

        // Assert
        Assert.Equal(0.0, metrics["precision"]);
        Assert.Equal(0.0, metrics["recall"]);
        Assert.Equal(0.0, metrics["f1_score"]);
        Assert.Equal(0.0, metrics["hit_rate"]);
        Assert.Equal(0.0, metrics["mrr"]);
        Assert.Equal(0.0, metrics["ndcg"]);
    }

    [Fact]
    public async Task EvaluateAnswerQualityAsync_ValidInput_ReturnsScores()
    {
        // Arrange
        var query = "인공지능이란 무엇인가요?";
        var generatedAnswer = "인공지능은 기계가 인간의 지능을 모방하는 기술입니다.";
        var sourceChunks = CreateMockDocumentChunks();
        var expectedAnswer = "인공지능은 컴퓨터가 인간의 지능을 시뮬레이션하는 분야입니다.";

        _mockTextCompletionService
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("0.85");

        // Act
        var metrics = await _service.EvaluateAnswerQualityAsync(
            query, generatedAnswer, sourceChunks, expectedAnswer);

        // Assert
        Assert.True(metrics.ContainsKey("faithfulness"));
        Assert.True(metrics.ContainsKey("answer_relevancy"));
        Assert.True(metrics["faithfulness"] >= 0 && metrics["faithfulness"] <= 1);
        Assert.True(metrics["answer_relevancy"] >= 0 && metrics["answer_relevancy"] <= 1);
    }

    [Fact]
    public async Task EvaluateContextQualityAsync_ValidInput_ReturnsScores()
    {
        // Arrange
        var query = "인공지능이란 무엇인가요?";
        var retrievedChunks = CreateMockDocumentChunks();
        var relevantChunkIds = retrievedChunks.Take(2).Select(c => c.Id).ToList();

        // Act
        var metrics = await _service.EvaluateContextQualityAsync(
            query, retrievedChunks, relevantChunkIds);

        // Assert
        Assert.True(metrics.ContainsKey("context_relevancy"));
        Assert.True(metrics.ContainsKey("context_precision"));
        Assert.True(metrics.ContainsKey("context_recall"));
        Assert.True(metrics["context_precision"] >= 0 && metrics["context_precision"] <= 1);
        Assert.True(metrics["context_recall"] >= 0 && metrics["context_recall"] <= 1);
    }

    [Fact]
    public async Task EvaluateBatchAsync_ValidDataset_ReturnsAggregatedResults()
    {
        // Arrange
        var dataset = CreateMockGoldenDataset(5);
        var configuration = new EvaluationConfiguration
        {
            EnableFaithfulnessEvaluation = false, // LLM 호출 비활성화
            EnableAnswerRelevancyEvaluation = false,
            EnableContextEvaluation = true
        };

        // Act
        var result = await _service.EvaluateBatchAsync(dataset, configuration);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.TotalQueries);
        Assert.Equal(5, result.SuccessfulQueries);
        Assert.Equal(0, result.FailedQueries);
        Assert.Equal(1.0, result.SuccessRate);
        Assert.True(result.AveragePrecision >= 0);
        Assert.True(result.AverageRecall >= 0);
        Assert.True(result.TotalDuration > TimeSpan.Zero);
    }

    [Fact]
    public async Task ValidateQualityThresholdsAsync_PassingResults_ReturnsTrue()
    {
        // Arrange
        var result = new BatchEvaluationResult
        {
            AveragePrecision = 0.8,
            AverageRecall = 0.8,
            AverageF1Score = 0.8,
            AverageMRR = 0.8,
            AverageNDCG = 0.8,
            AverageHitRate = 0.9,
            AverageFaithfulness = 0.85,
            AverageAnswerRelevancy = 0.8,
            AverageContextRelevancy = 0.8,
            AverageQueryDuration = 500
        };

        var thresholds = new QualityThresholds
        {
            MinPrecision = 0.7,
            MinRecall = 0.7,
            MinF1Score = 0.7,
            MinMRR = 0.7,
            MinNDCG = 0.7,
            MinHitRate = 0.8,
            MinFaithfulness = 0.8,
            MinAnswerRelevancy = 0.7,
            MinContextRelevancy = 0.7,
            MaxAcceptableLatency = 1000
        };

        // Act
        var passed = await _service.ValidateQualityThresholdsAsync(result, thresholds);

        // Assert
        Assert.True(passed);
    }

    [Fact]
    public async Task ValidateQualityThresholdsAsync_FailingResults_ReturnsFalse()
    {
        // Arrange
        var result = new BatchEvaluationResult
        {
            AveragePrecision = 0.6, // 임계값 미달
            AverageRecall = 0.8,
            AverageF1Score = 0.8,
            AverageMRR = 0.8,
            AverageNDCG = 0.8,
            AverageHitRate = 0.9,
            AverageFaithfulness = 0.85,
            AverageAnswerRelevancy = 0.8,
            AverageContextRelevancy = 0.8,
            AverageQueryDuration = 500
        };

        var thresholds = new QualityThresholds
        {
            MinPrecision = 0.7 // 임계값
        };

        // Act
        var passed = await _service.ValidateQualityThresholdsAsync(result, thresholds);

        // Assert
        Assert.False(passed);
    }

    [Fact]
    public async Task CompareEvaluationResultsAsync_BetterCandidate_ShowsImprovement()
    {
        // Arrange
        var baseline = new BatchEvaluationResult
        {
            AveragePrecision = 0.7,
            AverageRecall = 0.7,
            AverageF1Score = 0.7
        };

        var candidate = new BatchEvaluationResult
        {
            AveragePrecision = 0.8,
            AverageRecall = 0.8,
            AverageF1Score = 0.8
        };

        // Act
        var comparison = await _service.CompareEvaluationResultsAsync(baseline, candidate);

        // Assert
        Assert.Equal(0.1, (double)comparison["precision_improvement"], 2);
        Assert.Equal(0.1, (double)comparison["recall_improvement"], 2);
        Assert.Equal(0.1, (double)comparison["f1_improvement"], 2);
        Assert.True((double)comparison["overall_improvement"] > 0);
        Assert.True((bool)comparison["is_better"]);
    }

    #region Helper Methods

    private List<DocumentChunk> CreateMockDocumentChunks()
    {
        return new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = "chunk_1",
                Content = "머신러닝은 인공지능의 한 분야로, 컴퓨터가 데이터를 통해 학습하는 기술입니다.",
                DocumentId = "doc_1",
                StartPosition = 0,
                EndPosition = 100,
                Embedding = new float[384]
            },
            new DocumentChunk
            {
                Id = "chunk_2",
                Content = "딥러닝은 머신러닝의 하위 분야로, 신경망을 사용합니다.",
                DocumentId = "doc_1",
                StartPosition = 100,
                EndPosition = 200,
                Embedding = new float[384]
            },
            new DocumentChunk
            {
                Id = "chunk_3",
                Content = "자연어 처리는 컴퓨터가 인간의 언어를 이해하고 처리하는 분야입니다.",
                DocumentId = "doc_2",
                StartPosition = 0,
                EndPosition = 100,
                Embedding = new float[384]
            }
        };
    }

    private GoldenDatasetItem CreateMockGoldenDatasetItem()
    {
        return new GoldenDatasetItem
        {
            Id = "query_1",
            Query = "머신러닝이란 무엇인가요?",
            ExpectedAnswer = "머신러닝은 인공지능의 한 분야로, 컴퓨터가 데이터를 통해 자동으로 학습하고 예측하는 기술입니다.",
            RelevantChunkIds = new List<string> { "chunk_1", "chunk_2" },
            Weight = 1.0,
            Difficulty = EvaluationDifficulty.Medium,
            Categories = new List<string> { "기술", "인공지능" }
        };
    }

    private List<GoldenDatasetItem> CreateMockGoldenDataset(int count)
    {
        var dataset = new List<GoldenDatasetItem>();

        for (int i = 1; i <= count; i++)
        {
            dataset.Add(new GoldenDatasetItem
            {
                Id = $"query_{i}",
                Query = $"테스트 쿼리 {i}",
                ExpectedAnswer = $"테스트 답변 {i}",
                RelevantChunkIds = new List<string> { $"chunk_{i}", $"chunk_{i + 10}" },
                Weight = 1.0,
                Difficulty = EvaluationDifficulty.Medium,
                Categories = new List<string> { "테스트" }
            });
        }

        return dataset;
    }

    #endregion
}