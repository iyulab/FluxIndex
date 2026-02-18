using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Domain.Entities;
using EvaluationThresholds = FluxIndex.Core.Domain.Models.QualityThresholds;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Services;

/// <summary>
/// 평가 작업 관리 서비스
/// </summary>
public partial class EvaluationJobManager : IEvaluationJobManager
{
    private readonly IRAGEvaluationService _evaluationService;
    private readonly IGoldenDatasetManager _datasetManager;
    private readonly IEvaluationSearchProvider _searchProvider;
    private readonly ILogger<EvaluationJobManager> _logger;

    // In-memory job storage (use database in production)
    private readonly ConcurrentDictionary<string, EvaluationJob> _jobs = new();
    private readonly ConcurrentDictionary<string, BatchEvaluationResult> _results = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();

    public EvaluationJobManager(
        IRAGEvaluationService evaluationService,
        IGoldenDatasetManager datasetManager,
        ILogger<EvaluationJobManager> logger,
        IEvaluationSearchProvider? searchProvider = null)
    {
        _evaluationService = evaluationService ?? throw new ArgumentNullException(nameof(evaluationService));
        _datasetManager = datasetManager ?? throw new ArgumentNullException(nameof(datasetManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _searchProvider = searchProvider ?? new MockEvaluationSearchProvider();
    }

    /// <summary>
    /// Creates an evaluation job.
    /// </summary>
    public async Task<string> CreateEvaluationJobAsync(
        string name,
        string datasetId,
        EvaluationConfiguration configuration,
        EvaluationThresholds thresholds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(1, cancellationToken);

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

            if (_logger.IsEnabled(LogLevel.Information))
                LogEvaluationJob9(_logger, jobId, name, datasetId);

            return jobId;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Information))
                LogEvaluationJob8(_logger, ex, name);
            throw;
        }
    }

    /// <summary>
    /// Executes an evaluation job.
    /// </summary>
    public async Task<BatchEvaluationResult> ExecuteEvaluationJobAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            throw new ArgumentException($"Job not found: {jobId}");
        }

        if (job.Status != EvaluationStatus.Pending)
        {
            throw new InvalidOperationException($"Job already running or completed: {jobId}");
        }

        var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationTokens.TryAdd(jobId, combinedCts);

        try
        {
            job.Status = EvaluationStatus.Running;
            job.StartedAt = DateTime.UtcNow;
            job.Progress = 0;

            if (_logger.IsEnabled(LogLevel.Information))
                LogEvaluationJob7(_logger, jobId, job.Name);

            var dataset = await _datasetManager.LoadDatasetAsync(job.DatasetId, combinedCts.Token);
            if (!dataset.Any())
            {
                throw new InvalidOperationException($"Dataset is empty: {job.DatasetId}");
            }

            job.Progress = 10;

            var progressReporter = new Progress<int>(progress =>
            {
                job.Progress = Math.Max(job.Progress, progress);
                if (_logger.IsEnabled(LogLevel.Information))
                    LogEvaluationJob6(_logger, jobId, job.Progress);
            });

            var result = await ExecuteBatchEvaluationWithProgressAsync(
                dataset,
                job.Configuration,
                progressReporter,
                combinedCts.Token);

            job.Status = EvaluationStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.Progress = 100;

            _results.TryAdd(jobId, result);

            if (_logger.IsEnabled(LogLevel.Information))
                LogEvaluationJob5(_logger, jobId, result.TotalQueries, result.TotalDuration);

            return result;
        }
        catch (OperationCanceledException)
        {
            job.Status = EvaluationStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = "Job was cancelled.";

            if (_logger.IsEnabled(LogLevel.Information))
                LogEvaluationJob4(_logger, jobId);
            throw;
        }
        catch (Exception ex)
        {
            job.Status = EvaluationStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = ex.Message;

            if (_logger.IsEnabled(LogLevel.Information))
                LogEvaluationJob3(_logger, ex, jobId);
            throw;
        }
        finally
        {
            _cancellationTokens.TryRemove(jobId, out var cts);
            cts?.Dispose();
        }
    }

    /// <summary>
    /// Gets job status.
    /// </summary>
    public async Task<EvaluationJob> GetJobStatusAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);

        if (!_jobs.TryGetValue(jobId, out var job))
        {
            throw new ArgumentException($"Job not found: {jobId}");
        }

        return job;
    }

    /// <summary>
    /// Cancels a running job.
    /// </summary>
    public async Task CancelJobAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);

        if (!_jobs.TryGetValue(jobId, out var job))
        {
            throw new ArgumentException($"Job not found: {jobId}");
        }

        if (job.Status != EvaluationStatus.Running)
        {
            throw new InvalidOperationException($"Cannot cancel job that is not running: {jobId}");
        }

        if (_cancellationTokens.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
        }

        if (_logger.IsEnabled(LogLevel.Information))
            LogEvaluationJob2(_logger, jobId);
    }

    /// <summary>
    /// Gets list of jobs.
    /// </summary>
    public async Task<IEnumerable<EvaluationJob>> GetJobsAsync(
        EvaluationStatus? status = null,
        DateTime? from = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);

        var jobs = _jobs.Values.AsEnumerable();

        if (status.HasValue)
        {
            jobs = jobs.Where(j => j.Status == status.Value);
        }

        if (from.HasValue)
        {
            jobs = jobs.Where(j => j.CreatedAt >= from.Value);
        }

        if (toDate.HasValue)
        {
            jobs = jobs.Where(j => j.CreatedAt <= toDate.Value);
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
        var baseProgress = 10;

        foreach (var item in datasetList)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Use pluggable search provider for retrieval and answer generation
                var retrievedChunks = await _searchProvider.RetrieveChunksAsync(
                    item.Query, 5, cancellationToken);
                var generatedAnswer = await _searchProvider.GenerateAnswerAsync(
                    item.Query, retrievedChunks, cancellationToken);

                var result = await _evaluationService.EvaluateQueryAsync(
                    item.Query,
                    retrievedChunks,
                    generatedAnswer,
                    item,
                    configuration,
                    cancellationToken);

                results.Add(result);
                processedCount++;

                var progress = baseProgress + (int)((double)processedCount / datasetList.Count * 80);
                progressReporter?.Report(progress);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogEvaluationJob1(_logger, ex, item.Id);
                failedCount++;
            }
        }

        batchResult.Results = results;
        batchResult.SuccessfulQueries = results.Count;
        batchResult.FailedQueries = failedCount;
        batchResult.SuccessRate = batchResult.TotalQueries > 0
            ? (double)batchResult.SuccessfulQueries / batchResult.TotalQueries
            : 0.0;

        if (results.Count != 0)
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

        progressReporter?.Report(90);

        return batchResult;
    }

    #endregion

    /// <summary>
    /// Disposes resources.
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

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Evaluation job created: JobId={JobId}, Name={Name}, Dataset={Dataset}")]
    private static partial void LogEvaluationJob9(ILogger logger, string jobId, string name, string dataset);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error creating evaluation job: Name={Name}")]
    private static partial void LogEvaluationJob8(ILogger logger, Exception exception, string name);
    [LoggerMessage(Level = LogLevel.Information, Message = "Evaluation job started: JobId={JobId}, Name={Name}")]
    private static partial void LogEvaluationJob7(ILogger logger, string jobId, string name);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Evaluation job progress updated: JobId={JobId}, Progress={Progress}%")]
    private static partial void LogEvaluationJob6(ILogger logger, string jobId, int progress);
    [LoggerMessage(Level = LogLevel.Information, Message = "Evaluation job completed: JobId={JobId}, TotalQueries={TotalQueries}, Duration={Duration}")]
    private static partial void LogEvaluationJob5(ILogger logger, string jobId, int totalQueries, TimeSpan duration);
    [LoggerMessage(Level = LogLevel.Information, Message = "Evaluation job cancelled: JobId={JobId}")]
    private static partial void LogEvaluationJob4(ILogger logger, string jobId);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error executing evaluation job: JobId={JobId}")]
    private static partial void LogEvaluationJob3(ILogger logger, Exception exception, string jobId);
    [LoggerMessage(Level = LogLevel.Information, Message = "Evaluation job cancellation requested: JobId={JobId}")]
    private static partial void LogEvaluationJob2(ILogger logger, string jobId);
    [LoggerMessage(Level = LogLevel.Error, Message = "Individual query evaluation failed: QueryId={QueryId}")]
    private static partial void LogEvaluationJob1(ILogger logger, Exception exception, string queryId);

    #endregion
}
