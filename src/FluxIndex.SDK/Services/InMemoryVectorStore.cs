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
    // Document id -> ids of its chunks. Every read and write of this index, and of _chunks together with it, holds
    // _indexLock: an update is several steps (look up, change the set, drop an empty set), and two callers writing
    // chunks of one document at once would otherwise lose ids or leave stale ones.
    private readonly Dictionary<string, HashSet<string>> _documentChunks = new();
    private readonly object _indexLock = new();
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
        chunk.EnsureId();

        Put(chunk);

        await AutoSaveIfEnabledAsync(cancellationToken);
        return chunk.Id;
    }

    /// <summary>
    /// Writes <paramref name="chunk"/> under its id, replacing a chunk already stored under it —
    /// re-storing an id is an update, not a duplicate (docs/REFERENCE.md, "Chunk identity"). The
    /// document index follows: the id appears once under its current document, and leaves the
    /// document it was filed under before if that changed.
    /// </summary>
    private void Put(DocumentChunk chunk)
    {
        var embedding = chunk.Embedding ?? Array.Empty<float>();

        lock (_indexLock)
        {
            if (_chunks.TryGetValue(chunk.Id, out var previous)
                && !string.IsNullOrEmpty(previous.chunk.DocumentId)
                && previous.chunk.DocumentId != chunk.DocumentId)
            {
                RemoveFromIndex(previous.chunk.DocumentId, chunk.Id);
            }

            _chunks[chunk.Id] = (chunk, embedding);

            if (!string.IsNullOrEmpty(chunk.DocumentId))
            {
                AddToIndex(chunk.DocumentId, chunk.Id);
            }
        }
    }

    /// <summary>Files <paramref name="chunkId"/> under <paramref name="documentId"/>. Caller holds <see cref="_indexLock"/>.</summary>
    private void AddToIndex(string documentId, string chunkId)
    {
        if (!_documentChunks.TryGetValue(documentId, out var ids))
        {
            ids = new HashSet<string>(StringComparer.Ordinal);
            _documentChunks[documentId] = ids;
        }

        ids.Add(chunkId);
    }

    /// <summary>Removes <paramref name="chunkId"/> from <paramref name="documentId"/>, dropping an emptied entry. Caller holds <see cref="_indexLock"/>.</summary>
    private void RemoveFromIndex(string documentId, string chunkId)
    {
        if (_documentChunks.TryGetValue(documentId, out var ids) && ids.Remove(chunkId) && ids.Count == 0)
        {
            _documentChunks.Remove(documentId);
        }
    }

    protected override Task<DocumentChunk?> GetCoreAsync(string id, CancellationToken cancellationToken)
    {
        _chunks.TryGetValue(id, out var item);
        return Task.FromResult<DocumentChunk?>(item.chunk);
    }

    protected override Task<IEnumerable<VectorSearchResult>> SearchCoreAsync(
        float[] queryEmbedding,
        int topK,
        Dictionary<string, object>? filters,
        CancellationToken cancellationToken)
    {
        // Metadata filters are applied before the topK*2 trim — otherwise higher-scoring
        // non-matching chunks crowd matching ones out of the window.
        var matcher = MetadataFilterMatcher.Compile(filters);
        var results = _chunks.Values
            .Where(item => item.embedding != null && item.embedding.Length > 0)
            .Where(item => matcher.Matches(item.chunk.DocumentId, item.chunk.Metadata))
            .Select(item => new VectorSearchResult(
                item.chunk,
                ComputeCosineSimilarity(queryEmbedding, item.embedding)))
            .OrderByDescending(r => r.Score)
            .Take(topK * 2);

        return Task.FromResult(results);
    }

    protected override async Task<bool> DeleteCoreAsync(string id, CancellationToken cancellationToken)
    {
        bool removed;
        lock (_indexLock)
        {
            removed = _chunks.TryRemove(id, out var item);
            if (removed && !string.IsNullOrEmpty(item.chunk.DocumentId))
            {
                RemoveFromIndex(item.chunk.DocumentId, id);
            }
        }

        if (removed)
        {
            await AutoSaveIfEnabledAsync(cancellationToken);
        }
        return removed;
    }

    protected override async Task<bool> UpdateCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
    {
        bool updated;
        lock (_indexLock)
        {
            updated = _chunks.TryGetValue(chunk.Id, out var previous);
            if (updated)
            {
                _chunks[chunk.Id] = (chunk, chunk.Embedding ?? Array.Empty<float>());
                if (previous.chunk.DocumentId != chunk.DocumentId)
                {
                    if (!string.IsNullOrEmpty(previous.chunk.DocumentId))
                        RemoveFromIndex(previous.chunk.DocumentId, chunk.Id);
                    if (!string.IsNullOrEmpty(chunk.DocumentId))
                        AddToIndex(chunk.DocumentId, chunk.Id);
                }
            }
        }

        if (updated)
        {
            await AutoSaveIfEnabledAsync(cancellationToken);
        }
        return updated;
    }

    protected override Task<IEnumerable<DocumentChunk>> GetByDocumentIdCoreAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        lock (_indexLock)
        {
            if (_documentChunks.TryGetValue(documentId, out var chunkIds))
            {
                var chunks = chunkIds
                    .Where(id => _chunks.ContainsKey(id))
                    .Select(id => _chunks[id].chunk)
                    .ToList();
                return Task.FromResult<IEnumerable<DocumentChunk>>(chunks);
            }
        }
        return Task.FromResult<IEnumerable<DocumentChunk>>([]);
    }

    protected override async Task<bool> DeleteByDocumentIdCoreAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        bool removed;
        lock (_indexLock)
        {
            removed = _documentChunks.Remove(documentId, out var chunkIds);
            if (removed)
            {
                foreach (var id in chunkIds!)
                {
                    _chunks.TryRemove(id, out _);
                }
            }
        }

        if (removed)
        {
            await AutoSaveIfEnabledAsync(cancellationToken);
        }
        return removed;
    }

    /// <inheritdoc />
    public override async Task<int> DeleteByFilterAsync(
        Dictionary<string, object> filters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.Count == 0)
            throw new ArgumentException(
                "Filter must contain at least one key/value; use ClearAsync to remove all vectors.",
                nameof(filters));

        var deleteMatcher = MetadataFilterMatcher.Compile(filters);
        var matchedIds = _chunks
            .Where(kvp => deleteMatcher.Matches(kvp.Value.chunk.DocumentId, kvp.Value.chunk.Metadata))
            .Select(kvp => kvp.Key)
            .ToList();

        var deleted = 0;
        lock (_indexLock)
        {
            foreach (var id in matchedIds)
            {
                if (!_chunks.TryRemove(id, out var item))
                    continue;

                deleted++;
                if (!string.IsNullOrEmpty(item.chunk.DocumentId))
                {
                    RemoveFromIndex(item.chunk.DocumentId, id);
                }
            }
        }

        if (deleted > 0)
            await AutoSaveIfEnabledAsync(cancellationToken);

        return deleted;
    }

    protected override Task<int> CountCoreAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_chunks.Count);
    }

    /// <inheritdoc />
    public override Task<int> GetDistinctDocumentCountAsync(CancellationToken cancellationToken = default)
    {
        lock (_indexLock)
        {
            return Task.FromResult(_documentChunks.Count);
        }
    }

    protected override async Task ClearCoreAsync(CancellationToken cancellationToken)
    {
        lock (_indexLock)
        {
            _chunks.Clear();
            _documentChunks.Clear();
        }

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
            chunk.EnsureId();
            Put(chunk);
            results.Add(chunk.Id);
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

            lock (_indexLock)
            {
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
                    AddToIndex(chunkData.DocumentId, chunkData.Id);
                }
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
