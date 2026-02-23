using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Interfaces;
using FluxIndex.Core.Models;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using DocumentChunkEntity = FluxIndex.Core.Domain.Entities.DocumentChunk;
using DocumentChunkModel = FluxIndex.Core.Domain.Models.CacheDocumentChunk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.SDK;

/// <summary>
/// Indexer - 문서 인덱싱 및 저장 담당 (Phase 3: DX 개선으로 이벤트 및 진행률 모니터링 지원)
/// </summary>
public partial class Indexer
{
    private readonly IVectorStore _vectorStore;
    private readonly IDocumentRepository _documentRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly IChunkingService _chunkingService;
    private readonly IMetadataExtractor? _metadataExtractor;
    private readonly IGraphRAGService? _graphRAGService;
    private readonly IHybridSearchService? _hybridSearchService;
    private readonly ILogger<Indexer> _logger;
    private readonly IndexerOptions _options;

    /// <summary>
    /// GraphRAG 서비스 사용 가능 여부
    /// </summary>
    public bool SupportsGraphRAG => _graphRAGService != null;

    /// <summary>
    /// 하이브리드 검색 서비스 사용 가능 여부
    /// </summary>
    public bool SupportsHybridSearch => _hybridSearchService != null;

    // Phase 3: 이벤트 기반 모니터링
    /// <summary>
    /// 인덱싱 시작 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<IndexingStartedEventArgs>? IndexingStarted;

    /// <summary>
    /// 인덱싱 완료 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<IndexingCompletedEventArgs>? IndexingCompleted;

    /// <summary>
    /// 인덱싱 실패 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<IndexingFailedEventArgs>? IndexingFailed;

    /// <summary>
    /// 배치 작업 시작 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<BatchStartedEventArgs>? BatchStarted;

    /// <summary>
    /// 배치 작업 완료 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<BatchCompletedEventArgs>? BatchCompleted;

    public Indexer(
        IVectorStore vectorStore,
        IDocumentRepository documentRepository,
        IEmbeddingService embeddingService,
        IChunkingService chunkingService,
        IndexerOptions options,
        ILogger<Indexer>? logger = null,
        IMetadataExtractor? metadataExtractor = null,
        IGraphRAGService? graphRAGService = null,
        IHybridSearchService? hybridSearchService = null)
    {
        _vectorStore = vectorStore;
        _documentRepository = documentRepository;
        _embeddingService = embeddingService;
        _chunkingService = chunkingService;
        _metadataExtractor = metadataExtractor;
        _graphRAGService = graphRAGService;
        _hybridSearchService = hybridSearchService;
        _options = options;
        _logger = logger ?? NullLogger<Indexer>.Instance;
    }

    /// <summary>
    /// 간편 API: 문자열 콘텐츠로 직접 문서 인덱싱
    /// README 예제 코드와 호환되는 간단한 인터페이스 제공
    /// </summary>
    /// <param name="content">인덱싱할 문서 내용</param>
    /// <param name="documentId">문서 ID</param>
    /// <param name="metadata">메타데이터 (선택)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>인덱싱된 문서 ID</returns>
    public async Task<string> IndexDocumentAsync(
        string content,
        string documentId,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty", nameof(content));
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        // Create Document entity
        var document = Document.Create(documentId);
        document.Content = content;

        // Add metadata if provided
        if (metadata != null)
        {
            foreach (var (key, value) in metadata)
            {
                document.SetMetadata(key, value);
            }
        }

        // Create single chunk from content
        var chunk = DocumentChunkEntity.Create(documentId, content, 0, 1);
        document.AddChunk(chunk);

        // Use existing indexing logic
        return await IndexDocumentAsync(document, cancellationToken);
    }

    /// <summary>
    /// 문서 인덱싱
    /// </summary>
    public async Task<string> IndexDocumentAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        return await IndexDocumentAsync(document, null, null, cancellationToken);
    }

    /// <summary>
    /// 문서 인덱싱 (진행률 모니터링 지원)
    /// </summary>
    /// <param name="document">인덱싱할 문서</param>
    /// <param name="progress">진행률 보고 객체</param>
    /// <param name="cancellationToken">취소 토큰</param>
    public async Task<string> IndexDocumentAsync(
        Document document,
        IProgress<IndexingProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        return await IndexDocumentAsync(document, null, progress, cancellationToken);
    }

    /// <summary>
    /// 문서 인덱싱 (IndexingOptions 지원)
    /// </summary>
    /// <param name="document">인덱싱할 문서</param>
    /// <param name="options">인덱싱 옵션. null이면 등록된 서비스에 따라 자동 설정</param>
    /// <param name="cancellationToken">취소 토큰</param>
    public async Task<string> IndexDocumentAsync(
        Document document,
        IndexingOptions? options,
        CancellationToken cancellationToken = default)
    {
        return await IndexDocumentAsync(document, options, null, cancellationToken);
    }

    /// <summary>
    /// 문서 인덱싱 (Phase 3: 진행률 모니터링 지원)
    /// </summary>
    /// <param name="document">인덱싱할 문서</param>
    /// <param name="options">인덱싱 옵션. null이면 등록된 서비스에 따라 자동 설정</param>
    /// <param name="progress">진행률 보고 객체 (선택)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    public async Task<string> IndexDocumentAsync(
        Document document,
        IndexingOptions? options,
        IProgress<IndexingProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var jobId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        LogIndexingDocument(_logger, document.Id, jobId);

        try
        {
            // Phase 3: 이벤트 발생 - 인덱싱 시작
            var chunks = document.Chunks.ToList();
            IndexingStarted?.Invoke(this, new IndexingStartedEventArgs
            {
                JobId = jobId,
                DocumentId = document.Id,
                TotalChunks = chunks.Count,
                StartedAt = startTime
            });

            // Phase 3: 진행률 보고 - 초기화
            progress?.Report(new IndexingProgress
            {
                JobId = jobId,
                DocumentId = document.Id,
                CurrentChunk = 0,
                TotalChunks = chunks.Count,
                ProgressPercentage = 0,
                Status = "Starting",
                Message = "Saving document metadata"
            });

            // Phase 3: AI 메타데이터 추출 (선택적)
            if (_metadataExtractor != null && !string.IsNullOrEmpty(document.Content))
            {
                try
                {
                    LogExtractingAIMetadata(_logger, document.Id);

                    // IndexingOptions에서 AI 메타데이터 설정 확인
                    var indexingOptions = new IndexingOptions();
                    if (_options.CustomOptions != null)
                    {
                        foreach (var (key, value) in _options.CustomOptions)
                        {
                            indexingOptions.CustomOptions[key] = value;
                        }
                    }

                    if (indexingOptions.ShouldExtractAIMetadata())
                    {
                        var schema = indexingOptions.GetMetadataSchema();
                        var strategy = indexingOptions.GetMetadataExtractionStrategy();
                        var minConfidence = indexingOptions.GetMinMetadataConfidence();
                        var customPrompt = indexingOptions.GetCustomMetadataPrompt();

                        var extractionOptions = new AIMetadataExtractionOptions
                        {
                            Strategy = strategy,
                            MinConfidence = minConfidence,
                            CustomPrompt = customPrompt
                        };

                        // 캐시 키 생성
                        var cacheKey = _metadataExtractor.GenerateCacheKey(document.Content, schema);

                        // AI 메타데이터 추출 (캐싱 지원)
                        var extractedMetadata = await _metadataExtractor.ExtractWithCacheAsync(
                            document.Content,
                            cacheKey,
                            schema,
                            extractionOptions,
                            cancellationToken);

                        // IndexingResult에 메타데이터 저장 (Document.Metadata에 포함)
                        document.SetMetadata("AIExtractedMetadata", extractedMetadata);
                        document.SetMetadata("MetadataExtractionMethod", extractedMetadata.ExtractionMethod);
                        document.SetMetadata("MetadataConfidence", extractedMetadata.OverallConfidence);

                        LogAIMetadataExtracted(_logger, extractedMetadata.OverallConfidence, extractedMetadata.Topics.Length);
                    }
                }
                catch (Exception ex)
                {
                    LogFailedToExtractAIMetadata(_logger, ex, document.Id);
                    // Continue indexing without AI metadata
                }
            }

            // Save document metadata
            await _documentRepository.AddAsync(document, cancellationToken);

            // Process chunks
            if (chunks.Count == 0)
            {
                LogDocumentHasNoChunks(_logger, document.Id);

                IndexingCompleted?.Invoke(this, new IndexingCompletedEventArgs
                {
                    JobId = jobId,
                    DocumentId = document.Id,
                    ChunksIndexed = 0,
                    TotalChunks = 0,
                    Success = true,
                    ProcessingTime = DateTime.UtcNow - startTime
                });

                return document.Id;
            }

            // Phase 3: 진행률 보고 - 청크 처리 시작
            progress?.Report(new IndexingProgress
            {
                JobId = jobId,
                DocumentId = document.Id,
                CurrentChunk = 0,
                TotalChunks = chunks.Count,
                ProgressPercentage = 10,
                Status = "Processing",
                Message = $"Processing {chunks.Count} chunks"
            });

            // Convert to Entity chunks first with automatic oversized chunk splitting
            LogConvertingChunksToEntities(_logger, chunks.Count);

            var entityChunks = new List<DocumentChunkEntity>();
            var chunkIndex = 0;

            foreach (var chunk in chunks)
            {
                var estimatedTokens = chunk.Content.Length / 4;
                LogChunkDetails(_logger, chunk.ChunkIndex, chunks.Count, chunk.Content.Length, estimatedTokens);

                // SAFETY: Split oversized chunks automatically (WebFlux chunking bug workaround)
                if (estimatedTokens > 8000)
                {
                    LogChunkExceedsTokenLimit(_logger, chunk.ChunkIndex, estimatedTokens);

                    // Split into chunks of ~2000 tokens (8000 chars) to be safe
                    const int maxChunkChars = 8000; // ~2000 tokens
                    var content = chunk.Content;
                    var subChunkCount = (int)Math.Ceiling((double)content.Length / maxChunkChars);

                    for (int i = 0; i < subChunkCount; i++)
                    {
                        var startPos = i * maxChunkChars;
                        var length = Math.Min(maxChunkChars, content.Length - startPos);
                        var subContent = content.Substring(startPos, length);

                        var subChunk = DocumentChunkEntity.Create(
                            chunk.DocumentId,
                            subContent,
                            chunkIndex++,
                            chunks.Count // Will be adjusted later
                        );
                        // Copy metadata if exists
                        if (chunk.Metadata != null)
                        {
                            subChunk.Metadata = chunk.Metadata;
                        }

                        entityChunks.Add(subChunk);
                        LogSubChunkCreated(_logger, i + 1, subChunkCount, subContent.Length, subContent.Length / 4);
                    }

                    LogSplitOversizedChunk(_logger, chunk.ChunkIndex, subChunkCount);
                }
                else
                {
                    // Normal sized chunk - add directly
                    entityChunks.Add(chunk);
                    chunkIndex++;
                }
            }

            LogTotalEntityChunksAfterSplitting(_logger, entityChunks.Count, chunks.Count);

            // Phase 3: 진행률 보고 - 임베딩 생성
            progress?.Report(new IndexingProgress
            {
                JobId = jobId,
                DocumentId = document.Id,
                CurrentChunk = 0,
                TotalChunks = chunks.Count,
                ProgressPercentage = 30,
                Status = "Embedding",
                Message = "Generating embeddings"
            });

            LogCallingGenerateEmbeddings(_logger, entityChunks.Count);

            // Generate embeddings for entity chunks
            var embeddedEntityChunks = await GenerateEmbeddingsAsync(entityChunks, cancellationToken);

            // Phase 3: 진행률 보고 - 벡터 스토어 저장
            progress?.Report(new IndexingProgress
            {
                JobId = jobId,
                DocumentId = document.Id,
                CurrentChunk = chunks.Count,
                TotalChunks = chunks.Count,
                ProgressPercentage = 80,
                Status = "Storing",
                Message = "Storing in vector store"
            });

            // Store in vector store
            await _vectorStore.StoreBatchAsync(embeddedEntityChunks, cancellationToken);

            // GraphRAG 인덱싱 (자동 감지)
            // - options?.EnableGraphRAG == null: 서비스가 등록되어 있으면 자동 활성화
            // - options?.EnableGraphRAG == true: 강제 활성화
            // - options?.EnableGraphRAG == false: 강제 비활성화
            var enableGraphRAG = options?.EnableGraphRAG ?? (_graphRAGService != null);
            if (enableGraphRAG)
            {
                if (_graphRAGService == null)
                {
                    throw new InvalidOperationException(
                        "GraphRAG is enabled but IGraphRAGService is not registered. " +
                        "Use UseNeo4jGraph() or register IGraphRAGService manually.");
                }

                progress?.Report(new IndexingProgress
                {
                    JobId = jobId,
                    DocumentId = document.Id,
                    CurrentChunk = chunks.Count,
                    TotalChunks = chunks.Count,
                    ProgressPercentage = 90,
                    Status = "GraphRAG",
                    Message = "Building GraphRAG index"
                });

                LogBuildingGraphRAGIndex(_logger, document.Id);
                await _graphRAGService.BuildIndexAsync(embeddedEntityChunks, options?.GraphRAGOptions, cancellationToken);
                LogGraphRAGIndexBuilt(_logger, document.Id);
            }

            // Phase 3: 진행률 보고 - 완료
            progress?.Report(new IndexingProgress
            {
                JobId = jobId,
                DocumentId = document.Id,
                CurrentChunk = chunks.Count,
                TotalChunks = chunks.Count,
                ProgressPercentage = 100,
                Status = "Completed",
                Message = "Indexing completed successfully"
            });

            LogSuccessfullyIndexedDocument(_logger, document.Id, chunks.Count);

            // Phase 3: 이벤트 발생 - 인덱싱 완료
            IndexingCompleted?.Invoke(this, new IndexingCompletedEventArgs
            {
                JobId = jobId,
                DocumentId = document.Id,
                ChunksIndexed = chunks.Count,
                TotalChunks = chunks.Count,
                Success = true,
                ProcessingTime = DateTime.UtcNow - startTime
            });

            return document.Id;
        }
        catch (Exception ex)
        {
            LogFailedToIndexDocument(_logger, ex, document.Id);

            // Phase 3: 이벤트 발생 - 인덱싱 실패
            IndexingFailed?.Invoke(this, new IndexingFailedEventArgs
            {
                JobId = jobId,
                DocumentId = document.Id,
                ErrorMessage = ex.Message,
                Exception = ex
            });

            throw;
        }
    }

    /// <summary>
    /// 청크 리스트에서 문서 생성 및 인덱싱
    /// </summary>
    public async Task<string> IndexChunksAsync(
        IEnumerable<DocumentChunkModel> chunks,
        string? documentId = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        documentId ??= Guid.NewGuid().ToString();
        LogIndexingChunksAsDocument(_logger, documentId);

        // Materialize chunks for multiple iterations
        var chunkList = chunks.ToList();

        // Create document with combined content from all chunks
        var combinedContent = string.Join("\n", chunkList.Select(c => c.Content));
        var document = new Document { Id = documentId, Content = combinedContent, CreatedAt = DateTime.UtcNow };

        // Add metadata if provided
        if (metadata != null)
        {
            foreach (var (key, value) in metadata)
            {
                document.SetMetadata(key, value);
            }
        }

        // Add chunks to document
        foreach (var chunk in chunkList)
        {
            document.AddChunk(ConvertToEntityChunk(chunk));
        }

        // Index the document
        return await IndexDocumentAsync(document, cancellationToken);
    }


    /// <summary>
    /// 배치 인덱싱
    /// </summary>
    public async Task<IEnumerable<string>> IndexBatchAsync(
        IEnumerable<Document> documents,
        int parallelism = 4,
        CancellationToken cancellationToken = default)
    {
        return await IndexBatchAsync(documents, null, parallelism, cancellationToken);
    }

    /// <summary>
    /// 배치 인덱싱 (Phase 3: 진행률 모니터링 지원)
    /// </summary>
    /// <param name="documents">인덱싱할 문서 목록</param>
    /// <param name="progress">배치 진행률 보고 객체 (선택)</param>
    /// <param name="parallelism">병렬 처리 수준</param>
    /// <param name="cancellationToken">취소 토큰</param>
    public async Task<IEnumerable<string>> IndexBatchAsync(
        IEnumerable<Document> documents,
        IProgress<BatchProgress>? progress,
        int parallelism = 4,
        CancellationToken cancellationToken = default)
    {
        var batchId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        var documentList = documents.ToList();
        var totalDocuments = documentList.Count;

        LogBatchIndexing(_logger, totalDocuments, batchId);

        // Phase 3: 이벤트 발생 - 배치 시작
        BatchStarted?.Invoke(this, new BatchStartedEventArgs
        {
            BatchId = batchId,
            TotalItems = totalDocuments,
            BatchType = "IndexBatch",
            StartedAt = startTime
        });

        // Phase 3: 진행률 보고 - 초기화
        progress?.Report(new BatchProgress
        {
            BatchId = batchId,
            CurrentItem = 0,
            TotalItems = totalDocuments,
            ProgressPercentage = 0,
            Status = "Starting",
            Message = $"Starting batch indexing of {totalDocuments} documents",
            SuccessfulItems = 0,
            FailedItems = 0
        });

        var semaphore = new SemaphoreSlim(parallelism);
        var completedCount = 0;
        var successCount = 0;
        var failedCount = 0;
        var lockObject = new object();

        var tasks = documentList.Select(async doc =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await IndexDocumentAsync(doc, cancellationToken);

                // Phase 3: 진행률 업데이트 (성공)
                lock (lockObject)
                {
                    completedCount++;
                    successCount++;

                    progress?.Report(new BatchProgress
                    {
                        BatchId = batchId,
                        CurrentItem = completedCount,
                        TotalItems = totalDocuments,
                        ProgressPercentage = (float)completedCount / totalDocuments * 100,
                        Status = "Processing",
                        Message = $"Indexed document {doc.Id} ({completedCount}/{totalDocuments})",
                        SuccessfulItems = successCount,
                        FailedItems = failedCount
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                LogFailedToIndexDocumentInBatch(_logger, ex, doc.Id, batchId);

                // Phase 3: 진행률 업데이트 (실패)
                lock (lockObject)
                {
                    completedCount++;
                    failedCount++;

                    progress?.Report(new BatchProgress
                    {
                        BatchId = batchId,
                        CurrentItem = completedCount,
                        TotalItems = totalDocuments,
                        ProgressPercentage = (float)completedCount / totalDocuments * 100,
                        Status = "Processing",
                        Message = $"Failed to index document {doc.Id} ({completedCount}/{totalDocuments})",
                        SuccessfulItems = successCount,
                        FailedItems = failedCount
                    });
                }

                return string.Empty; // Return empty for failed documents
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        // Phase 3: 진행률 보고 - 완료
        progress?.Report(new BatchProgress
        {
            BatchId = batchId,
            CurrentItem = totalDocuments,
            TotalItems = totalDocuments,
            ProgressPercentage = 100,
            Status = "Completed",
            Message = $"Batch indexing completed: {successCount} succeeded, {failedCount} failed",
            SuccessfulItems = successCount,
            FailedItems = failedCount
        });

        LogBatchIndexingCompleted(_logger, successCount, totalDocuments, batchId);

        // Phase 3: 이벤트 발생 - 배치 완료
        BatchCompleted?.Invoke(this, new BatchCompletedEventArgs
        {
            BatchId = batchId,
            TotalItems = totalDocuments,
            SuccessfulItems = successCount,
            FailedItems = failedCount,
            TotalProcessingTime = DateTime.UtcNow - startTime
        });

        return results.Where(r => !string.IsNullOrEmpty(r));
    }

    /// <summary>
    /// 문서 업데이트
    /// </summary>
    public async Task UpdateDocumentAsync(
        string documentId,
        Document updatedDocument,
        CancellationToken cancellationToken = default)
    {
        LogUpdatingDocument(_logger, documentId);

        // Delete existing chunks
        await _vectorStore.DeleteByDocumentIdAsync(documentId, cancellationToken);

        // Update document (Id should already match documentId)
        await _documentRepository.UpdateAsync(updatedDocument, cancellationToken);

        // Process new chunks
        var chunks = updatedDocument.Chunks.ToList();
        if (chunks.Count != 0)
        {
            chunks = await GenerateEmbeddingsAsync(chunks, cancellationToken);
            await _vectorStore.StoreBatchAsync(chunks, cancellationToken);
        }

        LogSuccessfullyUpdatedDocument(_logger, documentId);
    }

    /// <summary>
    /// 청크 추가
    /// </summary>
    public async Task AddChunksAsync(
        string documentId,
        IEnumerable<string> chunkTexts,
        CancellationToken cancellationToken = default)
    {
        var chunkCount = chunkTexts.Count();
        LogAddingChunks(_logger, chunkCount, documentId);

        // Get existing document
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
            throw new InvalidOperationException($"Document {documentId} not found");

        // Get current max chunk index
        var existingChunks = await _vectorStore.GetByDocumentIdAsync(documentId, cancellationToken);
        var maxIndex = existingChunks.Any() ? existingChunks.Max(c => c.ChunkIndex) : -1;

        // Create new chunks
        var newChunks = new List<DocumentChunkEntity>();
        foreach (var text in chunkTexts)
        {
            var chunk = DocumentChunkEntity.Create(
                documentId,
                text,
                ++maxIndex,
                existingChunks.Count() + chunkTexts.Count());
            newChunks.Add(chunk);
        }

        // Generate embeddings and store
        newChunks = await GenerateEmbeddingsAsync(newChunks, cancellationToken);
        await _vectorStore.StoreBatchAsync(newChunks, cancellationToken);

        LogSuccessfullyAddedChunks(_logger, newChunks.Count, documentId);
    }

    /// <summary>
    /// 문서 삭제
    /// </summary>
    public async Task<bool> DeleteByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        LogDeletingDocument(_logger, documentId);

        try
        {
            // Delete chunks from vector store
            await _vectorStore.DeleteByDocumentIdAsync(documentId, cancellationToken);

            // Delete document from repository
            var deleted = await _documentRepository.DeleteAsync(documentId, cancellationToken);

            if (deleted)
            {
                LogSuccessfullyDeletedDocument(_logger, documentId);
            }
            else
            {
                LogDocumentNotFound(_logger, documentId);
            }

            return deleted;
        }
        catch (Exception ex)
        {
            LogFailedToDeleteDocument(_logger, ex, documentId);
            throw;
        }
    }

    /// <summary>
    /// 청크 삭제
    /// </summary>
    public async Task<bool> DeleteChunkAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        LogDeletingChunk(_logger, chunkId);
        return await _vectorStore.DeleteAsync(chunkId, cancellationToken);
    }

    /// <summary>
    /// 인덱스 재구성
    /// </summary>
    public async Task ReindexDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        LogReindexingDocument(_logger, documentId);

        // Get document
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
            throw new InvalidOperationException($"Document {documentId} not found");

        // Get existing chunks
        var chunks = await _vectorStore.GetByDocumentIdAsync(documentId, cancellationToken);
        
        // Regenerate embeddings
        var chunksList = chunks.ToList();
        chunksList = await GenerateEmbeddingsAsync(chunksList, cancellationToken);

        // Update chunks in vector store by re-storing them
        await _vectorStore.DeleteByDocumentIdAsync(documentId, cancellationToken);
        await _vectorStore.StoreBatchAsync(chunksList, cancellationToken);

        LogSuccessfullyReindexedDocument(_logger, documentId, chunksList.Count);
    }

    /// <summary>
    /// 인덱싱 통계
    /// </summary>
    public async Task<IndexingStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var docCount = await _documentRepository.GetCountAsync(cancellationToken);
        var chunkCount = await _vectorStore.CountAsync(cancellationToken);

        return new IndexingStatistics
        {
            TotalDocuments = docCount,
            TotalChunks = chunkCount,
            AverageChunksPerDocument = docCount > 0 ? (double)chunkCount / docCount : 0,
            DefaultChunkSize = _options.ChunkSize,
            DefaultChunkOverlap = _options.ChunkOverlap,
            EmbeddingModel = _embeddingService.GetType().Name
        };
    }

    private async Task<List<DocumentChunkEntity>> GenerateEmbeddingsAsync(
        List<DocumentChunkEntity> chunks,
        CancellationToken cancellationToken)
    {
        if (chunks.Count == 0) return chunks;

        // 배치 임베딩 API 사용 (성능 최적화)
        try
        {
            var texts = chunks.Select(c => c.Content).ToList();
            LogExtractedTextsFromChunks(_logger, texts.Count);

            for (int i = 0; i < texts.Count; i++)
            {
                var tokens = texts[i].Length / 4;
                LogTextDetails(_logger, i, texts.Count, texts[i].Length, tokens);

                if (tokens > 8000)
                {
                    LogTextExceedsLimit(_logger, i, tokens);
                }
            }

            LogCallingGenerateEmbeddingsBatch(_logger, texts.Count);
            var embeddings = await _embeddingService.GenerateEmbeddingsBatchAsync(texts, cancellationToken);
            var embeddingArray = embeddings.ToArray();

            // 임베딩을 청크에 할당
            for (int i = 0; i < chunks.Count && i < embeddingArray.Length; i++)
            {
                chunks[i] = new DocumentChunkEntity
                {
                    Id = chunks[i].Id,
                    DocumentId = chunks[i].DocumentId,
                    Content = chunks[i].Content,
                    ChunkIndex = chunks[i].ChunkIndex,
                    Embedding = embeddingArray[i],
                    TokenCount = chunks[i].TokenCount,
                    Metadata = chunks[i].Metadata
                };
            }

            LogBatchEmbeddingCompleted(_logger, chunks.Count);
            return chunks;
        }
        catch (Exception ex)
        {
            LogBatchEmbeddingFailed(_logger, ex);

            // Fallback: 개별 임베딩 생성 (기존 병렬 처리 방식)
            if (_options.ParallelEmbedding && chunks.Count > 1)
            {
                var semaphore = new SemaphoreSlim(_options.MaxParallelEmbedding);
                var tasks = chunks.Select(async chunk =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        var embedding = await _embeddingService.GenerateEmbeddingAsync(
                            chunk.Content, cancellationToken);
                        return new DocumentChunkEntity
                        {
                            Id = chunk.Id,
                            DocumentId = chunk.DocumentId,
                            Content = chunk.Content,
                        ChunkIndex = chunk.ChunkIndex,
                        TokenCount = chunk.TokenCount,
                        Metadata = chunk.Metadata,
                        Embedding = embedding,
                        Score = chunk.Score,
                        CreatedAt = chunk.CreatedAt
                    };
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }
        else
        {
            // Sequential embedding generation
            var result = new List<DocumentChunkEntity>();
            foreach (var chunk in chunks)
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(
                    chunk.Content, cancellationToken);
                result.Add(new DocumentChunkEntity
                {
                    Id = chunk.Id,
                    DocumentId = chunk.DocumentId,
                    Content = chunk.Content,
                    ChunkIndex = chunk.ChunkIndex,
                    TokenCount = chunk.TokenCount,
                    Metadata = chunk.Metadata,
                    Embedding = embedding,
                    Score = chunk.Score,
                    CreatedAt = chunk.CreatedAt
                });
            }
            return result;
        }
        }
    }

    private static DocumentChunkEntity ConvertToEntityChunk(DocumentChunkModel modelChunk)
    {
        return DocumentChunkEntity.Create(
            modelChunk.DocumentId,
            modelChunk.Content,
            modelChunk.ChunkIndex,
            modelChunk.TotalChunks
        );
    }

    private static DocumentChunkModel ConvertToModelChunk(DocumentChunkEntity entityChunk)
    {
        return DocumentChunkModel.Create(
            entityChunk.DocumentId,
            entityChunk.Content,
            entityChunk.ChunkIndex,
            entityChunk.TotalChunks,
            entityChunk.Embedding,
            0f, // score
            entityChunk.TokenCount,
            entityChunk.Metadata
        );
    }

    private static int EstimateTokenCount(string text)
    {
        // Simple estimation: ~4 characters per token
        return text.Length / 4;
    }

    /// <summary>
    /// Update document metadata (supports user corrections to AI-extracted metadata)
    /// </summary>
    /// <param name="documentId">Document ID</param>
    /// <param name="metadata">Updated metadata dictionary</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task UpdateDocumentMetadataAsync(
        string documentId,
        Dictionary<string, object> metadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));
        if (metadata == null || metadata.Count == 0)
            throw new ArgumentException("Metadata cannot be null or empty", nameof(metadata));

        LogUpdatingMetadata(_logger, documentId);

        // Get existing document
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
        {
            throw new InvalidOperationException($"Document with ID '{documentId}' not found");
        }

        // Update metadata
        foreach (var (key, value) in metadata)
        {
            document.SetMetadata(key, value);
        }

        // Mark metadata source as User-corrected
        document.SetMetadata("MetadataSource", "UserCorrected");
        document.SetMetadata("MetadataLastUpdated", DateTime.UtcNow);

        // Save updated document
        await _documentRepository.UpdateAsync(document, cancellationToken);

        LogMetadataUpdated(_logger, documentId);
    }

    /// <summary>
    /// Correct AI-extracted metadata for a document
    /// </summary>
    /// <param name="documentId">Document ID</param>
    /// <param name="correctedMetadata">Corrected ExtractedMetadata instance</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task CorrectExtractedMetadataAsync(
        string documentId,
        ExtractedMetadata correctedMetadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));
        ArgumentNullException.ThrowIfNull(correctedMetadata);

        LogCorrectingExtractedMetadata(_logger, documentId);

        // Get existing document
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
        {
            throw new InvalidOperationException($"Document with ID '{documentId}' not found");
        }

        // Update ExtractedMetadata
        correctedMetadata.Source = MetadataSource.User;
        correctedMetadata.ExtractionMethod = "User-Corrected";
        correctedMetadata.OverallConfidence = 1.0f; // User corrections are 100% confident

        document.SetMetadata("AIExtractedMetadata", correctedMetadata);
        document.SetMetadata("MetadataExtractionMethod", "User-Corrected");
        document.SetMetadata("MetadataConfidence", 1.0f);
        document.SetMetadata("MetadataLastUpdated", DateTime.UtcNow);

        // Save updated document
        await _documentRepository.UpdateAsync(document, cancellationToken);

        LogExtractedMetadataCorrected(_logger, documentId);
    }

    /// <summary>
    /// Get current extracted metadata for a document
    /// </summary>
    /// <param name="documentId">Document ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ExtractedMetadata if available, null otherwise</returns>
    public async Task<ExtractedMetadata?> GetExtractedMetadataAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
        {
            return null;
        }

        if (document.Metadata.TryGetValue("AIExtractedMetadata", out var metadataObj) &&
            metadataObj is ExtractedMetadata extractedMetadata)
        {
            return extractedMetadata;
        }

        return null;
    }

    /// <summary>
    /// 여러 문서의 메타데이터를 배치로 추출
    /// </summary>
    /// <param name="documents">문서 목록 (DocumentId, Content)</param>
    /// <param name="schema">메타데이터 스키마</param>
    /// <param name="strategy">추출 전략</param>
    /// <param name="maxConcurrency">최대 병렬 처리 수</param>
    /// <param name="progressCallback">진행 상황 콜백</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>배치 추출 결과</returns>
    public async Task<BatchMetadataExtractionResult> ExtractMetadataBatchAsync(
        IEnumerable<(string DocumentId, string Content)> documents,
        MetadataSchema schema = MetadataSchema.General,
        MetadataExtractionStrategy strategy = MetadataExtractionStrategy.Smart,
        int maxConcurrency = 4,
        IProgress<BatchMetadataExtractionProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (_metadataExtractor == null)
        {
            throw new InvalidOperationException(
                "Metadata extractor is not configured. Use WithOpenAIMetadataExtractor() or WithCustomMetadataExtractor() in the builder.");
        }

        var docList = documents.ToList();
        if (docList.Count == 0)
        {
            throw new ArgumentException("Document list cannot be empty", nameof(documents));
        }

        LogStartingBatchMetadataExtraction(_logger, docList.Count);

        // Create batch request
        var request = new BatchMetadataExtractionRequest
        {
            MaxConcurrency = maxConcurrency,
            ContinueOnError = true,
            Items = docList.Select(doc => new MetadataExtractionItem
            {
                DocumentId = doc.DocumentId,
                Content = doc.Content,
                Schema = schema,
                Strategy = strategy
            }).ToList()
        };

        // Extract metadata options from IndexerOptions
        var indexingOptions = new IndexingOptions();
        if (_options.CustomOptions != null)
        {
            foreach (var (key, value) in _options.CustomOptions)
            {
                indexingOptions.CustomOptions[key] = value;
            }
        }

        var minConfidence = indexingOptions.GetMinMetadataConfidence();
        var customPrompt = indexingOptions.GetCustomMetadataPrompt();

        var extractionOptions = new AIMetadataExtractionOptions
        {
            Strategy = strategy,
            MinConfidence = minConfidence,
            CustomPrompt = customPrompt
        };

        // Call batch extraction with progress reporting
        var result = await _metadataExtractor.ExtractBatchWithProgressAsync(
            request,
            extractionOptions,
            progressCallback,
            cancellationToken);

        LogBatchMetadataExtractionCompleted(_logger, result.SuccessfulItems, result.TotalItems);

        return result;
    }

    /// <summary>
    /// 여러 문서를 인덱싱하고 메타데이터를 자동 추출 (배치 모드)
    /// </summary>
    /// <param name="documents">문서 목록 (DocumentId, Content, Metadata)</param>
    /// <param name="options">인덱싱 옵션</param>
    /// <param name="progressCallback">진행 상황 콜백</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>배치 인덱싱 결과</returns>
    public async Task<BatchIndexingResult> IndexDocumentsBatchAsync(
        IEnumerable<(string DocumentId, string Content, Dictionary<string, object>? Metadata)> documents,
        IndexingOptions? options = null,
        IProgress<BatchProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var docList = documents.ToList();
        if (docList.Count == 0)
        {
            throw new ArgumentException("Document list cannot be empty", nameof(documents));
        }

        options ??= new IndexingOptions();

        LogStartingBatchDocumentIndexing(_logger, docList.Count);

        var result = new BatchIndexingResult
        {
            TotalDocuments = docList.Count
        };

        var startTime = DateTime.UtcNow;

        // Report initial progress
        progressCallback?.Report(new BatchProgress
        {
            BatchId = result.BatchId,
            TotalItems = docList.Count,
            Status = "Processing",
            Message = "Starting batch document indexing..."
        });

        for (int i = 0; i < docList.Count; i++)
        {
            var (documentId, content, metadata) = docList[i];

            try
            {
                // Index document with automatic metadata extraction
                await IndexDocumentAsync(content, documentId, metadata, cancellationToken);

                result.SuccessfulDocuments++;

                // Report progress
                progressCallback?.Report(new BatchProgress
                {
                    BatchId = result.BatchId,
                    CurrentItem = i + 1,
                    TotalItems = docList.Count,
                    SuccessfulItems = result.SuccessfulDocuments,
                    FailedItems = result.FailedDocuments,
                    Status = "Processing",
                    Message = $"Indexed document {i + 1}/{docList.Count}: {documentId}"
                });
            }
            catch (Exception ex)
            {
                LogFailedToIndexDocumentWarning(_logger, ex, documentId);

                result.FailedDocuments++;
                result.Results.Add(new IndexingResult
                {
                    DocumentId = documentId,
                    Success = false,
                    Errors = new List<IndexingError>
                    {
                        new IndexingError
                        {
                            Message = ex.Message,
                            ErrorCode = "INDEXING_FAILED"
                        }
                    }
                });
            }
        }

        result.TotalProcessingTime = DateTime.UtcNow - startTime;

        // Report final progress
        progressCallback?.Report(new BatchProgress
        {
            BatchId = result.BatchId,
            CurrentItem = docList.Count,
            TotalItems = docList.Count,
            SuccessfulItems = result.SuccessfulDocuments,
            FailedItems = result.FailedDocuments,
            Status = "Completed",
            Message = $"Batch indexing completed: {result.SuccessfulDocuments} succeeded, {result.FailedDocuments} failed"
        });

        LogBatchDocumentIndexingCompleted(_logger, result.SuccessfulDocuments, result.TotalDocuments, result.TotalProcessingTime.TotalMilliseconds);

        return result;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexing document {DocumentId}, JobId: {JobId}")]
    private static partial void LogIndexingDocument(ILogger logger, string documentId, string jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracting AI metadata for document {DocumentId}")]
    private static partial void LogExtractingAIMetadata(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI metadata extracted: confidence={Confidence}, topics={TopicCount}")]
    private static partial void LogAIMetadataExtracted(ILogger logger, float confidence, int topicCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to extract AI metadata for document {DocumentId}, continuing without it")]
    private static partial void LogFailedToExtractAIMetadata(ILogger logger, Exception exception, string documentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Document {DocumentId} has no chunks")]
    private static partial void LogDocumentHasNoChunks(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Converting {Count} DocumentChunks to entities")]
    private static partial void LogConvertingChunksToEntities(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Chunk {Index}/{Total}: {Length} chars (~{Tokens} tokens)")]
    private static partial void LogChunkDetails(ILogger logger, int index, int total, int length, int tokens);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Chunk {Index} exceeds token limit (~{Tokens} tokens) - splitting into smaller chunks")]
    private static partial void LogChunkExceedsTokenLimit(ILogger logger, int index, int tokens);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created sub-chunk {SubIndex}/{SubTotal}: {Length} chars (~{Tokens} tokens)")]
    private static partial void LogSubChunkCreated(ILogger logger, int subIndex, int subTotal, int length, int tokens);

    [LoggerMessage(Level = LogLevel.Information, Message = "Split oversized chunk {Index} into {SubChunks} smaller chunks")]
    private static partial void LogSplitOversizedChunk(ILogger logger, int index, int subChunks);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Total entity chunks after splitting: {EntityCount} (original: {OriginalCount})")]
    private static partial void LogTotalEntityChunksAfterSplitting(ILogger logger, int entityCount, int originalCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Converted {Count} entities, calling GenerateEmbeddingsAsync")]
    private static partial void LogCallingGenerateEmbeddings(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Building GraphRAG index for document {DocumentId}")]
    private static partial void LogBuildingGraphRAGIndex(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "GraphRAG index built successfully for document {DocumentId}")]
    private static partial void LogGraphRAGIndexBuilt(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully indexed document {DocumentId} with {ChunkCount} chunks")]
    private static partial void LogSuccessfullyIndexedDocument(ILogger logger, string documentId, int chunkCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to index document {DocumentId}")]
    private static partial void LogFailedToIndexDocument(ILogger logger, Exception exception, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexing chunks as document: {DocumentId}")]
    private static partial void LogIndexingChunksAsDocument(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch indexing {Count} documents, BatchId: {BatchId}")]
    private static partial void LogBatchIndexing(ILogger logger, int count, string batchId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to index document {DocumentId} in batch {BatchId}")]
    private static partial void LogFailedToIndexDocumentInBatch(ILogger logger, Exception exception, string documentId, string batchId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch indexing completed. Indexed {Success}/{Total} documents (BatchId: {BatchId})")]
    private static partial void LogBatchIndexingCompleted(ILogger logger, int success, int total, string batchId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updating document: {DocumentId}")]
    private static partial void LogUpdatingDocument(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully updated document {DocumentId}")]
    private static partial void LogSuccessfullyUpdatedDocument(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Adding {Count} chunks to document: {DocumentId}")]
    private static partial void LogAddingChunks(ILogger logger, int count, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully added {Count} chunks to document {DocumentId}")]
    private static partial void LogSuccessfullyAddedChunks(ILogger logger, int count, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleting document: {DocumentId}")]
    private static partial void LogDeletingDocument(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully deleted document {DocumentId}")]
    private static partial void LogSuccessfullyDeletedDocument(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Document {DocumentId} not found")]
    private static partial void LogDocumentNotFound(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to delete document {DocumentId}")]
    private static partial void LogFailedToDeleteDocument(ILogger logger, Exception exception, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleting chunk: {ChunkId}")]
    private static partial void LogDeletingChunk(ILogger logger, string chunkId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reindexing document: {DocumentId}")]
    private static partial void LogReindexingDocument(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully reindexed document {DocumentId} with {ChunkCount} chunks")]
    private static partial void LogSuccessfullyReindexedDocument(ILogger logger, string documentId, int chunkCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted {Count} texts from chunks for embedding")]
    private static partial void LogExtractedTextsFromChunks(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Text {Index}/{Total}: {Length} chars (~{Tokens} tokens)")]
    private static partial void LogTextDetails(ILogger logger, int index, int total, int length, int tokens);

    [LoggerMessage(Level = LogLevel.Error, Message = "Text {Index} exceeds limit: ~{Tokens} tokens")]
    private static partial void LogTextExceedsLimit(ILogger logger, int index, int tokens);

    [LoggerMessage(Level = LogLevel.Information, Message = "Calling GenerateEmbeddingsBatchAsync with {Count} texts")]
    private static partial void LogCallingGenerateEmbeddingsBatch(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch embedding completed: {Count} chunks")]
    private static partial void LogBatchEmbeddingCompleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Batch embedding failed, falling back to individual processing")]
    private static partial void LogBatchEmbeddingFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updating metadata for document {DocumentId}")]
    private static partial void LogUpdatingMetadata(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Metadata updated successfully for document {DocumentId}")]
    private static partial void LogMetadataUpdated(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Correcting extracted metadata for document {DocumentId}")]
    private static partial void LogCorrectingExtractedMetadata(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted metadata corrected successfully for document {DocumentId}")]
    private static partial void LogExtractedMetadataCorrected(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting batch metadata extraction for {Count} documents")]
    private static partial void LogStartingBatchMetadataExtraction(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch metadata extraction completed: {Successful}/{Total} documents succeeded")]
    private static partial void LogBatchMetadataExtractionCompleted(ILogger logger, int successful, int total);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting batch document indexing for {Count} documents")]
    private static partial void LogStartingBatchDocumentIndexing(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to index document: {DocumentId}")]
    private static partial void LogFailedToIndexDocumentWarning(ILogger logger, Exception exception, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch document indexing completed: {Successful}/{Total} documents succeeded, Time={Time}ms")]
    private static partial void LogBatchDocumentIndexingCompleted(ILogger logger, int successful, int total, double time);

    #endregion
}

/// <summary>
/// Indexer 옵션
/// </summary>
public class IndexerOptions
{
    public int ChunkSize { get; set; } = 512;
    public int ChunkOverlap { get; set; } = 64;
    public bool ParallelEmbedding { get; set; } = true;
    public int MaxParallelEmbedding { get; set; } = 4;
    public ChunkingStrategy ChunkingStrategy { get; set; } = ChunkingStrategy.Auto;
    public Dictionary<string, object>? CustomOptions { get; set; }
}

/// <summary>
/// 청킹 전략
/// </summary>
public enum ChunkingStrategy
{
    Auto,
    Fixed,
    Sentence,
    Paragraph,
    Semantic
}

/// <summary>
/// 인덱싱 통계
/// </summary>
public class IndexingStatistics
{
    public int TotalDocuments { get; set; }
    public int TotalChunks { get; set; }
    public double AverageChunksPerDocument { get; set; }
    public int DefaultChunkSize { get; set; }
    public int DefaultChunkOverlap { get; set; }
    public string EmbeddingModel { get; set; } = string.Empty;
}