using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Evaluation;
using Microsoft.AspNetCore.Mvc;

namespace FluxIndex.Stack.Api.Controllers;

/// <summary>
/// API controller for RAG evaluation operations.
/// Provides endpoints to run evaluation jobs, retrieve results, and manage evaluation datasets.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public partial class EvaluationController : ControllerBase
{
    private readonly IEvaluationJobManager _evaluationJobManager;
    private readonly IGoldenDatasetManager _datasetManager;
    private readonly ILogger<EvaluationController> _logger;

    public EvaluationController(
        IEvaluationJobManager evaluationJobManager,
        IGoldenDatasetManager datasetManager,
        ILogger<EvaluationController> logger)
    {
        _evaluationJobManager = evaluationJobManager;
        _datasetManager = datasetManager;
        _logger = logger;
    }

    /// <summary>
    /// Run a new RAG evaluation job.
    /// </summary>
    /// <param name="request">Evaluation request with queries and configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Evaluation job response with job ID for tracking.</returns>
    [HttpPost("run")]
    [ProducesResponseType(typeof(ApiResponse<EvaluationJobResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RunEvaluation(
        [FromBody] RunEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Queries == null || request.Queries.Count == 0)
        {
            return BadRequest(ApiResponse<object>.Fail("At least one query is required for evaluation."));
        }

        try
        {
            var queryCount = request.Queries.Count;
            LogStartingEvaluationJob(_logger, request.JobName, queryCount);

            // Create golden dataset from request queries
            var datasetId = $"inline_{Guid.NewGuid():N}";
            var datasetItems = request.Queries.Select((q, idx) => new GoldenDatasetItem
            {
                Id = $"q_{idx}",
                Query = q.Query,
                ExpectedAnswer = q.ExpectedAnswer,
                RelevantDocumentIds = q.RelevantDocumentIds ?? new List<string>()
            }).ToList();

            // Save inline dataset temporarily
            await _datasetManager.SaveDatasetAsync(datasetId, datasetItems, cancellationToken);

            // Create evaluation configuration
            var configuration = new EvaluationConfiguration
            {
                MaxRetrievedDocuments = request.TopK,
                EnableFaithfulnessEvaluation = request.GenerateAnswers,
                EnableAnswerRelevancyEvaluation = request.GenerateAnswers,
                EnableContextEvaluation = true,
                MinRelevanceThreshold = 0.5
            };

            var thresholds = new FluxIndex.Core.Domain.Models.QualityThresholds
            {
                MinPrecision = 0.5,
                MinRecall = 0.5,
                MinF1Score = 0.5,
                MinMRR = 0.5,
                MinNDCG = 0.5
            };

            // Create and execute evaluation job
            var jobId = await _evaluationJobManager.CreateEvaluationJobAsync(
                request.JobName,
                datasetId,
                configuration,
                thresholds,
                cancellationToken);

            // Start job execution in background (fire and forget with proper error handling)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _evaluationJobManager.ExecuteEvaluationJobAsync(jobId, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    LogBackgroundEvaluationJobFailed(_logger, ex, jobId);
                }
            }, CancellationToken.None);

            var response = new EvaluationJobResponseDto
            {
                JobId = jobId,
                JobName = request.JobName,
                Status = "Queued",
                TotalQueries = request.Queries.Count,
                CreatedAt = DateTime.UtcNow,
                EstimatedCompletionAt = DateTime.UtcNow.AddMinutes(request.Queries.Count * 0.5)
            };

            LogEvaluationJobCreated(_logger, jobId);
            return Ok(ApiResponse<EvaluationJobResponseDto>.Ok(response));
        }
        catch (Exception ex)
        {
            LogStartEvaluationJobFailed(_logger, ex, request.JobName);
            return BadRequest(ApiResponse<object>.Fail($"Failed to start evaluation: {ex.Message}"));
        }
    }

    /// <summary>
    /// Get evaluation job status and results.
    /// </summary>
    /// <param name="jobId">Evaluation job ID.</param>
    /// <param name="includeQueryResults">Whether to include individual query results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Evaluation result with metrics.</returns>
    [HttpGet("results/{jobId}")]
    [ProducesResponseType(typeof(ApiResponse<EvaluationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetResults(
        string jobId,
        [FromQuery] bool includeQueryResults = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var job = await _evaluationJobManager.GetJobStatusAsync(jobId, cancellationToken);

            var result = new EvaluationResultDto
            {
                JobId = job.JobId,
                JobName = job.Name,
                Status = job.Status.ToString(),
                TotalQueries = job.Progress > 0 ? 100 : 0, // Progress as proxy for query count
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                ErrorMessage = job.ErrorMessage
            };

            if (job.Status == EvaluationStatus.Completed)
            {
                // Try to get detailed results
                var jobs = await _evaluationJobManager.GetJobsAsync(
                    status: EvaluationStatus.Completed,
                    cancellationToken: cancellationToken);

                var completedJob = jobs.FirstOrDefault(j => j.JobId == jobId);
                if (completedJob != null)
                {
                    result.DurationMs = completedJob.CompletedAt.HasValue && completedJob.StartedAt.HasValue
                        ? (long)(completedJob.CompletedAt.Value - completedJob.StartedAt.Value).TotalMilliseconds
                        : null;
                }

                // Note: Full metrics would require accessing BatchEvaluationResult
                // which is stored internally in EvaluationJobManager
                result.Metrics = new EvaluationMetricsDto
                {
                    MRR = 0.0,
                    PrecisionAtK = 0.0,
                    RecallAtK = 0.0,
                    NDCG = 0.0,
                    OverallScore = 0.0,
                    QualityTier = "Unknown"
                };
            }

            return Ok(ApiResponse<EvaluationResultDto>.Ok(result));
        }
        catch (ArgumentException)
        {
            return NotFound(ApiResponse<object>.Fail($"Evaluation job not found: {jobId}"));
        }
        catch (Exception ex)
        {
            LogGetEvaluationResultsFailed(_logger, ex, jobId);
            return BadRequest(ApiResponse<object>.Fail($"Failed to get results: {ex.Message}"));
        }
    }

    /// <summary>
    /// List evaluation jobs with optional filtering.
    /// </summary>
    /// <param name="request">Filter and pagination parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list of evaluation jobs.</returns>
    [HttpGet("jobs")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<EvaluationJobResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListJobs(
        [FromQuery] ListEvaluationJobsRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EvaluationStatus? statusFilter = null;
            if (!string.IsNullOrEmpty(request.Status) &&
                Enum.TryParse<EvaluationStatus>(request.Status, true, out var status))
            {
                statusFilter = status;
            }

            var jobs = await _evaluationJobManager.GetJobsAsync(
                status: statusFilter,
                cancellationToken: cancellationToken);

            var jobList = jobs.ToList();
            var totalCount = jobList.Count;

            // Apply pagination
            var pagedJobs = jobList
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .Select(j => new EvaluationJobResponseDto
                {
                    JobId = j.JobId,
                    JobName = j.Name,
                    Status = j.Status.ToString(),
                    TotalQueries = j.Progress,
                    CreatedAt = j.CreatedAt,
                    EstimatedCompletionAt = j.Status == EvaluationStatus.Running
                        ? DateTime.UtcNow.AddMinutes(5)
                        : null
                })
                .ToList();

            var result = PagedResult<EvaluationJobResponseDto>.Create(
                pagedJobs,
                request.Page,
                request.PageSize,
                totalCount);

            return Ok(ApiResponse<PagedResult<EvaluationJobResponseDto>>.Ok(result));
        }
        catch (Exception ex)
        {
            LogListEvaluationJobsFailed(_logger, ex);
            return BadRequest(ApiResponse<object>.Fail($"Failed to list jobs: {ex.Message}"));
        }
    }

    /// <summary>
    /// Cancel a running evaluation job.
    /// </summary>
    /// <param name="jobId">Evaluation job ID to cancel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cancellation confirmation.</returns>
    [HttpPost("{jobId}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CancelJob(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _evaluationJobManager.CancelJobAsync(jobId, cancellationToken);
            LogEvaluationJobCancelled(_logger, jobId);
            return Ok(ApiResponse<string>.Ok("Evaluation job cancelled successfully."));
        }
        catch (ArgumentException)
        {
            return NotFound(ApiResponse<object>.Fail($"Evaluation job not found: {jobId}"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            LogCancelEvaluationJobFailed(_logger, ex, jobId);
            return BadRequest(ApiResponse<object>.Fail($"Failed to cancel job: {ex.Message}"));
        }
    }

    /// <summary>
    /// Get job status by ID.
    /// </summary>
    /// <param name="jobId">Evaluation job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Job status details.</returns>
    [HttpGet("{jobId}")]
    [ProducesResponseType(typeof(ApiResponse<EvaluationJobResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJobStatus(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var job = await _evaluationJobManager.GetJobStatusAsync(jobId, cancellationToken);

            var response = new EvaluationJobResponseDto
            {
                JobId = job.JobId,
                JobName = job.Name,
                Status = job.Status.ToString(),
                TotalQueries = job.Progress,
                CreatedAt = job.CreatedAt,
                EstimatedCompletionAt = job.Status == EvaluationStatus.Running
                    ? DateTime.UtcNow.AddMinutes(5)
                    : null
            };

            return Ok(ApiResponse<EvaluationJobResponseDto>.Ok(response));
        }
        catch (ArgumentException)
        {
            return NotFound(ApiResponse<object>.Fail($"Evaluation job not found: {jobId}"));
        }
        catch (Exception ex)
        {
            LogGetJobStatusFailed(_logger, ex, jobId);
            return BadRequest(ApiResponse<object>.Fail($"Failed to get job status: {ex.Message}"));
        }
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting evaluation job: {JobName} with {QueryCount} queries")]
    private static partial void LogStartingEvaluationJob(ILogger logger, string jobName, int queryCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Background evaluation job failed: {JobId}")]
    private static partial void LogBackgroundEvaluationJobFailed(ILogger logger, Exception? exception, string jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Evaluation job created: {JobId}")]
    private static partial void LogEvaluationJobCreated(ILogger logger, string jobId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to start evaluation job: {JobName}")]
    private static partial void LogStartEvaluationJobFailed(ILogger logger, Exception? exception, string jobName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get evaluation results: {JobId}")]
    private static partial void LogGetEvaluationResultsFailed(ILogger logger, Exception? exception, string jobId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to list evaluation jobs")]
    private static partial void LogListEvaluationJobsFailed(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Evaluation job cancelled: {JobId}")]
    private static partial void LogEvaluationJobCancelled(ILogger logger, string jobId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to cancel evaluation job: {JobId}")]
    private static partial void LogCancelEvaluationJobFailed(ILogger logger, Exception? exception, string jobId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get job status: {JobId}")]
    private static partial void LogGetJobStatusFailed(ILogger logger, Exception? exception, string jobId);

    #endregion
}
