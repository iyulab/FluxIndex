using FluxIndex.Service.Application.Interfaces.Repositories;
using FluxIndex.Service.Application.Interfaces.Services;
using FluxIndex.Service.Domain.Entities;
using FluxIndex.Service.Shared.Common;
using FluxIndex.Service.Shared.DTOs.Jobs;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Service.Application.Services;

/// <summary>
/// Service implementation for indexing operations.
/// Manages indexing job lifecycle and document processing.
/// </summary>
public class IndexingService : IIndexingService
{
    private readonly IIndexingJobRepository _jobRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly ILogger<IndexingService> _logger;

    public IndexingService(
        IIndexingJobRepository jobRepository,
        IDocumentRepository documentRepository,
        IDocumentChunkRepository chunkRepository,
        ILogger<IndexingService> logger)
    {
        _jobRepository = jobRepository;
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _logger = logger;
    }

    public async Task<Guid> QueueIndexingJobAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document with id '{documentId}' not found.");

        // Check if there's already a pending job for this document
        var existingJob = await _jobRepository.GetByDocumentIdAsync(documentId, cancellationToken);
        if (existingJob != null && (existingJob.Status == IndexingJobStatus.Queued || existingJob.Status == IndexingJobStatus.Processing))
        {
            _logger.LogWarning("Document {DocumentId} already has a pending job: {JobId}", documentId, existingJob.Id);
            return existingJob.Id;
        }

        var job = IndexingJob.Create(documentId);
        await _jobRepository.AddAsync(job, cancellationToken);

        _logger.LogInformation("Indexing job queued: {JobId} for document {DocumentId}", job.Id, documentId);

        return job.Id;
    }

    public async Task ProcessNextJobAsync(CancellationToken cancellationToken = default)
    {
        var job = await _jobRepository.GetNextQueuedAsync(cancellationToken);
        if (job == null)
        {
            return; // No jobs to process
        }

        _logger.LogInformation("Processing indexing job: {JobId} for document {DocumentId}", job.Id, job.DocumentId);

        try
        {
            // Get document
            var document = await _documentRepository.GetByIdAsync(job.DocumentId, cancellationToken);
            if (document == null)
            {
                job.Fail("Document not found");
                await _jobRepository.UpdateAsync(job, cancellationToken);
                _logger.LogError("Document not found for job {JobId}: {DocumentId}", job.Id, job.DocumentId);
                return;
            }

            // Mark document as processing
            document.MarkAsProcessing();
            await _documentRepository.UpdateAsync(document, cancellationToken);

            // Start job processing
            // For now, we simulate chunk creation with a simple split
            // In production, this would integrate with FluxIndex SDK's chunking service
            var totalChunks = await SimulateChunkingAsync(document, job, cancellationToken);

            job.Start(totalChunks);
            await _jobRepository.UpdateAsync(job, cancellationToken);

            // Process each chunk (in production, this would generate embeddings)
            for (int i = 0; i < totalChunks; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    job.Cancel();
                    await _jobRepository.UpdateAsync(job, cancellationToken);
                    return;
                }

                job.UpdateProgress(i + 1);
                await _jobRepository.UpdateAsync(job, cancellationToken);

                // Simulate processing time
                await Task.Delay(100, cancellationToken);
            }

            // Complete job
            job.Complete();
            await _jobRepository.UpdateAsync(job, cancellationToken);

            // Mark document as indexed
            document.MarkAsIndexed(totalChunks);
            await _documentRepository.UpdateAsync(document, cancellationToken);

            _logger.LogInformation("Indexing job completed: {JobId} with {ChunkCount} chunks", job.Id, totalChunks);
        }
        catch (OperationCanceledException)
        {
            job.Cancel();
            await _jobRepository.UpdateAsync(job, cancellationToken);
            _logger.LogWarning("Indexing job cancelled: {JobId}", job.Id);
        }
        catch (Exception ex)
        {
            job.Fail(ex.Message);
            await _jobRepository.UpdateAsync(job, cancellationToken);

            // Mark document as failed
            var document = await _documentRepository.GetByIdAsync(job.DocumentId, CancellationToken.None);
            if (document != null)
            {
                document.MarkAsFailed(ex.Message);
                await _documentRepository.UpdateAsync(document, CancellationToken.None);
            }

            _logger.LogError(ex, "Indexing job failed: {JobId}", job.Id);
        }
    }

    private async Task<int> SimulateChunkingAsync(Document document, IndexingJob job, CancellationToken cancellationToken)
    {
        // In production, this would:
        // 1. Retrieve document content from storage
        // 2. Use FluxIndex SDK's chunking service
        // 3. Generate embeddings for each chunk
        // 4. Store chunks in vector store

        // For now, simulate with a fixed number based on file size
        var estimatedChunks = Math.Max(1, (int)((document.FileSize ?? 1000) / 500));
        return await Task.FromResult(Math.Min(estimatedChunks, 20)); // Cap at 20 for simulation
    }

    public async Task<IndexingJobDto?> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _jobRepository.GetByIdAsync(jobId, cancellationToken);
        return job?.ToDto();
    }

    public async Task<PagedResult<IndexingJobDto>> GetJobsAsync(
        int page,
        int pageSize,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        IndexingJobStatus? statusEnum = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<IndexingJobStatus>(status, true, out var parsed))
        {
            statusEnum = parsed;
        }

        var (items, totalCount) = await _jobRepository.GetPagedAsync(page, pageSize, statusEnum, cancellationToken);
        var dtos = items.Select(j => j.ToDto()).ToList();

        return PagedResult<IndexingJobDto>.Create(dtos, page, pageSize, totalCount);
    }

    public async Task CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _jobRepository.GetByIdAsync(jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job with id '{jobId}' not found.");

        if (job.Status == IndexingJobStatus.Completed || job.Status == IndexingJobStatus.Failed)
        {
            throw new InvalidOperationException($"Cannot cancel job in {job.Status} status.");
        }

        job.Cancel();
        await _jobRepository.UpdateAsync(job, cancellationToken);

        _logger.LogInformation("Indexing job cancelled by request: {JobId}", jobId);
    }

    public async Task<JobStatusSummaryDto> GetStatusSummaryAsync(CancellationToken cancellationToken = default)
    {
        var queuedCount = await _jobRepository.GetCountByStatusAsync(IndexingJobStatus.Queued, cancellationToken);
        var processingCount = await _jobRepository.GetCountByStatusAsync(IndexingJobStatus.Processing, cancellationToken);
        var completedCount = await _jobRepository.GetCountByStatusAsync(IndexingJobStatus.Completed, cancellationToken);
        var failedCount = await _jobRepository.GetCountByStatusAsync(IndexingJobStatus.Failed, cancellationToken);
        var avgProcessingTime = await _jobRepository.GetAverageProcessingTimeAsync(cancellationToken);

        return new JobStatusSummaryDto
        {
            QueuedCount = queuedCount,
            ProcessingCount = processingCount,
            CompletedCount = completedCount,
            FailedCount = failedCount,
            TotalCount = queuedCount + processingCount + completedCount + failedCount,
            AverageProcessingTimeMs = avgProcessingTime
        };
    }
}

/// <summary>
/// Extension methods for IndexingJob to DTO conversion.
/// </summary>
internal static class IndexingJobExtensions
{
    public static IndexingJobDto ToDto(this IndexingJob job)
    {
        return new IndexingJobDto
        {
            Id = job.Id,
            DocumentId = job.DocumentId,
            DocumentTitle = job.Document?.Title ?? "Unknown",
            Status = job.Status.ToString(),
            TotalChunks = job.TotalChunks,
            ProcessedChunks = job.ProcessedChunks,
            ProgressPercentage = job.GetProgressPercentage(),
            ErrorMessage = job.ErrorMessage,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            DurationMs = job.StartedAt.HasValue && job.CompletedAt.HasValue
                ? (job.CompletedAt.Value - job.StartedAt.Value).TotalMilliseconds
                : null
        };
    }
}
