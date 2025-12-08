namespace FluxIndex.Stack.Domain.Entities;

/// <summary>
/// Represents a unique embedding model configuration.
/// Tracks all embedding models that have been used in the system.
/// </summary>
public class EmbeddingModel
{
    public Guid Id { get; private set; }

    /// <summary>
    /// Unique key identifying the model (e.g., "openai:text-embedding-3-small", "local:all-MiniLM-L6-v2")
    /// </summary>
    public string ModelKey { get; private set; } = string.Empty;

    /// <summary>
    /// Provider name (e.g., "OpenAI", "Azure", "Local", "Cohere")
    /// </summary>
    public string ProviderName { get; private set; } = string.Empty;

    /// <summary>
    /// Model name (e.g., "text-embedding-3-small", "all-MiniLM-L6-v2")
    /// </summary>
    public string ModelName { get; private set; } = string.Empty;

    /// <summary>
    /// Vector dimension of this embedding model
    /// </summary>
    public int Dimension { get; private set; }

    /// <summary>
    /// Whether this model is currently the active/default embedding model
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Number of chunk embeddings using this model
    /// </summary>
    public int EmbeddingCount { get; private set; }

    /// <summary>
    /// Display name for UI
    /// </summary>
    public string DisplayName { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }
    public DateTime? LastUsedAt { get; private set; }

    // Navigation
    public ICollection<ChunkEmbedding> ChunkEmbeddings { get; private set; } = new List<ChunkEmbedding>();

    private EmbeddingModel() { }

    public static EmbeddingModel Create(
        string providerName,
        string modelName,
        int dimension,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 1);

        var modelKey = $"{providerName.ToLowerInvariant()}:{modelName.ToLowerInvariant()}";

        return new EmbeddingModel
        {
            Id = Guid.NewGuid(),
            ModelKey = modelKey,
            ProviderName = providerName,
            ModelName = modelName,
            Dimension = dimension,
            DisplayName = displayName ?? $"{providerName} - {modelName}",
            IsActive = false,
            EmbeddingCount = 0,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Generates a model key from provider and model name.
    /// </summary>
    public static string GenerateModelKey(string providerName, string modelName)
    {
        return $"{providerName.ToLowerInvariant()}:{modelName.ToLowerInvariant()}";
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        if (isActive)
        {
            LastUsedAt = DateTime.UtcNow;
        }
    }

    public void IncrementEmbeddingCount(int count = 1)
    {
        EmbeddingCount += count;
        LastUsedAt = DateTime.UtcNow;
    }

    public void DecrementEmbeddingCount(int count = 1)
    {
        EmbeddingCount = Math.Max(0, EmbeddingCount - count);
    }

    public void UpdateDisplayName(string displayName)
    {
        DisplayName = displayName;
    }

    /// <summary>
    /// Marks this model as used by updating LastUsedAt.
    /// </summary>
    public void MarkUsed()
    {
        LastUsedAt = DateTime.UtcNow;
    }
}
