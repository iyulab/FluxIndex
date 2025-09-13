using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.SDK.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.SDK;

/// <summary>
/// Indexer - 문서 인덱싱 및 저장 담당
/// </summary>
public class Indexer
{
    private readonly IVectorStore _vectorStore;
    private readonly IDocumentRepository _documentRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly IChunkingService _chunkingService;
    private readonly ILogger<Indexer> _logger;
    private readonly IndexerOptions _options;

    public Indexer(
        IVectorStore vectorStore,
        IDocumentRepository documentRepository,
        IEmbeddingService embeddingService,
        IChunkingService chunkingService,
        IndexerOptions options,
        ILogger<Indexer>? logger = null)
    {
        _vectorStore = vectorStore;
        _documentRepository = documentRepository;
        _embeddingService = embeddingService;
        _chunkingService = chunkingService;
        _options = options;
        _logger = logger ?? new NullLogger<Indexer>();
    }

    /// <summary>
    /// 문서 인덱싱
    /// </summary>
    public async Task<string> IndexDocumentAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Indexing document: {DocumentId}", document.Id);

        try
        {
            // Save document metadata
            await _documentRepository.AddAsync(document, cancellationToken);

            // Process chunks
            var chunks = document.Chunks.ToList();
            if (!chunks.Any())
            {
                _logger.LogWarning("Document {DocumentId} has no chunks", document.Id);
                return document.Id;
            }

            // Generate embeddings for chunks
            await GenerateEmbeddingsAsync(chunks, cancellationToken);

            // Store chunks in vector store
            await _vectorStore.StoreBatchAsync(chunks, cancellationToken);

            _logger.LogInformation("Successfully indexed document {DocumentId} with {ChunkCount} chunks", 
                document.Id, chunks.Count);

            return document.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index document {DocumentId}", document.Id);
            throw;
        }
    }

    /// <summary>
    /// 청크 리스트에서 문서 생성 및 인덱싱
    /// </summary>
    public async Task<string> IndexChunksAsync(
        IEnumerable<DocumentChunk> chunks,
        string documentId = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        documentId ??= Guid.NewGuid().ToString();
        _logger.LogInformation("Indexing chunks as document: {DocumentId}", documentId);

        // Create document
        var document = new Document(documentId)
        {
            Metadata = metadata ?? new Dictionary<string, object>()
        };

        // Add chunks to document
        foreach (var chunk in chunks)
        {
            chunk.DocumentId = documentId;
            document.AddChunk(chunk);
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
        _logger.LogInformation("Batch indexing {Count} documents", documents.Count());

        var semaphore = new SemaphoreSlim(parallelism);
        var tasks = documents.Select(async doc =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await IndexDocumentAsync(doc, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        _logger.LogInformation("Batch indexing completed. Indexed {Count} documents", results.Length);

        return results;
    }

    /// <summary>
    /// 문서 업데이트
    /// </summary>
    public async Task UpdateDocumentAsync(
        string documentId,
        Document updatedDocument,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating document: {DocumentId}", documentId);

        // Delete existing chunks
        await _vectorStore.DeleteByDocumentIdAsync(documentId, cancellationToken);

        // Update document
        updatedDocument.Id = documentId;
        await _documentRepository.UpdateAsync(updatedDocument, cancellationToken);

        // Process new chunks
        var chunks = updatedDocument.Chunks.ToList();
        if (chunks.Any())
        {
            await GenerateEmbeddingsAsync(chunks, cancellationToken);
            await _vectorStore.StoreBatchAsync(chunks, cancellationToken);
        }

        _logger.LogInformation("Successfully updated document {DocumentId}", documentId);
    }

    /// <summary>
    /// 청크 추가
    /// </summary>
    public async Task AddChunksAsync(
        string documentId,
        IEnumerable<string> chunkTexts,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Adding {Count} chunks to document: {DocumentId}", 
            chunkTexts.Count(), documentId);

        // Get existing document
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
            throw new InvalidOperationException($"Document {documentId} not found");

        // Get current max chunk index
        var existingChunks = await _vectorStore.GetByDocumentIdAsync(documentId, cancellationToken);
        var maxIndex = existingChunks.Any() ? existingChunks.Max(c => c.ChunkIndex) : -1;

        // Create new chunks
        var newChunks = new List<DocumentChunk>();
        foreach (var text in chunkTexts)
        {
            var chunk = new DocumentChunk(text, ++maxIndex)
            {
                DocumentId = documentId,
                TokenCount = EstimateTokenCount(text)
            };
            newChunks.Add(chunk);
        }

        // Generate embeddings and store
        await GenerateEmbeddingsAsync(newChunks, cancellationToken);
        await _vectorStore.StoreBatchAsync(newChunks, cancellationToken);

        _logger.LogInformation("Successfully added {Count} chunks to document {DocumentId}", 
            newChunks.Count, documentId);
    }

    /// <summary>
    /// 문서 삭제
    /// </summary>
    public async Task<bool> DeleteDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting document: {DocumentId}", documentId);

        try
        {
            // Delete chunks from vector store
            await _vectorStore.DeleteByDocumentIdAsync(documentId, cancellationToken);

            // Delete document from repository
            var deleted = await _documentRepository.DeleteAsync(documentId, cancellationToken);

            if (deleted)
            {
                _logger.LogInformation("Successfully deleted document {DocumentId}", documentId);
            }
            else
            {
                _logger.LogWarning("Document {DocumentId} not found", documentId);
            }

            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete document {DocumentId}", documentId);
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
        _logger.LogInformation("Deleting chunk: {ChunkId}", chunkId);
        return await _vectorStore.DeleteAsync(chunkId, cancellationToken);
    }

    /// <summary>
    /// 인덱스 재구성
    /// </summary>
    public async Task ReindexDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reindexing document: {DocumentId}", documentId);

        // Get document
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
            throw new InvalidOperationException($"Document {documentId} not found");

        // Get existing chunks
        var chunks = await _vectorStore.GetByDocumentIdAsync(documentId, cancellationToken);
        
        // Regenerate embeddings
        var chunksList = chunks.ToList();
        await GenerateEmbeddingsAsync(chunksList, cancellationToken);

        // Update chunks in vector store
        foreach (var chunk in chunksList)
        {
            await _vectorStore.UpdateAsync(chunk, cancellationToken);
        }

        _logger.LogInformation("Successfully reindexed document {DocumentId} with {ChunkCount} chunks", 
            documentId, chunksList.Count);
    }

    /// <summary>
    /// 인덱싱 통계
    /// </summary>
    public async Task<IndexingStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var docCount = await _documentRepository.GetCountAsync(cancellationToken);
        var chunkCount = await _vectorStore.GetCountAsync(cancellationToken);

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

    private async Task GenerateEmbeddingsAsync(
        List<DocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        if (_options.ParallelEmbedding && chunks.Count > 1)
        {
            // Parallel embedding generation
            var semaphore = new SemaphoreSlim(_options.MaxParallelEmbedding);
            var tasks = chunks.Select(async chunk =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var embedding = await _embeddingService.GenerateEmbeddingAsync(
                        chunk.Content, cancellationToken);
                    chunk.Embedding = embedding;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }
        else
        {
            // Sequential embedding generation
            foreach (var chunk in chunks)
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(
                    chunk.Content, cancellationToken);
                chunk.Embedding = embedding;
            }
        }
    }

    private int EstimateTokenCount(string text)
    {
        // Simple estimation: ~4 characters per token
        return text.Length / 4;
    }
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