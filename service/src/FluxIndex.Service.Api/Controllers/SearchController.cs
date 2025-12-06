using FluxIndex.Service.Api.Middleware;
using FluxIndex.Service.Application.Interfaces.Services;
using FluxIndex.Service.Shared.Common;
using FluxIndex.Service.Shared.DTOs.Search;
using Microsoft.AspNetCore.Mvc;

namespace FluxIndex.Service.Api.Controllers;

/// <summary>
/// API controller for search operations.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;
    private readonly ILogger<SearchController> _logger;

    public SearchController(
        ISearchService searchService,
        ILogger<SearchController> logger)
    {
        _searchService = searchService;
        _logger = logger;
    }

    /// <summary>
    /// Performs a search query.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<SearchResponse>>> Search(
        [FromBody] SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(ApiResponse<SearchResponse>.Fail("Query cannot be empty."));
        }

        var apiKey = HttpContext.GetApiKey();
        var apiKeyPrefix = apiKey?.KeyPrefix;

        _logger.LogInformation("Search request: {Query} in collection: {CollectionId}",
            request.Query, request.CollectionId);

        var response = await _searchService.SearchAsync(request, apiKeyPrefix, cancellationToken);

        _logger.LogInformation("Search completed: {ResultCount} results in {ExecutionTime}ms",
            response.TotalResults, response.ExecutionTimeMs);

        return Ok(ApiResponse<SearchResponse>.Ok(response));
    }

    /// <summary>
    /// Performs a simple GET search (for testing/convenience).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<SearchResponse>>> SearchGet(
        [FromQuery] string query,
        [FromQuery] Guid? collectionId = null,
        [FromQuery] int topK = 10,
        [FromQuery] SearchMode mode = SearchMode.Hybrid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(ApiResponse<SearchResponse>.Fail("Query cannot be empty."));
        }

        var request = new SearchRequest
        {
            Query = query,
            CollectionId = collectionId,
            TopK = topK,
            Mode = mode,
            IncludeContent = true,
            IncludeMetadata = true
        };

        var apiKey = HttpContext.GetApiKey();
        var apiKeyPrefix = apiKey?.KeyPrefix;

        var response = await _searchService.SearchAsync(request, apiKeyPrefix, cancellationToken);

        return Ok(ApiResponse<SearchResponse>.Ok(response));
    }

    /// <summary>
    /// Gets a cached response for a query if available.
    /// </summary>
    [HttpGet("cache")]
    public async Task<ActionResult<ApiResponse<SemanticCacheEntryDto>>> GetCachedResponse(
        [FromQuery] string query,
        [FromQuery] double similarityThreshold = 0.95,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(ApiResponse<SemanticCacheEntryDto>.Fail("Query cannot be empty."));
        }

        var cached = await _searchService.GetCachedResponseAsync(query, similarityThreshold, cancellationToken);

        if (cached == null)
        {
            return NotFound(ApiResponse<SemanticCacheEntryDto>.Fail("No cached response found."));
        }

        return Ok(ApiResponse<SemanticCacheEntryDto>.Ok(cached));
    }

    /// <summary>
    /// Caches a response for a query.
    /// </summary>
    [HttpPost("cache")]
    public async Task<ActionResult<ApiResponse<object>>> CacheResponse(
        [FromBody] CacheResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsWriter())
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Query) || string.IsNullOrWhiteSpace(request.Response))
        {
            return BadRequest(ApiResponse<object>.Fail("Query and response cannot be empty."));
        }

        await _searchService.CacheResponseAsync(request.Query, request.Response, cancellationToken);

        return Ok(ApiResponse<object>.Ok(null!, "Response cached successfully."));
    }

    /// <summary>
    /// Clears the semantic cache.
    /// </summary>
    [HttpDelete("cache")]
    public async Task<ActionResult<ApiResponse<object>>> ClearCache(
        [FromQuery] Guid? collectionId = null,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsAdmin())
        {
            return Forbid();
        }

        await _searchService.ClearCacheAsync(collectionId, cancellationToken);

        _logger.LogInformation("Cache cleared for collection: {CollectionId}", collectionId);

        return Ok(ApiResponse<object>.Ok(null!, "Cache cleared successfully."));
    }
}

/// <summary>
/// Request to cache a response.
/// </summary>
public class CacheResponseRequest
{
    public required string Query { get; init; }
    public required string Response { get; init; }
}
