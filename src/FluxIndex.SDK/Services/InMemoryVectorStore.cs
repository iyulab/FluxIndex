using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.SDK.Services;

/// <summary>
/// Interface for stores that support file persistence
/// </summary>
public interface IPersistableStore
{
    /// <summary>
    /// Saves the store data to a file
    /// </summary>
    Task SaveToFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the store data from a file
    /// </summary>
    Task LoadFromFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the persistence file path if configured
    /// </summary>
    string? PersistencePath { get; }

    /// <summary>
    /// Gets whether auto-save is enabled
    /// </summary>
    bool AutoSaveEnabled { get; }
}

/// <summary>
/// Memory-based vector store implementation (Core interface) with optional file persistence
/// </summary>
public class InMemoryVectorStore : IVectorStore, IPersistableStore
{
    private readonly ConcurrentDictionary<string, (DocumentChunk chunk, float[] embedding)> _chunks = new();
    private readonly ConcurrentDictionary<string, List<string>> _documentChunks = new();
    private readonly string? _persistencePath;
    private readonly bool _autoSave;
    private readonly SemaphoreSlim _persistenceLock = new(1, 1);

    /// <summary>
    /// Creates a new in-memory vector store without persistence
    /// </summary>
    public InMemoryVectorStore()
    {
        _persistencePath = null;
        _autoSave = false;
    }

    /// <summary>
    /// Creates a new in-memory vector store with optional file persistence
    /// </summary>
    /// <param name="persistencePath">Path to the persistence file (null for no persistence)</param>
    /// <param name="autoSave">If true, automatically saves after each modification</param>
    /// <param name="loadExisting">If true and file exists, loads data on construction</param>
    public InMemoryVectorStore(string? persistencePath, bool autoSave = false, bool loadExisting = true)
    {
        _persistencePath = persistencePath;
        _autoSave = autoSave;

        if (loadExisting && !string.IsNullOrEmpty(persistencePath) && File.Exists(persistencePath))
        {
            LoadFromFileAsync(persistencePath).GetAwaiter().GetResult();
        }
    }

    /// <inheritdoc />
    public string? PersistencePath => _persistencePath;

    /// <inheritdoc />
    public bool AutoSaveEnabled => _autoSave;

    public async Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
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

        var embedding = chunk.Embedding ?? Array.Empty<float>();
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

        await AutoSaveIfEnabledAsync(cancellationToken);
        return chunk.Id;
    }

    public async Task<IEnumerable<string>> StoreBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrEmpty(chunk.Id))
            {
                var newChunk = DocumentChunk.Create(
                    chunk.DocumentId,
                    chunk.Content,
                    chunk.ChunkIndex,
                    1
                );
                var embedding = chunk.Embedding ?? Array.Empty<float>();
                _chunks.TryAdd(newChunk.Id, (newChunk, embedding));

                if (!string.IsNullOrEmpty(newChunk.DocumentId))
                {
                    _documentChunks.AddOrUpdate(newChunk.DocumentId,
                        new List<string> { newChunk.Id },
                        (key, existing) =>
                        {
                            existing.Add(newChunk.Id);
                            return existing;
                        });
                }
                results.Add(newChunk.Id);
            }
            else
            {
                var embedding = chunk.Embedding ?? Array.Empty<float>();
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
                results.Add(chunk.Id);
            }
        }

        await AutoSaveIfEnabledAsync(cancellationToken);
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

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
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

            await AutoSaveIfEnabledAsync(cancellationToken);
            return true;
        }
        return false;
    }

    public async Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (_documentChunks.TryRemove(documentId, out var chunkIds))
        {
            foreach (var id in chunkIds)
            {
                _chunks.TryRemove(id, out _);
            }

            await AutoSaveIfEnabledAsync(cancellationToken);
            return true;
        }
        return false;
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

    public async Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        if (_chunks.ContainsKey(chunk.Id))
        {
            var embedding = chunk.Embedding ?? Array.Empty<float>();
            _chunks[chunk.Id] = (chunk, embedding);

            await AutoSaveIfEnabledAsync(cancellationToken);
            return true;
        }
        return false;
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_chunks.Count);
    }

    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_chunks.Count);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _chunks.Clear();
        _documentChunks.Clear();

        await AutoSaveIfEnabledAsync(cancellationToken);
    }

    #region Persistence Methods

    /// <inheritdoc />
    public async Task SaveToFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await _persistenceLock.WaitAsync(cancellationToken);
        try
        {
            var data = new VectorStoreData
            {
                Version = 1,
                SavedAt = DateTime.UtcNow,
                Chunks = _chunks.Select(kvp => new ChunkData
                {
                    Id = kvp.Key,
                    DocumentId = kvp.Value.chunk.DocumentId,
                    Content = kvp.Value.chunk.Content,
                    ChunkIndex = kvp.Value.chunk.ChunkIndex,
                    Embedding = kvp.Value.embedding
                }).ToList()
            };

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = false // Compact for performance
            };

            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, data, options, cancellationToken);
        }
        finally
        {
            _persistenceLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task LoadFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Vector store persistence file not found: {filePath}");
        }

        await _persistenceLock.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.OpenRead(filePath);
            var data = await JsonSerializer.DeserializeAsync<VectorStoreData>(stream, cancellationToken: cancellationToken);

            if (data?.Chunks == null)
            {
                throw new InvalidDataException("Invalid vector store data format");
            }

            _chunks.Clear();
            _documentChunks.Clear();

            foreach (var chunkData in data.Chunks)
            {
                var chunk = DocumentChunk.Create(
                    chunkData.DocumentId ?? string.Empty,
                    chunkData.Content ?? string.Empty,
                    chunkData.ChunkIndex,
                    1
                );

                // Restore the original ID
                var restoredChunk = RestoreChunkWithId(chunk, chunkData.Id, chunkData.Embedding);

                _chunks.TryAdd(chunkData.Id, (restoredChunk, chunkData.Embedding ?? Array.Empty<float>()));

                if (!string.IsNullOrEmpty(chunkData.DocumentId))
                {
                    _documentChunks.AddOrUpdate(chunkData.DocumentId,
                        new List<string> { chunkData.Id },
                        (key, existing) =>
                        {
                            existing.Add(chunkData.Id);
                            return existing;
                        });
                }
            }
        }
        finally
        {
            _persistenceLock.Release();
        }
    }

    private async Task AutoSaveIfEnabledAsync(CancellationToken cancellationToken)
    {
        if (_autoSave && !string.IsNullOrEmpty(_persistencePath))
        {
            await SaveToFileAsync(_persistencePath, cancellationToken);
        }
    }

    private static DocumentChunk RestoreChunkWithId(DocumentChunk template, string id, float[]? embedding)
    {
        // Use reflection or a builder to restore the chunk with specific ID
        // For now, we create a new chunk and rely on the stored ID
        var chunk = DocumentChunk.Create(
            template.DocumentId,
            template.Content,
            template.ChunkIndex,
            1
        );

        // Set embedding if available
        if (embedding != null && embedding.Length > 0)
        {
            chunk.SetEmbedding(embedding);
        }

        return chunk;
    }

    #endregion

    #region Persistence Data Classes

    private sealed class VectorStoreData
    {
        public int Version { get; set; }
        public DateTime SavedAt { get; set; }
        public List<ChunkData> Chunks { get; set; } = new();
    }

    private sealed class ChunkData
    {
        public string Id { get; set; } = string.Empty;
        public string? DocumentId { get; set; }
        public string? Content { get; set; }
        public int ChunkIndex { get; set; }
        public float[]? Embedding { get; set; }
    }

    #endregion

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
