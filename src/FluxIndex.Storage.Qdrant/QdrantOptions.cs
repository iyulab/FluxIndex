namespace FluxIndex.Storage.Qdrant;

/// <summary>
/// Configuration options for Qdrant vector store.
/// </summary>
public class QdrantOptions
{
    /// <summary>
    /// Qdrant server host (e.g., "localhost").
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// Qdrant gRPC port (default: 6334).
    /// </summary>
    public int GrpcPort { get; set; } = 6334;

    /// <summary>
    /// Qdrant HTTP port for REST API (default: 6333).
    /// </summary>
    public int HttpPort { get; set; } = 6333;

    /// <summary>
    /// Whether to use HTTPS for connection.
    /// </summary>
    public bool UseHttps { get; set; } = false;

    /// <summary>
    /// API key for Qdrant Cloud authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Collection name to store vectors.
    /// </summary>
    public string CollectionName { get; set; } = "fluxindex_chunks";

    /// <summary>
    /// Vector dimension size.
    /// </summary>
    public int VectorSize { get; set; } = 1536;

    /// <summary>
    /// Distance metric for similarity search.
    /// </summary>
    public QdrantDistanceMetric DistanceMetric { get; set; } = QdrantDistanceMetric.Cosine;

    /// <summary>
    /// Whether to create collection on startup if it doesn't exist.
    /// </summary>
    public bool CreateCollectionOnStartup { get; set; } = true;

    /// <summary>
    /// HNSW index parameter: Number of edges per node in graph.
    /// </summary>
    public int HnswM { get; set; } = 16;

    /// <summary>
    /// HNSW index parameter: Number of candidates to consider during construction.
    /// </summary>
    public int HnswEfConstruct { get; set; } = 100;

    /// <summary>
    /// On-disk payload storage for large metadata.
    /// </summary>
    public bool OnDiskPayload { get; set; } = false;

    /// <summary>
    /// Connection timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Distance metric for vector similarity search.
/// </summary>
public enum QdrantDistanceMetric
{
    /// <summary>
    /// Cosine similarity (recommended for normalized vectors).
    /// </summary>
    Cosine,

    /// <summary>
    /// Euclidean distance.
    /// </summary>
    Euclid,

    /// <summary>
    /// Dot product similarity.
    /// </summary>
    Dot
}
