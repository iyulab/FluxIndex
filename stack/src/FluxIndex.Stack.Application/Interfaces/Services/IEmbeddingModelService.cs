using FluxIndex.Stack.Domain.Entities;

namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Service for managing embedding models and their lifecycle.
/// </summary>
public interface IEmbeddingModelService
{
    /// <summary>
    /// Gets the currently active embedding model.
    /// </summary>
    Task<EmbeddingModel?> GetActiveModelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all registered embedding models.
    /// </summary>
    Task<List<EmbeddingModel>> GetAllModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or creates an embedding model based on provider settings.
    /// </summary>
    Task<EmbeddingModel> GetOrCreateModelAsync(
        string providerName,
        string modelName,
        int dimension,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the model that matches the current AI provider settings.
    /// Creates a new model record if it doesn't exist.
    /// </summary>
    Task<EmbeddingModel> GetCurrentConfiguredModelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the active embedding model and optionally triggers reindexing.
    /// </summary>
    Task<ReindexingJob?> SetActiveModelAsync(
        Guid modelId,
        bool triggerReindexing = true,
        bool deleteOldEmbeddings = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics about embedding models including counts.
    /// </summary>
    Task<List<EmbeddingModelStats>> GetModelStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects if the configured embedding model has changed since last check.
    /// </summary>
    Task<EmbeddingModelChange?> DetectModelChangeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Statistics about an embedding model.
/// </summary>
public record EmbeddingModelStats(
    Guid ModelId,
    string ModelKey,
    string DisplayName,
    int Dimension,
    bool IsActive,
    int EmbeddingCount,
    DateTime? LastUsedAt);

/// <summary>
/// Represents a detected embedding model change.
/// </summary>
public record EmbeddingModelChange(
    EmbeddingModel? PreviousModel,
    EmbeddingModel CurrentModel,
    int AffectedChunkCount);
