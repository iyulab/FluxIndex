using FluxIndex.Service.Api.Middleware;
using FluxIndex.Service.Application.Interfaces.Services;
using FluxIndex.Service.Shared.Common;
using FluxIndex.Service.Shared.DTOs.Auth;
using Microsoft.AspNetCore.Mvc;

namespace FluxIndex.Service.Api.Controllers;

/// <summary>
/// API controller for API key management.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class ApiKeysController : ControllerBase
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
        _logger.LogInformation("API key created: {KeyId} - {Name}", response.Id, response.Name);

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
        _logger.LogInformation("API key updated: {KeyId}", id);

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
        _logger.LogInformation("API key deleted: {KeyId}", id);

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
        _logger.LogInformation("API key activated: {KeyId}", id);

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
        _logger.LogInformation("API key deactivated: {KeyId}", id);

        return Ok(ApiResponse<object>.Ok(null!, "API key deactivated successfully."));
    }
}
