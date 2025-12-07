using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Settings;
using Microsoft.AspNetCore.Mvc;

namespace FluxIndex.Stack.Api.Controllers;

/// <summary>
/// Controller for managing AI provider settings and configuration.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly IAiProviderSettingsService _settingsService;
    private readonly IEmbeddingProviderCache? _embeddingProviderCache;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        IAiProviderSettingsService settingsService,
        ILogger<SettingsController> logger,
        IEmbeddingProviderCache? embeddingProviderCache = null)
    {
        _settingsService = settingsService;
        _logger = logger;
        _embeddingProviderCache = embeddingProviderCache;
    }

    /// <summary>
    /// Gets the overall AI configuration status.
    /// </summary>
    [HttpGet("ai")]
    [ProducesResponseType(typeof(ApiResponse<AiConfigurationStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAiConfiguration(CancellationToken cancellationToken)
    {
        var status = await _settingsService.GetConfigurationStatusAsync(cancellationToken);
        return Ok(ApiResponse<AiConfigurationStatusDto>.Ok(status));
    }

    /// <summary>
    /// Gets all configured AI providers.
    /// </summary>
    [HttpGet("ai/providers")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AiProviderSettingsDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllProviders(CancellationToken cancellationToken)
    {
        var providers = await _settingsService.GetAllProvidersAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AiProviderSettingsDto>>.Ok(providers));
    }

    /// <summary>
    /// Gets a specific AI provider's settings.
    /// </summary>
    [HttpGet("ai/providers/{providerName}")]
    [ProducesResponseType(typeof(ApiResponse<AiProviderSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProvider(string providerName, CancellationToken cancellationToken)
    {
        var provider = await _settingsService.GetProviderAsync(providerName, cancellationToken);
        if (provider == null)
        {
            return NotFound(ApiResponse<AiProviderSettingsDto>.Fail($"Provider '{providerName}' not found"));
        }
        return Ok(ApiResponse<AiProviderSettingsDto>.Ok(provider));
    }

    /// <summary>
    /// Updates an AI provider's settings.
    /// </summary>
    [HttpPut("ai/providers/{providerName}")]
    [ProducesResponseType(typeof(ApiResponse<AiProviderSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProvider(
        string providerName,
        [FromBody] UpdateAiProviderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var provider = await _settingsService.UpdateProviderAsync(providerName, request, cancellationToken);
            _logger.LogInformation("Updated AI provider settings for {Provider}", providerName);

            // Invalidate embedding provider cache when settings change
            // This ensures the new configuration takes effect immediately
            if (request.ApiKey != null || request.EmbeddingModel != null ||
                request.IsDefaultEmbedding == true || request.IsEnabled != null)
            {
                _embeddingProviderCache?.InvalidateCache();
                _logger.LogInformation("Embedding provider cache invalidated due to settings change");
            }

            return Ok(ApiResponse<AiProviderSettingsDto>.Ok(provider, "Provider settings updated successfully"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<AiProviderSettingsDto>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update provider {Provider}", providerName);
            return BadRequest(ApiResponse<AiProviderSettingsDto>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Gets available models for a provider.
    /// </summary>
    [HttpGet("ai/providers/{providerName}/models")]
    [ProducesResponseType(typeof(ApiResponse<AvailableModelsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvailableModels(string providerName, CancellationToken cancellationToken)
    {
        try
        {
            var models = await _settingsService.GetAvailableModelsAsync(providerName, cancellationToken);
            return Ok(ApiResponse<AvailableModelsDto>.Ok(models));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<AvailableModelsDto>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Tests connection to an AI provider.
    /// </summary>
    [HttpPost("ai/providers/{providerName}/test")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestProviderConnection(string providerName, CancellationToken cancellationToken)
    {
        var result = await _settingsService.TestProviderConnectionAsync(providerName, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(result, result ? "Connection successful" : "Connection failed"));
    }

    /// <summary>
    /// Initializes default provider configurations.
    /// </summary>
    [HttpPost("ai/providers/initialize")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> InitializeProviders(CancellationToken cancellationToken)
    {
        await _settingsService.InitializeDefaultProvidersAsync(cancellationToken);
        return Ok(ApiResponse<string>.Ok("Providers initialized", "Default AI providers have been initialized"));
    }

    /// <summary>
    /// Gets the current embedding provider status.
    /// </summary>
    [HttpGet("ai/embedding/status")]
    [ProducesResponseType(typeof(ApiResponse<EmbeddingProviderStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmbeddingProviderStatus(
        [FromServices] IEmbeddingProvider? embeddingProvider,
        CancellationToken cancellationToken)
    {
        var status = await _settingsService.GetConfigurationStatusAsync(cancellationToken);

        var dto = new EmbeddingProviderStatusDto
        {
            IsConfigured = status.HasEmbeddingProvider,
            ProviderName = status.DefaultEmbeddingProvider ?? "Local",
            ModelName = embeddingProvider?.ModelName ?? "unknown",
            Dimensions = embeddingProvider?.EmbeddingDimension ?? 0,
            IsUsingLocalFallback = !status.HasEmbeddingProvider || status.DefaultEmbeddingProvider == "Local"
        };

        return Ok(ApiResponse<EmbeddingProviderStatusDto>.Ok(dto));
    }

    /// <summary>
    /// Refreshes the embedding provider cache, forcing reconfiguration.
    /// </summary>
    [HttpPost("ai/embedding/refresh")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public IActionResult RefreshEmbeddingProvider()
    {
        _embeddingProviderCache?.InvalidateCache();
        _logger.LogInformation("Embedding provider cache manually refreshed");
        return Ok(ApiResponse<string>.Ok("Cache refreshed", "Embedding provider will be reconfigured on next request"));
    }
}

/// <summary>
/// DTO for embedding provider status.
/// </summary>
public class EmbeddingProviderStatusDto
{
    public bool IsConfigured { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public bool IsUsingLocalFallback { get; set; }
}
