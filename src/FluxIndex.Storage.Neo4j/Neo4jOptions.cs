namespace FluxIndex.Storage.Neo4j;

/// <summary>
/// Configuration options for Neo4j graph store.
/// </summary>
public class Neo4jOptions
{
    /// <summary>
    /// Neo4j Bolt URI (e.g., "bolt://localhost:7687" or "neo4j://localhost:7687")
    /// </summary>
    public string Uri { get; set; } = "bolt://localhost:7687";

    /// <summary>
    /// Neo4j username for authentication.
    /// </summary>
    public string Username { get; set; } = "neo4j";

    /// <summary>
    /// Neo4j password for authentication.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Database name to use. Leave empty for default database.
    /// </summary>
    public string? Database { get; set; }

    /// <summary>
    /// Connection timeout in seconds.
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum connection pool size.
    /// </summary>
    public int MaxConnectionPoolSize { get; set; } = 100;

    /// <summary>
    /// Whether to create indexes on first connection.
    /// </summary>
    public bool CreateIndexesOnStartup { get; set; } = true;

    /// <summary>
    /// Prefix for node labels to namespace entities.
    /// </summary>
    public string NodeLabelPrefix { get; set; } = "FluxIndex_";

    /// <summary>
    /// Whether to encrypt connections (for production environments).
    /// </summary>
    public bool Encrypted { get; set; } = false;
}
