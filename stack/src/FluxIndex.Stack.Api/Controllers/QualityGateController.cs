using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Evaluation;
using Microsoft.AspNetCore.Mvc;
using EvaluationThresholds = FluxIndex.Core.Domain.Models.QualityThresholds;

namespace FluxIndex.Stack.Api.Controllers;

/// <summary>
/// API controller for Quality Gate operations in CI/CD pipelines.
/// Provides endpoints to validate RAG system quality before deployment.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class QualityGateController : ControllerBase
{
    private readonly IQualityGateService _qualityGateService;
    private readonly ILogger<QualityGateController> _logger;

    public QualityGateController(
        IQualityGateService qualityGateService,
        ILogger<QualityGateController> logger)
    {
        _qualityGateService = qualityGateService;
        _logger = logger;
    }

    /// <summary>
    /// Execute a quality gate check against a golden dataset.
    /// Used in CI/CD pipelines to validate RAG system quality before deployment.
    /// </summary>
    /// <param name="request">Quality gate request with version, dataset ID, and thresholds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Quality gate result indicating pass/fail with detailed metrics.</returns>
    [HttpPost("execute")]
    [ProducesResponseType(typeof(ApiResponse<QualityGateResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<QualityGateResultDto>>> ExecuteQualityGate(
        [FromBody] QualityGateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SystemVersion))
        {
            return BadRequest(ApiResponse<object>.Fail("System version is required."));
        }

        if (string.IsNullOrWhiteSpace(request.DatasetId))
        {
            return BadRequest(ApiResponse<object>.Fail("Dataset ID is required."));
        }

        try
        {
            var startTime = DateTime.UtcNow;
            _logger.LogInformation(
                "Executing quality gate for version {Version} against dataset {DatasetId}",
                request.SystemVersion, request.DatasetId);

            // Convert DTO thresholds to Core thresholds
            var coreThresholds = new EvaluationThresholds
            {
                MinPrecision = request.Thresholds.MinPrecision,
                MinRecall = request.Thresholds.MinRecall,
                MinF1Score = request.Thresholds.MinF1Score,
                MinMRR = request.Thresholds.MinMRR,
                MinNDCG = request.Thresholds.MinNDCG
            };

            var result = await _qualityGateService.ExecuteQualityGateAsync(
                request.SystemVersion,
                request.DatasetId,
                coreThresholds,
                cancellationToken);

            var dto = new QualityGateResultDto
            {
                Passed = result.Passed,
                SystemVersion = result.SystemVersion,
                DatasetId = request.DatasetId,
                Metrics = new EvaluationMetricsDto
                {
                    MRR = result.EvaluationResult.AverageMRR,
                    PrecisionAtK = result.EvaluationResult.AveragePrecision,
                    RecallAtK = result.EvaluationResult.AverageRecall,
                    NDCG = result.EvaluationResult.AverageNDCG,
                    AverageFaithfulness = result.EvaluationResult.AverageFaithfulness,
                    AverageRelevancy = result.EvaluationResult.AverageAnswerRelevancy,
                    OverallScore = result.EvaluationResult.AverageF1Score,
                    QualityTier = GetQualityTier(result.EvaluationResult.AverageF1Score)
                },
                AppliedThresholds = new QualityThresholdsDto
                {
                    MinPrecision = result.AppliedThresholds.MinPrecision,
                    MinRecall = result.AppliedThresholds.MinRecall,
                    MinF1Score = result.AppliedThresholds.MinF1Score,
                    MinMRR = result.AppliedThresholds.MinMRR,
                    MinNDCG = result.AppliedThresholds.MinNDCG
                },
                FailedCriteria = result.FailedCriteria,
                Summary = result.Summary,
                ExecutedAt = result.ExecutedAt,
                DurationMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };

            _logger.LogInformation(
                "Quality gate {Status} for version {Version}: {FailedCount} criteria failed",
                dto.Passed ? "PASSED" : "FAILED", request.SystemVersion, dto.FailedCriteria.Count);

            return Ok(ApiResponse<QualityGateResultDto>.Ok(dto));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Quality gate validation error for version {Version}", request.SystemVersion);
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Quality gate execution failed for version {Version}", request.SystemVersion);
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail($"Quality gate execution failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Compare performance between two system versions.
    /// Useful for A/B testing and regression detection.
    /// </summary>
    /// <param name="request">Comparison request with current and baseline versions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Comparison result with improvements and regressions.</returns>
    [HttpPost("compare")]
    [ProducesResponseType(typeof(ApiResponse<VersionComparisonResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<VersionComparisonResultDto>>> CompareVersions(
        [FromBody] VersionComparisonRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentVersion))
        {
            return BadRequest(ApiResponse<object>.Fail("Current version is required."));
        }

        if (string.IsNullOrWhiteSpace(request.BaselineVersion))
        {
            return BadRequest(ApiResponse<object>.Fail("Baseline version is required."));
        }

        if (string.IsNullOrWhiteSpace(request.DatasetId))
        {
            return BadRequest(ApiResponse<object>.Fail("Dataset ID is required."));
        }

        try
        {
            _logger.LogInformation(
                "Comparing versions: {CurrentVersion} vs {BaselineVersion}",
                request.CurrentVersion, request.BaselineVersion);

            var result = await _qualityGateService.CompareWithBaselineAsync(
                request.CurrentVersion,
                request.BaselineVersion,
                request.DatasetId,
                cancellationToken);

            var dto = new VersionComparisonResultDto
            {
                CurrentVersion = result.CurrentVersion,
                BaselineVersion = result.BaselineVersion,
                CurrentMetrics = result.CurrentMetrics,
                BaselineMetrics = result.BaselineMetrics,
                Improvements = result.Improvements,
                Regressions = result.Regressions,
                OverallImprovement = result.OverallImprovement,
                HasSignificantRegression = result.HasSignificantRegression,
                Recommendation = GetRecommendation(result)
            };

            _logger.LogInformation(
                "Version comparison completed: {OverallChange:P2} overall change, regression={HasRegression}",
                result.OverallImprovement, result.HasSignificantRegression);

            return Ok(ApiResponse<VersionComparisonResultDto>.Ok(dto));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Version comparison validation error");
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Version comparison failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail($"Version comparison failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Quick health check endpoint for CI/CD pipeline integration.
    /// Returns pass/fail status code for easy integration with deployment tools.
    /// </summary>
    /// <param name="version">System version to check.</param>
    /// <param name="datasetId">Dataset ID for evaluation.</param>
    /// <param name="minScore">Minimum acceptable F1 score (default: 0.7).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 OK if passed, 400 if failed.</returns>
    [HttpGet("check")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<object>>> QuickCheck(
        [FromQuery] string version,
        [FromQuery] string datasetId,
        [FromQuery] double minScore = 0.7,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(datasetId))
        {
            return BadRequest(ApiResponse<object>.Fail("Version and datasetId are required."));
        }

        try
        {
            var thresholds = new EvaluationThresholds
            {
                MinPrecision = minScore,
                MinRecall = minScore,
                MinF1Score = minScore,
                MinMRR = minScore,
                MinNDCG = minScore
            };

            var result = await _qualityGateService.ExecuteQualityGateAsync(
                version, datasetId, thresholds, cancellationToken);

            if (result.Passed)
            {
                return Ok(ApiResponse<object>.Ok(new
                {
                    status = "PASSED",
                    version = version,
                    score = result.EvaluationResult.AverageF1Score
                }));
            }
            else
            {
                return BadRequest(ApiResponse<object>.Fail(
                    $"Quality gate failed. Score: {result.EvaluationResult.AverageF1Score:F3}, " +
                    $"Required: {minScore:F3}. Failed criteria: {string.Join(", ", result.FailedCriteria)}"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Quick check failed for version {Version}", version);
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail($"Quick check failed: {ex.Message}"));
        }
    }

    private static string GetQualityTier(double score)
    {
        return score switch
        {
            >= 0.9 => "Excellent",
            >= 0.8 => "High",
            >= 0.6 => "Medium",
            _ => "Low"
        };
    }

    private static string GetRecommendation(PerformanceComparisonResult result)
    {
        if (result.HasSignificantRegression)
        {
            return $"REJECT: Significant regression detected in {result.Regressions.Count} metrics. " +
                   $"Review changes before deployment.";
        }

        if (result.OverallImprovement > 0.05)
        {
            return $"APPROVE: Overall improvement of {result.OverallImprovement:P1} detected. " +
                   $"Recommended for deployment.";
        }

        if (result.OverallImprovement >= -0.02)
        {
            return "APPROVE: No significant changes detected. Safe to deploy.";
        }

        return $"REVIEW: Minor regression of {Math.Abs(result.OverallImprovement):P1} detected. " +
               $"Manual review recommended.";
    }
}
