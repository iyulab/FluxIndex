using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Entities;
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

    public Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(chunk.Id))
        {
            chunk = DocumentChunk.Create(
                chunk.DocumentId,
                chunk.Content,
                chunk.ChunkIndex,
                1 // totalChunks - default
            );
        }

        var embedding = chunk.Embedding ?? new float[0];
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

    public async Task<IEnumerable<string>> StoreBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        foreach (var chunk in chunks)
        {
            var id = await StoreAsync(chunk, cancellationToken);
            results.Add(id);
        }
        return results;
    }

    public Task<DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        _chunks.TryGetValue(id, out var item);
        return Task.FromResult<DocumentChunk?>(item.chunk);
    }

    public Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (_documentChunks.TryGetValue(documentId, out var chunkIds))
        {
            var chunks = chunkIds
                .Where(id => _chunks.ContainsKey(id))
                .Select(id => _chunks[id].chunk)
                .ToList();
            return Task.FromResult<IEnumerable<DocumentChunk>>(chunks);
        }
        return Task.FromResult<IEnumerable<DocumentChunk>>(new List<DocumentChunk>());
    }

    public Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var chunks = ids
            .Where(id => _chunks.ContainsKey(id))
            .Select(id => _chunks[id].chunk)
            .ToList();
        return Task.FromResult<IEnumerable<DocumentChunk>>(chunks);
    }

    public Task<IEnumerable<DocumentChunk>> SearchAsync(float[] queryEmbedding, int topK = 10, float minScore = 0.0f, CancellationToken cancellationToken = default)
    {
        var results = _chunks.Values
            .Select(item => new { chunk = item.chunk, score = CosineSimilarity(queryEmbedding, item.embedding) })
            .Where(r => r.score >= minScore)
            .OrderByDescending(r => r.score)
            .Take(topK)
            .Select(r => r.chunk)
            .ToList();

        return Task.FromResult<IEnumerable<DocumentChunk>>(results);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (_chunks.TryRemove(id, out var item))
        {
            // Remove from document chunks mapping
            if (!string.IsNullOrEmpty(item.chunk.DocumentId) &&
                _documentChunks.TryGetValue(item.chunk.DocumentId, out var chunkIds))
            {
                chunkIds.Remove(id);
                if (!chunkIds.Any())
                    _documentChunks.TryRemove(item.chunk.DocumentId, out _);
            }
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (_documentChunks.TryRemove(documentId, out var chunkIds))
        {
            foreach (var id in chunkIds)
            {
                _chunks.TryRemove(id, out _);
            }
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_chunks.ContainsKey(id));
    }

    public Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        _chunks.TryGetValue(id, out var item);
        return Task.FromResult<DocumentChunk?>(item.chunk);
    }

    public Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        if (_chunks.ContainsKey(chunk.Id))
        {
            var embedding = chunk.Embedding ?? new float[0];
            _chunks[chunk.Id] = (chunk, embedding);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_chunks.Count);
    }

    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_chunks.Count);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _chunks.Clear();
        _documentChunks.Clear();
        return Task.CompletedTask;
    }

    private static float CosineSimilarity(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length)
            return 0;

        double dotProduct = 0;
        double normA = 0;
        double normB = 0;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            normA += vectorA[i] * vectorA[i];
            normB += vectorB[i] * vectorB[i];
        }

        return (float)(dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }
}