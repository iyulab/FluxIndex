using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Jobs;
using Microsoft.AspNetCore.Mvc;

namespace FluxIndex.Stack.Api.Controllers;

/// <summary>
/// API controller for indexing job operations.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public partial class JobsController : ControllerBase
{
    private readonly IIndexingService _indexingService;
    private readonly IIndexingJobLogRepository _logRepository;
    private readonly ILogger<JobsController> _logger;

    public JobsController(
        IIndexingService indexingService,
        IIndexingJobLogRepository logRepository,
        ILogger<JobsController> logger)
    {
        _indexingService = indexingService;
        _logRepository = logRepository;
        _logger = logger;
    }

    /// <summary>
    /// Get paginated list of indexing jobs.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<IndexingJobDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetJobs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _indexingService.GetJobsAsync(page, pageSize, status, cancellationToken);
        return Ok(ApiResponse<PagedResult<IndexingJobDto>>.Ok(result));
    }

    /// <summary>
    /// Get job status summary.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApiResponse<JobStatusSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken = default)
    {
        var summary = await _indexingService.GetStatusSummaryAsync(cancellationToken);
        return Ok(ApiResponse<JobStatusSummaryDto>.Ok(summary));
    }

    /// <summary>
    /// Get specific job status by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<IndexingJobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await _indexingService.GetJobStatusAsync(id, cancellationToken);
        if (job == null)
        {
            return NotFound(ApiResponse<object>.Fail($"Job with id '{id}' not found."));
        }
        return Ok(ApiResponse<IndexingJobDto>.Ok(job));
    }

    /// <summary>
    /// Cancel a pending or processing job.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            await _indexingService.CancelJobAsync(id, cancellationToken);
            LogJobCancelled(_logger, id);
            return Ok(ApiResponse<string>.Ok("Job cancelled successfully."));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail($"Job with id '{id}' not found."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Get logs for a specific job.
    /// </summary>
    [HttpGet("{id:guid}/logs")]
    [ProducesResponseType(typeof(ApiResponse<List<IndexingJobLogDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLogs(
        Guid id,
        [FromQuery] string? minLevel = null,
        CancellationToken cancellationToken = default)
    {
        var job = await _indexingService.GetJobStatusAsync(id, cancellationToken);
        if (job == null)
        {
            return NotFound(ApiResponse<object>.Fail($"Job with id '{id}' not found."));
        }

        IndexingJobLogLevel? minLevelEnum = null;
        if (!string.IsNullOrEmpty(minLevel) && Enum.TryParse<IndexingJobLogLevel>(minLevel, true, out var level))
        {
            minLevelEnum = level;
        }

        var logs = await _logRepository.GetByJobIdAsync(id, minLevelEnum, cancellationToken);
        var logDtos = logs.Select(l => new IndexingJobLogDto
        {
            Id = l.Id,
            JobId = l.JobId,
            Level = l.Level.ToString(),
            Message = l.Message,
            Details = l.Details,
            Phase = l.Phase,
            ChunkIndex = l.ChunkIndex,
            CreatedAt = l.CreatedAt
        }).ToList();

        return Ok(ApiResponse<List<IndexingJobLogDto>>.Ok(logDtos));
    }

    /// <summary>
    /// Get job detail with logs.
    /// </summary>
    [HttpGet("{id:guid}/detail")]
    [ProducesResponseType(typeof(ApiResponse<IndexingJobDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await _indexingService.GetJobStatusAsync(id, cancellationToken);
        if (job == null)
        {
            return NotFound(ApiResponse<object>.Fail($"Job with id '{id}' not found."));
        }

        var logs = await _logRepository.GetByJobIdAsync(id, cancellationToken);
        var logDtos = logs.Select(l => new IndexingJobLogDto
        {
            Id = l.Id,
            JobId = l.JobId,
            Level = l.Level.ToString(),
            Message = l.Message,
            Details = l.Details,
            Phase = l.Phase,
            ChunkIndex = l.ChunkIndex,
            CreatedAt = l.CreatedAt
        }).ToList();

        var detail = new IndexingJobDetailDto
        {
            Id = job.Id,
            DocumentId = job.DocumentId,
            DocumentTitle = job.DocumentTitle,
            Status = job.Status,
            TotalChunks = job.TotalChunks,
            ProcessedChunks = job.ProcessedChunks,
            ProgressPercentage = job.ProgressPercentage,
            ErrorMessage = job.ErrorMessage,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            DurationMs = job.DurationMs,
            Logs = logDtos
        };

        return Ok(ApiResponse<IndexingJobDetailDto>.Ok(detail));
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Job cancelled: {JobId}")]
    private static partial void LogJobCancelled(ILogger logger, Guid jobId);

    #endregion
}
