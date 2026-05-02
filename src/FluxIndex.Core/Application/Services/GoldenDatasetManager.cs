using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Services;

/// <summary>
/// 골든 데이터셋 관리 서비스
/// </summary>
public partial class GoldenDatasetManager : IGoldenDatasetManager
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ILogger<GoldenDatasetManager> _logger;
    private readonly string _datasetBasePath;

    public GoldenDatasetManager(ILogger<GoldenDatasetManager> logger, string? datasetBasePath = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _datasetBasePath = datasetBasePath ?? Path.Combine(Directory.GetCurrentDirectory(), "datasets");

        // 데이터셋 디렉토리 생성
        if (!Directory.Exists(_datasetBasePath))
        {
            Directory.CreateDirectory(_datasetBasePath);
        }
    }

    /// <summary>
    /// 골든 데이터셋 로드
    /// </summary>
    public async Task<IEnumerable<GoldenDatasetItem>> LoadDatasetAsync(
        string datasetId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = GetDatasetPath(datasetId);

            if (!File.Exists(filePath))
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    LogGoldenDataset15(_logger, filePath);
                return Enumerable.Empty<GoldenDatasetItem>();
            }

            if (_logger.IsEnabled(LogLevel.Warning))
                LogGoldenDataset14(_logger, datasetId);

            var jsonContent = await File.ReadAllTextAsync(filePath, cancellationToken);

            var dataset = JsonSerializer.Deserialize<List<GoldenDatasetItem>>(jsonContent, s_jsonOptions) ?? new List<GoldenDatasetItem>();

            if (_logger.IsEnabled(LogLevel.Warning))
                LogGoldenDataset13(_logger, datasetId, dataset.Count);

            return dataset;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                LogGoldenDataset12(_logger, ex, datasetId);
            throw;
        }
    }

    /// <summary>
    /// 골든 데이터셋 저장
    /// </summary>
    public async Task SaveDatasetAsync(
        string datasetId,
        IEnumerable<GoldenDatasetItem> dataset,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = GetDatasetPath(datasetId);
            var datasetList = dataset.ToList();

            if (_logger.IsEnabled(LogLevel.Warning))
                LogGoldenDataset11(_logger, datasetId, datasetList.Count);

            // 업데이트 시간 설정
            var now = DateTime.UtcNow;
            foreach (var item in datasetList)
            {
                item.UpdatedAt = now;
                if (item.CreatedAt == default)
                {
                    item.CreatedAt = now;
                }
            }

            var jsonContent = JsonSerializer.Serialize(datasetList, s_jsonOptions);
            await File.WriteAllTextAsync(filePath, jsonContent, cancellationToken);

            if (_logger.IsEnabled(LogLevel.Warning))
                LogGoldenDataset10(_logger, datasetId);
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                LogGoldenDataset9(_logger, ex, datasetId);
            throw;
        }
    }

    /// <summary>
    /// 데이터셋 생성 (기존 검색 로그에서)
    /// </summary>
    public async Task<IEnumerable<GoldenDatasetItem>> CreateDatasetFromLogsAsync(
        IEnumerable<QueryLog> queryLogs,
        double minRelevanceScore = 0.8,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var logs = queryLogs.ToList();
            if (_logger.IsEnabled(LogLevel.Warning))
                LogGoldenDataset8(_logger, logs.Count, minRelevanceScore);

            var goldenItems = new List<GoldenDatasetItem>();

            foreach (var log in logs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 높은 사용자 평점과 관련성 점수가 있는 로그만 선택
                if (log.UserRating >= minRelevanceScore && log.UserAccepted)
                {
                    var relevantChunkIds = log.RetrievedChunkIds
                        .Where((id, index) => index < log.RelevanceScores.Count && log.RelevanceScores[index] >= minRelevanceScore)
                        .ToList();

                    if (relevantChunkIds.Count != 0)
                    {
                        var goldenItem = new GoldenDatasetItem
                        {
                            Id = Guid.NewGuid().ToString(),
                            Query = log.Query,
                            ExpectedAnswer = log.GeneratedAnswer,
                            RelevantChunkIds = relevantChunkIds,
                            Weight = log.UserRating,
                            Difficulty = ClassifyQueryDifficulty(log.Query),
                            Categories = ExtractQueryCategories(log.Query),
                            Source = "query_logs",
                            CreatedAt = log.Timestamp,
                            Metadata = new Dictionary<string, object>
                            {
                                ["original_log_id"] = log.Id,
                                ["user_rating"] = log.UserRating,
                                ["relevance_scores"] = log.RelevanceScores
                            }
                        };

                        goldenItems.Add(goldenItem);
                    }
                }
            }

            LogGoldenDataset7(_logger, goldenItems.Count);

            return goldenItems;
        }
        catch (Exception ex)
        {
            LogGoldenDataset6(_logger, ex);
            throw;
        }
    }

    /// <summary>
    /// 데이터셋 검증 및 품질 확인
    /// </summary>
    public async Task<DatasetValidationResult> ValidateDatasetAsync(
        IEnumerable<GoldenDatasetItem> dataset,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var datasetList = dataset.ToList();
            var result = new DatasetValidationResult
            {
                TotalItems = datasetList.Count
            };

            var errors = new List<string>();
            var warnings = new List<string>();
            var validItems = 0;

            if (_logger.IsEnabled(LogLevel.Warning))
                LogGoldenDataset5(_logger, result.TotalItems);

            foreach (var item in datasetList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var itemErrors = ValidateDatasetItem(item);
                if (itemErrors.Count != 0)
                {
                    errors.AddRange(itemErrors.Select(e => $"Item {item.Id}: {e}"));
                }
                else
                {
                    validItems++;
                }

                // 경고 체크
                if (string.IsNullOrWhiteSpace(item.ExpectedAnswer))
                {
                    warnings.Add($"Item {item.Id}: 기대 답변이 비어있습니다.");
                }

                if (item.RelevantChunkIds.Count == 0)
                {
                    warnings.Add($"Item {item.Id}: 관련 청크가 지정되지 않았습니다.");
                }
            }

            result.ValidationErrors = errors;
            result.Warnings = warnings;
            result.ValidItems = validItems;
            result.IsValid = errors.Count == 0;

            // 카테고리 분포 계산
            result.CategoryDistribution = datasetList
                .SelectMany(item => item.Categories)
                .GroupBy(category => category)
                .ToDictionary(g => g.Key, g => g.Count());

            // 난이도 분포 계산
            result.DifficultyDistribution = datasetList
                .GroupBy(item => item.Difficulty)
                .ToDictionary(g => g.Key, g => g.Count());

            if (_logger.IsEnabled(LogLevel.Warning))
                LogGoldenDataset4(_logger, result.ValidItems, result.ValidationErrors.Count, result.Warnings.Count);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            LogGoldenDataset3(_logger, ex);
            throw;
        }
    }

    /// <summary>
    /// 데이터셋 통계 정보
    /// </summary>
    public async Task<DatasetStatistics> GetDatasetStatisticsAsync(
        string datasetId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var dataset = await LoadDatasetAsync(datasetId, cancellationToken);
            var datasetList = dataset.ToList();

            var statistics = new DatasetStatistics
            {
                DatasetId = datasetId,
                TotalQueries = datasetList.Count,
                TotalRelevantDocuments = datasetList.SelectMany(item => item.RelevantChunkIds).Distinct().Count(),
                LastUpdated = datasetList.Count != 0 ? datasetList.Max(item => item.UpdatedAt) : DateTime.MinValue
            };

            // 카테고리별 개수
            statistics.CategoryCounts = datasetList
                .SelectMany(item => item.Categories)
                .GroupBy(category => category)
                .ToDictionary(g => g.Key, g => g.Count());

            // 난이도별 개수
            statistics.DifficultyCounts = datasetList
                .GroupBy(item => item.Difficulty)
                .ToDictionary(g => g.Key, g => g.Count());

            // 문서당 평균 쿼리 수
            if (statistics.TotalRelevantDocuments > 0)
            {
                statistics.AverageQueriesPerDocument = (double)statistics.TotalQueries / statistics.TotalRelevantDocuments;
            }

            if (_logger.IsEnabled(LogLevel.Warning))
                LogGoldenDataset2(_logger, datasetId, statistics.TotalQueries);

            return statistics;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                LogGoldenDataset1(_logger, ex, datasetId);
            throw;
        }
    }

    #region Private Helper Methods

    private string GetDatasetPath(string datasetId)
    {
        return Path.Combine(_datasetBasePath, $"{datasetId}.json");
    }

    private static List<string> ValidateDatasetItem(GoldenDatasetItem item)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(item.Id))
            errors.Add("ID가 비어있습니다.");

        if (string.IsNullOrWhiteSpace(item.Query))
            errors.Add("쿼리가 비어있습니다.");

        if (item.Weight < 0 || item.Weight > 10)
            errors.Add("가중치는 0과 10 사이여야 합니다.");

        if (!Enum.IsDefined(item.Difficulty))
            errors.Add("유효하지 않은 난이도입니다.");

        return errors;
    }

    private static EvaluationDifficulty ClassifyQueryDifficulty(string query)
    {
        // 간단한 휴리스틱 기반 난이도 분류
        var wordCount = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var hasComplexWords = query.Contains("분석") || query.Contains("비교") || query.Contains("설명");
        var hasQuestionWords = query.Contains('왜') || query.Contains("어떻게") || query.Contains("무엇");

        if (wordCount <= 3 && !hasComplexWords)
            return EvaluationDifficulty.Easy;

        if (wordCount <= 7 && !hasComplexWords && hasQuestionWords)
            return EvaluationDifficulty.Medium;

        if (wordCount > 7 || hasComplexWords)
            return EvaluationDifficulty.Hard;

        return EvaluationDifficulty.Medium;
    }

    private static List<string> ExtractQueryCategories(string query)
    {
        var categories = new List<string>();

        // 간단한 키워드 기반 카테고리 분류
        var categoryKeywords = new Dictionary<string, string[]>
        {
            ["기술"] = new[] { "기술", "프로그래밍", "개발", "시스템", "알고리즘" },
            ["비즈니스"] = new[] { "비즈니스", "마케팅", "경영", "전략", "수익" },
            ["과학"] = new[] { "과학", "연구", "실험", "데이터", "분석" },
            ["일반"] = new[] { "일반", "정보", "설명", "개념", "정의" }
        };

        foreach (var category in categoryKeywords)
        {
            if (category.Value.Any(keyword => query.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                categories.Add(category.Key);
            }
        }

        return categories.Count != 0 ? categories : new List<string> { "일반" };
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dataset file not found: {FilePath}")]
    private static partial void LogGoldenDataset15(ILogger logger, string filePath);
    [LoggerMessage(Level = LogLevel.Information, Message = "Dataset load started: {DatasetId}")]
    private static partial void LogGoldenDataset14(ILogger logger, string datasetId);
    [LoggerMessage(Level = LogLevel.Information, Message = "Dataset loaded: {DatasetId}, Items={ItemCount}")]
    private static partial void LogGoldenDataset13(ILogger logger, string datasetId, int itemCount);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading dataset: {DatasetId}")]
    private static partial void LogGoldenDataset12(ILogger logger, Exception exception, string datasetId);
    [LoggerMessage(Level = LogLevel.Information, Message = "Dataset save started: {DatasetId}, Items={ItemCount}")]
    private static partial void LogGoldenDataset11(ILogger logger, string datasetId, int itemCount);
    [LoggerMessage(Level = LogLevel.Information, Message = "Dataset saved: {DatasetId}")]
    private static partial void LogGoldenDataset10(ILogger logger, string datasetId);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error saving dataset: {DatasetId}")]
    private static partial void LogGoldenDataset9(ILogger logger, Exception exception, string datasetId);
    [LoggerMessage(Level = LogLevel.Information, Message = "Dataset generation from logs started: LogCount={LogCount}, MinRelevanceScore={MinRelevanceScore}")]
    private static partial void LogGoldenDataset8(ILogger logger, int logCount, double minRelevanceScore);
    [LoggerMessage(Level = LogLevel.Information, Message = "Dataset generation from logs completed: CreatedItems={CreatedItems}")]
    private static partial void LogGoldenDataset7(ILogger logger, int createdItems);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error generating dataset from logs")]
    private static partial void LogGoldenDataset6(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Information, Message = "Dataset validation started: TotalItems={TotalItems}")]
    private static partial void LogGoldenDataset5(ILogger logger, int totalItems);
    [LoggerMessage(Level = LogLevel.Information, Message = "Dataset validation completed: ValidItems={ValidItems}, Errors={ErrorCount}, Warnings={WarningCount}")]
    private static partial void LogGoldenDataset4(ILogger logger, int validItems, int errorCount, int warningCount);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error validating dataset")]
    private static partial void LogGoldenDataset3(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Information, Message = "Dataset statistics retrieved: {DatasetId}, TotalQueries={TotalQueries}")]
    private static partial void LogGoldenDataset2(ILogger logger, string datasetId, int totalQueries);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error retrieving dataset statistics: {DatasetId}")]
    private static partial void LogGoldenDataset1(ILogger logger, Exception exception, string datasetId);

    #endregion
}
