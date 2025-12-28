using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Services;
using IContextualEmbeddingService = FluxIndex.Core.Application.Services.IContextualEmbeddingService;
using IEnrichedChunk = FluxIndex.Core.Application.Interfaces.IEnrichedChunk;
using ISourceMetadata = FluxIndex.Core.Application.Interfaces.ISourceMetadata;
using IAdvancedEntityExtractionService = FluxIndex.Core.Application.Interfaces.IAdvancedEntityExtractionService;
using EntityExtractionOptions = FluxIndex.Core.Application.Interfaces.EntityExtractionOptions;
using ExtractedEntity = FluxIndex.Core.Application.Interfaces.ExtractedEntity;
using EntityRelation = FluxIndex.Core.Application.Interfaces.EntityRelation;
using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Jobs;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Application.Services;

/// <summary>
/// Service implementation for indexing operations.
/// Manages indexing job lifecycle and document processing.
/// Integrates with FluxIndex SDK for chunking and embedding generation.
/// </summary>
public class IndexingService : IIndexingService
{
    private readonly IIndexingJobRepository _jobRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IIndexingJobLogRepository? _logRepository;
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly IDocumentContentProvider? _contentProvider;
    private readonly IChunkingService? _chunkingService;
    private readonly IChunkEnrichmentService? _enrichmentService;
    private readonly ILateChunkingEmbeddingService? _lateChunkingService;
    private readonly IContextualEmbeddingService? _contextualEmbeddingService;
    private readonly IAdvancedEntityExtractionService? _entityExtractionService;
    private readonly INeo4jGraphService? _graphService;
    private readonly ILogger<IndexingService> _logger;

    // Default chunking configuration (fallback when IChunkingService not available)
    private const int DefaultChunkSize = 1024;
    private const int DefaultChunkOverlap = 128;

    public IndexingService(
        IIndexingJobRepository jobRepository,
        IDocumentRepository documentRepository,
        IDocumentChunkRepository chunkRepository,
        ILogger<IndexingService> logger,
        IIndexingJobLogRepository? logRepository = null,
        IEmbeddingProvider? embeddingProvider = null,
        IDocumentContentProvider? contentProvider = null,
        IChunkingService? chunkingService = null,
        IChunkEnrichmentService? enrichmentService = null,
        ILateChunkingEmbeddingService? lateChunkingService = null,
        IContextualEmbeddingService? contextualEmbeddingService = null,
        IAdvancedEntityExtractionService? entityExtractionService = null,
        INeo4jGraphService? graphService = null)
    {
        _jobRepository = jobRepository;
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _logRepository = logRepository;
        _embeddingProvider = embeddingProvider;
        _contentProvider = contentProvider;
        _chunkingService = chunkingService;
        _enrichmentService = enrichmentService;
        _lateChunkingService = lateChunkingService;
        _contextualEmbeddingService = contextualEmbeddingService;
        _entityExtractionService = entityExtractionService;
        _graphService = graphService;
        _logger = logger;
    }

    /// <summary>
    /// Adds a log entry for the specified job.
    /// </summary>
    private async Task AddLogAsync(Guid jobId, IndexingJobLogLevel level, string message, string? details = null, string? phase = null, int? chunkIndex = null)
    {
        if (_logRepository == null) return;

        try
        {
            var log = IndexingJobLog.Create(jobId, level, message, details, phase, chunkIndex);
            await _logRepository.AddAsync(log);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save job log for job {JobId}", jobId);
        }
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
        await AddLogAsync(job.Id, IndexingJobLogLevel.Info, "Starting indexing job", phase: "Initialize");

        try
        {
            // Get document
            var document = await _documentRepository.GetByIdAsync(job.DocumentId, cancellationToken);
            if (document == null)
            {
                await AddLogAsync(job.Id, IndexingJobLogLevel.Error, "Document not found", $"Document ID: {job.DocumentId}", "Initialize");
                job.Fail("Document not found");
                await _jobRepository.UpdateAsync(job, cancellationToken);
                _logger.LogError("Document not found for job {JobId}: {DocumentId}", job.Id, job.DocumentId);
                return;
            }

            await AddLogAsync(job.Id, IndexingJobLogLevel.Info, $"Document retrieved: {document.Title}", phase: "Initialize");

            // Mark document as processing
            document.MarkAsProcessing();
            await _documentRepository.UpdateAsync(document, cancellationToken);

            // Start job processing
            await AddLogAsync(job.Id, IndexingJobLogLevel.Info, "Starting document processing", phase: "Processing");
            var totalChunks = await SimulateChunkingAsync(document, job, cancellationToken);

            job.Start(totalChunks);
            await _jobRepository.UpdateAsync(job, cancellationToken);
            await AddLogAsync(job.Id, IndexingJobLogLevel.Info, $"Document split into {totalChunks} chunks", phase: "Chunking");

            // Process each chunk (in production, this would generate embeddings)
            for (int i = 0; i < totalChunks; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    await AddLogAsync(job.Id, IndexingJobLogLevel.Warning, "Job cancelled by user request", phase: "Processing");
                    job.Cancel();
                    await _jobRepository.UpdateAsync(job, cancellationToken);
                    return;
                }

                job.UpdateProgress(i + 1);
                await _jobRepository.UpdateAsync(job, cancellationToken);

                // Log progress for every 10 chunks or the last chunk
                if ((i + 1) % 10 == 0 || i == totalChunks - 1)
                {
                    await AddLogAsync(job.Id, IndexingJobLogLevel.Debug, $"Processed chunk {i + 1}/{totalChunks}", phase: "Processing", chunkIndex: i);
                }

                // Simulate processing time
                await Task.Delay(100, cancellationToken);
            }

            // Complete job
            job.Complete();
            await _jobRepository.UpdateAsync(job, cancellationToken);

            // Mark document as indexed
            document.MarkAsIndexed(totalChunks);
            await _documentRepository.UpdateAsync(document, cancellationToken);

            await AddLogAsync(job.Id, IndexingJobLogLevel.Info, $"Indexing completed successfully with {totalChunks} chunks", phase: "Complete");
            _logger.LogInformation("Indexing job completed: {JobId} with {ChunkCount} chunks", job.Id, totalChunks);
        }
        catch (OperationCanceledException)
        {
            await AddLogAsync(job.Id, IndexingJobLogLevel.Warning, "Job cancelled", phase: "Cancelled");
            job.Cancel();
            await _jobRepository.UpdateAsync(job, cancellationToken);
            _logger.LogWarning("Indexing job cancelled: {JobId}", job.Id);
        }
        catch (Exception ex)
        {
            await AddLogAsync(job.Id, IndexingJobLogLevel.Error, $"Job failed: {ex.Message}", ex.StackTrace, "Failed");
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

    private async Task<int> ProcessDocumentAsync(Document document, IndexingJob job, CancellationToken cancellationToken)
    {
        // 1. Retrieve document content
        await AddLogAsync(job.Id, IndexingJobLogLevel.Info, "Retrieving document content", phase: "ContentRetrieval");
        string content;
        if (_contentProvider != null)
        {
            content = await _contentProvider.GetContentAsync(document.Id, cancellationToken);
            await AddLogAsync(job.Id, IndexingJobLogLevel.Info, $"Content retrieved: {content.Length} characters", phase: "ContentRetrieval");
        }
        else
        {
            // Fallback: use document title as minimal content for testing
            _logger.LogWarning("Document content provider not available, using minimal content for {DocumentId}", document.Id);
            await AddLogAsync(job.Id, IndexingJobLogLevel.Warning, "Content provider not available, using minimal content", phase: "ContentRetrieval");
            content = $"Document: {document.Title}";
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("Document content is empty: {DocumentId}", document.Id);
            await AddLogAsync(job.Id, IndexingJobLogLevel.Warning, "Document content is empty", phase: "ContentRetrieval");
            return 0;
        }

        // 2. Chunk the content using intelligent chunking (FileFlux) or fallback
        await AddLogAsync(job.Id, IndexingJobLogLevel.Info, "Starting content chunking", phase: "Chunking");
        List<DocumentChunk> chunkList;

        if (_chunkingService != null)
        {
            // Use FileFlux intelligent chunking
            _logger.LogInformation("Using FileFlux intelligent chunking for document {DocumentId}", document.Id);
            await AddLogAsync(job.Id, IndexingJobLogLevel.Info, "Using intelligent chunking (FileFlux)", phase: "Chunking");

            var detectedLanguage = _chunkingService.DetectLanguage(content);
            if (!string.IsNullOrEmpty(detectedLanguage))
            {
                await AddLogAsync(job.Id, IndexingJobLogLevel.Info, $"Detected language: {detectedLanguage}", phase: "Chunking");
            }

            chunkList = await _chunkingService.ChunkContentAsync(
                content,
                document.Id,
                new ChunkingOptions
                {
                    Strategy = "Auto", // Let FileFlux choose optimal strategy
                    MaxChunkSize = DefaultChunkSize,
                    OverlapSize = DefaultChunkOverlap,
                    Language = detectedLanguage
                },
                cancellationToken);
        }
        else
        {
            // Fallback to simple chunking
            _logger.LogWarning("FileFlux not available, using fallback chunking for document {DocumentId}", document.Id);
            await AddLogAsync(job.Id, IndexingJobLogLevel.Warning, "Using fallback chunking (FileFlux not available)", phase: "Chunking");
            chunkList = ChunkContent(content, document.Id).ToList();
        }

        _logger.LogInformation("Document {DocumentId} split into {ChunkCount} chunks", document.Id, chunkList.Count);
        await AddLogAsync(job.Id, IndexingJobLogLevel.Info, $"Content split into {chunkList.Count} chunks", phase: "Chunking");

        // 3. Generate embeddings - use Late Chunking if available and enabled, otherwise use standard embedding
        var useLateChunking = _lateChunkingService != null && _chunkingService != null;

        if (useLateChunking)
        {
            await AddLogAsync(job.Id, IndexingJobLogLevel.Info, "Starting Late Chunking embedding generation", phase: "Embedding");
            try
            {
                // Convert DocumentChunks to ChunkBoundaries for Late Chunking API
                var chunkBoundaries = chunkList.Select(c => new ChunkBoundary
                {
                    ChunkId = c.Id.ToString(),
                    Index = c.ChunkIndex,
                    StartPosition = c.StartPosition,
                    EndPosition = c.EndPosition
                }).ToList();

                var lateChunkingResult = await _lateChunkingService!.GenerateLateChunkingEmbeddingsAsync(
                    content,
                    chunkBoundaries,
                    cancellationToken);

                // Apply embeddings from Late Chunking result
                foreach (var chunkEmbedding in lateChunkingResult.ChunkEmbeddings)
                {
                    if (Guid.TryParse(chunkEmbedding.ChunkId, out var chunkId))
                    {
                        var chunk = chunkList.FirstOrDefault(c => c.Id == chunkId);
                        if (chunk != null && chunkEmbedding.Embedding != null)
                        {
                            chunk.SetEmbedding(chunkEmbedding.Embedding.Values);
                        }
                    }
                }

                _logger.LogInformation(
                    "Generated Late Chunking embeddings for {ChunkCount} chunks of document {DocumentId}",
                    lateChunkingResult.ChunkEmbeddings.Count, document.Id);
                await AddLogAsync(job.Id, IndexingJobLogLevel.Info,
                    $"Generated Late Chunking embeddings for {lateChunkingResult.ChunkEmbeddings.Count} chunks (contextual)",
                    phase: "Embedding");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Late Chunking failed, falling back to standard embedding for document {DocumentId}", document.Id);
                await AddLogAsync(job.Id, IndexingJobLogLevel.Warning,
                    $"Late Chunking failed: {ex.Message}. Falling back to standard embedding.",
                    ex.StackTrace, "Embedding");

                // Fallback to standard embedding
                await GenerateStandardEmbeddingsAsync(chunkList, document, job, cancellationToken);
            }
        }
        else if (_embeddingProvider != null)
        {
            await GenerateStandardEmbeddingsAsync(chunkList, document, job, cancellationToken);
        }
        else
        {
            _logger.LogWarning("Embedding provider not available, chunks will have no embeddings: {DocumentId}", document.Id);
            await AddLogAsync(job.Id, IndexingJobLogLevel.Warning, "Embedding provider not available, chunks will have no embeddings", phase: "Embedding");
        }

        // 4. Enrich chunks with AI-generated metadata (QA pairs, keywords, etc.)
        if (_enrichmentService != null && _enrichmentService.IsAvailable)
        {
            await AddLogAsync(job.Id, IndexingJobLogLevel.Info, "Starting chunk enrichment with FluxImprover", phase: "Enrichment");
            try
            {
                var enrichmentOptions = new ChunkEnrichmentOptions
                {
                    GenerateQAPairs = true,
                    MaxQAPairsPerChunk = 3,
                    ExtractKeywords = true,
                    GenerateSummary = false, // Can be enabled for detailed summaries
                    EvaluateQuality = false  // Can be enabled for quality filtering
                };

                var enrichmentResult = await _enrichmentService.EnrichChunksAsync(
                    chunkList,
                    document,
                    enrichmentOptions,
                    (processed, total) =>
                    {
                        if (processed % 5 == 0 || processed == total)
                        {
                            _logger.LogDebug("Enrichment progress: {Processed}/{Total}", processed, total);
                        }
                    },
                    cancellationToken);

                await AddLogAsync(job.Id, IndexingJobLogLevel.Info,
                    $"Enrichment completed: {enrichmentResult.EnrichedChunks}/{enrichmentResult.TotalChunks} chunks, " +
                    $"{enrichmentResult.TotalQAPairs} QA pairs generated",
                    phase: "Enrichment");

                if (enrichmentResult.FailedChunks > 0)
                {
                    await AddLogAsync(job.Id, IndexingJobLogLevel.Warning,
                        $"Enrichment partial failure: {enrichmentResult.FailedChunks} chunks failed",
                        string.Join("\n", enrichmentResult.Errors.Take(5)),
                        "Enrichment");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enrich chunks for document {DocumentId}", document.Id);
                await AddLogAsync(job.Id, IndexingJobLogLevel.Warning,
                    $"Chunk enrichment failed: {ex.Message}",
                    ex.StackTrace,
                    "Enrichment");
                // Continue without enrichment - not critical
            }
        }
        else
        {
            _logger.LogDebug("Chunk enrichment skipped: service not available for document {DocumentId}", document.Id);
            await AddLogAsync(job.Id, IndexingJobLogLevel.Debug,
                "Chunk enrichment skipped (LLM service not configured)",
                phase: "Enrichment");
        }

        // 5. Extract entities and build knowledge graph
        if (_entityExtractionService != null && _graphService != null && _graphService.IsAvailable)
        {
            await AddLogAsync(job.Id, IndexingJobLogLevel.Info, "Starting entity extraction for knowledge graph", phase: "EntityExtraction");
            try
            {
                var entityOptions = new EntityExtractionOptions
                {
                    ExtractRelations = true,
                    MinConfidence = 0.6,
                    MaxEntities = 50
                };

                var allEntities = new List<ExtractedEntity>();
                var allRelations = new List<EntityRelation>();
                var chunkEntityMap = new Dictionary<Guid, List<string>>(); // ChunkId -> EntityIds
                var entityIdMapping = new Dictionary<string, string>(); // Original entity.Id -> Our normalized ID

                foreach (var chunk in chunkList)
                {
                    try
                    {
                        var extractionResult = await _entityExtractionService.ExtractEntitiesAsync(
                            chunk.Content,
                            entityOptions,
                            cancellationToken);

                        if (extractionResult.Any())
                        {
                            var entityIds = new List<string>();
                            foreach (var entity in extractionResult)
                            {
                                // Generate unique entity ID based on type and normalized text
                                var normalizedId = $"{entity.Type}:{entity.NormalizedText}".ToLowerInvariant();
                                entityIds.Add(normalizedId);
                                entityIdMapping[entity.Id] = normalizedId;

                                // Store entity in graph
                                var graphEntity = new GraphEntity(
                                    normalizedId,
                                    entity.Text,
                                    entity.Type.ToString(),
                                    entity.Metadata,
                                    chunk.Id,
                                    document.Id);

                                await _graphService.StoreEntityAsync(graphEntity, cancellationToken);
                                allEntities.Add(entity);
                            }

                            chunkEntityMap[chunk.Id] = entityIds;

                            // Extract relations between entities in this chunk
                            if (extractionResult.Count >= 2)
                            {
                                var relations = await _entityExtractionService.ExtractRelationsAsync(
                                    chunk.Content,
                                    extractionResult,
                                    entityOptions,
                                    cancellationToken);

                                foreach (var relation in relations)
                                {
                                    // Map original entity IDs to our normalized IDs
                                    if (entityIdMapping.TryGetValue(relation.SourceEntityId, out var sourceId) &&
                                        entityIdMapping.TryGetValue(relation.TargetEntityId, out var targetId))
                                    {
                                        await _graphService.StoreRelationshipAsync(
                                            sourceId,
                                            targetId,
                                            relation.Type.ToString(),
                                            relation.Metadata,
                                            cancellationToken);

                                        allRelations.Add(relation);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to extract entities from chunk {ChunkId}", chunk.Id);
                        // Continue with other chunks
                    }
                }

                // Link chunks to their entities
                foreach (var (chunkId, entityIds) in chunkEntityMap)
                {
                    if (entityIds.Count > 0)
                    {
                        await _graphService.LinkChunkToEntitiesAsync(chunkId, entityIds, cancellationToken);
                    }
                }

                _logger.LogInformation(
                    "Entity extraction completed for document {DocumentId}: {EntityCount} entities, {RelationCount} relations",
                    document.Id, allEntities.Count, allRelations.Count);

                await AddLogAsync(job.Id, IndexingJobLogLevel.Info,
                    $"Entity extraction completed: {allEntities.Count} entities, {allRelations.Count} relations",
                    phase: "EntityExtraction");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract entities for document {DocumentId}", document.Id);
                await AddLogAsync(job.Id, IndexingJobLogLevel.Warning,
                    $"Entity extraction failed: {ex.Message}",
                    ex.StackTrace,
                    "EntityExtraction");
                // Continue without entity extraction - not critical
            }
        }
        else
        {
            var reason = _entityExtractionService == null
                ? "Entity extraction service not configured"
                : "Neo4j graph service not available";
            _logger.LogDebug("Entity extraction skipped: {Reason} for document {DocumentId}", reason, document.Id);
            await AddLogAsync(job.Id, IndexingJobLogLevel.Debug,
                $"Entity extraction skipped ({reason})",
                phase: "EntityExtraction");
        }

        // 6. Store chunks in database
        await AddLogAsync(job.Id, IndexingJobLogLevel.Info, "Storing chunks in database", phase: "Storage");
        await _chunkRepository.AddRangeAsync(chunkList, cancellationToken);
        await AddLogAsync(job.Id, IndexingJobLogLevel.Info, $"Successfully stored {chunkList.Count} chunks", phase: "Storage");

        return chunkList.Count;
    }

    /// <summary>
    /// Chunks content into smaller segments with overlap.
    /// </summary>
    private IEnumerable<DocumentChunk> ChunkContent(string content, Guid documentId)
    {
        if (string.IsNullOrWhiteSpace(content))
            yield break;

        int chunkIndex = 0;
        int position = 0;

        while (position < content.Length)
        {
            // Calculate end position
            int endPosition = Math.Min(position + DefaultChunkSize, content.Length);

            // Try to find a natural break point (sentence end, paragraph)
            if (endPosition < content.Length)
            {
                int lastBreak = SimpleChunkingService.FindNaturalBreakPoint(content, position, endPosition);
                if (lastBreak > position)
                {
                    endPosition = lastBreak;
                }
            }

            // Extract chunk content
            string chunkContent = content.Substring(position, endPosition - position).Trim();

            if (!string.IsNullOrWhiteSpace(chunkContent))
            {
                var chunk = DocumentChunk.Create(
                    documentId,
                    chunkIndex,
                    chunkContent,
                    position,
                    endPosition,
                    EstimateTokenCount(chunkContent));

                yield return chunk;
                chunkIndex++;
            }

            // Move position with overlap (ensure forward progress)
            int nextPosition = endPosition - DefaultChunkOverlap;
            if (nextPosition <= position)
            {
                // Content is too short for overlap, just move to end
                position = endPosition;
            }
            else
            {
                position = nextPosition;
            }
        }
    }

    private static int EstimateTokenCount(string text)
    {
        // Rough estimation: ~4 characters per token for English
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <summary>
    /// Generates embeddings using Contextual Retrieval if available, otherwise standard embedding.
    /// Contextual Retrieval prepends document context to each chunk before embedding (Anthropic's approach).
    /// </summary>
    private async Task GenerateStandardEmbeddingsAsync(
        List<DocumentChunk> chunkList,
        Document document,
        IndexingJob job,
        CancellationToken cancellationToken)
    {
        if (_embeddingProvider == null)
        {
            _logger.LogWarning("Embedding provider not available for standard embedding: {DocumentId}", document.Id);
            return;
        }

        // Try Contextual Embedding first (Anthropic's Contextual Retrieval - up to 67% improvement)
        if (_contextualEmbeddingService != null)
        {
            try
            {
                await AddLogAsync(job.Id, IndexingJobLogLevel.Info,
                    "Starting Contextual Retrieval embedding (Anthropic's approach)", phase: "Embedding");

                await GenerateContextualEmbeddingsAsync(chunkList, document, job, cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Contextual embedding failed, falling back to standard embedding for document {DocumentId}",
                    document.Id);
                await AddLogAsync(job.Id, IndexingJobLogLevel.Warning,
                    $"Contextual embedding failed: {ex.Message}. Falling back to standard embedding.",
                    ex.StackTrace, "Embedding");
            }
        }

        // Standard embedding fallback
        await AddLogAsync(job.Id, IndexingJobLogLevel.Info, "Starting standard embedding generation", phase: "Embedding");
        var chunkContents = chunkList.Select(c => c.Content).ToArray();

        try
        {
            var embeddings = await _embeddingProvider.GetEmbeddingsAsync(chunkContents, cancellationToken);

            for (int i = 0; i < chunkList.Count && i < embeddings.Length; i++)
            {
                chunkList[i].SetEmbedding(embeddings[i]);
            }

            _logger.LogInformation("Generated standard embeddings for {ChunkCount} chunks of document {DocumentId}",
                chunkList.Count, document.Id);
            await AddLogAsync(job.Id, IndexingJobLogLevel.Info, $"Generated standard embeddings for {chunkList.Count} chunks", phase: "Embedding");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embeddings for document {DocumentId}", document.Id);
            await AddLogAsync(job.Id, IndexingJobLogLevel.Warning, $"Embedding generation failed: {ex.Message}", ex.StackTrace, "Embedding");
            // Continue without embeddings - can be regenerated later
        }
    }

    /// <summary>
    /// Generates contextual embeddings using Anthropic's Contextual Retrieval approach.
    /// Each chunk is prepended with document context before embedding for improved retrieval.
    /// </summary>
    private async Task GenerateContextualEmbeddingsAsync(
        List<DocumentChunk> chunkList,
        Document document,
        IndexingJob job,
        CancellationToken cancellationToken)
    {
        // Convert Stack chunks to IEnrichedChunk for Core API using inline adapter
        var enrichedChunks = chunkList.Select(chunk =>
            (IEnrichedChunk)new DocumentChunkEnrichedAdapter(chunk, document))
            .ToList();

        // Generate document summary for context (if available in metadata)
        string? documentSummary = null;
        if (document.Metadata.TryGetValue("summary", out var summary))
        {
            documentSummary = summary?.ToString();
        }

        // Generate contextual embeddings in batch
        var contextualResults = await _contextualEmbeddingService!.GenerateContextualEmbeddingsBatchAsync(
            enrichedChunks,
            documentSummary,
            cancellationToken);

        // Apply embeddings to chunks and store contextual content
        var resultDict = contextualResults.ToDictionary(r => r.ChunkId, r => r);
        var appliedCount = 0;

        foreach (var chunk in chunkList)
        {
            var chunkIdStr = chunk.Id.ToString();
            if (resultDict.TryGetValue(chunkIdStr, out var result))
            {
                chunk.SetEmbedding(result.Embedding.Values);

                // Store contextual header in metadata for reference
                if (!string.IsNullOrEmpty(result.ContextualHeader))
                {
                    chunk.Metadata["contextual_header"] = result.ContextualHeader;
                    chunk.Metadata["contextual_source"] = result.ContextSource.ToString();
                }
                appliedCount++;
            }
        }

        _logger.LogInformation(
            "Generated contextual embeddings for {AppliedCount}/{TotalCount} chunks of document {DocumentId}",
            appliedCount, chunkList.Count, document.Id);
        await AddLogAsync(job.Id, IndexingJobLogLevel.Info,
            $"Generated contextual embeddings for {appliedCount}/{chunkList.Count} chunks (Contextual Retrieval)",
            phase: "Embedding");
    }

    /// <summary>
    /// Inline adapter to convert Stack's DocumentChunk to Core's IEnrichedChunk.
    /// Used for Contextual Embedding integration without circular dependencies.
    /// </summary>
    private sealed class DocumentChunkEnrichedAdapter : IEnrichedChunk
    {
        private readonly DocumentChunk _chunk;
        private readonly Document? _document;
        private readonly ISourceMetadata _source;

        public DocumentChunkEnrichedAdapter(DocumentChunk chunk, Document? document = null)
        {
            _chunk = chunk;
            _document = document ?? chunk.Document;
            _source = new DocumentChunkSourceAdapter(_chunk, _document);
        }

        public string Content => _chunk.Content;
        public string ChunkId => _chunk.Id.ToString();
        public int ChunkIndex => _chunk.ChunkIndex;

        public IReadOnlyList<string> HeadingPath
        {
            get
            {
                if (_chunk.Metadata.TryGetValue("heading_path", out var path))
                {
                    if (path is string pathStr && !string.IsNullOrEmpty(pathStr))
                        return pathStr.Split(" > ", StringSplitOptions.RemoveEmptyEntries);
                    if (path is IEnumerable<string> pathList)
                        return pathList.ToList();
                }
                return Array.Empty<string>();
            }
        }

        public string? SectionTitle
        {
            get
            {
                if (_chunk.Metadata.TryGetValue("section", out var section))
                    return section?.ToString();
                var path = HeadingPath;
                return path.Count > 0 ? path[^1] : null;
            }
        }

        public int? StartPage => GetMetadataInt("start_page");
        public int? EndPage => GetMetadataInt("end_page");
        public double Quality => GetMetadataDouble("ff_quality", 0.8);
        public double ContextDependency => GetMetadataDouble("context_dependency", 0.5);
        public int? TokenCount => _chunk.TokenCount > 0 ? _chunk.TokenCount : null;
        public ISourceMetadata Source => _source;

        private int? GetMetadataInt(string key) =>
            _chunk.Metadata.TryGetValue(key, out var value) ? Convert.ToInt32(value) : null;

        private double GetMetadataDouble(string key, double defaultValue) =>
            _chunk.Metadata.TryGetValue(key, out var value) ? Convert.ToDouble(value) : defaultValue;
    }

    /// <summary>
    /// Inline source metadata adapter for DocumentChunk.
    /// </summary>
    private sealed class DocumentChunkSourceAdapter : ISourceMetadata
    {
        private readonly DocumentChunk _chunk;
        private readonly Document? _document;

        public DocumentChunkSourceAdapter(DocumentChunk chunk, Document? document)
        {
            _chunk = chunk;
            _document = document;
        }

        public string SourceId => _chunk.DocumentId.ToString();
        public string SourceType => _document?.SourceType ?? GetMetadata("content_type", "text");
        public string Title => _document?.Title ?? "Unknown Document";
        public string? FilePath => _document?.SourcePath;
        public string? Url => GetUrl();
        public DateTime CreatedAt => _chunk.CreatedAt;
        public string Language => GetMetadata("language", "en");
        public double? LanguageConfidence => null;
        public int WordCount => GetMetadataInt("word_count", _chunk.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        public int ChunkCount => GetMetadataInt("total_chunks", 1);
        public int? PageCount => null;
        public DateTime? PublishedAt => _document?.UpdatedAt;
        public string? Author => null;
        public IReadOnlyList<string>? Keywords => GetKeywords();

        private string GetMetadata(string key, string defaultValue) =>
            _chunk.Metadata.TryGetValue(key, out var v) ? v?.ToString() ?? defaultValue : defaultValue;

        private int GetMetadataInt(string key, int defaultValue) =>
            _chunk.Metadata.TryGetValue(key, out var v) ? Convert.ToInt32(v) : defaultValue;

        private string? GetUrl()
        {
            if (_chunk.Metadata.TryGetValue("url", out var url))
                return url?.ToString();
            var sourcePath = _document?.SourcePath;
            if (!string.IsNullOrEmpty(sourcePath) &&
                (sourcePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 sourcePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                return sourcePath;
            return null;
        }

        private IReadOnlyList<string>? GetKeywords()
        {
            if (_chunk.Metadata.TryGetValue("keywords", out var kw))
            {
                if (kw is IEnumerable<string> keywords)
                    return keywords.ToList();
                if (kw is string kwStr && !string.IsNullOrEmpty(kwStr))
                    return kwStr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            }
            return null;
        }
    }

    private async Task<int> SimulateChunkingAsync(Document document, IndexingJob job, CancellationToken cancellationToken)
    {
        // For backward compatibility - delegates to ProcessDocumentAsync
        return await ProcessDocumentAsync(document, job, cancellationToken);
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
