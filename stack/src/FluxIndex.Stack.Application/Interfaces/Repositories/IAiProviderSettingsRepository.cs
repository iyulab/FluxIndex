using FluxIndex.Stack.Domain.Entities;

namespace FluxIndex.Stack.Application.Interfaces.Repositories;

/// <summary>
/// Repository interface for AI provider settings.
/// </summary>
public interface IAiProviderSettingsRepository
{
    Task<AiProviderSettings?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AiProviderSettings?> GetByProviderNameAsync(string providerName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiProviderSettings>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<AiProviderSettings?> GetDefaultEmbeddingProviderAsync(CancellationToken cancellationToken = default);
    Task<AiProviderSettings?> GetDefaultLlmProviderAsync(CancellationToken cancellationToken = default);
    Task AddAsync(AiProviderSettings settings, CancellationToken cancellationToken = default);
    Task UpdateAsync(AiProviderSettings settings, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task ClearDefaultEmbeddingAsync(CancellationToken cancellationToken = default);
    Task ClearDefaultLlmAsync(CancellationToken cancellationToken = default);
}
