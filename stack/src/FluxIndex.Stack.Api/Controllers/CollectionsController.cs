using FluxIndex.Stack.Api.Middleware;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Collections;
using Microsoft.AspNetCore.Mvc;

namespace FluxIndex.Stack.Api.Controllers;

/// <summary>
/// API controller for collection management.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public partial class CollectionsController : ControllerBase
{
    private readonly ICollectionService _collectionService;
    private readonly ILogger<CollectionsController> _logger;

    public CollectionsController(
        ICollectionService collectionService,
        ILogger<CollectionsController> logger)
    {
        _collectionService = collectionService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all collections with pagination.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<CollectionDto>>>> GetCollections(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _collectionService.GetPagedAsync(page, pageSize, cancellationToken);
        return Ok(ApiResponse<List<CollectionDto>>.Ok(result.Items, result.ToMetadata()));
    }

    /// <summary>
    /// Gets a collection by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CollectionDto>>> GetCollection(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var collection = await _collectionService.GetByIdAsync(id, cancellationToken);
        if (collection == null)
        {
            return NotFound(ApiResponse<CollectionDto>.Fail($"Collection with id '{id}' not found."));
        }

        return Ok(ApiResponse<CollectionDto>.Ok(collection));
    }

    /// <summary>
    /// Gets a collection by name.
    /// </summary>
    [HttpGet("by-name/{name}")]
    public async Task<ActionResult<ApiResponse<CollectionDto>>> GetCollectionByName(
        string name,
        CancellationToken cancellationToken = default)
    {
        var collection = await _collectionService.GetByNameAsync(name, cancellationToken);
        if (collection == null)
        {
            return NotFound(ApiResponse<CollectionDto>.Fail($"Collection with name '{name}' not found."));
        }

        return Ok(ApiResponse<CollectionDto>.Ok(collection));
    }

    /// <summary>
    /// Creates a new collection.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<CollectionDto>>> CreateCollection(
        [FromBody] CreateCollectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsWriter())
        {
            return Forbid();
        }

        var collection = await _collectionService.CreateAsync(request, cancellationToken);
        LogCollectionCreated(_logger, collection.Id, collection.Name);

        return CreatedAtAction(
            nameof(GetCollection),
            new { id = collection.Id },
            ApiResponse<CollectionDto>.Ok(collection, "Collection created successfully."));
    }

    /// <summary>
    /// Updates an existing collection.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CollectionDto>>> UpdateCollection(
        Guid id,
        [FromBody] UpdateCollectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsWriter())
        {
            return Forbid();
        }

        var collection = await _collectionService.UpdateAsync(id, request, cancellationToken);
        LogCollectionUpdated(_logger, id);

        return Ok(ApiResponse<CollectionDto>.Ok(collection, "Collection updated successfully."));
    }

    /// <summary>
    /// Deletes a collection.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteCollection(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsAdmin())
        {
            return Forbid();
        }

        await _collectionService.DeleteAsync(id, cancellationToken);
        LogCollectionDeleted(_logger, id);

        return Ok(ApiResponse<object>.Ok(null!, "Collection deleted successfully."));
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Collection created: {CollectionId} - {Name}")]
    private static partial void LogCollectionCreated(ILogger logger, Guid collectionId, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Collection updated: {CollectionId}")]
    private static partial void LogCollectionUpdated(ILogger logger, Guid collectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Collection deleted: {CollectionId}")]
    private static partial void LogCollectionDeleted(ILogger logger, Guid collectionId);

    #endregion
}
