using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FluxIndex.Tests.Evaluation;

/// <summary>
/// 골든 데이터셋 매니저 테스트
/// </summary>
public class GoldenDatasetManagerTests : IDisposable
{
    private readonly Mock<ILogger<GoldenDatasetManager>> _mockLogger;
    private readonly string _testDatasetPath;
    private readonly GoldenDatasetManager _manager;

    public GoldenDatasetManagerTests()
    {
        _mockLogger = new Mock<ILogger<GoldenDatasetManager>>();
        _testDatasetPath = Path.Combine(Path.GetTempPath(), "FluxIndexTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDatasetPath);
        _manager = new GoldenDatasetManager(_mockLogger.Object, _testDatasetPath);
    }

    [Fact]
    public async Task SaveAndLoadDatasetAsync_ValidDataset_SuccessfulRoundTrip()
    {
        // Arrange
        var datasetId = "test_dataset";
        var originalDataset = CreateMockGoldenDataset(3);

        // Act - Save
        await _manager.SaveDatasetAsync(datasetId, originalDataset);

        // Act - Load
        var loadedDataset = await _manager.LoadDatasetAsync(datasetId);

        // Assert
        var loadedList = loadedDataset.ToList();
        Assert.Equal(originalDataset.Count, loadedList.Count);

        for (int i = 0; i < originalDataset.Count; i++)
        {
            var original = originalDataset[i];
            var loaded = loadedList.FirstOrDefault(x => x.Id == original.Id);

            Assert.NotNull(loaded);
            Assert.Equal(original.Query, loaded.Query);
            Assert.Equal(original.ExpectedAnswer, loaded.ExpectedAnswer);
            Assert.Equal(original.RelevantChunkIds.Count, loaded.RelevantChunkIds.Count);
            Assert.Equal(original.Difficulty, loaded.Difficulty);
        }
    }

    [Fact]
    public async Task LoadDatasetAsync_NonExistentDataset_ReturnsEmptyCollection()
    {
        // Arrange
        var datasetId = "non_existent_dataset";

        // Act
        var dataset = await _manager.LoadDatasetAsync(datasetId);

        // Assert
        Assert.Empty(dataset);
    }

    [Fact]
    public async Task CreateDatasetFromLogsAsync_ValidLogs_ReturnsFilteredDataset()
    {
        // Arrange
        var queryLogs = CreateMockQueryLogs(5);
        var minRelevanceScore = 0.8;

        // Act
        var dataset = await _manager.CreateDatasetFromLogsAsync(queryLogs, minRelevanceScore);

        // Assert
        var datasetList = dataset.ToList();
        Assert.True(datasetList.Count <= queryLogs.Count);

        foreach (var item in datasetList)
        {
            Assert.NotEmpty(item.Id);
            Assert.NotEmpty(item.Query);
            Assert.NotEmpty(item.ExpectedAnswer);
            Assert.True(item.Weight >= minRelevanceScore);
            Assert.Equal("query_logs", item.Source);
        }
    }

    [Fact]
    public async Task ValidateDatasetAsync_ValidDataset_ReturnsValidResult()
    {
        // Arrange
        var dataset = CreateMockGoldenDataset(3);

        // Act
        var validationResult = await _manager.ValidateDatasetAsync(dataset);

        // Assert
        Assert.True(validationResult.IsValid);
        Assert.Equal(3, validationResult.TotalItems);
        Assert.Equal(3, validationResult.ValidItems);
        Assert.Empty(validationResult.ValidationErrors);
        Assert.True(validationResult.CategoryDistribution.Count > 0);
        Assert.True(validationResult.DifficultyDistribution.Count > 0);
    }

    [Fact]
    public async Task ValidateDatasetAsync_InvalidDataset_ReturnsErrorsAndWarnings()
    {
        // Arrange
        var dataset = new List<GoldenDatasetItem>
        {
            new GoldenDatasetItem
            {
                Id = "", // Invalid: empty ID
                Query = "Valid query",
                ExpectedAnswer = "",  // Warning: empty expected answer
                Weight = 15.0 // Invalid: weight out of range
            },
            new GoldenDatasetItem
            {
                Id = "valid_id",
                Query = "", // Invalid: empty query
                ExpectedAnswer = "Valid answer",
                RelevantChunkIds = new List<string>() // Warning: no relevant chunks
            }
        };

        // Act
        var validationResult = await _manager.ValidateDatasetAsync(dataset);

        // Assert
        Assert.False(validationResult.IsValid);
        Assert.Equal(2, validationResult.TotalItems);
        Assert.Equal(0, validationResult.ValidItems);
        Assert.NotEmpty(validationResult.ValidationErrors);
        Assert.NotEmpty(validationResult.Warnings);
    }

    [Fact]
    public async Task GetDatasetStatisticsAsync_ExistingDataset_ReturnsStatistics()
    {
        // Arrange
        var datasetId = "stats_test_dataset";
        var dataset = CreateMockGoldenDataset(5);
        await _manager.SaveDatasetAsync(datasetId, dataset);

        // Act
        var statistics = await _manager.GetDatasetStatisticsAsync(datasetId);

        // Assert
        Assert.Equal(datasetId, statistics.DatasetId);
        Assert.Equal(5, statistics.TotalQueries);
        Assert.True(statistics.TotalRelevantDocuments > 0);
        Assert.True(statistics.CategoryCounts.Count > 0);
        Assert.True(statistics.DifficultyCounts.Count > 0);
        Assert.True(statistics.AverageQueriesPerDocument > 0);
    }

    [Fact]
    public async Task GetDatasetStatisticsAsync_NonExistentDataset_ReturnsEmptyStatistics()
    {
        // Arrange
        var datasetId = "non_existent_stats_dataset";

        // Act
        var statistics = await _manager.GetDatasetStatisticsAsync(datasetId);

        // Assert
        Assert.Equal(datasetId, statistics.DatasetId);
        Assert.Equal(0, statistics.TotalQueries);
        Assert.Equal(0, statistics.TotalRelevantDocuments);
        Assert.Empty(statistics.CategoryCounts);
        Assert.Empty(statistics.DifficultyCounts);
    }

    [Fact]
    public async Task SaveDatasetAsync_UpdatesTimestamps_CorrectTimestamps()
    {
        // Arrange
        var datasetId = "timestamp_test_dataset";
        var dataset = CreateMockGoldenDataset(2);
        var originalCreatedAt = dataset[0].CreatedAt;

        // Wait a small amount to ensure timestamp difference
        await Task.Delay(10);

        // Act
        await _manager.SaveDatasetAsync(datasetId, dataset);
        var loadedDataset = await _manager.LoadDatasetAsync(datasetId);

        // Assert
        var loadedList = loadedDataset.ToList();
        foreach (var item in loadedList)
        {
            Assert.True(item.UpdatedAt > originalCreatedAt);
            if (originalCreatedAt == default)
            {
                Assert.True(item.CreatedAt > originalCreatedAt);
            }
        }
    }

    #region Helper Methods

    private List<GoldenDatasetItem> CreateMockGoldenDataset(int count)
    {
        var dataset = new List<GoldenDatasetItem>();

        for (int i = 1; i <= count; i++)
        {
            dataset.Add(new GoldenDatasetItem
            {
                Id = $"item_{i}",
                Query = $"테스트 쿼리 {i}: 머신러닝에 대해 설명해주세요.",
                ExpectedAnswer = $"머신러닝은 테스트 답변 {i}입니다.",
                RelevantChunkIds = new List<string> { $"chunk_{i}", $"chunk_{i + 10}" },
                Weight = 1.0,
                Difficulty = i % 2 == 0 ? EvaluationDifficulty.Easy : EvaluationDifficulty.Medium,
                Categories = new List<string> { "기술", "테스트" },
                Source = "unit_test",
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                Metadata = new Dictionary<string, object>
                {
                    ["test_index"] = i,
                    ["is_synthetic"] = true
                }
            });
        }

        return dataset;
    }

    private List<QueryLog> CreateMockQueryLogs(int count)
    {
        var logs = new List<QueryLog>();

        for (int i = 1; i <= count; i++)
        {
            logs.Add(new QueryLog
            {
                Id = $"log_{i}",
                Query = $"테스트 쿼리 {i}",
                Timestamp = DateTime.UtcNow.AddHours(-i),
                RetrievedChunkIds = new List<string> { $"chunk_{i}", $"chunk_{i + 10}" },
                RelevanceScores = new List<double> { 0.9, 0.8 }, // High relevance scores
                GeneratedAnswer = $"테스트 생성 답변 {i}",
                UserRating = 0.85, // Above threshold
                UserAccepted = true
            });
        }

        // Add one low-quality log that should be filtered out
        logs.Add(new QueryLog
        {
            Id = "log_low_quality",
            Query = "낮은 품질 쿼리",
            UserRating = 0.3, // Below threshold
            UserAccepted = false,
            RelevanceScores = new List<double> { 0.3, 0.2 }
        });

        return logs;
    }

    #endregion

    public void Dispose()
    {
        if (Directory.Exists(_testDatasetPath))
        {
            Directory.Delete(_testDatasetPath, true);
        }
    }
}