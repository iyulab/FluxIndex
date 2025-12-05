using FluxIndex.Service.Api.Middleware;
using FluxIndex.Service.Application.Interfaces.Services;
using FluxIndex.Service.Shared.Common;
using FluxIndex.Service.Shared.DTOs.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace FluxIndex.Service.Api.Controllers;

/// <summary>
/// API controller for analytics and statistics.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsService _analyticsService;

    public AnalyticsController(IAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
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
}
