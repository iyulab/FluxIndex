using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Stack.Api.Middleware;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace FluxIndex.Stack.Api.Controllers;

/// <summary>
/// API controller for analytics and statistics.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsService _analyticsService;
    private readonly ISemanticCacheService? _semanticCacheService;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(
        IAnalyticsService analyticsService,
        ILogger<AnalyticsController> logger,
        ISemanticCacheService? semanticCacheService = null)
    {
        _analyticsService = analyticsService;
        _logger = logger;
        _semanticCacheService = semanticCacheService;
    }

    /// <summary>
    /// Gets system-wide statistics.
    /// </summary>
    [HttpGet("system")]
    public async Task<ActionResult<ApiResponse<SystemStatsDto>>> GetSystemStats(
        CancellationToken cancellationToken = default)
    {
        var stats = await _analyticsService.GetSystemStatsAsync(cancellationToken);
        return Ok(ApiResponse<SystemStatsDto>.Ok(stats));
    }

    /// <summary>
    /// Gets search analytics.
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<SearchAnalyticsDto>>> GetSearchAnalytics(
        [FromQuery] int days = 30,
        [FromQuery] Guid? collectionId = null,
        CancellationToken cancellationToken = default)
    {
        var analytics = await _analyticsService.GetSearchAnalyticsAsync(days, collectionId, cancellationToken);
        return Ok(ApiResponse<SearchAnalyticsDto>.Ok(analytics));
    }

    /// <summary>
    /// Gets document analytics.
    /// </summary>
    [HttpGet("documents")]
    public async Task<ActionResult<ApiResponse<DocumentAnalyticsDto>>> GetDocumentAnalytics(
        [FromQuery] int days = 30,
        [FromQuery] Guid? collectionId = null,
        CancellationToken cancellationToken = default)
    {
        var analytics = await _analyticsService.GetDocumentAnalyticsAsync(days, collectionId, cancellationToken);
        return Ok(ApiResponse<DocumentAnalyticsDto>.Ok(analytics));
    }

    /// <summary>
    /// Gets semantic cache statistics for monitoring and evaluation.
    /// Returns cache hit rate, average response time, and top performing queries.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Semantic cache statistics.</returns>
    [HttpGet("cache")]
    [ProducesResponseType(typeof(ApiResponse<SemanticCacheStatsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<SemanticCacheStatsDto>>> GetCacheStatistics(
        CancellationToken cancellationToken = default)
    {
        if (_semanticCacheService == null)
        {
            _logger.LogWarning("Semantic cache service not configured");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResponse<object>.Fail("Semantic cache service is not configured. Enable Redis cache in configuration."));
        }

        try
        {
            var stats = await _semanticCacheService.GetCacheStatisticsAsync(cancellationToken);

            var dto = new SemanticCacheStatsDto
            {
                TotalEntries = stats.TotalEntries,
                CacheHits = stats.CacheHits,
                CacheMisses = stats.CacheMisses,
                HitRate = stats.HitRate,
                AverageResponseTimeMs = stats.AverageResponseTimeMs,
                CacheSizeBytes = stats.CacheSizeBytes,
                ExpiredEntries = stats.ExpiredEntries,
                AverageSimilarityScore = stats.AverageSimilarityScore,
                TopPerformingQueries = stats.TopPerformingQueries.Select(q => new CacheQueryPerformanceDto
                {
                    Query = q.Query,
                    HitCount = q.HitCount,
                    AverageSimilarity = q.AverageSimilarity,
                    AverageResponseTimeMs = q.AverageResponseTimeMs,
                    LastUsedAt = q.LastUsedAt
                }).ToList(),
                CollectedAt = stats.CollectedAt
            };

            return Ok(ApiResponse<SemanticCacheStatsDto>.Ok(dto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve cache statistics");
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail($"Failed to retrieve cache statistics: {ex.Message}"));
        }
    }

    /// <summary>
    /// Invalidates cached results matching the specified pattern.
    /// Useful for clearing stale cache entries after document updates.
    /// </summary>
    /// <param name="pattern">Query pattern to invalidate (e.g., "*product*").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success message.</returns>
    [HttpDelete("cache")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<string>>> InvalidateCache(
        [FromQuery] string pattern,
        CancellationToken cancellationToken = default)
    {
        if (_semanticCacheService == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResponse<object>.Fail("Semantic cache service is not configured."));
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return BadRequest(ApiResponse<object>.Fail("Pattern is required for cache invalidation."));
        }

        try
        {
            await _semanticCacheService.InvalidateCacheAsync(pattern, cancellationToken);
            _logger.LogInformation("Cache invalidated for pattern: {Pattern}", pattern);
            return Ok(ApiResponse<string>.Ok($"Cache invalidated for pattern: {pattern}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate cache for pattern: {Pattern}", pattern);
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail($"Failed to invalidate cache: {ex.Message}"));
        }
    }

    /// <summary>
    /// Performs cache compaction to remove expired entries and optimize storage.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success message.</returns>
    [HttpPost("cache/compact")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<string>>> CompactCache(
        CancellationToken cancellationToken = default)
    {
        if (_semanticCacheService == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResponse<object>.Fail("Semantic cache service is not configured."));
        }

        try
        {
            await _semanticCacheService.CompactCacheAsync(cancellationToken);
            _logger.LogInformation("Cache compaction completed");
            return Ok(ApiResponse<string>.Ok("Cache compaction completed successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compact cache");
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail($"Failed to compact cache: {ex.Message}"));
        }
    }

    /// <summary>
    /// Warms up the cache with popular queries to improve initial response times.
    /// </summary>
    /// <param name="queries">List of popular queries to pre-cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success message with number of queries warmed up.</returns>
    [HttpPost("cache/warmup")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<string>>> WarmupCache(
        [FromBody] List<string>? queries,
        CancellationToken cancellationToken = default)
    {
        if (_semanticCacheService == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResponse<object>.Fail("Semantic cache service is not configured."));
        }

        if (queries == null || queries.Count == 0)
        {
            return BadRequest(ApiResponse<object>.Fail("At least one query is required for cache warmup."));
        }

        try
        {
            await _semanticCacheService.WarmupCacheAsync(queries, cancellationToken);
            _logger.LogInformation("Cache warmup completed for {Count} queries", queries.Count);
            return Ok(ApiResponse<string>.Ok($"Cache warmup completed for {queries.Count} queries."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to warmup cache");
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail($"Failed to warmup cache: {ex.Message}"));
        }
    }
}
