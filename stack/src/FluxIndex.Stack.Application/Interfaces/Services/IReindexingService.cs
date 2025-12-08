using FluxIndex.Stack.Domain.Entities;

namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Service for managing reindexing operations when embedding models change.
/// </summary>
public interface IReindexingService
{
    /// <summary>
    /// Creates a system-wide reindexing job to migrate all chunks to a new model.
    /// </summary>
    Task<ReindexingJob> CreateSystemReindexingJobAsync(
        Guid targetModelId,
        Guid? sourceModelId = null,
        bool deleteOldEmbeddings = false,
        int priority = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a reindexing job for a specific collection.
    /// </summary>
    Task<ReindexingJob> CreateCollectionReindexingJobAsync(
        Guid collectionId,
        Guid targetModelId,
        Guid? sourceModelId = null,
        bool deleteOldEmbeddings = false,
        int priority = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a reindexing job for a specific document.
    /// </summary>
    Task<ReindexingJob> CreateDocumentReindexingJobAsync(
        Guid documentId,
        Guid targetModelId,
        Guid? sourceModelId = null,
        bool deleteOldEmbeddings = false,
        int priority = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes the next pending reindexing job.
    /// Returns true if a job was processed, false if no pending jobs.
    /// </summary>
    Task<bool> ProcessNextJobAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a specific reindexing job.
    /// </summary>
    Task ProcessJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a pending or processing job.
    /// </summary>
    Task CancelJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current status of reindexing operations.
    /// </summary>
    Task<ReindexingStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets job by ID with full details.
    /// </summary>
    Task<ReindexingJob?> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent reindexing jobs with pagination.
    /// </summary>
    Task<(List<ReindexingJob> Items, int TotalCount)> GetJobsAsync(
        int page = 1,
        int pageSize = 20,
        ReindexingJobStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reindexes a single chunk with the current active model.
    /// Used for on-demand reindexing during search.
    /// </summary>
    Task<ChunkEmbedding?> ReindexChunkAsync(
        Guid chunkId,
        Guid targetModelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures chunks have embeddings for the specified model.
    /// Returns chunks that already have embeddings and queues missing ones for reindexing.
    /// </summary>
    Task<ReindexingEnsureResult> EnsureEmbeddingsAsync(
        IEnumerable<Guid> chunkIds,
        Guid embeddingModelId,
        bool waitForCompletion = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Current status of reindexing operations.
/// </summary>
public record ReindexingStatus(
    bool IsProcessing,
    ReindexingJob? CurrentJob,
    int PendingJobCount,
    int TotalChunksQueued,
    int TotalChunksProcessed,
    double OverallProgress);

/// <summary>
/// Result of ensuring embeddings exist.
/// </summary>
public record ReindexingEnsureResult(
    List<Guid> ReadyChunkIds,
    List<Guid> QueuedChunkIds,
    ReindexingJob? CreatedJob);
