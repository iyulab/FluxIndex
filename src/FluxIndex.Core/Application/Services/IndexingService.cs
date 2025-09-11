using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// 문서 인덱싱 서비스 - 고도화된 메타데이터 처리 포함
/// </summary>
public class IndexingService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IMetadataEnrichmentService _metadataEnrichmentService;
    private readonly ILogger<IndexingService> _logger;

    public IndexingService(
        IDocumentRepository documentRepository,
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        IMetadataEnrichmentService metadataEnrichmentService,
        ILogger<IndexingService> logger)
    {
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _metadataEnrichmentService = metadataEnrichmentService ?? throw new ArgumentNullException(nameof(metadataEnrichmentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Document> IndexDocumentAsync(
        string documentId,
        IEnumerable<DocumentChunk> chunks,
        DocumentMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting document indexing for {DocumentId}", documentId);

        // Create document entity
        var document = Document.Create(documentId);
        document.UpdateMetadata(metadata);

        try
        {
            // Save document to repository
            await _documentRepository.AddAsync(document, cancellationToken);

            // Process chunks with advanced metadata enrichment
            var chunksList = chunks.ToList();
            var totalChunks = chunksList.Count;
            
            for (int i = 0; i < totalChunks; i++)
            {
                var chunk = chunksList[i];
                chunk.DocumentId = document.Id;
                
                // Enrich metadata with contextual information
                var previousContent = i > 0 ? chunksList[i - 1].Content : null;
                var nextContent = i < totalChunks - 1 ? chunksList[i + 1].Content : null;
                
                var enrichedMetadata = await _metadataEnrichmentService.EnrichMetadataAsync(
                    chunk.Content,
                    i,
                    previousContent,
                    nextContent,
                    metadata?.Properties,
                    cancellationToken
                );
                
                chunk.SetMetadata(enrichedMetadata);
                
                // Evaluate chunk quality
                var quality = await _metadataEnrichmentService.EvaluateQualityAsync(
                    chunk, cancellationToken: cancellationToken);
                chunk.SetQuality(quality);
                
                // Generate embedding
                var embedding = await _embeddingService.GenerateEmbeddingAsync(
                    chunk.Content,
                    cancellationToken
                );
                chunk.SetEmbedding(embedding);

                // Store in vector store
                await _vectorStore.StoreAsync(chunk, cancellationToken);

                // Add to document
                document.AddChunk(chunk);

                _logger.LogDebug("Indexed chunk {ChunkIndex}/{TotalChunks} with enriched metadata for document {DocumentId}",
                    i + 1, totalChunks, document.Id);
            }
            
            // Analyze relationships between chunks
            await AnalyzeChunkRelationshipsAsync(chunksList, cancellationToken);

            // Mark document as indexed
            document.MarkAsIndexed();
            await _documentRepository.UpdateAsync(document, cancellationToken);

            _logger.LogInformation("Successfully indexed document {DocumentId} with {ChunkCount} chunks",
                document.Id, totalChunks);

            return document;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index document {DocumentId}", documentId);
            document.MarkAsFailed(ex.Message);
            await _documentRepository.UpdateAsync(document, cancellationToken);
            throw;
        }
    }

    public async Task<bool> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting document {DocumentId}", documentId);

        // Delete chunks from vector store
        var deletedCount = await _vectorStore.DeleteByDocumentIdAsync(documentId, cancellationToken);
        
        // Delete document from repository
        await _documentRepository.DeleteAsync(documentId, cancellationToken);

        _logger.LogInformation("Deleted document {DocumentId} and {ChunkCount} chunks",
            documentId, deletedCount);

        return true;
    }
    
    /// <summary>
    /// 청크 간 관계 분석 및 저장
    /// </summary>
    private async Task AnalyzeChunkRelationshipsAsync(
        List<DocumentChunk> chunks, 
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Analyzing relationships between {ChunkCount} chunks", chunks.Count);
        
        foreach (var sourceChunk in chunks)
        {
            var relationships = await _metadataEnrichmentService.AnalyzeRelationshipsAsync(
                sourceChunk, chunks, cancellationToken);
            
            foreach (var relationship in relationships)
            {
                sourceChunk.AddRelationship(relationship);
            }
        }
        
        // Update chunks in vector store with relationship information
        foreach (var chunk in chunks)
        {
            if (chunk.Relationships.Any())
            {
                await _vectorStore.UpdateAsync(chunk, cancellationToken);
            }
        }
        
        _logger.LogDebug("Relationship analysis completed");
    }
}