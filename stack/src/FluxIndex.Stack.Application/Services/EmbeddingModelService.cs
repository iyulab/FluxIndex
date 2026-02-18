using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Application.Services;

/// <summary>
/// Service implementation for managing embedding models and their lifecycle.
/// </summary>
public partial class EmbeddingModelService : IEmbeddingModelService
{
    private readonly IEmbeddingModelRepository _modelRepository;
    private readonly IChunkEmbeddingRepository _embeddingRepository;
    private readonly IAiProviderSettingsRepository _settingsRepository;
    private readonly IReindexingService _reindexingService;
    private readonly ILogger<EmbeddingModelService> _logger;

    // Known provider dimension mappings
    private static readonly Dictionary<string, int> KnownModelDimensions = new()
    {
        // OpenAI
        ["text-embedding-3-small"] = 1536,
        ["text-embedding-3-large"] = 3072,
        ["text-embedding-ada-002"] = 1536,

        // Cohere
        ["embed-english-v3.0"] = 1024,
        ["embed-multilingual-v3.0"] = 1024,
        ["embed-english-light-v3.0"] = 384,

        // Google
        ["text-embedding-004"] = 768,
        ["text-multilingual-embedding-002"] = 768,

        // Local
        ["nomic-embed-text"] = 768,
        ["mxbai-embed-large"] = 1024,
        ["all-minilm"] = 384,
        ["all-MiniLM-L6-v2"] = 384
    };

    public EmbeddingModelService(
        IEmbeddingModelRepository modelRepository,
        IChunkEmbeddingRepository embeddingRepository,
        IAiProviderSettingsRepository settingsRepository,
        IReindexingService reindexingService,
        ILogger<EmbeddingModelService> logger)
    {
        _modelRepository = modelRepository;
        _embeddingRepository = embeddingRepository;
        _settingsRepository = settingsRepository;
        _reindexingService = reindexingService;
        _logger = logger;
    }

    public async Task<EmbeddingModel?> GetActiveModelAsync(CancellationToken cancellationToken = default)
    {
        return await _modelRepository.GetActiveModelAsync(cancellationToken);
    }

    public async Task<List<EmbeddingModel>> GetAllModelsAsync(CancellationToken cancellationToken = default)
    {
        return await _modelRepository.GetAllAsync(cancellationToken);
    }

    public async Task<EmbeddingModel> GetOrCreateModelAsync(
        string providerName,
        string modelName,
        int dimension,
        CancellationToken cancellationToken = default)
    {
        return await _modelRepository.GetOrCreateAsync(providerName, modelName, dimension, cancellationToken);
    }

    public async Task<EmbeddingModel> GetCurrentConfiguredModelAsync(CancellationToken cancellationToken = default)
    {
        // Get the current default embedding provider settings
        var allSettings = await _settingsRepository.GetAllAsync(cancellationToken);
        var defaultProvider = allSettings.FirstOrDefault(s => s.IsDefaultEmbedding && s.IsEnabled);

        string providerName;
        string modelName;
        int dimension;

        if (defaultProvider != null && !string.IsNullOrWhiteSpace(defaultProvider.ApiKey))
        {
            providerName = defaultProvider.ProviderName;
            modelName = defaultProvider.EmbeddingModel ?? GetDefaultModelForProvider(providerName);
            dimension = GetDimensionForModel(modelName);
        }
        else
        {
            // Fall back to local embedder
            providerName = "Local";
            modelName = "all-MiniLM-L6-v2";
            dimension = 384;
        }

        return await GetOrCreateModelAsync(providerName, modelName, dimension, cancellationToken);
    }

    public async Task<ReindexingJob?> SetActiveModelAsync(
        Guid modelId,
        bool triggerReindexing = true,
        bool deleteOldEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        var currentActive = await _modelRepository.GetActiveModelAsync(cancellationToken);
        var targetModel = await _modelRepository.GetByIdAsync(modelId, cancellationToken);

        if (targetModel == null)
        {
            throw new ArgumentException($"Embedding model with ID {modelId} not found");
        }

        // If the target model is already active, no action needed
        if (currentActive?.Id == modelId)
        {
            LogModelAlreadyActive(_logger, targetModel.ModelKey);
            return null;
        }

        // Set the new active model
        await _modelRepository.SetActiveModelAsync(modelId, cancellationToken);
        LogActiveModelSet(_logger, targetModel.ModelKey);

        // Trigger reindexing if requested
        if (triggerReindexing)
        {
            var job = await _reindexingService.CreateSystemReindexingJobAsync(
                modelId,
                currentActive?.Id,
                deleteOldEmbeddings,
                priority: 10, // High priority for model change
                cancellationToken);

            var oldModelKey = currentActive?.ModelKey ?? "none";
            LogReindexingJobCreated(_logger, job.Id, oldModelKey, targetModel.ModelKey);

            return job;
        }

        return null;
    }

    public async Task<List<EmbeddingModelStats>> GetModelStatsAsync(CancellationToken cancellationToken = default)
    {
        var models = await _modelRepository.GetAllAsync(cancellationToken);
        var embeddingCounts = await _modelRepository.GetEmbeddingCountsAsync(cancellationToken);

        var stats = new List<EmbeddingModelStats>();
        foreach (var model in models)
        {
            embeddingCounts.TryGetValue(model.Id, out var count);
            stats.Add(new EmbeddingModelStats(
                model.Id,
                model.ModelKey,
                model.DisplayName ?? model.ModelName,
                model.Dimension,
                model.IsActive,
                count,
                model.LastUsedAt));
        }

        return stats;
    }

    public async Task<EmbeddingModelChange?> DetectModelChangeAsync(CancellationToken cancellationToken = default)
    {
        var currentActive = await _modelRepository.GetActiveModelAsync(cancellationToken);
        var configuredModel = await GetCurrentConfiguredModelAsync(cancellationToken);

        // No change if they're the same
        if (currentActive?.Id == configuredModel.Id)
        {
            return null;
        }

        // Calculate affected chunks (those that need reindexing)
        var chunkIdsWithoutEmbedding = await _embeddingRepository.GetChunkIdsWithoutEmbeddingAsync(
            configuredModel.Id,
            limit: null,
            cancellationToken: cancellationToken);

        var oldModelKey = currentActive?.ModelKey ?? "none";
        var affectedCount = chunkIdsWithoutEmbedding.Count;
        LogModelChangeDetected(_logger, oldModelKey, configuredModel.ModelKey, affectedCount);

        return new EmbeddingModelChange(
            currentActive,
            configuredModel,
            chunkIdsWithoutEmbedding.Count);
    }

    private static string GetDefaultModelForProvider(string providerName)
    {
        return providerName switch
        {
            "OpenAI" or "Azure" => "text-embedding-3-small",
            "Cohere" => "embed-english-v3.0",
            "Google" => "text-embedding-004",
            "Local" => "all-MiniLM-L6-v2",
            _ => "text-embedding-3-small"
        };
    }

    private static int GetDimensionForModel(string modelName)
    {
        if (KnownModelDimensions.TryGetValue(modelName, out var dimension))
        {
            return dimension;
        }

        // Default to 1536 for unknown models (OpenAI default)
        return 1536;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Model {ModelKey} is already active")]
    private static partial void LogModelAlreadyActive(ILogger logger, string modelKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Set {ModelKey} as active embedding model")]
    private static partial void LogActiveModelSet(ILogger logger, string modelKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created reindexing job {JobId} to migrate from {OldModel} to {NewModel}")]
    private static partial void LogReindexingJobCreated(ILogger logger, Guid jobId, string oldModel, string newModel);

    [LoggerMessage(Level = LogLevel.Information, Message = "Detected embedding model change: {OldModel} -> {NewModel}, {AffectedCount} chunks affected")]
    private static partial void LogModelChangeDetected(ILogger logger, string oldModel, string newModel, int affectedCount);

    #endregion
}
