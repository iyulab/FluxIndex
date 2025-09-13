using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.SDK.Services;

/// <summary>
/// 메모리 기반 벡터 저장소 구현
/// </summary>
public class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, DocumentChunk> _chunks = new();
    private readonly ConcurrentDictionary<string, List<string>> _documentChunks = new();

    public Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(chunk.Id))
            chunk.Id = Guid.NewGuid().ToString();

        _chunks.TryAdd(chunk.Id, chunk);
        
        if (!string.IsNullOrEmpty(chunk.DocumentId))
        {
            _documentChunks.AddOrUpdate(chunk.DocumentId,
                new List<string> { chunk.Id },
                (key, list) => { list.Add(chunk.Id); return list; });
        }

        return Task.FromResult(chunk.Id);
    }

    public async Task<IEnumerable<string>> StoreBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        var ids = new List<string>();
        foreach (var chunk in chunks)
        {
            var id = await StoreAsync(chunk, cancellationToken);
            ids.Add(id);
        }
        return ids;
    }

    public Task<DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        _chunks.TryGetValue(id, out var chunk);
        return Task.FromResult(chunk);
    }

    public Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return GetAsync(id, cancellationToken);
    }

    public Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (_documentChunks.TryGetValue(documentId, out var chunkIds))
        {
            var chunks = chunkIds
                .Select(id => _chunks.TryGetValue(id, out var chunk) ? chunk : null)
                .Where(c => c != null)
                .Cast<DocumentChunk>();
            return Task.FromResult<IEnumerable<DocumentChunk>>(chunks.ToList());
        }
        return Task.FromResult<IEnumerable<DocumentChunk>>(Enumerable.Empty<DocumentChunk>());
    }

    public Task<IEnumerable<DocumentChunk>> SearchAsync(float[] queryEmbedding, int topK = 10, float minScore = 0.0f, CancellationToken cancellationToken = default)
    {
        var results = _chunks.Values
            .Where(c => c.Embedding != null)
            .Select(chunk => new { Chunk = chunk, Score = CosineSimilarity(queryEmbedding, chunk.Embedding!) })
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .Select(r => 
            {
                r.Chunk.Score = r.Score;
                return r.Chunk;
            });

        return Task.FromResult<IEnumerable<DocumentChunk>>(results.ToList());
    }

    public Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(chunk.Id))
            return Task.FromResult(false);

        _chunks.AddOrUpdate(chunk.Id, chunk, (key, old) => chunk);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (_chunks.TryRemove(id, out var chunk) && !string.IsNullOrEmpty(chunk.DocumentId))
        {
            if (_documentChunks.TryGetValue(chunk.DocumentId, out var chunkIds))
            {
                chunkIds.Remove(id);
                if (!chunkIds.Any())
                    _documentChunks.TryRemove(chunk.DocumentId, out _);
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

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_chunks.Count);
    }

    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_chunks.Count);
    }

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_chunks.ContainsKey(id));
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _chunks.Clear();
        _documentChunks.Clear();
        return Task.CompletedTask;
    }

    private float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return 0;

        float dotProduct = 0;
        float magnitudeA = 0;
        float magnitudeB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        magnitudeA = (float)Math.Sqrt(magnitudeA);
        magnitudeB = (float)Math.Sqrt(magnitudeB);

        if (magnitudeA == 0 || magnitudeB == 0)
            return 0;

        return dotProduct / (magnitudeA * magnitudeB);
    }
}