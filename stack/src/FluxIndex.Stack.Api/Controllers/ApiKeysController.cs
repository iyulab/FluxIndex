using FluxIndex.Stack.Api.Middleware;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Auth;
using Microsoft.AspNetCore.Mvc;

namespace FluxIndex.Stack.Api.Controllers;

/// <summary>
/// API controller for API key management.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public partial class ApiKeysController : ControllerBase
{
    private readonly IApiKeyService _apiKeyService;
    private readonly ILogger<ApiKeysController> _logger;

    public ApiKeysController(
        IApiKeyService apiKeyService,
        ILogger<ApiKeysController> logger)
    {
        _apiKeyService = apiKeyService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all API keys with pagination.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ApiKeyDto>>>> GetApiKeys(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsAdmin())
        {
            return Forbid();
        }

        var result = await _apiKeyService.GetPagedAsync(page, pageSize, cancellationToken);
        return Ok(ApiResponse<List<ApiKeyDto>>.Ok(result.Items, result.ToMetadata()));
    }

    /// <summary>
    /// Gets an API key by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ApiKeyDto>>> GetApiKey(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsAdmin())
        {
            return Forbid();
        }

        var apiKey = await _apiKeyService.GetByIdAsync(id, cancellationToken);
        if (apiKey == null)
        {
            return NotFound(ApiResponse<ApiKeyDto>.Fail($"API key with id '{id}' not found."));
        }

        return Ok(ApiResponse<ApiKeyDto>.Ok(apiKey));
    }

    /// <summary>
    /// Creates a new API key.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<CreateApiKeyResponse>>> CreateApiKey(
        [FromBody] CreateApiKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsAdmin())
        {
            return Forbid();
        }

        var response = await _apiKeyService.CreateAsync(request, cancellationToken);
        LogApiKeyCreated(_logger, response.Id, response.Name);

        return CreatedAtAction(
            nameof(GetApiKey),
            new { id = response.Id },
            ApiResponse<CreateApiKeyResponse>.Ok(response, "API key created. Store the raw key securely."));
    }

    /// <summary>
    /// Updates an existing API key.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ApiKeyDto>>> UpdateApiKey(
        Guid id,
        [FromBody] UpdateApiKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsAdmin())
        {
            return Forbid();
        }

        var apiKey = await _apiKeyService.UpdateAsync(id, request, cancellationToken);
        LogApiKeyUpdated(_logger, id);

        return Ok(ApiResponse<ApiKeyDto>.Ok(apiKey, "API key updated successfully."));
    }

    /// <summary>
    /// Deletes an API key.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteApiKey(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsAdmin())
        {
            return Forbid();
        }

        await _apiKeyService.DeleteAsync(id, cancellationToken);
        LogApiKeyDeleted(_logger, id);

        return Ok(ApiResponse<object>.Ok(null!, "API key deleted successfully."));
    }

    /// <summary>
    /// Activates an API key.
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<ApiResponse<object>>> ActivateApiKey(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsAdmin())
        {
            return Forbid();
        }

        await _apiKeyService.ActivateAsync(id, cancellationToken);
        LogApiKeyActivated(_logger, id);

        return Ok(ApiResponse<object>.Ok(null!, "API key activated successfully."));
    }

    /// <summary>
    /// Deactivates an API key.
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<ApiResponse<object>>> DeactivateApiKey(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsAdmin())
        {
            return Forbid();
        }

        await _apiKeyService.DeactivateAsync(id, cancellationToken);
        LogApiKeyDeactivated(_logger, id);

        return Ok(ApiResponse<object>.Ok(null!, "API key deactivated successfully."));
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "API key created: {KeyId} - {Name}")]
    private static partial void LogApiKeyCreated(ILogger logger, Guid keyId, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "API key updated: {KeyId}")]
    private static partial void LogApiKeyUpdated(ILogger logger, Guid keyId);

    [LoggerMessage(Level = LogLevel.Information, Message = "API key deleted: {KeyId}")]
    private static partial void LogApiKeyDeleted(ILogger logger, Guid keyId);

    [LoggerMessage(Level = LogLevel.Information, Message = "API key activated: {KeyId}")]
    private static partial void LogApiKeyActivated(ILogger logger, Guid keyId);

    [LoggerMessage(Level = LogLevel.Information, Message = "API key deactivated: {KeyId}")]
    private static partial void LogApiKeyDeactivated(ILogger logger, Guid keyId);

    #endregion
}
