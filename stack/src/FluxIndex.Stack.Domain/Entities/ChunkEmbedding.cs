using Pgvector;

namespace FluxIndex.Stack.Domain.Entities;

/// <summary>
/// Represents an embedding vector for a document chunk.
/// Separates embedding storage from chunk content, allowing multiple embeddings per chunk
/// with different models.
/// </summary>
public class ChunkEmbedding
{
    public Guid Id { get; private set; }

    /// <summary>
    /// Reference to the document chunk
    /// </summary>
    public Guid ChunkId { get; private set; }

    /// <summary>
    /// Reference to the embedding model used
    /// </summary>
    public Guid EmbeddingModelId { get; private set; }

    /// <summary>
    /// The embedding vector
    /// </summary>
    public Vector Embedding { get; private set; } = null!;

    /// <summary>
    /// Vector dimension (denormalized for query optimization)
    /// </summary>
    public int Dimension { get; private set; }

    public DateTime CreatedAt { get; private set; }

    // Navigation properties
    public DocumentChunk? Chunk { get; private set; }
    public EmbeddingModel? Model { get; private set; }

    private ChunkEmbedding() { }

    public static ChunkEmbedding Create(
        Guid chunkId,
        Guid embeddingModelId,
        float[] embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentOutOfRangeException.ThrowIfLessThan(embedding.Length, 1);

        return new ChunkEmbedding
        {
            Id = Guid.NewGuid(),
            ChunkId = chunkId,
            EmbeddingModelId = embeddingModelId,
            Embedding = new Vector(embedding),
            Dimension = embedding.Length,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static ChunkEmbedding Create(
        Guid chunkId,
        Guid embeddingModelId,
        Vector embedding,
        int dimension)
    {
        ArgumentNullException.ThrowIfNull(embedding);

        return new ChunkEmbedding
        {
            Id = Guid.NewGuid(),
            ChunkId = chunkId,
            EmbeddingModelId = embeddingModelId,
            Embedding = embedding,
            Dimension = dimension,
            CreatedAt = DateTime.UtcNow
        };
    }

    public float[] GetEmbeddingArray()
    {
        return Embedding.ToArray();
    }

    public void UpdateEmbedding(float[] embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        Embedding = new Vector(embedding);
        Dimension = embedding.Length;
    }
}
