using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL 기반 벡터 저장소 구현 (Core 인터페이스)
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

    public async Task<string> StoreAsync(DocumentChunk chunk, float[] embedding, CancellationToken cancellationToken = default)
    {
        if (chunk == null)
            throw new ArgumentNullException(nameof(chunk));

        _logger.LogDebug("Storing chunk {ChunkId}", chunk.Id);

        var vectorChunk = new VectorChunk
        {
            Id = Guid.NewGuid(),
            DocumentChunkId = Guid.Parse(chunk.DocumentId), // 문서 ID 사용
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            ContentHash = ComputeHash(chunk.Content),
            TokenCount = chunk.TokenCount,
            Metadata = chunk.Metadata,
            Embedding = new Vector(embedding),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Chunks.Add(vectorChunk);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Successfully stored chunk {ChunkId}", chunk.Id);
        return vectorChunk.Id.ToString();
    }

    public async Task<IReadOnlyList<string>> StoreBatchAsync(
        IReadOnlyList<(DocumentChunk chunk, float[] embedding)> items,
        CancellationToken cancellationToken = default)
    {
        if (items == null || !items.Any())
            return new List<string>();

        _logger.LogInformation("Storing batch of {Count} chunks", items.Count);

        var storedIds = new List<string>();
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var (chunk, embedding) in items)
            {
                var vectorChunk = new VectorChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentChunkId = Guid.Parse(chunk.DocumentId),
                    ChunkIndex = chunk.ChunkIndex,
                    Content = chunk.Content,
                    ContentHash = ComputeHash(chunk.Content),
                    TokenCount = chunk.TokenCount,
                    Metadata = chunk.Metadata,
                    Embedding = new Vector(embedding),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.Chunks.Add(vectorChunk);
                storedIds.Add(vectorChunk.Id.ToString());
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Successfully stored {Count} chunks", storedIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store chunk batch");
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return storedIds;
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchSimilarAsync(
        float[] queryEmbedding,
        int maxResults = 10,
        double minScore = 0.0,
        CancellationToken cancellationToken = default)
    {
        if (queryEmbedding == null || queryEmbedding.Length == 0)
            throw new ArgumentException("Query embedding cannot be null or empty", nameof(queryEmbedding));

        _logger.LogDebug("Searching for top {MaxResults} results with minimum score {MinScore}", maxResults, minScore);

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
            .Take(maxResults);

        var results = await query.ToListAsync(cancellationToken);

        var searchResults = results.Select((r, index) => {
            var chunk = new DocumentChunk
            {
                Id = r.Chunk.Id.ToString(),
                DocumentId = r.Chunk.DocumentChunkId.ToString(),
                Content = r.Chunk.Content,
                ChunkIndex = r.Chunk.ChunkIndex,
                TokenCount = r.Chunk.TokenCount,
                Metadata = r.Chunk.Metadata ?? new Dictionary<string, object>()
            };

            return new VectorSearchResult
            {
                DocumentChunk = chunk,
                Score = r.Score,
                Rank = index + 1,
                Distance = r.Distance,
                Metadata = new Dictionary<string, object>()
            };
        }).ToList();

        return searchResults;
    }

    public async Task<DocumentChunk?> GetChunkAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(chunkId, out var id))
            return null;

        var vectorChunk = await _context.Chunks
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (vectorChunk == null)
            return null;

        return new DocumentChunk
        {
            Id = vectorChunk.Id.ToString(),
            DocumentId = vectorChunk.DocumentChunkId.ToString(),
            Content = vectorChunk.Content,
            ChunkIndex = vectorChunk.ChunkIndex,
            TokenCount = vectorChunk.TokenCount,
            Metadata = vectorChunk.Metadata ?? new Dictionary<string, object>()
        };
    }

    public async Task<IReadOnlyList<DocumentChunk>> GetChunksByIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        var guids = chunkIds
            .Where(id => Guid.TryParse(id, out _))
            .Select(Guid.Parse)
            .ToList();

        if (!guids.Any())
            return new List<DocumentChunk>();

        var vectorChunks = await _context.Chunks
            .Where(c => guids.Contains(c.Id))
            .ToListAsync(cancellationToken);

        return vectorChunks.Select(vc => new DocumentChunk
        {
            Id = vc.Id.ToString(),
            DocumentId = vc.DocumentChunkId.ToString(),
            Content = vc.Content,
            ChunkIndex = vc.ChunkIndex,
            TokenCount = vc.TokenCount,
            Metadata = vc.Metadata ?? new Dictionary<string, object>()
        }).ToList();
    }

    public async Task<IReadOnlyList<DocumentChunk>> GetDocumentChunksAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(documentId, out var docId))
            return new List<DocumentChunk>();

        var vectorChunks = await _context.Chunks
            .Where(c => c.DocumentChunkId == docId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(cancellationToken);

        return vectorChunks.Select(vc => new DocumentChunk
        {
            Id = vc.Id.ToString(),
            DocumentId = vc.DocumentChunkId.ToString(),
            Content = vc.Content,
            ChunkIndex = vc.ChunkIndex,
            TokenCount = vc.TokenCount,
            Metadata = vc.Metadata ?? new Dictionary<string, object>()
        }).ToList();
    }

    public async Task<bool> DeleteAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(chunkId, out var id))
            return false;

        var vectorChunk = await _context.Chunks
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (vectorChunk == null)
            return false;

        _context.Chunks.Remove(vectorChunk);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Deleted chunk {ChunkId}", chunkId);
        return true;
    }

    public async Task<int> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(documentId, out var docId))
            return 0;

        var chunks = await _context.Chunks
            .Where(c => c.DocumentChunkId == docId)
            .ToListAsync(cancellationToken);

        if (!chunks.Any())
            return 0;

        _context.Chunks.RemoveRange(chunks);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted {Count} chunks for document {DocumentId}", chunks.Count, documentId);
        return chunks.Count;
    }

    public async Task<VectorStoreStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var totalChunks = await _context.Chunks.CountAsync(cancellationToken);
        var totalDocuments = await _context.Chunks
            .Select(c => c.DocumentChunkId)
            .Distinct()
            .CountAsync(cancellationToken);

        var vectorDimension = await _context.Chunks
            .Where(c => c.Embedding != null)
            .Select(c => c.Embedding!.Dim)
            .FirstOrDefaultAsync(cancellationToken);

        return new VectorStoreStatistics
        {
            TotalDocuments = totalDocuments,
            TotalChunks = totalChunks,
            VectorDimension = vectorDimension,
            IndexSizeMB = 0, // TODO: 실제 인덱스 크기 계산
            LastUpdated = DateTime.UtcNow
        };
    }

    public async Task OptimizeIndexAsync(CancellationToken cancellationToken = default)
    {
        // PostgreSQL에서 pgvector 인덱스 최적화
        _logger.LogInformation("Optimizing vector index");

        await _context.Database.ExecuteSqlRawAsync(
            "VACUUM ANALYZE chunks;",
            cancellationToken);

        _logger.LogInformation("Vector index optimization completed");
    }

    private string ComputeHash(string content)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(bytes);
    }
}