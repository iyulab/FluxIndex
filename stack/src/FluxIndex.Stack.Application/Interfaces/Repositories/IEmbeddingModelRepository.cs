using FluxIndex.Stack.Domain.Entities;

namespace FluxIndex.Stack.Application.Interfaces.Repositories;

/// <summary>
/// Repository interface for EmbeddingModel entity.
/// </summary>
public interface IEmbeddingModelRepository
{
    Task<EmbeddingModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an embedding model by its unique key (provider:model format).
    /// </summary>
    Task<EmbeddingModel?> GetByModelKeyAsync(string modelKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the currently active embedding model.
    /// </summary>
    Task<EmbeddingModel?> GetActiveModelAsync(CancellationToken cancellationToken = default);

    Task<List<EmbeddingModel>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(EmbeddingModel model, CancellationToken cancellationToken = default);

    Task UpdateAsync(EmbeddingModel model, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or creates an embedding model by provider and model name.
    /// </summary>
    Task<EmbeddingModel> GetOrCreateAsync(
        string providerName,
        string modelName,
        int dimension,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the specified model as active and deactivates all others.
    /// </summary>
    Task SetActiveModelAsync(Guid modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics about embedding models.
    /// </summary>
    Task<Dictionary<Guid, int>> GetEmbeddingCountsAsync(CancellationToken cancellationToken = default);
}
