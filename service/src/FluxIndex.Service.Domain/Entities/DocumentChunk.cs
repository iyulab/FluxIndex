using Pgvector;

namespace FluxIndex.Service.Domain.Entities;

/// <summary>
/// Represents a chunk of a document with its embedding vector.
/// </summary>
public class DocumentChunk
{
    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public int ChunkIndex { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public Vector? Embedding { get; private set; }
    public int TokenCount { get; private set; }
    public int StartPosition { get; private set; }
    public int EndPosition { get; private set; }
    public Dictionary<string, object> Metadata { get; private set; } = new();
    public DateTime CreatedAt { get; private set; }

    // Navigation
    public Document? Document { get; private set; }

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

    public void SetEmbedding(float[] embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        Embedding = new Vector(embedding);
    }

    public void SetEmbedding(Vector embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        Embedding = embedding;
    }

    public float[]? GetEmbeddingArray()
    {
        return Embedding?.ToArray();
    }

    public void SetMetadata(Dictionary<string, object> metadata)
    {
        Metadata = metadata ?? new Dictionary<string, object>();
    }

    public void UpdateTokenCount(int tokenCount)
    {
        TokenCount = tokenCount;
    }
}
