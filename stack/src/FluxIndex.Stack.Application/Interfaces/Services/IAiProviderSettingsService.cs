using FluxIndex.Stack.Shared.DTOs.Settings;

namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Service interface for AI provider settings management.
/// </summary>
public interface IAiProviderSettingsService
{
    /// <summary>
    /// Gets all configured AI providers.
    /// </summary>
    Task<IReadOnlyList<AiProviderSettingsDto>> GetAllProvidersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific provider by name.
    /// </summary>
    Task<AiProviderSettingsDto?> GetProviderAsync(string providerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the overall AI configuration status.
    /// </summary>
    Task<AiConfigurationStatusDto> GetConfigurationStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a provider's settings.
    /// </summary>
    Task<AiProviderSettingsDto> UpdateProviderAsync(string providerName, UpdateAiProviderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests if a provider's API key is valid.
    /// </summary>
    Task<bool> TestProviderConnectionAsync(string providerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available models for a provider.
    /// </summary>
    Task<AvailableModelsDto> GetAvailableModelsAsync(string providerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes default provider configurations if none exist.
    /// </summary>
    Task InitializeDefaultProvidersAsync(CancellationToken cancellationToken = default);
}
