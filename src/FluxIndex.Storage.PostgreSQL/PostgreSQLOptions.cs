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
    public int EmbeddingDimensions { get; set; } = 1536;

    /// <summary>
    /// Auto migrate database on startup
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// Command timeout in seconds
    /// </summary>
    public int CommandTimeout { get; set; } = 30;
}