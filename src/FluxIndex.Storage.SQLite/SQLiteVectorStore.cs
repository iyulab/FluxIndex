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
            Title = document.Metadata?.GetValueOrDefault("title")?.ToString(),
            Source = document.Metadata?.GetValueOrDefault("source")?.ToString(),
            ContentHash = contentHash,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        sqliteDoc.SetMetadata(document.Metadata);

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
            
            sqliteChunk.SetEmbedding(chunk.Embedding?.Vector);
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
            .Select(r => new SearchResult
            {
                DocumentId = r.chunk.Document.ExternalId,
                ChunkIndex = r.chunk.ChunkIndex,
                Content = r.chunk.Content,
                Score = r.similarity,
                Metadata = MergeMetadata(r.chunk.Document.GetMetadata(), r.chunk.GetMetadata())
            })
            .ToList();

        _logger.LogInformation("Found {ResultCount} search results", topResults.Count);
        return topResults;
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
            var keywordResults = await _context.Chunks
                .Where(c => EF.Functions.Like(c.Content, $"%{keyword}%"))
                .Include(c => c.Document)
                .Select(c => new SearchResult
                {
                    DocumentId = c.Document.ExternalId,
                    ChunkIndex = c.ChunkIndex,
                    Content = c.Content,
                    Score = 1.0, // 키워드 매칭은 1.0으로 설정
                    Metadata = MergeMetadata(c.Document.GetMetadata(), c.GetMetadata())
                })
                .Take(topK * 2)
                .ToListAsync(cancellationToken);

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
                    existing.Score = (existing.Score * (1 - vectorWeight)) + (vr.Score * vectorWeight);
                }
                else
                {
                    // 벡터 검색에서만 찾은 경우
                    vr.Score *= vectorWeight;
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

        var document = new Document(documentId)
        {
            Metadata = sqliteDoc.GetMetadata()
        };

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
                docChunk.Embedding = new EmbeddingVector(
                    embedding, 
                    chunk.GetMetadata()?.GetValueOrDefault("model")?.ToString() ?? "unknown");
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
}