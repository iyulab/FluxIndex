using FluxIndex.Core.Constants;

namespace FluxIndex.Storage.Qdrant;

/// <summary>
/// Collection naming strategy for Qdrant vector store.
/// </summary>
public enum CollectionNamingStrategy
{
    /// <summary>
    /// Dynamic naming: {baseName}_{dimension}.
    /// Separates collections by embedding dimension only.
    /// Does NOT distinguish different models with the same dimension.
    /// </summary>
    [Obsolete("Use ModelFingerprint instead. DimensionSuffix cannot distinguish different models with the same dimension.")]
    DimensionSuffix,

    /// <summary>
    /// Fixed naming: uses the exact name specified (legacy compatibility).
    /// Requires explicit VectorSize configuration.
    /// </summary>
    Fixed,

    /// <summary>
    /// Dynamic naming: {baseName}_{fingerprint} (recommended default).
    /// Uses the embedding model's fingerprint (SHA256 hash of Provider:Model) to identify collections.
    /// Automatically creates separate collections per embedding model, regardless of dimension.
    /// </summary>
    ModelFingerprint
}

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
    public bool UseHttps { get; set; }

    /// <summary>
    /// API key for Qdrant Cloud authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Base collection name. Actual name may include dimension suffix based on NamingStrategy.
    /// With DimensionSuffix strategy: "fluxindex_chunks" becomes "fluxindex_chunks_384" for 384-dim vectors.
    /// </summary>
    public string BaseCollectionName { get; set; } = "fluxindex_chunks";

    /// <summary>
    /// Collection name to store vectors (alias for BaseCollectionName for backward compatibility).
    /// Prefer using BaseCollectionName with NamingStrategy for new code.
    /// </summary>
    public string CollectionName
    {
        get => BaseCollectionName;
        set => BaseCollectionName = value;
    }

    /// <summary>
    /// Collection naming strategy. Default: ModelFingerprint (recommended).
    /// - ModelFingerprint: {baseName}_{fingerprint} - auto-adapts to embedding model identity
    /// - DimensionSuffix: {baseName}_{dimension} - legacy, cannot distinguish same-dimension models
    /// - Fixed: exact name specified - requires explicit VectorSize
    /// </summary>
    public CollectionNamingStrategy NamingStrategy { get; set; } = CollectionNamingStrategy.ModelFingerprint;

    /// <summary>
    /// Vector dimension size. Only used when NamingStrategy is Fixed.
    /// With DimensionSuffix strategy, dimension is auto-detected from embeddings.
    /// </summary>
    public int VectorSize { get; set; } = EmbeddingDefaults.DefaultVectorDimension;

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
    public bool OnDiskPayload { get; set; }

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
