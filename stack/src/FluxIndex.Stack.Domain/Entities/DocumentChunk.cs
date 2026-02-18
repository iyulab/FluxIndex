namespace FluxIndex.Stack.Domain.Entities;

/// <summary>
/// Represents a chunk of a document with its embedding vectors.
/// Supports multiple embedding models through ChunkEmbeddings collection.
/// </summary>
public class DocumentChunk
{
    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public int ChunkIndex { get; private set; }
    public string Content { get; private set; } = string.Empty;

    public int TokenCount { get; private set; }
    public int StartPosition { get; private set; }
    public int EndPosition { get; private set; }
    public Dictionary<string, object> Metadata { get; private set; } = new();
    public DateTime CreatedAt { get; private set; }

    // Navigation
    public Document? Document { get; private set; }

    /// <summary>
    /// Collection of embeddings for this chunk (supports multiple embedding models)
    /// </summary>
    public ICollection<ChunkEmbedding> ChunkEmbeddings { get; private set; } = new List<ChunkEmbedding>();

    private DocumentChunk() { } // EF Core

    public static DocumentChunk Create(
        Guid documentId,
        int chunkIndex,
        string content,
        int startPosition,
        int endPosition,
        int tokenCount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        return new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            ChunkIndex = chunkIndex,
            Content = content,
            StartPosition = startPosition,
            EndPosition = endPosition,
            TokenCount = tokenCount,
            Metadata = new Dictionary<string, object>(),
            CreatedAt = DateTime.UtcNow
        };
    }

    public void SetMetadata(Dictionary<string, object> metadata)
    {
        Metadata = metadata ?? new Dictionary<string, object>();
    }

    public void UpdateTokenCount(int tokenCount)
    {
        TokenCount = tokenCount;
    }

    /// <summary>
    /// Updates the chunk content.
    /// Note: caller should clear/regenerate ChunkEmbeddings after content changes.
    /// </summary>
    public void UpdateContent(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        Content = content;
    }

    /// <summary>
    /// Merges new metadata with existing metadata.
    /// </summary>
    public void MergeMetadata(Dictionary<string, object> metadata, bool overwrite = false)
    {
        if (metadata == null) return;

        foreach (var kvp in metadata)
        {
            if (overwrite || !Metadata.ContainsKey(kvp.Key))
            {
                Metadata[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// Gets the embedding for a specific model.
    /// </summary>
    public ChunkEmbedding? GetEmbeddingForModel(Guid embeddingModelId)
    {
        return ChunkEmbeddings.FirstOrDefault(e => e.EmbeddingModelId == embeddingModelId);
    }

    /// <summary>
    /// Checks if this chunk has an embedding for the specified model.
    /// </summary>
    public bool HasEmbeddingForModel(Guid embeddingModelId)
    {
        return ChunkEmbeddings.Any(e => e.EmbeddingModelId == embeddingModelId);
    }

    /// <summary>
    /// Gets all embedding model IDs that have embeddings for this chunk.
    /// </summary>
    public IEnumerable<Guid> GetEmbeddingModelIds()
    {
        return ChunkEmbeddings.Select(e => e.EmbeddingModelId);
    }
}
