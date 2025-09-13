using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL 기반 벡터 저장소 구현
/// </summary>
public class PostgreSQLVectorStore : IVectorStore
{
    private readonly FluxIndexDbContext _context;
    private readonly ILogger<PostgreSQLVectorStore> _logger;
    private readonly PostgreSQLOptions _options;

    public PostgreSQLVectorStore(
        FluxIndexDbContext context,
        ILogger<PostgreSQLVectorStore> logger,
        PostgreSQLOptions options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        if (chunk == null)
            throw new ArgumentNullException(nameof(chunk));

        _logger.LogDebug("Storing chunk {ChunkId}", chunk.Id);

        var vectorChunk = new VectorChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.Parse(chunk.DocumentId), // Assume document already exists
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            Embedding = chunk.Embedding,
            TokenCount = chunk.TokenCount,
            Metadata = chunk.Metadata,
            CreatedAt = DateTime.UtcNow
        };

        _context.Chunks.Add(vectorChunk);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Successfully stored chunk {ChunkId}", chunk.Id);
        return vectorChunk.Id.ToString();
    }

    public async Task<IEnumerable<string>> StoreBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        if (!chunkList.Any())
            return Enumerable.Empty<string>();

        _logger.LogInformation("Storing batch of {ChunkCount} chunks", chunkList.Count);

        var storedIds = new List<string>();
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var chunk in chunkList)
            {
                var id = await StoreAsync(chunk, cancellationToken);
                storedIds.Add(id);
            }

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Successfully stored {ChunkCount} chunks", storedIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store chunk batch");
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return storedIds;
    }

    public async Task<string> StoreDocumentAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        _logger.LogDebug("Storing document {DocumentId} with {ChunkCount} chunks", 
            document.Id, document.Chunks.Count);

        // 문서 해시 계산
        var contentHash = ComputeHash(document);

        // 중복 체크
        var existingDoc = await _context.Documents
            .FirstOrDefaultAsync(d => d.ContentHash == contentHash, cancellationToken);

        if (existingDoc != null && !_options.AllowDuplicates)
        {
            _logger.LogInformation("Document with same content already exists: {ExistingId}", 
                existingDoc.ExternalId);
            return existingDoc.ExternalId;
        }

        // 새 문서 생성
        var vectorDoc = new VectorDocument
        {
            Id = Guid.NewGuid(),
            ExternalId = document.Id,
            Title = document.Metadata?.Properties.GetValueOrDefault("title")?.ToString(),
            Source = document.Metadata?.Properties.GetValueOrDefault("source")?.ToString(),
            ContentHash = contentHash,
            Metadata = document.Metadata?.Properties ?? new Dictionary<string, object>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // 청크 생성
        var vectorChunks = document.Chunks.Select((chunk, index) => new VectorChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = vectorDoc.Id,
            ChunkIndex = index,
            Content = chunk.Content,
            Embedding = chunk.Embedding,
            TokenCount = chunk.TokenCount,
            Metadata = chunk.Metadata,
            CreatedAt = DateTime.UtcNow
        }).ToList();

        vectorDoc.Chunks = vectorChunks;

        // 데이터베이스에 저장
        _context.Documents.Add(vectorDoc);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Successfully stored document {DocumentId} with {ChunkCount} chunks", 
            document.Id, vectorChunks.Count);

        return vectorDoc.ExternalId;
    }

    public async Task<IEnumerable<string>> StoreDocumentsBatchAsync(
        IEnumerable<Document> documents,
        CancellationToken cancellationToken = default)
    {
        var documentList = documents.ToList();
        if (!documentList.Any())
            return Enumerable.Empty<string>();

        _logger.LogInformation("Storing batch of {DocumentCount} documents", documentList.Count);

        var storedIds = new List<string>();

        // 트랜잭션으로 배치 처리
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var document in documentList)
            {
                var id = await StoreDocumentAsync(document, cancellationToken);
                storedIds.Add(id);
            }

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Successfully stored {DocumentCount} documents", storedIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store document batch");
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return storedIds;
    }

    public async Task<IEnumerable<DocumentChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        if (queryEmbedding == null || queryEmbedding.Length == 0)
            throw new ArgumentException("Query embedding cannot be null or empty", nameof(queryEmbedding));

        _logger.LogDebug("Searching for top {TopK} results with minimum score {MinScore}", topK, minScore);

        var pgVector = new Vector(queryEmbedding);

        var query = _context.Chunks
            .Where(c => c.Embedding != null)
            .Select(c => new
            {
                Chunk = c,
                Distance = c.Embedding!.CosineDistance(pgVector),
                Score = 1.0 - c.Embedding!.CosineDistance(pgVector)
            })
            .Where(x => x.Score >= minScore)
            .OrderByDescending(x => x.Score)
            .Take(topK);

        var results = await query
            .Include(x => x.Chunk.Document)
            .ToListAsync(cancellationToken);

        var documentChunks = results.Select(r => {
            var chunk = new DocumentChunk(r.Chunk.Content, r.Chunk.ChunkIndex)
            {
                Id = r.Chunk.Id.ToString(),
                DocumentId = r.Chunk.Document.ExternalId,
                TokenCount = r.Chunk.TokenCount,
                Metadata = r.Chunk.Metadata
            };

            if (r.Chunk.Embedding != null)
            {
                chunk.Embedding = r.Chunk.Embedding;
            }

            return chunk;
        }).ToList();

        _logger.LogInformation("Found {ResultCount} search results", documentChunks.Count);
        return documentChunks;
    }

    public async Task<DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Chunk ID cannot be empty", nameof(id));

        var vectorChunk = await _context.Chunks
            .Include(c => c.Document)
            .FirstOrDefaultAsync(c => c.Id.ToString() == id, cancellationToken);

        if (vectorChunk == null)
            return null;

        var chunk = new DocumentChunk(vectorChunk.Content, vectorChunk.ChunkIndex)
        {
            Id = vectorChunk.Id.ToString(),
            DocumentId = vectorChunk.Document.ExternalId,
            TokenCount = vectorChunk.TokenCount,
            Metadata = vectorChunk.Metadata
        };

        if (vectorChunk.Embedding != null)
        {
            chunk.Embedding = vectorChunk.Embedding;
        }

        return chunk;
    }

    public async Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        var vectorChunks = await _context.Chunks
            .Include(c => c.Document)
            .Where(c => c.Document.ExternalId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(cancellationToken);

        return vectorChunks.Select(c => {
            var chunk = new DocumentChunk(c.Content, c.ChunkIndex)
            {
                Id = c.Id.ToString(),
                DocumentId = c.Document.ExternalId,
                TokenCount = c.TokenCount,
                Metadata = c.Metadata
            };

            if (c.Embedding != null)
            {
                chunk.Embedding = c.Embedding;
            }

            return chunk;
        }).ToList();
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Chunk ID cannot be empty", nameof(id));

        var chunk = await _context.Chunks
            .FirstOrDefaultAsync(c => c.Id.ToString() == id, cancellationToken);

        if (chunk == null)
        {
            _logger.LogWarning("Chunk {ChunkId} not found for deletion", id);
            return false;
        }

        _context.Chunks.Remove(chunk);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Successfully deleted chunk {ChunkId}", id);
        return true;
    }

    public async Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        var chunks = await _context.Chunks
            .Include(c => c.Document)
            .Where(c => c.Document.ExternalId == documentId)
            .ToListAsync(cancellationToken);

        if (!chunks.Any())
        {
            _logger.LogWarning("No chunks found for document {DocumentId}", documentId);
            return false;
        }

        _context.Chunks.RemoveRange(chunks);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Successfully deleted {ChunkCount} chunks for document {DocumentId}", chunks.Count, documentId);
        return true;
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        return await _context.Chunks
            .AnyAsync(c => c.Id.ToString() == id, cancellationToken);
    }

    public async Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await GetAsync(id, cancellationToken);
    }

    public async Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        if (chunk == null)
            throw new ArgumentNullException(nameof(chunk));

        if (string.IsNullOrWhiteSpace(chunk.Id))
            throw new ArgumentException("Chunk ID cannot be empty", nameof(chunk));

        var existingChunk = await _context.Chunks
            .FirstOrDefaultAsync(c => c.Id.ToString() == chunk.Id, cancellationToken);

        if (existingChunk == null)
        {
            _logger.LogWarning("Chunk {ChunkId} not found for update", chunk.Id);
            return false;
        }

        existingChunk.Content = chunk.Content;
        existingChunk.Embedding = chunk.Embedding;
        existingChunk.TokenCount = chunk.TokenCount;
        existingChunk.Metadata = chunk.Metadata;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Updated chunk {ChunkId}", chunk.Id);
        return true;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Chunks.CountAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return await CountAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _context.Chunks.ExecuteDeleteAsync(cancellationToken);
        _logger.LogInformation("Cleared all chunks from vector store");
    }

    public async Task<IEnumerable<SearchResult>> SearchDocumentsAsync(
        float[] queryVector,
        int topK = 10,
        double threshold = 0.7,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (queryVector == null || queryVector.Length == 0)
            throw new ArgumentException("Query vector cannot be null or empty", nameof(queryVector));

        _logger.LogDebug("Searching for top {TopK} results with threshold {Threshold}", topK, threshold);

        var pgVector = new Vector(queryVector);
        
        // 벡터 검색 쿼리 구성
        var query = _context.Chunks
            .Where(c => c.Embedding != null)
            .Select(c => new
            {
                Chunk = c,
                Distance = c.Embedding!.CosineDistance(pgVector)
            })
            .Where(x => x.Distance <= (1 - threshold)) // Cosine distance to similarity
            .OrderBy(x => x.Distance)
            .Take(topK);

        // 필터 적용
        if (filter != null && filter.Any())
        {
            // JSONB 필터링 구현 (필요 시 확장)
            // query = query.Where(x => x.Chunk.Metadata.Contains(filter));
        }

        var results = await query
            .Include(x => x.Chunk.Document)
            .ToListAsync(cancellationToken);

        var searchResults = results.Select(r => {
            var chunk = new DocumentChunk(r.Chunk.Content, r.Chunk.ChunkIndex)
            {
                Id = r.Chunk.Id.ToString(),
                DocumentId = r.Chunk.Document.ExternalId,
                TokenCount = r.Chunk.TokenCount
            };

            if (r.Chunk.Embedding != null)
            {
                chunk.Embedding = r.Chunk.Embedding;
            }

            return new SearchResult
            {
                Chunk = chunk,
                Score = (float)(1 - r.Distance) // Convert distance to similarity
            };
        }).ToList();

        _logger.LogInformation("Found {ResultCount} search results", searchResults.Count);
        return searchResults;
    }

    public async Task<IEnumerable<SearchResult>> HybridSearchAsync(
        string keyword,
        float[]? queryVector = null,
        int topK = 10,
        double vectorWeight = 0.5,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword) && (queryVector == null || queryVector.Length == 0))
            throw new ArgumentException("Either keyword or query vector must be provided");

        _logger.LogDebug("Performing hybrid search with keyword '{Keyword}' and vector weight {Weight}", 
            keyword, vectorWeight);

        var results = new List<SearchResult>();

        // 키워드 검색
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var keywordChunks = await _context.Chunks
                .Where(c => EF.Functions.ILike(c.Content, $"%{keyword}%"))
                .Include(c => c.Document)
                .Take(topK * 2) // 더 많이 가져와서 나중에 필터링
                .ToListAsync(cancellationToken);

            var keywordResults = keywordChunks.Select(c => {
                var chunk = new DocumentChunk(c.Content, c.ChunkIndex)
                {
                    Id = c.Id.ToString(),
                    DocumentId = c.Document.ExternalId,
                    TokenCount = c.TokenCount
                };
                if (c.Embedding != null)
                {
                    chunk.Embedding = c.Embedding;
                }
                return new SearchResult
                {
                    Chunk = chunk,
                    Score = 1.0f // 키워드 매칭은 1.0으로 설정
                };
            }).ToList();

            results.AddRange(keywordResults);
        }

        // 벡터 검색
        if (queryVector != null && queryVector.Length > 0)
        {
            var vectorChunks = await SearchAsync(queryVector, topK * 2, 0.5f, cancellationToken);
            var vectorResults = vectorChunks.Select(c => new SearchResult
            {
                Chunk = c,
                Score = c.Score ?? 0.8f
            }).ToList();

            // 결과 병합 및 점수 조정
            foreach (var vr in vectorResults)
            {
                var existing = results.FirstOrDefault(r =>
                    r.DocumentId == vr.DocumentId && r.ChunkIndex == vr.ChunkIndex);

                if (existing != null)
                {
                    // 이미 키워드 검색에서 찾은 경우, 점수 결합
                    existing.Score = (float)((existing.Score * (1 - vectorWeight)) + (vr.Score * vectorWeight));
                }
                else
                {
                    // 벡터 검색에서만 찾은 경우
                    vr.Score = (float)(vr.Score * vectorWeight);
                    results.Add(vr);
                }
            }
        }

        // 점수로 정렬하고 상위 K개 반환
        var finalResults = results
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        _logger.LogInformation("Hybrid search returned {ResultCount} results", finalResults.Count);
        return finalResults;
    }

    public async Task<Document?> GetDocumentAsync(
        string documentId, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        var vectorDoc = await _context.Documents
            .Include(d => d.Chunks.OrderBy(c => c.ChunkIndex))
            .FirstOrDefaultAsync(d => d.ExternalId == documentId, cancellationToken);

        if (vectorDoc == null)
        {
            _logger.LogWarning("Document {DocumentId} not found", documentId);
            return null;
        }

        var document = Document.Create(documentId);

        foreach (var chunk in vectorDoc.Chunks)
        {
            var docChunk = new DocumentChunk(chunk.Content, chunk.ChunkIndex)
            {
                TokenCount = chunk.TokenCount,
                Metadata = chunk.Metadata
            };

            if (chunk.Embedding != null)
            {
                docChunk.Embedding = chunk.Embedding;
            }

            document.AddChunk(docChunk);
        }

        return document;
    }

    public async Task<bool> DeleteDocumentAsync(
        string documentId, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.ExternalId == documentId, cancellationToken);

        if (document == null)
        {
            _logger.LogWarning("Document {DocumentId} not found for deletion", documentId);
            return false;
        }

        _context.Documents.Remove(document);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Successfully deleted document {DocumentId}", documentId);
        return true;
    }

    public async Task<bool> UpdateEmbeddingAsync(
        string documentId,
        int chunkIndex,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));
        
        if (embedding == null || embedding.Length == 0)
            throw new ArgumentException("Embedding cannot be null or empty", nameof(embedding));

        var chunk = await _context.Chunks
            .Include(c => c.Document)
            .FirstOrDefaultAsync(c => 
                c.Document.ExternalId == documentId && 
                c.ChunkIndex == chunkIndex, 
                cancellationToken);

        if (chunk == null)
        {
            _logger.LogWarning("Chunk {ChunkIndex} in document {DocumentId} not found", 
                chunkIndex, documentId);
            return false;
        }

        chunk.Embedding = embedding;
        chunk.Document.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Updated embedding for chunk {ChunkIndex} in document {DocumentId}", 
            chunkIndex, documentId);
        return true;
    }

    public async Task<long> GetDocumentCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Documents.LongCountAsync(cancellationToken);
    }

    public async Task<long> GetChunkCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Chunks.LongCountAsync(cancellationToken);
    }

    private string ComputeHash(Document document)
    {
        var content = string.Join("\n", document.Chunks.Select(c => c.Content));
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(bytes);
    }

    private Dictionary<string, object>? MergeMetadata(
        Dictionary<string, object>? docMetadata, 
        Dictionary<string, object>? chunkMetadata)
    {
        if (docMetadata == null && chunkMetadata == null)
            return null;

        var merged = new Dictionary<string, object>();

        if (docMetadata != null)
        {
            foreach (var kvp in docMetadata)
                merged[kvp.Key] = kvp.Value;
        }

        if (chunkMetadata != null)
        {
            foreach (var kvp in chunkMetadata)
                merged[$"chunk_{kvp.Key}"] = kvp.Value;
        }

        return merged;
    }
}