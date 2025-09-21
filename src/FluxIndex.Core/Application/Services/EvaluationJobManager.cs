using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// 평가 작업 관리 서비스
/// </summary>
public class EvaluationJobManager : IEvaluationJobManager
{
    private readonly IRAGEvaluationService _evaluationService;
    private readonly IGoldenDatasetManager _datasetManager;
    private readonly ILogger<EvaluationJobManager> _logger;

    // 인메모리 작업 스토리지 (실제 구현에서는 데이터베이스 사용)
    private readonly ConcurrentDictionary<string, EvaluationJob> _jobs = new();
    private readonly ConcurrentDictionary<string, BatchEvaluationResult> _results = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();

    public EvaluationJobManager(
        IRAGEvaluationService evaluationService,
        IGoldenDatasetManager datasetManager,
        ILogger<EvaluationJobManager> logger)
    {
        _evaluationService = evaluationService ?? throw new ArgumentNullException(nameof(evaluationService));
        _datasetManager = datasetManager ?? throw new ArgumentNullException(nameof(datasetManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 평가 작업 생성
    /// </summary>
    public async Task<string> CreateEvaluationJobAsync(
        string name,
        string datasetId,
        EvaluationConfiguration configuration,
        QualityThresholds thresholds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(1, cancellationToken); // 비동기 시뮬레이션

            var jobId = Guid.NewGuid().ToString();
            var job = new EvaluationJob
            {
                JobId = jobId,
                Name = name,
                Status = EvaluationStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                DatasetId = datasetId,
                Configuration = configuration,
                Thresholds = thresholds,
                Progress = 0
            };

            _jobs.TryAdd(jobId, job);

            _logger.LogInformation("평가 작업 생성 완료: JobId={JobId}, Name={Name}, Dataset={Dataset}",
                jobId, name, datasetId);

            return jobId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "평가 작업 생성 중 오류 발생: Name={Name}", name);
            throw;
        }
    }

    /// <summary>
    /// 평가 작업 실행
    /// </summary>
    public async Task<BatchEvaluationResult> ExecuteEvaluationJobAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            throw new ArgumentException($"작업을 찾을 수 없습니다: {jobId}");
        }

        if (job.Status != EvaluationStatus.Pending)
        {
            throw new InvalidOperationException($"작업이 이미 실행 중이거나 완료되었습니다: {jobId}");
        }

        // 취소 토큰 생성 및 저장
        var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationTokens.TryAdd(jobId, combinedCts);

        try
        {
            // 작업 상태 업데이트
            job.Status = EvaluationStatus.Running;
            job.StartedAt = DateTime.UtcNow;
            job.Progress = 0;

            _logger.LogInformation("평가 작업 실행 시작: JobId={JobId}, Name={Name}",
                jobId, job.Name);

            // 골든 데이터셋 로드
            var dataset = await _datasetManager.LoadDatasetAsync(job.DatasetId, combinedCts.Token);
            if (!dataset.Any())
            {
                throw new InvalidOperationException($"데이터셋이 비어있습니다: {job.DatasetId}");
            }

            job.Progress = 10;

            // 진행률 추적을 위한 Progress Reporter 생성
            var progressReporter = new Progress<int>(progress =>
            {
                job.Progress = Math.Max(job.Progress, progress);
                _logger.LogDebug("평가 작업 진행률 업데이트: JobId={JobId}, Progress={Progress}%",
                    jobId, job.Progress);
            });

            // 배치 평가 실행
            var result = await ExecuteBatchEvaluationWithProgressAsync(
                dataset,
                job.Configuration,
                progressReporter,
                combinedCts.Token);

            // 작업 완료 처리
            job.Status = EvaluationStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.Progress = 100;

            // 결과 저장
            _results.TryAdd(jobId, result);

            _logger.LogInformation("평가 작업 실행 완료: JobId={JobId}, TotalQueries={TotalQueries}, Duration={Duration}",
                jobId, result.TotalQueries, result.TotalDuration);

            return result;
        }
        catch (OperationCanceledException)
        {
            job.Status = EvaluationStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = "작업이 취소되었습니다.";

            _logger.LogInformation("평가 작업 취소됨: JobId={JobId}", jobId);
            throw;
        }
        catch (Exception ex)
        {
            job.Status = EvaluationStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = ex.Message;

            _logger.LogError(ex, "평가 작업 실행 중 오류 발생: JobId={JobId}", jobId);
            throw;
        }
        finally
        {
            // 정리
            _cancellationTokens.TryRemove(jobId, out var cts);
            cts?.Dispose();
        }
    }

    /// <summary>
    /// 평가 작업 상태 확인
    /// </summary>
    public async Task<EvaluationJob> GetJobStatusAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken); // 비동기 시뮬레이션

        if (!_jobs.TryGetValue(jobId, out var job))
        {
            throw new ArgumentException($"작업을 찾을 수 없습니다: {jobId}");
        }

        return job;
    }

    /// <summary>
    /// 실행 중인 작업 취소
    /// </summary>
    public async Task CancelJobAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken); // 비동기 시뮬레이션

        if (!_jobs.TryGetValue(jobId, out var job))
        {
            throw new ArgumentException($"작업을 찾을 수 없습니다: {jobId}");
        }

        if (job.Status != EvaluationStatus.Running)
        {
            throw new InvalidOperationException($"실행 중이지 않은 작업은 취소할 수 없습니다: {jobId}");
        }

        // 취소 토큰 활성화
        if (_cancellationTokens.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
        }

        _logger.LogInformation("평가 작업 취소 요청: JobId={JobId}", jobId);
    }

    /// <summary>
    /// 작업 목록 조회
    /// </summary>
    public async Task<IEnumerable<EvaluationJob>> GetJobsAsync(
        EvaluationStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken); // 비동기 시뮬레이션

        var jobs = _jobs.Values.AsEnumerable();

        // 상태 필터
        if (status.HasValue)
        {
            jobs = jobs.Where(j => j.Status == status.Value);
        }

        // 날짜 범위 필터
        if (from.HasValue)
        {
            jobs = jobs.Where(j => j.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            jobs = jobs.Where(j => j.CreatedAt <= to.Value);
        }

        return jobs.OrderByDescending(j => j.CreatedAt).ToList();
    }

    #region Private Helper Methods

    private async Task<BatchEvaluationResult> ExecuteBatchEvaluationWithProgressAsync(
        IEnumerable<GoldenDatasetItem> dataset,
        EvaluationConfiguration configuration,
        IProgress<int> progressReporter,
        CancellationToken cancellationToken)
    {
        var datasetList = dataset.ToList();
        var batchResult = new BatchEvaluationResult
        {
            BatchId = Guid.NewGuid().ToString(),
            StartedAt = DateTime.UtcNow,
            TotalQueries = datasetList.Count,
            Configuration = System.Text.Json.JsonSerializer.Serialize(configuration)
        };

        var results = new List<RAGEvaluationResult>();
        var failedCount = 0;

        var processedCount = 0;
        var basePrgress = 10; // 데이터셋 로드 완료

        foreach (var item in datasetList)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // TODO: 실제 검색 및 답변 생성 로직 호출
                var mockRetrievedChunks = CreateMockRetrievedChunks(item);
                var mockGeneratedAnswer = $"Mock answer for: {item.Query}";

                var result = await _evaluationService.EvaluateQueryAsync(
                    item.Query,
                    mockRetrievedChunks,
                    mockGeneratedAnswer,
                    item,
                    configuration,
                    cancellationToken);

                results.Add(result);
                processedCount++;

                // 진행률 업데이트 (10% ~ 90%)
                var progress = basePrgress + (int)((double)processedCount / datasetList.Count * 80);
                progressReporter?.Report(progress);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "개별 쿼리 평가 실패: QueryId={QueryId}", item.Id);
                failedCount++;
            }
        }

        // 집계 계산
        batchResult.Results = results;
        batchResult.SuccessfulQueries = results.Count;
        batchResult.FailedQueries = failedCount;
        batchResult.SuccessRate = batchResult.TotalQueries > 0
            ? (double)batchResult.SuccessfulQueries / batchResult.TotalQueries
            : 0.0;

        if (results.Any())
        {
            batchResult.AveragePrecision = results.Average(r => r.Precision);
            batchResult.AverageRecall = results.Average(r => r.Recall);
            batchResult.AverageF1Score = results.Average(r => r.F1Score);
            batchResult.AverageMRR = results.Average(r => r.MRR);
            batchResult.AverageNDCG = results.Average(r => r.NDCG);
            batchResult.AverageHitRate = results.Average(r => r.HitRate);
            batchResult.AverageFaithfulness = results.Average(r => r.Faithfulness);
            batchResult.AverageAnswerRelevancy = results.Average(r => r.AnswerRelevancy);
            batchResult.AverageContextRelevancy = results.Average(r => r.ContextRelevancy);
            batchResult.AverageQueryDuration = results.Average(r => r.Duration.TotalMilliseconds);
        }

        batchResult.CompletedAt = DateTime.UtcNow;
        batchResult.TotalDuration = batchResult.CompletedAt - batchResult.StartedAt;

        // 최종 진행률 업데이트
        progressReporter?.Report(90);

        return batchResult;
    }

    private IEnumerable<DocumentChunk> CreateMockRetrievedChunks(GoldenDatasetItem item)
    {
        // TODO: 실제 검색 시스템 호출로 대체
        var mockChunks = new List<DocumentChunk>();

        for (int i = 0; i < Math.Min(5, item.RelevantChunkIds.Count); i++)
        {
            mockChunks.Add(new DocumentChunk
            {
                Id = item.RelevantChunkIds[i],
                Content = $"Mock content for chunk {item.RelevantChunkIds[i]}",
                DocumentId = $"doc_{i}",
                ChunkIndex = i,
                Embedding = new float[384] // Mock embedding
            });
        }

        return mockChunks;
    }

    #endregion

    /// <summary>
    /// 리소스 정리
    /// </summary>
    public void Dispose()
    {
        foreach (var cts in _cancellationTokens.Values)
        {
            cts?.Cancel();
            cts?.Dispose();
        }
        _cancellationTokens.Clear();
    }
}