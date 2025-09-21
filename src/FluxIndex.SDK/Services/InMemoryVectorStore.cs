using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.SDK.Services;

/// <summary>
/// 메모리 기반 벡터 저장소 구현 (Core 인터페이스)
/// </summary>
public class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, (DocumentChunk chunk, float[] embedding)> _chunks = new();
    private readonly ConcurrentDictionary<string, List<string>> _documentChunks = new();

    public Task<string> StoreAsync(DocumentChunk chunk, float[] embedding, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(chunk.Id))
        {
            chunk = new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = chunk.DocumentId,
                Content = chunk.Content,
                ChunkIndex = chunk.ChunkIndex,
                TokenCount = chunk.TokenCount,
                Metadata = chunk.Metadata,
                Embedding = chunk.Embedding,
                Score = chunk.Score,
                CreatedAt = chunk.CreatedAt
            };
        }

        _chunks.TryAdd(chunk.Id, (chunk, embedding));

        if (!string.IsNullOrEmpty(chunk.DocumentId))
        {
            _documentChunks.AddOrUpdate(chunk.DocumentId,
                new List<string> { chunk.Id },
                (key, existing) =>
                {
                    existing.Add(chunk.Id);
                    return existing;
                });
        }

        return Task.FromResult(chunk.Id);
    }

    public async Task<IReadOnlyList<string>> StoreBatchAsync(IReadOnlyList<(DocumentChunk chunk, float[] embedding)> items, CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        foreach (var (chunk, embedding) in items)
        {
            var id = await StoreAsync(chunk, embedding, cancellationToken);
            results.Add(id);
        }
        return results;
    }

    public Task<IReadOnlyList<VectorSearchResult>> SearchSimilarAsync(float[] queryEmbedding, int maxResults = 10, double minScore = 0.0, CancellationToken cancellationToken = default)
    {
        var results = _chunks.Values
            .Select((item, index) => new VectorSearchResult
            {
                DocumentChunk = item.chunk,
                Score = CosineSimilarity(queryEmbedding, item.embedding),
                Rank = index + 1,
                Distance = 1.0 - CosineSimilarity(queryEmbedding, item.embedding)
            })
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();

        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(results);
    }

    public Task<DocumentChunk?> GetChunkAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        _chunks.TryGetValue(chunkId, out var item);
        return Task.FromResult<DocumentChunk?>(item.chunk);
    }

    public Task<IReadOnlyList<DocumentChunk>> GetChunksByIdsAsync(IEnumerable<string> chunkIds, CancellationToken cancellationToken = default)
    {
        var chunks = chunkIds
            .Where(id => _chunks.ContainsKey(id))
            .Select(id => _chunks[id].chunk)
            .ToList();
        return Task.FromResult<IReadOnlyList<DocumentChunk>>(chunks);
    }

    public Task<IReadOnlyList<DocumentChunk>> GetDocumentChunksAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (_documentChunks.TryGetValue(documentId, out var chunkIds))
        {
            var chunks = chunkIds
                .Where(id => _chunks.ContainsKey(id))
                .Select(id => _chunks[id].chunk)
                .ToList();
            return Task.FromResult<IReadOnlyList<DocumentChunk>>(chunks);
        }
        return Task.FromResult<IReadOnlyList<DocumentChunk>>(new List<DocumentChunk>());
    }

    public Task<bool> DeleteAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if (_chunks.TryRemove(chunkId, out var item))
        {
            // Remove from document chunks mapping
            if (!string.IsNullOrEmpty(item.chunk.DocumentId) &&
                _documentChunks.TryGetValue(item.chunk.DocumentId, out var chunkIds))
            {
                chunkIds.Remove(chunkId);
                if (!chunkIds.Any())
                    _documentChunks.TryRemove(item.chunk.DocumentId, out _);
            }
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<int> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (_documentChunks.TryRemove(documentId, out var chunkIds))
        {
            int deletedCount = 0;
            foreach (var id in chunkIds)
            {
                if (_chunks.TryRemove(id, out _))
                    deletedCount++;
            }
            return Task.FromResult(deletedCount);
        }
        return Task.FromResult(0);
    }

    public Task<VectorStoreStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var stats = new VectorStoreStatistics
        {
            TotalDocuments = _documentChunks.Count,
            TotalChunks = _chunks.Count,
            VectorDimension = _chunks.Values.FirstOrDefault().embedding?.Length ?? 0,
            IndexSizeMB = (_chunks.Count * 4 * 1536) / (1024.0 * 1024.0), // Rough estimate
            LastUpdated = DateTime.UtcNow
        };
        return Task.FromResult(stats);
    }

    public Task OptimizeIndexAsync(CancellationToken cancellationToken = default)
    {
        // In-memory store doesn't need optimization
        return Task.CompletedTask;
    }

    private double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return 0;

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
}