using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// SQLite 기반 벡터 저장소 구현 (로컬 개발용)
/// 벡터 검색은 메모리에서 수행
/// </summary>
public class SQLiteVectorStore : IVectorStore
{
    private readonly SQLiteDbContext _context;
    private readonly ILogger<SQLiteVectorStore> _logger;
    private readonly SQLiteOptions _options;

    public SQLiteVectorStore(
        SQLiteDbContext context,
        ILogger<SQLiteVectorStore> logger,
        SQLiteOptions options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
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
        var sqliteDoc = new SQLiteDocument
        {
            Id = Guid.NewGuid(),
            ExternalId = document.Id,
            Title = document.Metadata?.Properties.GetValueOrDefault("title")?.ToString(),
            Source = document.Metadata?.Properties.GetValueOrDefault("source")?.ToString(),
            ContentHash = contentHash,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        sqliteDoc.SetMetadata(document.Metadata?.Properties ?? new Dictionary<string, object>());

        // 청크 생성
        var sqliteChunks = document.Chunks.Select((chunk, index) => 
        {
            var sqliteChunk = new SQLiteChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = sqliteDoc.Id,
                ChunkIndex = index,
                Content = chunk.Content,
                TokenCount = chunk.TokenCount,
                CreatedAt = DateTime.UtcNow
            };
            
            sqliteChunk.SetEmbedding(chunk.Embedding);
            sqliteChunk.SetMetadata(chunk.Metadata);
            
            return sqliteChunk;
        }).ToList();

        sqliteDoc.Chunks = sqliteChunks;

        // 데이터베이스에 저장
        _context.Documents.Add(sqliteDoc);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Successfully stored document {DocumentId} with {ChunkCount} chunks", 
            document.Id, sqliteChunks.Count);

        return sqliteDoc.ExternalId;
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

    public async Task<IEnumerable<SearchResult>> SearchAsync(
        float[] queryVector,
        int topK = 10,
        double threshold = 0.7,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (queryVector == null || queryVector.Length == 0)
            throw new ArgumentException("Query vector cannot be null or empty", nameof(queryVector));

        _logger.LogDebug("Searching for top {TopK} results with threshold {Threshold}", topK, threshold);

        // SQLite는 벡터 연산을 지원하지 않으므로 모든 청크를 메모리로 로드
        var chunks = await _context.Chunks
            .Include(c => c.Document)
            .Where(c => c.EmbeddingJson != null)
            .ToListAsync(cancellationToken);

        // 메모리에서 코사인 유사도 계산
        var results = new List<(SQLiteChunk chunk, double similarity)>();
        
        foreach (var chunk in chunks)
        {
            var embedding = chunk.GetEmbedding();
            if (embedding == null) continue;
            
            var similarity = CosineSimilarity(queryVector, embedding);
            if (similarity >= threshold)
            {
                results.Add((chunk, similarity));
            }
        }

        // 상위 K개 선택
        var topResults = results
            .OrderByDescending(r => r.similarity)
            .Take(topK)
            .ToList();

        var searchResultsList = topResults.Select(r => {
            var chunk = new DocumentChunk(r.chunk.Content, r.chunk.ChunkIndex)
            {
                Id = r.chunk.Id.ToString(),
                DocumentId = r.chunk.Document.ExternalId,
                TokenCount = r.chunk.TokenCount
            };
            var embedding = r.chunk.GetEmbedding();
            if (embedding != null)
            {
                chunk.Embedding = embedding;
            }
            return new SearchResult
            {
                Chunk = chunk,
                Score = (float)r.similarity
            };
        }).ToList();

        _logger.LogInformation("Found {ResultCount} search results", searchResultsList.Count);
        return searchResultsList;
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
                .Where(c => EF.Functions.Like(c.Content, $"%{keyword}%"))
                .Include(c => c.Document)
                .Take(topK * 2)
                .ToListAsync(cancellationToken);

            var keywordResults = keywordChunks.Select(c => {
                var chunk = new DocumentChunk(c.Content, c.ChunkIndex)
                {
                    Id = c.Id.ToString(),
                    DocumentId = c.Document.ExternalId,
                    TokenCount = c.TokenCount
                };
                var embedding = c.GetEmbedding();
                if (embedding != null)
                {
                    chunk.Embedding = embedding;
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
            var vectorResults = await SearchAsync(queryVector, topK * 2, 0.5, filter, cancellationToken);
            
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

        var sqliteDoc = await _context.Documents
            .Include(d => d.Chunks.OrderBy(c => c.ChunkIndex))
            .FirstOrDefaultAsync(d => d.ExternalId == documentId, cancellationToken);

        if (sqliteDoc == null)
        {
            _logger.LogWarning("Document {DocumentId} not found", documentId);
            return null;
        }

        var document = Document.Create(documentId);

        foreach (var chunk in sqliteDoc.Chunks)
        {
            var docChunk = new DocumentChunk(chunk.Content, chunk.ChunkIndex)
            {
                TokenCount = chunk.TokenCount,
                Metadata = chunk.GetMetadata()
            };

            var embedding = chunk.GetEmbedding();
            if (embedding != null)
            {
                docChunk.Embedding = embedding;
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

        chunk.SetEmbedding(embedding);
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

    // IVectorStore interface implementation
    public async Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        if (chunk == null)
            throw new ArgumentNullException(nameof(chunk));

        _logger.LogDebug("Storing chunk {ChunkId} from document {DocumentId}", chunk.Id, chunk.DocumentId);

        var sqliteChunk = new SQLiteChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.Parse(chunk.DocumentId), // Assuming DocumentId is a GUID string
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            TokenCount = chunk.TokenCount,
            CreatedAt = DateTime.UtcNow
        };

        sqliteChunk.SetEmbedding(chunk.Embedding);
        sqliteChunk.SetMetadata(chunk.Metadata?.ToDictionary() ?? new Dictionary<string, object>());

        _context.Chunks.Add(sqliteChunk);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Successfully stored chunk {ChunkId}", chunk.Id);
        return sqliteChunk.Id.ToString();
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

    public async Task<DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Chunk ID cannot be empty", nameof(id));

        if (!Guid.TryParse(id, out var chunkId))
            return null;

        var sqliteChunk = await _context.Chunks
            .Include(c => c.Document)
            .FirstOrDefaultAsync(c => c.Id == chunkId, cancellationToken);

        if (sqliteChunk == null)
        {
            _logger.LogWarning("Chunk {ChunkId} not found", id);
            return null;
        }

        return ConvertToDocumentChunk(sqliteChunk);
    }

    public async Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        var chunks = await _context.Chunks
            .Include(c => c.Document)
            .Where(c => c.Document.ExternalId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(cancellationToken);

        return chunks.Select(ConvertToDocumentChunk).ToList();
    }

    public async Task<IEnumerable<DocumentChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        if (queryEmbedding == null || queryEmbedding.Length == 0)
            throw new ArgumentException("Query embedding cannot be null or empty", nameof(queryEmbedding));

        _logger.LogDebug("Searching for top {TopK} chunks with minimum score {MinScore}", topK, minScore);

        // SQLite doesn't support vector operations, so load all chunks into memory
        var chunks = await _context.Chunks
            .Include(c => c.Document)
            .Where(c => c.EmbeddingJson != null)
            .ToListAsync(cancellationToken);

        // Calculate cosine similarity in memory
        var results = new List<(SQLiteChunk chunk, float similarity)>();

        foreach (var chunk in chunks)
        {
            var embedding = chunk.GetEmbedding();
            if (embedding == null) continue;

            var similarity = (float)CosineSimilarity(queryEmbedding, embedding);
            if (similarity >= minScore)
            {
                results.Add((chunk, similarity));
            }
        }

        // Return top K results
        var topResults = results
            .OrderByDescending(r => r.similarity)
            .Take(topK)
            .Select(r => ConvertToDocumentChunk(r.chunk))
            .ToList();

        _logger.LogInformation("Found {ResultCount} search results", topResults.Count);
        return topResults;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Chunk ID cannot be empty", nameof(id));

        if (!Guid.TryParse(id, out var chunkId))
            return false;

        var chunk = await _context.Chunks
            .FirstOrDefaultAsync(c => c.Id == chunkId, cancellationToken);

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

        if (!Guid.TryParse(id, out var chunkId))
            return false;

        return await _context.Chunks
            .AnyAsync(c => c.Id == chunkId, cancellationToken);
    }

    public async Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        // This is the same as GetAsync for this implementation
        return await GetAsync(id, cancellationToken);
    }

    public async Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        if (chunk == null)
            throw new ArgumentNullException(nameof(chunk));

        if (!Guid.TryParse(chunk.Id, out var chunkId))
            return false;

        var sqliteChunk = await _context.Chunks
            .FirstOrDefaultAsync(c => c.Id == chunkId, cancellationToken);

        if (sqliteChunk == null)
        {
            _logger.LogWarning("Chunk {ChunkId} not found for update", chunk.Id);
            return false;
        }

        // Update chunk properties
        sqliteChunk.Content = chunk.Content;
        sqliteChunk.TokenCount = chunk.TokenCount;
        sqliteChunk.SetEmbedding(chunk.Embedding);
        sqliteChunk.SetMetadata(chunk.Metadata?.ToDictionary() ?? new Dictionary<string, object>());

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
        // This is the same as CountAsync for this implementation
        return await CountAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Clearing all chunks from SQLite vector store");

        // Remove all chunks first (due to foreign key constraints)
        await _context.Chunks.ExecuteDeleteAsync(cancellationToken);

        // Then remove all documents
        await _context.Documents.ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation("Successfully cleared all data from SQLite vector store");
    }

    private string ComputeHash(Document document)
    {
        var content = string.Join("\n", document.Chunks.Select(c => c.Content));
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(bytes);
    }

    private double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same dimension");

        double dotProduct = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        magnitudeA = Math.Sqrt(magnitudeA);
        magnitudeB = Math.Sqrt(magnitudeB);

        if (magnitudeA == 0 || magnitudeB == 0)
            return 0;

        return dotProduct / (magnitudeA * magnitudeB);
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

    private DocumentChunk ConvertToDocumentChunk(SQLiteChunk sqliteChunk)
    {
        var chunk = new DocumentChunk(sqliteChunk.Content, sqliteChunk.ChunkIndex)
        {
            Id = sqliteChunk.Id.ToString(),
            DocumentId = sqliteChunk.Document.ExternalId,
            TokenCount = sqliteChunk.TokenCount,
            Metadata = sqliteChunk.GetMetadata()
        };

        var embedding = sqliteChunk.GetEmbedding();
        if (embedding != null)
        {
            chunk.Embedding = embedding;
        }

        return chunk;
    }
}