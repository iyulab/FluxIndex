using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Application.Services;

/// <summary>
/// Service implementation for managing reindexing operations when embedding models change.
/// </summary>
public class ReindexingService : IReindexingService
{
    private readonly IReindexingJobRepository _jobRepository;
    private readonly IChunkEmbeddingRepository _embeddingRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IEmbeddingModelRepository _modelRepository;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<ReindexingService> _logger;

    private const int DefaultBatchSize = 50;

    public ReindexingService(
        IReindexingJobRepository jobRepository,
        IChunkEmbeddingRepository embeddingRepository,
        IDocumentChunkRepository chunkRepository,
        IDocumentRepository documentRepository,
        IEmbeddingModelRepository modelRepository,
        IEmbeddingProvider embeddingProvider,
        ILogger<ReindexingService> logger)
    {
        _jobRepository = jobRepository;
        _embeddingRepository = embeddingRepository;
        _chunkRepository = chunkRepository;
        _documentRepository = documentRepository;
        _modelRepository = modelRepository;
        _embeddingProvider = embeddingProvider;
        _logger = logger;
    }

    public async Task<ReindexingJob> CreateSystemReindexingJobAsync(
        Guid targetModelId,
        Guid? sourceModelId = null,
        bool deleteOldEmbeddings = false,
        int priority = 0,
        CancellationToken cancellationToken = default)
    {
        // Check if there's already an active system reindexing job for this model
        if (await _jobRepository.HasActiveJobAsync(ReindexingScope.System, null, targetModelId, cancellationToken))
        {
            throw new InvalidOperationException($"A system reindexing job for target model {targetModelId} is already active");
        }

        // Count total chunks to be processed
        var totalChunks = await _chunkRepository.GetCountAsync(cancellationToken: cancellationToken);

        var job = ReindexingJob.CreateForSystem(targetModelId, totalChunks, sourceModelId, priority, deleteOldEmbeddings);

        await _jobRepository.AddAsync(job, cancellationToken);

        _logger.LogInformation(
            "Created system reindexing job {JobId} for {TotalChunks} chunks",
            job.Id,
            totalChunks);

        return job;
    }

    public async Task<ReindexingJob> CreateCollectionReindexingJobAsync(
        Guid collectionId,
        Guid targetModelId,
        Guid? sourceModelId = null,
        bool deleteOldEmbeddings = false,
        int priority = 0,
        CancellationToken cancellationToken = default)
    {
        // Check if there's already an active collection reindexing job for this model
        if (await _jobRepository.HasActiveJobAsync(ReindexingScope.Collection, collectionId, targetModelId, cancellationToken))
        {
            throw new InvalidOperationException($"A collection reindexing job for collection {collectionId} and target model {targetModelId} is already active");
        }

        // Get documents in this collection and count their chunks
        var documents = await _documentRepository.GetByCollectionIdAsync(collectionId, cancellationToken);
        var totalChunks = 0;
        foreach (var doc in documents)
        {
            totalChunks += await _chunkRepository.GetCountAsync(doc.Id, cancellationToken);
        }

        var job = ReindexingJob.CreateForCollection(collectionId, targetModelId, totalChunks, sourceModelId, priority, deleteOldEmbeddings);

        await _jobRepository.AddAsync(job, cancellationToken);

        _logger.LogInformation(
            "Created collection reindexing job {JobId} for collection {CollectionId} with {TotalChunks} chunks",
            job.Id,
            collectionId,
            totalChunks);

        return job;
    }

    public async Task<ReindexingJob> CreateDocumentReindexingJobAsync(
        Guid documentId,
        Guid targetModelId,
        Guid? sourceModelId = null,
        bool deleteOldEmbeddings = false,
        int priority = 0,
        CancellationToken cancellationToken = default)
    {
        // Check if there's already an active document reindexing job for this model
        if (await _jobRepository.HasActiveJobAsync(ReindexingScope.Document, documentId, targetModelId, cancellationToken))
        {
            throw new InvalidOperationException($"A document reindexing job for document {documentId} and target model {targetModelId} is already active");
        }

        var totalChunks = await _chunkRepository.GetCountAsync(documentId, cancellationToken);

        var job = ReindexingJob.CreateForDocument(documentId, targetModelId, totalChunks, sourceModelId, priority, deleteOldEmbeddings);

        await _jobRepository.AddAsync(job, cancellationToken);

        _logger.LogInformation(
            "Created document reindexing job {JobId} for document {DocumentId} with {TotalChunks} chunks",
            job.Id,
            documentId,
            totalChunks);

        return job;
    }

    public async Task<bool> ProcessNextJobAsync(CancellationToken cancellationToken = default)
    {
        var job = await _jobRepository.GetNextPendingJobAsync(cancellationToken);
        if (job == null)
        {
            return false;
        }

        await ProcessJobAsync(job.Id, cancellationToken);
        return true;
    }

    public async Task ProcessJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _jobRepository.GetByIdAsync(jobId, cancellationToken);
        if (job == null)
        {
            throw new ArgumentException($"Reindexing job {jobId} not found");
        }

        if (job.Status != ReindexingJobStatus.Pending && job.Status != ReindexingJobStatus.Processing)
        {
            _logger.LogWarning("Job {JobId} is in status {Status}, cannot process", jobId, job.Status);
            return;
        }

        try
        {
            job.Start();
            await _jobRepository.UpdateAsync(job, cancellationToken);

            _logger.LogInformation("Starting reindexing job {JobId}", jobId);

            // Get chunk IDs to process based on scope
            var chunkIds = await GetChunkIdsForJobAsync(job, cancellationToken);

            // Filter to only chunks that don't have embeddings for the target model
            var chunkIdsToProcess = await _embeddingRepository.GetChunkIdsWithoutEmbeddingAsync(
                job.TargetModelId,
                documentIds: null,
                limit: null,
                cancellationToken);

            // Intersect with job scope
            var targetChunkIds = chunkIds.Intersect(chunkIdsToProcess).ToList();

            _logger.LogInformation(
                "Job {JobId}: {Count} chunks need embeddings for model {ModelId}",
                jobId,
                targetChunkIds.Count,
                job.TargetModelId);

            // Process in batches
            var processedCount = 0;
            for (int i = 0; i < targetChunkIds.Count; i += DefaultBatchSize)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Job {JobId} cancelled", jobId);
                    break;
                }

                var batchIds = targetChunkIds.Skip(i).Take(DefaultBatchSize).ToList();
                await ProcessBatchAsync(batchIds, job.TargetModelId, cancellationToken);

                processedCount += batchIds.Count;
                job.UpdateProgress(processedCount);
                await _jobRepository.UpdateAsync(job, cancellationToken);

                _logger.LogDebug(
                    "Job {JobId}: Processed {Processed}/{Total} chunks",
                    jobId,
                    processedCount,
                    targetChunkIds.Count);
            }

            // Delete old embeddings if requested
            if (job.DeleteOldEmbeddings && job.SourceModelId.HasValue)
            {
                await DeleteOldEmbeddingsAsync(chunkIds, job.SourceModelId.Value, cancellationToken);
            }

            job.Complete();
            await _jobRepository.UpdateAsync(job, cancellationToken);

            _logger.LogInformation(
                "Completed reindexing job {JobId}: {Processed} chunks processed",
                jobId,
                processedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process reindexing job {JobId}", jobId);
            job.Fail(ex.Message);
            await _jobRepository.UpdateAsync(job, cancellationToken);
            throw;
        }
    }

    public async Task CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _jobRepository.GetByIdAsync(jobId, cancellationToken);
        if (job == null)
        {
            throw new ArgumentException($"Reindexing job {jobId} not found");
        }

        if (job.Status != ReindexingJobStatus.Pending && job.Status != ReindexingJobStatus.Processing)
        {
            _logger.LogWarning("Cannot cancel job {JobId} in status {Status}", jobId, job.Status);
            return;
        }

        job.Cancel();
        await _jobRepository.UpdateAsync(job, cancellationToken);

        _logger.LogInformation("Cancelled reindexing job {JobId}", jobId);
    }

    public async Task<ReindexingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var stats = await _jobRepository.GetStatsAsync(cancellationToken);
        var processingJobs = await _jobRepository.GetByStatusAsync(ReindexingJobStatus.Processing, cancellationToken);
        var currentJob = processingJobs.FirstOrDefault();

        var overallProgress = stats.TotalChunksQueued > 0
            ? (double)stats.TotalChunksProcessed / stats.TotalChunksQueued
            : 0;

        return new ReindexingStatus(
            IsProcessing: currentJob != null,
            CurrentJob: currentJob,
            PendingJobCount: stats.PendingCount,
            TotalChunksQueued: stats.TotalChunksQueued,
            TotalChunksProcessed: stats.TotalChunksProcessed,
            OverallProgress: overallProgress);
    }

    public async Task<ReindexingJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _jobRepository.GetByIdAsync(jobId, cancellationToken);
    }

    public async Task<(List<ReindexingJob> Items, int TotalCount)> GetJobsAsync(
        int page = 1,
        int pageSize = 20,
        ReindexingJobStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        return await _jobRepository.GetPagedAsync(page, pageSize, status, cancellationToken);
    }

    public async Task<ChunkEmbedding?> ReindexChunkAsync(
        Guid chunkId,
        Guid targetModelId,
        CancellationToken cancellationToken = default)
    {
        // Check if embedding already exists
        var existing = await _embeddingRepository.GetByChunkAndModelAsync(chunkId, targetModelId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        // Get the chunk
        var chunk = await _chunkRepository.GetByIdAsync(chunkId, cancellationToken);
        if (chunk == null)
        {
            _logger.LogWarning("Chunk {ChunkId} not found for reindexing", chunkId);
            return null;
        }

        // Get the model
        var model = await _modelRepository.GetByIdAsync(targetModelId, cancellationToken);
        if (model == null)
        {
            _logger.LogWarning("Embedding model {ModelId} not found", targetModelId);
            return null;
        }

        // Generate embedding
        var embeddingVector = await _embeddingProvider.GetEmbeddingAsync(chunk.Content, cancellationToken);

        // Create and save the chunk embedding
        var chunkEmbedding = ChunkEmbedding.Create(chunkId, targetModelId, embeddingVector);
        await _embeddingRepository.AddAsync(chunkEmbedding, cancellationToken);

        // Update model usage timestamp
        model.MarkUsed();
        await _modelRepository.UpdateAsync(model, cancellationToken);

        _logger.LogDebug("Created embedding for chunk {ChunkId} with model {ModelKey}", chunkId, model.ModelKey);

        return chunkEmbedding;
    }

    public async Task<ReindexingEnsureResult> EnsureEmbeddingsAsync(
        IEnumerable<Guid> chunkIds,
        Guid embeddingModelId,
        bool waitForCompletion = false,
        CancellationToken cancellationToken = default)
    {
        var chunkIdList = chunkIds.ToList();

        // Check which chunks already have embeddings
        var existingEmbeddings = await _embeddingRepository.GetByChunkIdsAndModelAsync(
            chunkIdList,
            embeddingModelId,
            cancellationToken);

        var readyChunkIds = existingEmbeddings.Select(e => e.ChunkId).ToList();
        var missingChunkIds = chunkIdList.Except(readyChunkIds).ToList();

        if (!missingChunkIds.Any())
        {
            return new ReindexingEnsureResult(readyChunkIds, new List<Guid>(), null);
        }

        if (waitForCompletion)
        {
            // Process missing chunks immediately
            await ProcessBatchAsync(missingChunkIds, embeddingModelId, cancellationToken);
            return new ReindexingEnsureResult(chunkIdList, new List<Guid>(), null);
        }
        else
        {
            // Queue for background processing - create a chunk-level job for each
            // For simplicity, we'll just return the queued IDs
            // The background service will pick them up
            return new ReindexingEnsureResult(readyChunkIds, missingChunkIds, null);
        }
    }

    private async Task<List<Guid>> GetChunkIdsForJobAsync(ReindexingJob job, CancellationToken cancellationToken)
    {
        switch (job.Scope)
        {
            case ReindexingScope.System:
                var allChunks = await _chunkRepository.GetPagedAsync(1, int.MaxValue, cancellationToken: cancellationToken);
                return allChunks.Items.Select(c => c.Id).ToList();

            case ReindexingScope.Collection:
                if (!job.CollectionId.HasValue)
                {
                    throw new InvalidOperationException("Collection job missing CollectionId");
                }
                var docs = await _documentRepository.GetByCollectionIdAsync(job.CollectionId.Value, cancellationToken);
                var collectionChunks = new List<Guid>();
                foreach (var doc in docs)
                {
                    var docChunks = await _chunkRepository.GetByDocumentIdAsync(doc.Id, cancellationToken);
                    collectionChunks.AddRange(docChunks.Select(c => c.Id));
                }
                return collectionChunks;

            case ReindexingScope.Document:
                if (!job.DocumentId.HasValue)
                {
                    throw new InvalidOperationException("Document job missing DocumentId");
                }
                var documentChunks = await _chunkRepository.GetByDocumentIdAsync(job.DocumentId.Value, cancellationToken);
                return documentChunks.Select(c => c.Id).ToList();

            case ReindexingScope.Chunk:
                if (!job.ChunkId.HasValue)
                {
                    throw new InvalidOperationException("Chunk job missing ChunkId");
                }
                return new List<Guid> { job.ChunkId.Value };

            default:
                throw new ArgumentException($"Unknown scope: {job.Scope}");
        }
    }

    private async Task ProcessBatchAsync(List<Guid> chunkIds, Guid targetModelId, CancellationToken cancellationToken)
    {
        var chunks = await _chunkRepository.GetByIdsAsync(chunkIds, cancellationToken);
        var model = await _modelRepository.GetByIdAsync(targetModelId, cancellationToken);

        if (model == null)
        {
            throw new InvalidOperationException($"Target model {targetModelId} not found");
        }

        var contents = chunks.Select(c => c.Content).ToList();
        var embeddings = await _embeddingProvider.GetEmbeddingsAsync(contents, cancellationToken);

        var chunkEmbeddings = new List<ChunkEmbedding>();
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunkEmbedding = ChunkEmbedding.Create(
                chunks[i].Id,
                targetModelId,
                embeddings[i]);
            chunkEmbeddings.Add(chunkEmbedding);
        }

        await _embeddingRepository.AddRangeAsync(chunkEmbeddings, cancellationToken);

        // Update model usage timestamp
        model.MarkUsed();
        await _modelRepository.UpdateAsync(model, cancellationToken);
    }

    private async Task DeleteOldEmbeddingsAsync(List<Guid> chunkIds, Guid sourceModelId, CancellationToken cancellationToken)
    {
        foreach (var chunkId in chunkIds)
        {
            await _embeddingRepository.DeleteByChunkAndModelAsync(chunkId, sourceModelId, cancellationToken);
        }

        _logger.LogInformation(
            "Deleted {Count} old embeddings for model {ModelId}",
            chunkIds.Count,
            sourceModelId);
    }
}
