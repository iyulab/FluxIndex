namespace FluxIndex.Stack.Domain.Entities;

/// <summary>
/// Status of a reindexing job
/// </summary>
public enum ReindexingJobStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// Type of reindexing scope
/// </summary>
public enum ReindexingScope
{
    /// <summary>
    /// Reindex a single chunk
    /// </summary>
    Chunk = 0,

    /// <summary>
    /// Reindex all chunks in a document
    /// </summary>
    Document = 1,

    /// <summary>
    /// Reindex all chunks in a collection
    /// </summary>
    Collection = 2,

    /// <summary>
    /// Reindex all chunks in the system
    /// </summary>
    System = 3
}

/// <summary>
/// Represents a reindexing job for migrating embeddings to a new model.
/// </summary>
public class ReindexingJob
{
    public Guid Id { get; private set; }

    /// <summary>
    /// The scope of this reindexing job
    /// </summary>
    public ReindexingScope Scope { get; private set; }

    /// <summary>
    /// Target entity ID based on scope (ChunkId, DocumentId, CollectionId, or null for System)
    /// </summary>
    public Guid? TargetId { get; private set; }

    /// <summary>
    /// Source embedding model (null if no previous embedding)
    /// </summary>
    public Guid? SourceModelId { get; private set; }

    /// <summary>
    /// Target embedding model to migrate to
    /// </summary>
    public Guid TargetModelId { get; private set; }

    /// <summary>
    /// Current status of the job
    /// </summary>
    public ReindexingJobStatus Status { get; private set; }

    /// <summary>
    /// Total number of chunks to process
    /// </summary>
    public int TotalChunks { get; private set; }

    /// <summary>
    /// Number of chunks processed
    /// </summary>
    public int ProcessedChunks { get; private set; }

    /// <summary>
    /// Number of chunks that failed
    /// </summary>
    public int FailedChunks { get; private set; }

    /// <summary>
    /// Priority (higher = processed first)
    /// </summary>
    public int Priority { get; private set; }

    /// <summary>
    /// Whether to delete old embeddings after successful reindexing
    /// </summary>
    public bool DeleteOldEmbeddings { get; private set; }

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    // Navigation
    public EmbeddingModel? SourceModel { get; private set; }
    public EmbeddingModel? TargetModel { get; private set; }

    // Convenience properties for accessing TargetId based on Scope
    public Guid? ChunkId => Scope == ReindexingScope.Chunk ? TargetId : null;
    public Guid? DocumentId => Scope == ReindexingScope.Document ? TargetId : null;
    public Guid? CollectionId => Scope == ReindexingScope.Collection ? TargetId : null;

    private ReindexingJob() { }

    public static ReindexingJob CreateForChunk(
        Guid chunkId,
        Guid targetModelId,
        Guid? sourceModelId = null,
        int priority = 0,
        bool deleteOldEmbeddings = false)
    {
        return new ReindexingJob
        {
            Id = Guid.NewGuid(),
            Scope = ReindexingScope.Chunk,
            TargetId = chunkId,
            SourceModelId = sourceModelId,
            TargetModelId = targetModelId,
            Status = ReindexingJobStatus.Pending,
            TotalChunks = 1,
            ProcessedChunks = 0,
            FailedChunks = 0,
            Priority = priority,
            DeleteOldEmbeddings = deleteOldEmbeddings,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static ReindexingJob CreateForDocument(
        Guid documentId,
        Guid targetModelId,
        int totalChunks,
        Guid? sourceModelId = null,
        int priority = 0,
        bool deleteOldEmbeddings = false)
    {
        return new ReindexingJob
        {
            Id = Guid.NewGuid(),
            Scope = ReindexingScope.Document,
            TargetId = documentId,
            SourceModelId = sourceModelId,
            TargetModelId = targetModelId,
            Status = ReindexingJobStatus.Pending,
            TotalChunks = totalChunks,
            ProcessedChunks = 0,
            FailedChunks = 0,
            Priority = priority,
            DeleteOldEmbeddings = deleteOldEmbeddings,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static ReindexingJob CreateForCollection(
        Guid collectionId,
        Guid targetModelId,
        int totalChunks,
        Guid? sourceModelId = null,
        int priority = 0,
        bool deleteOldEmbeddings = false)
    {
        return new ReindexingJob
        {
            Id = Guid.NewGuid(),
            Scope = ReindexingScope.Collection,
            TargetId = collectionId,
            SourceModelId = sourceModelId,
            TargetModelId = targetModelId,
            Status = ReindexingJobStatus.Pending,
            TotalChunks = totalChunks,
            ProcessedChunks = 0,
            FailedChunks = 0,
            Priority = priority,
            DeleteOldEmbeddings = deleteOldEmbeddings,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static ReindexingJob CreateForSystem(
        Guid targetModelId,
        int totalChunks,
        Guid? sourceModelId = null,
        int priority = 0,
        bool deleteOldEmbeddings = false)
    {
        return new ReindexingJob
        {
            Id = Guid.NewGuid(),
            Scope = ReindexingScope.System,
            TargetId = null,
            SourceModelId = sourceModelId,
            TargetModelId = targetModelId,
            Status = ReindexingJobStatus.Pending,
            TotalChunks = totalChunks,
            ProcessedChunks = 0,
            FailedChunks = 0,
            Priority = priority,
            DeleteOldEmbeddings = deleteOldEmbeddings,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Start()
    {
        Status = ReindexingJobStatus.Processing;
        StartedAt = DateTime.UtcNow;
    }

    public void UpdateProgress(int processedChunks, int failedChunks = 0)
    {
        ProcessedChunks = processedChunks;
        FailedChunks = failedChunks;
    }

    public void IncrementProgress(bool success = true)
    {
        if (success)
        {
            ProcessedChunks++;
        }
        else
        {
            FailedChunks++;
        }
    }

    public void Complete()
    {
        Status = ReindexingJobStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void Fail(string errorMessage)
    {
        Status = ReindexingJobStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        Status = ReindexingJobStatus.Cancelled;
        CompletedAt = DateTime.UtcNow;
    }

    public void SetTotalChunks(int totalChunks)
    {
        TotalChunks = totalChunks;
    }

    public double GetProgressPercentage()
    {
        if (TotalChunks == 0) return 0;
        return (double)(ProcessedChunks + FailedChunks) / TotalChunks * 100;
    }
}
