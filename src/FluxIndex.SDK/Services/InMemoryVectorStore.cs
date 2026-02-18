using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using System.Collections.Concurrent;
using System.Text.Json;

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
/// Memory-based vector store implementation with optional file persistence.
/// Inherits common functionality from VectorStoreBase.
/// </summary>
public class InMemoryVectorStore : VectorStoreBase, IPersistableStore, IDisposable
{
    private static readonly JsonSerializerOptions s_persistenceJsonOptions = new()
    {
        WriteIndented = false
    };

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

    #region VectorStoreBase Core Implementations

    protected override async Task<string> StoreCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
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

    protected override Task<DocumentChunk?> GetCoreAsync(string id, CancellationToken cancellationToken)
    {
        _chunks.TryGetValue(id, out var item);
        return Task.FromResult<DocumentChunk?>(item.chunk);
    }

    protected override Task<IEnumerable<VectorSearchResult>> SearchCoreAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken)
    {
        var results = _chunks.Values
            .Where(item => item.embedding != null && item.embedding.Length > 0)
            .Select(item => new VectorSearchResult(
                item.chunk,
                ComputeCosineSimilarity(queryEmbedding, item.embedding)))
            .OrderByDescending(r => r.Score)
            .Take(topK * 2);

        return Task.FromResult(results);
    }

    protected override async Task<bool> DeleteCoreAsync(string id, CancellationToken cancellationToken)
    {
        if (_chunks.TryRemove(id, out var item))
        {
            // Remove from document chunks mapping
            if (!string.IsNullOrEmpty(item.chunk.DocumentId) &&
                _documentChunks.TryGetValue(item.chunk.DocumentId, out var chunkIds))
            {
                chunkIds.Remove(id);
                if (chunkIds.Count == 0)
                    _documentChunks.TryRemove(item.chunk.DocumentId, out _);
            }

            await AutoSaveIfEnabledAsync(cancellationToken);
            return true;
        }
        return false;
    }

    protected override async Task<bool> UpdateCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
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

    protected override Task<IEnumerable<DocumentChunk>> GetByDocumentIdCoreAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        if (_documentChunks.TryGetValue(documentId, out var chunkIds))
        {
            var chunks = chunkIds
                .Where(id => _chunks.ContainsKey(id))
                .Select(id => _chunks[id].chunk)
                .ToList();
            return Task.FromResult<IEnumerable<DocumentChunk>>(chunks);
        }
        return Task.FromResult<IEnumerable<DocumentChunk>>([]);
    }

    protected override async Task<bool> DeleteByDocumentIdCoreAsync(
        string documentId,
        CancellationToken cancellationToken)
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

    protected override Task<int> CountCoreAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_chunks.Count);
    }

    protected override async Task ClearCoreAsync(CancellationToken cancellationToken)
    {
        _chunks.Clear();
        _documentChunks.Clear();

        await AutoSaveIfEnabledAsync(cancellationToken);
    }

    #endregion

    #region Overrides for Batch Optimization

    public override async Task<IEnumerable<string>> StoreBatchAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        foreach (var chunk in chunks)
        {
            var chunkToStore = chunk;
            if (string.IsNullOrEmpty(chunk.Id))
            {
                chunkToStore = DocumentChunk.Create(
                    chunk.DocumentId,
                    chunk.Content,
                    chunk.ChunkIndex,
                    1
                );
                // Copy embedding from original
                if (chunk.Embedding != null)
                    chunkToStore.SetEmbedding(chunk.Embedding);
            }

            var embedding = chunkToStore.Embedding ?? Array.Empty<float>();
            _chunks.TryAdd(chunkToStore.Id, (chunkToStore, embedding));

            if (!string.IsNullOrEmpty(chunkToStore.DocumentId))
            {
                _documentChunks.AddOrUpdate(chunkToStore.DocumentId,
                    new List<string> { chunkToStore.Id },
                    (key, existing) =>
                    {
                        existing.Add(chunkToStore.Id);
                        return existing;
                    });
            }
            results.Add(chunkToStore.Id);
        }

        await AutoSaveIfEnabledAsync(cancellationToken);
        return results;
    }

    public override Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        var chunks = ids
            .Where(id => _chunks.ContainsKey(id))
            .Select(id => _chunks[id].chunk)
            .ToList();
        return Task.FromResult<IEnumerable<DocumentChunk>>(chunks);
    }

    public override Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult(false);
        return Task.FromResult(_chunks.ContainsKey(id));
    }

    #endregion

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

            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, data, s_persistenceJsonOptions, cancellationToken);
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

                // Set embedding if available
                if (chunkData.Embedding != null && chunkData.Embedding.Length > 0)
                {
                    chunk.SetEmbedding(chunkData.Embedding);
                }

                _chunks.TryAdd(chunkData.Id, (chunk, chunkData.Embedding ?? Array.Empty<float>()));

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

    #endregion

    /// <summary>
    /// Disposes the persistence lock semaphore.
    /// </summary>
    public void Dispose()
    {
        _persistenceLock.Dispose();
        GC.SuppressFinalize(this);
    }

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
}
