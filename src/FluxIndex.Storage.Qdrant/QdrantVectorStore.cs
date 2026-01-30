using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace FluxIndex.Storage.Qdrant;

/// <summary>
/// Qdrant implementation of IVectorStore for high-performance vector similarity search.
/// Supports dynamic dimension adaptation via CollectionNamingStrategy.
/// </summary>
public class QdrantVectorStore : IVectorStore, IAsyncDisposable
{
    private readonly QdrantClient _client;
    private readonly QdrantOptions _options;
    private readonly ILogger<QdrantVectorStore> _logger;
    private bool _collectionInitialized;

    // Dynamic dimension adaptation fields
    private string? _resolvedCollectionName;
    private int? _detectedDimension;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public QdrantVectorStore(
        IOptions<QdrantOptions> options,
        ILogger<QdrantVectorStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = CreateClient();
    }

    private QdrantClient CreateClient()
    {
        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            // Qdrant Cloud connection
            return new QdrantClient(
                host: _options.Host,
                https: _options.UseHttps,
                apiKey: _options.ApiKey,
                grpcTimeout: TimeSpan.FromSeconds(_options.TimeoutSeconds));
        }

        // Local Qdrant connection
        return new QdrantClient(
            host: _options.Host,
            port: _options.GrpcPort,
            https: _options.UseHttps,
            grpcTimeout: TimeSpan.FromSeconds(_options.TimeoutSeconds));
    }

    /// <summary>
    /// Ensures the collection exists for the given embedding dimension.
    /// With DimensionSuffix strategy, creates dimension-specific collections automatically.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when collection initialization fails.</exception>
    private async Task EnsureCollectionForDimensionAsync(int dimension, CancellationToken ct)
    {
        // Fast path: already initialized for this dimension
        if (_detectedDimension == dimension && _collectionInitialized && _resolvedCollectionName != null)
            return;

        await _initLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_detectedDimension == dimension && _collectionInitialized && _resolvedCollectionName != null)
                return;

            // Resolve collection name based on naming strategy
            var collectionName = _options.NamingStrategy switch
            {
                CollectionNamingStrategy.DimensionSuffix => $"{_options.BaseCollectionName}_{dimension}",
                _ => _options.BaseCollectionName
            };

            try
            {
                var collections = await _client.ListCollectionsAsync(ct);
                var exists = collections.Any(c => c == collectionName);

                if (!exists && _options.CreateCollectionOnStartup)
                {
                    // Set _resolvedCollectionName before creating (used in CreateCollectionWithDimensionAsync)
                    _resolvedCollectionName = collectionName;
                    await CreateCollectionWithDimensionAsync(dimension, ct);
                }

                // Only update state after successful initialization
                _resolvedCollectionName = collectionName;
                _detectedDimension = dimension;
                _collectionInitialized = true;

                _logger.LogInformation(
                    "Qdrant collection ready: '{Collection}' (dim={Dimension}, strategy={Strategy})",
                    _resolvedCollectionName, dimension, _options.NamingStrategy);
            }
            catch (Exception ex)
            {
                // Log warning but still set collection name (assume exists)
                _logger.LogWarning(ex, "Failed to initialize Qdrant collection '{Collection}' (assuming it exists)", collectionName);

                // Set state even on failure to prevent repeated initialization attempts
                _resolvedCollectionName = collectionName;
                _detectedDimension = dimension;
                _collectionInitialized = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Creates a new collection with the specified dimension.
    /// </summary>
    private async Task CreateCollectionWithDimensionAsync(int dimension, CancellationToken ct)
    {
        var distance = _options.DistanceMetric switch
        {
            QdrantDistanceMetric.Cosine => Distance.Cosine,
            QdrantDistanceMetric.Euclid => Distance.Euclid,
            QdrantDistanceMetric.Dot => Distance.Dot,
            _ => Distance.Cosine
        };

        await _client.CreateCollectionAsync(
            collectionName: _resolvedCollectionName!,
            vectorsConfig: new VectorParams
            {
                Size = (ulong)dimension,
                Distance = distance,
                OnDisk = _options.OnDiskPayload
            },
            hnswConfig: new HnswConfigDiff
            {
                M = (ulong)_options.HnswM,
                EfConstruct = (ulong)_options.HnswEfConstruct
            },
            cancellationToken: ct);

        // Create payload indexes for filtering
        await _client.CreatePayloadIndexAsync(
            collectionName: _resolvedCollectionName!,
            fieldName: "document_id",
            schemaType: PayloadSchemaType.Keyword,
            cancellationToken: ct);

        await _client.CreatePayloadIndexAsync(
            collectionName: _resolvedCollectionName!,
            fieldName: "chunk_index",
            schemaType: PayloadSchemaType.Integer,
            cancellationToken: ct);

        _logger.LogInformation("Created Qdrant collection '{CollectionName}' with dimension {Dimension}",
            _resolvedCollectionName, dimension);
    }

    /// <summary>
    /// Legacy method for backward compatibility. Uses VectorSize from options.
    /// Prefer using EnsureCollectionForDimensionAsync with actual embedding dimension.
    /// </summary>
    private async Task EnsureCollectionAsync(CancellationToken ct)
    {
        // For Fixed strategy or when dimension is unknown, use configured VectorSize
        var dimension = _options.NamingStrategy == CollectionNamingStrategy.Fixed
            ? _options.VectorSize
            : _detectedDimension ?? _options.VectorSize;

        await EnsureCollectionForDimensionAsync(dimension, ct);
    }

    /// <summary>
    /// Gets the resolved collection name (with dimension suffix if applicable).
    /// Returns null if collection hasn't been initialized yet.
    /// </summary>
    public string? GetResolvedCollectionName() => _resolvedCollectionName;

    /// <summary>
    /// Gets the detected embedding dimension. Returns null if not yet detected.
    /// </summary>
    public int? GetDetectedDimension() => _detectedDimension;

    #region Store Operations

    public async Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        if (chunk.Embedding == null || chunk.Embedding.Length == 0)
        {
            throw new ArgumentException("Chunk must have embedding", nameof(chunk));
        }

        // Dynamic dimension detection: use actual embedding dimension
        await EnsureCollectionForDimensionAsync(chunk.Embedding.Length, cancellationToken);

        var point = CreatePointFromChunk(chunk);

        await _client.UpsertAsync(
            collectionName: _resolvedCollectionName!,
            points: [point],
            cancellationToken: cancellationToken);

        _logger.LogDebug("Stored chunk {ChunkId} for document {DocumentId}", chunk.Id, chunk.DocumentId);
        return chunk.Id;
    }

    public async Task<IEnumerable<string>> StoreBatchAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        if (chunkList.Count == 0) return [];

        var validChunks = chunkList.Where(c => c.Embedding != null && c.Embedding.Length > 0).ToList();
        if (validChunks.Count == 0)
        {
            _logger.LogWarning("No chunks with embeddings to store");
            return [];
        }

        // Dynamic dimension detection: use dimension from first valid chunk
        var dimension = validChunks[0].Embedding!.Length;

        // Validate all chunks have consistent dimensions (Critical: prevents data corruption)
        var inconsistentChunks = validChunks
            .Where(c => c.Embedding!.Length != dimension)
            .Select(c => new { c.Id, Dimension = c.Embedding!.Length })
            .ToList();

        if (inconsistentChunks.Count > 0)
        {
            var examples = string.Join(", ", inconsistentChunks.Take(3).Select(c => $"{c.Id}({c.Dimension}d)"));
            throw new InvalidOperationException(
                $"Batch contains chunks with inconsistent embedding dimensions. " +
                $"Expected {dimension}, found mismatches: {examples}" +
                (inconsistentChunks.Count > 3 ? $" and {inconsistentChunks.Count - 3} more" : ""));
        }

        await EnsureCollectionForDimensionAsync(dimension, cancellationToken);

        var points = validChunks.Select(CreatePointFromChunk).ToList();

        await _client.UpsertAsync(
            collectionName: _resolvedCollectionName!,
            points: points,
            cancellationToken: cancellationToken);

        _logger.LogDebug("Stored {Count} chunks in batch (dim={Dimension})", validChunks.Count, dimension);
        return validChunks.Select(c => c.Id);
    }

    private PointStruct CreatePointFromChunk(DocumentChunk chunk)
    {
        var payload = new Dictionary<string, Value>
        {
            ["document_id"] = chunk.DocumentId,
            ["content"] = chunk.Content,
            ["chunk_index"] = chunk.ChunkIndex,
            ["total_chunks"] = chunk.TotalChunks,
            ["token_count"] = chunk.TokenCount,
            ["created_at"] = chunk.CreatedAt.ToString("O")
        };

        // Add custom properties
        foreach (var prop in chunk.Properties)
        {
            payload[$"prop_{prop.Key}"] = prop.Value?.ToString() ?? string.Empty;
        }

        // Add metadata
        if (chunk.Metadata != null)
        {
            foreach (var meta in chunk.Metadata)
            {
                payload[$"meta_{meta.Key}"] = meta.Value?.ToString() ?? string.Empty;
            }
        }

        // Use Guid for ID
        return new PointStruct
        {
            Id = Guid.Parse(chunk.Id),
            Vectors = chunk.Embedding!,
            Payload = { payload }
        };
    }

    #endregion

    #region Retrieve Operations

    public async Task<DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(cancellationToken);

        try
        {
            if (!Guid.TryParse(id, out var guid))
            {
                _logger.LogDebug("Invalid chunk ID format: {ChunkId}", id);
                return null;
            }

            var points = await _client.RetrieveAsync(
                collectionName: _resolvedCollectionName!,
                id: guid,
                withPayload: true,
                withVectors: true,
                cancellationToken: cancellationToken);

            var point = points.FirstOrDefault();
            if (point == null) return null;

            return MapPointToChunk(point);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to retrieve chunk {ChunkId}", id);
            return null;
        }
    }

    public async Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(cancellationToken);

        var filter = new Filter
        {
            Must =
            {
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "document_id",
                        Match = new Match { Keyword = documentId }
                    }
                }
            }
        };

        var scrollResponse = await _client.ScrollAsync(
            collectionName: _resolvedCollectionName!,
            filter: filter,
            limit: 10000,
            payloadSelector: new WithPayloadSelector { Enable = true },
            vectorsSelector: new WithVectorsSelector { Enable = true },
            cancellationToken: cancellationToken);

        return scrollResponse.Result.Select(MapPointToChunk);
    }

    public async Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(cancellationToken);

        var idList = ids.ToList();
        if (idList.Count == 0) return [];

        var results = new List<DocumentChunk>();

        foreach (var id in idList)
        {
            if (Guid.TryParse(id, out var guid))
            {
                var points = await _client.RetrieveAsync(
                    collectionName: _resolvedCollectionName!,
                    id: guid,
                    withPayload: true,
                    withVectors: true,
                    cancellationToken: cancellationToken);

                var point = points.FirstOrDefault();
                if (point != null)
                {
                    results.Add(MapPointToChunk(point));
                }
            }
        }

        return results;
    }

    private DocumentChunk MapPointToChunk(RetrievedPoint point)
    {
        var payload = point.Payload;

        var chunk = new DocumentChunk
        {
            Id = point.Id.Uuid,
            DocumentId = GetPayloadString(payload, "document_id"),
            Content = GetPayloadString(payload, "content"),
            ChunkIndex = GetPayloadInt(payload, "chunk_index"),
            TotalChunks = GetPayloadInt(payload, "total_chunks"),
            TokenCount = GetPayloadInt(payload, "token_count"),
            CreatedAt = GetPayloadDateTime(payload, "created_at")
        };

        // Extract embedding from vectors
        var denseVector = point.Vectors?.Vector?.GetDenseVector();
        if (denseVector?.Data is { Count: > 0 })
        {
            chunk.SetEmbedding(denseVector.Data.ToArray());
        }

        // Extract custom properties
        foreach (var kv in payload.Where(p => p.Key.StartsWith("prop_")))
        {
            var key = kv.Key[5..]; // Remove "prop_" prefix
            chunk.AddProperty(key, GetPayloadString(payload, kv.Key));
        }

        // Extract metadata
        chunk.Metadata = new Dictionary<string, object>();
        foreach (var kv in payload.Where(p => p.Key.StartsWith("meta_")))
        {
            var key = kv.Key[5..]; // Remove "meta_" prefix
            chunk.Metadata[key] = GetPayloadString(payload, kv.Key);
        }

        // Include standard fields in metadata for consumer apps (RAG source citation)
        chunk.Metadata["chunkIndex"] = chunk.ChunkIndex;
        chunk.Metadata["totalChunks"] = chunk.TotalChunks;
        chunk.Metadata["tokenCount"] = chunk.TokenCount;

        // Restore rich metadata (ChunkMetadata, ChunkQuality, ChunkRelationships)
        RestoreRichMetadataStatic(chunk);

        return chunk;
    }

    #endregion

    #region Search Operations

    public async Task<IEnumerable<DocumentChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        // Validate dimension consistency if collection is already initialized
        if (_detectedDimension.HasValue && queryEmbedding.Length != _detectedDimension.Value)
        {
            throw new InvalidOperationException(
                $"Query embedding dimension ({queryEmbedding.Length}) does not match " +
                $"collection dimension ({_detectedDimension.Value}). " +
                $"Collection '{_resolvedCollectionName}' was initialized with {_detectedDimension.Value}-dimensional vectors.");
        }

        // Ensure collection exists for this dimension
        await EnsureCollectionForDimensionAsync(queryEmbedding.Length, cancellationToken);

        var results = await _client.SearchAsync(
            collectionName: _resolvedCollectionName!,
            vector: queryEmbedding,
            limit: (ulong)topK,
            scoreThreshold: minScore,
            cancellationToken: cancellationToken);

        return results.Select(r =>
        {
            var chunk = MapScoredPointToChunk(r);
            chunk.Score = r.Score;
            return chunk;
        });
    }

    private DocumentChunk MapScoredPointToChunk(ScoredPoint point)
    {
        var payload = point.Payload;

        var chunk = new DocumentChunk
        {
            Id = point.Id.Uuid,
            DocumentId = GetPayloadString(payload, "document_id"),
            Content = GetPayloadString(payload, "content"),
            ChunkIndex = GetPayloadInt(payload, "chunk_index"),
            TotalChunks = GetPayloadInt(payload, "total_chunks"),
            TokenCount = GetPayloadInt(payload, "token_count"),
            CreatedAt = GetPayloadDateTime(payload, "created_at")
        };

        // Extract embedding from vectors if present
        var scoredDenseVector = point.Vectors?.Vector?.GetDenseVector();
        if (scoredDenseVector?.Data is { Count: > 0 })
        {
            chunk.SetEmbedding(scoredDenseVector.Data.ToArray());
        }

        // Extract custom properties
        foreach (var kv in payload.Where(p => p.Key.StartsWith("prop_")))
        {
            var key = kv.Key[5..];
            chunk.AddProperty(key, GetPayloadString(payload, kv.Key));
        }

        // Extract metadata
        chunk.Metadata = new Dictionary<string, object>();
        foreach (var kv in payload.Where(p => p.Key.StartsWith("meta_")))
        {
            var key = kv.Key[5..];
            chunk.Metadata[key] = GetPayloadString(payload, kv.Key);
        }

        // Include standard fields in metadata for consumer apps (RAG source citation)
        chunk.Metadata["chunkIndex"] = chunk.ChunkIndex;
        chunk.Metadata["totalChunks"] = chunk.TotalChunks;
        chunk.Metadata["tokenCount"] = chunk.TokenCount;

        // Restore rich metadata (ChunkMetadata, ChunkQuality, ChunkRelationships)
        RestoreRichMetadataStatic(chunk);

        return chunk;
    }

    private static void RestoreRichMetadataStatic(DocumentChunk chunk)
    {
        if (chunk.Metadata == null)
            return;

        var chunkMetadata = MetadataHelper.DeserializeChunkMetadata(chunk.Metadata);
        if (chunkMetadata != null)
            chunk.SetMetadata(chunkMetadata);

        var quality = MetadataHelper.DeserializeChunkQuality(chunk.Metadata);
        if (quality != null)
            chunk.SetQuality(quality);

        var relationships = MetadataHelper.DeserializeRelationships(chunk.Metadata);
        if (relationships != null)
        {
            foreach (var rel in relationships)
                chunk.AddRelationship(rel);
        }
    }

    #endregion

    #region Update/Delete Operations

    public async Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        if (chunk.Embedding == null || chunk.Embedding.Length == 0)
        {
            throw new ArgumentException("Chunk must have embedding for update", nameof(chunk));
        }

        // Validate dimension consistency if collection is already initialized
        if (_detectedDimension.HasValue && chunk.Embedding.Length != _detectedDimension.Value)
        {
            throw new InvalidOperationException(
                $"Chunk embedding dimension ({chunk.Embedding.Length}) does not match " +
                $"collection dimension ({_detectedDimension.Value}). " +
                $"Cannot update chunk '{chunk.Id}' with different dimension.");
        }

        // Use actual embedding dimension for collection resolution
        await EnsureCollectionForDimensionAsync(chunk.Embedding.Length, cancellationToken);

        var point = CreatePointFromChunk(chunk);

        await _client.UpsertAsync(
            collectionName: _resolvedCollectionName!,
            points: [point],
            cancellationToken: cancellationToken);

        return true;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(cancellationToken);

        if (!Guid.TryParse(id, out var guid))
        {
            _logger.LogWarning("Invalid chunk ID format for delete: {ChunkId}", id);
            return false;
        }

        await _client.DeleteAsync(
            collectionName: _resolvedCollectionName!,
            id: guid,
            cancellationToken: cancellationToken);

        _logger.LogDebug("Deleted chunk {ChunkId}", id);
        return true;
    }

    public async Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(cancellationToken);

        var filter = new Filter
        {
            Must =
            {
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "document_id",
                        Match = new Match { Keyword = documentId }
                    }
                }
            }
        };

        await _client.DeleteAsync(
            collectionName: _resolvedCollectionName!,
            filter: filter,
            cancellationToken: cancellationToken);

        _logger.LogDebug("Deleted chunks for document {DocumentId}", documentId);
        return true;
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        var chunk = await GetByIdAsync(id, cancellationToken);
        return chunk != null;
    }

    #endregion

    #region Count/Clear Operations

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await GetCountAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(cancellationToken);

        var info = await _client.GetCollectionInfoAsync(_resolvedCollectionName!, cancellationToken);
        return (int)info.PointsCount;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _initLock.WaitAsync(cancellationToken);
        try
        {
            // If not initialized, nothing to clear
            if (_resolvedCollectionName == null)
            {
                _logger.LogDebug("Clear called but no collection initialized");
                return;
            }

            var collectionToClear = _resolvedCollectionName;

            try
            {
                await _client.DeleteCollectionAsync(collectionToClear);
                _logger.LogInformation("Cleared Qdrant collection '{CollectionName}'", collectionToClear);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete Qdrant collection '{CollectionName}'", collectionToClear);
            }

            // Reset state under lock to prevent race conditions
            _collectionInitialized = false;
            _detectedDimension = null;
            _resolvedCollectionName = null;

            // Note: Do NOT recreate collection here. Let next Store/Search operation
            // initialize with the correct dimension automatically.
        }
        finally
        {
            _initLock.Release();
        }
    }

    #endregion

    #region Helper Methods

    private static string GetPayloadString(IDictionary<string, Value> payload, string key)
    {
        if (payload.TryGetValue(key, out var value))
        {
            return value.StringValue ?? string.Empty;
        }
        return string.Empty;
    }

    private static int GetPayloadInt(IDictionary<string, Value> payload, string key)
    {
        if (payload.TryGetValue(key, out var value))
        {
            return (int)value.IntegerValue;
        }
        return 0;
    }

    private static DateTime GetPayloadDateTime(IDictionary<string, Value> payload, string key)
    {
        if (payload.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value.StringValue))
        {
            if (DateTime.TryParse(value.StringValue, out var dt))
            {
                return dt;
            }
        }
        return DateTime.UtcNow;
    }

    #endregion

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        _initLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
