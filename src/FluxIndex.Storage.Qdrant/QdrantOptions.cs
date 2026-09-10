using FluxIndex.Core.Constants;

namespace FluxIndex.Storage.Qdrant;

/// <summary>
/// Collection naming strategy for Qdrant vector store.
/// </summary>
public enum CollectionNamingStrategy
{
    // Values are assigned explicitly and 0 is deliberately left unused: a deprecated dimension-suffix
    // member used to occupy it, and renumbering on its removal would silently change the meaning of
    // any configuration that binds this strategy as a number rather than a name.

    /// <summary>
    /// Fixed naming: uses the exact name specified (legacy compatibility).
    /// Requires explicit VectorSize configuration.
    /// </summary>
    Fixed = 1,

    /// <summary>
    /// Dynamic naming: {baseName}_{fingerprint} (recommended default).
    /// Uses the embedding model's fingerprint (SHA256 hash of Provider:Model) to identify collections.
    /// Automatically creates separate collections per embedding model, regardless of dimension.
    /// Until an embedding identity is bound, falls back to a dimension suffix.
    /// </summary>
    ModelFingerprint = 2
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
    /// Base collection name. Actual name may include a suffix based on NamingStrategy.
    /// With ModelFingerprint and no identity bound yet: "fluxindex_chunks" becomes
    /// "fluxindex_chunks_384" for 384-dim vectors.
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
    /// - Fixed: exact name specified - requires explicit VectorSize
    /// </summary>
    /// <remarks>
    /// Changing this on an existing deployment - or changing the bound embedding identity - resolves
    /// the same logical index to a different collection name. The data already written stays in the
    /// old collection and is not migrated: a strategy change means re-indexing, or migrating the
    /// collection explicitly. The store warns at startup when the collection it is about to serve is
    /// empty while a sibling of the same base name still holds points; see
    /// <see cref="FailOnCollectionMismatch"/> to turn that warning into a startup failure.
    /// </remarks>
    public CollectionNamingStrategy NamingStrategy { get; set; } = CollectionNamingStrategy.ModelFingerprint;

    /// <summary>
    /// Whether serving an empty collection that has populated siblings should fail startup instead of
    /// logging a warning. Default: false.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The situation this covers is a naming change on an existing deployment (see
    /// <see cref="NamingStrategy"/>): the store creates an empty collection, reports ready, and every
    /// search returns zero results while the previous collection still holds the data. Nothing
    /// throws, so the deployment looks healthy.
    /// </para>
    /// <para>
    /// The default is false because an empty collection is also the ordinary state of a deployment
    /// that simply has not indexed anything yet. A deployment that would rather not start than serve
    /// empty results should set this to true.
    /// </para>
    /// <para>
    /// A collection created during this initialization is left in place when it fails - deleting it
    /// would put a destructive call on the startup path. The check clears itself once the collection
    /// holds data, so nothing has to be suppressed after a deliberate re-index.
    /// </para>
    /// </remarks>
    public bool FailOnCollectionMismatch { get; set; }

    /// <summary>
    /// Vector dimension size. Only used when NamingStrategy is Fixed.
    /// With ModelFingerprint, dimension is auto-detected from embeddings.
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
