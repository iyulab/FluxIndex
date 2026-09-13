using FluxIndex.Core.Constants;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// Configuration options for PostgreSQL storage provider
/// </summary>
public class PostgreSQLOptions
{
    /// <summary>
    /// PostgreSQL connection string
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Embedding vector dimensions (default: 1536 for OpenAI text-embedding-3-small)
    /// </summary>
    public int EmbeddingDimensions { get; set; } = EmbeddingDefaults.DefaultVectorDimension;

    /// <summary>
    /// Provision the pgvector extension and this store's tables at start-up — on the SDK builder's
    /// <c>Build()</c> and on host start. <c>false</c> when the schema is managed externally or the role
    /// lacks CREATE EXTENSION; the store then assumes the relations exist. The builder maps
    /// <c>FluxIndexOptions.VectorStore.EnableAutoMigration</c> here. Also read by the quantized store
    /// (<c>PostgreSQLQuantizedOptions</c> inherits it).
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// Command timeout in seconds
    /// </summary>
    public int CommandTimeout { get; set; } = 30;
}