using FluxIndex.Stack.Api.Middleware;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Search;
using Microsoft.AspNetCore.Mvc;

namespace FluxIndex.Stack.Api.Controllers;

/// <summary>
/// API controller for advanced search operations with dynamic fusion,
/// listwise reranking, entity extraction, and community search.
/// </summary>
[ApiController]
[Route("api/v1/search/advanced")]
public class AdvancedSearchController : ControllerBase
{
    private readonly IAdvancedSearchService _advancedSearchService;
    private readonly ILogger<AdvancedSearchController> _logger;

    public AdvancedSearchController(
        IAdvancedSearchService advancedSearchService,
        ILogger<AdvancedSearchController> logger)
    {
        _advancedSearchService = advancedSearchService;
        _logger = logger;
    }

    /// <summary>
    /// Performs an advanced search with dynamic fusion and optional advanced features.
    /// </summary>
    /// <remarks>
    /// Advanced search includes:
    /// - Dynamic Alpha Tuning (DAT) for query-adaptive fusion weights
    /// - Listwise reranking with multiple methods (attention-based, tournament, etc.)
    /// - Entity extraction and linking
    /// - Community-based search for hierarchical document organization
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AdvancedSearchResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AdvancedSearchResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AdvancedSearchResponse>>> Search(
        [FromBody] AdvancedSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(ApiResponse<AdvancedSearchResponse>.Fail("Query cannot be empty."));
        }

        var apiKey = HttpContext.GetApiKey();
        var apiKeyPrefix = apiKey?.KeyPrefix;

        _logger.LogInformation(
            "Advanced search: {Query} in collection: {CollectionId} " +
            "(DynamicFusion: {DynamicFusion}, Listwise: {Listwise}, Entities: {Entities}, Community: {Community})",
            request.Query, request.CollectionId,
            request.EnableDynamicFusion, request.EnableListwiseReranking,
            request.EnableEntityExtraction, request.EnableCommunitySearch);

        var response = await _advancedSearchService.SearchAsync(request, apiKeyPrefix, cancellationToken);

        _logger.LogInformation(
            "Advanced search completed: {ResultCount} results in {ExecutionTime}ms",
            response.TotalResults, response.ExecutionTimeMs);

        return Ok(ApiResponse<AdvancedSearchResponse>.Ok(response));
    }

    /// <summary>
    /// Analyzes a query to determine its type, complexity, and optimal search strategy.
    /// </summary>
    [HttpGet("analyze")]
    [ProducesResponseType(typeof(ApiResponse<QueryAnalysisDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<QueryAnalysisDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<QueryAnalysisDto>>> AnalyzeQuery(
        [FromQuery] string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(ApiResponse<QueryAnalysisDto>.Fail("Query cannot be empty."));
        }

        var analysis = await _advancedSearchService.AnalyzeQueryAsync(query, cancellationToken);

        return Ok(ApiResponse<QueryAnalysisDto>.Ok(analysis));
    }

    /// <summary>
    /// Extracts entities from a collection's documents.
    /// </summary>
    [HttpGet("entities/{collectionId}")]
    [ProducesResponseType(typeof(ApiResponse<List<ExtractedEntityDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<ExtractedEntityDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<ExtractedEntityDto>>>> ExtractEntities(
        Guid collectionId,
        [FromQuery] int maxEntities = 100,
        CancellationToken cancellationToken = default)
    {
        var entities = await _advancedSearchService.ExtractEntitiesAsync(
            collectionId, maxEntities, cancellationToken);

        return Ok(ApiResponse<List<ExtractedEntityDto>>.Ok(entities));
    }

    /// <summary>
    /// Builds community hierarchy from a collection's documents.
    /// </summary>
    [HttpPost("communities/{collectionId}/build")]
    [ProducesResponseType(typeof(ApiResponse<CommunitySearchInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CommunitySearchInfoDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CommunitySearchInfoDto>>> BuildCommunities(
        Guid collectionId,
        [FromQuery] int maxLevels = 3,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsWriter())
        {
            return Forbid();
        }

        var communityInfo = await _advancedSearchService.BuildCommunitiesAsync(
            collectionId, maxLevels, cancellationToken);

        _logger.LogInformation(
            "Built {Count} communities for collection {CollectionId}",
            communityInfo.TotalCommunities, collectionId);

        return Ok(ApiResponse<CommunitySearchInfoDto>.Ok(communityInfo));
    }

    /// <summary>
    /// Gets community hierarchy for a collection.
    /// </summary>
    [HttpGet("communities/{collectionId}")]
    [ProducesResponseType(typeof(ApiResponse<List<CommunityDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<CommunityDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<CommunityDto>>>> GetCommunities(
        Guid collectionId,
        [FromQuery] int? level = null,
        CancellationToken cancellationToken = default)
    {
        var communities = await _advancedSearchService.GetCommunitiesAsync(
            collectionId, level, cancellationToken);

        return Ok(ApiResponse<List<CommunityDto>>.Ok(communities));
    }
}
