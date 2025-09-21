using FluxIndex.Core.Interfaces;
using FluxIndex.Domain.Models;
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
public class GoldenDatasetManager : IGoldenDatasetManager
{
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
                _logger.LogWarning("데이터셋 파일이 존재하지 않습니다: {FilePath}", filePath);
                return Enumerable.Empty<GoldenDatasetItem>();
            }

            _logger.LogInformation("데이터셋 로드 시작: {DatasetId}", datasetId);

            var jsonContent = await File.ReadAllTextAsync(filePath, cancellationToken);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            var dataset = JsonSerializer.Deserialize<List<GoldenDatasetItem>>(jsonContent, options) ?? new List<GoldenDatasetItem>();

            _logger.LogInformation("데이터셋 로드 완료: {DatasetId}, Items={ItemCount}", datasetId, dataset.Count);

            return dataset;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "데이터셋 로드 중 오류 발생: {DatasetId}", datasetId);
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

            _logger.LogInformation("데이터셋 저장 시작: {DatasetId}, Items={ItemCount}", datasetId, datasetList.Count);

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

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            var jsonContent = JsonSerializer.Serialize(datasetList, options);
            await File.WriteAllTextAsync(filePath, jsonContent, cancellationToken);

            _logger.LogInformation("데이터셋 저장 완료: {DatasetId}", datasetId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "데이터셋 저장 중 오류 발생: {DatasetId}", datasetId);
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
            _logger.LogInformation("로그에서 데이터셋 생성 시작: LogCount={LogCount}, MinRelevanceScore={MinRelevanceScore}",
                logs.Count, minRelevanceScore);

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

                    if (relevantChunkIds.Any())
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

            _logger.LogInformation("로그에서 데이터셋 생성 완료: CreatedItems={CreatedItems}", goldenItems.Count);

            return goldenItems;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "로그에서 데이터셋 생성 중 오류 발생");
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

            _logger.LogInformation("데이터셋 검증 시작: TotalItems={TotalItems}", result.TotalItems);

            foreach (var item in datasetList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var itemErrors = ValidateDatasetItem(item);
                if (itemErrors.Any())
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

                if (!item.RelevantChunkIds.Any())
                {
                    warnings.Add($"Item {item.Id}: 관련 청크가 지정되지 않았습니다.");
                }
            }

            result.ValidationErrors = errors;
            result.Warnings = warnings;
            result.ValidItems = validItems;
            result.IsValid = !errors.Any();

            // 카테고리 분포 계산
            result.CategoryDistribution = datasetList
                .SelectMany(item => item.Categories)
                .GroupBy(category => category)
                .ToDictionary(g => g.Key, g => g.Count());

            // 난이도 분포 계산
            result.DifficultyDistribution = datasetList
                .GroupBy(item => item.Difficulty)
                .ToDictionary(g => g.Key, g => g.Count());

            _logger.LogInformation("데이터셋 검증 완료: ValidItems={ValidItems}, Errors={ErrorCount}, Warnings={WarningCount}",
                result.ValidItems, result.ValidationErrors.Count, result.Warnings.Count);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "데이터셋 검증 중 오류 발생");
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
                LastUpdated = datasetList.Any() ? datasetList.Max(item => item.UpdatedAt) : DateTime.MinValue
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

            _logger.LogInformation("데이터셋 통계 정보 조회 완료: {DatasetId}, TotalQueries={TotalQueries}",
                datasetId, statistics.TotalQueries);

            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "데이터셋 통계 정보 조회 중 오류 발생: {DatasetId}", datasetId);
            throw;
        }
    }

    #region Private Helper Methods

    private string GetDatasetPath(string datasetId)
    {
        return Path.Combine(_datasetBasePath, $"{datasetId}.json");
    }

    private List<string> ValidateDatasetItem(GoldenDatasetItem item)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(item.Id))
            errors.Add("ID가 비어있습니다.");

        if (string.IsNullOrWhiteSpace(item.Query))
            errors.Add("쿼리가 비어있습니다.");

        if (item.Weight < 0 || item.Weight > 10)
            errors.Add("가중치는 0과 10 사이여야 합니다.");

        if (!Enum.IsDefined(typeof(EvaluationDifficulty), item.Difficulty))
            errors.Add("유효하지 않은 난이도입니다.");

        return errors;
    }

    private EvaluationDifficulty ClassifyQueryDifficulty(string query)
    {
        // 간단한 휴리스틱 기반 난이도 분류
        var wordCount = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var hasComplexWords = query.Contains("분석") || query.Contains("비교") || query.Contains("설명");
        var hasQuestionWords = query.Contains("왜") || query.Contains("어떻게") || query.Contains("무엇");

        if (wordCount <= 3 && !hasComplexWords)
            return EvaluationDifficulty.Easy;

        if (wordCount <= 7 && !hasComplexWords && hasQuestionWords)
            return EvaluationDifficulty.Medium;

        if (wordCount > 7 || hasComplexWords)
            return EvaluationDifficulty.Hard;

        return EvaluationDifficulty.Medium;
    }

    private List<string> ExtractQueryCategories(string query)
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

        return categories.Any() ? categories : new List<string> { "일반" };
    }

    #endregion
}