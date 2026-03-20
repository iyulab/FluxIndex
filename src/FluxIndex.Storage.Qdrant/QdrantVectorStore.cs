using System.Text.Json;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Exceptions;
using FluxIndex.Core.Domain.ValueObjects;
using Google.Protobuf.Collections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace FluxIndex.Storage.Qdrant;

/// <summary>
/// Qdrant implementation of IVectorStore for high-performance vector similarity search.
/// Supports dynamic dimension adaptation via CollectionNamingStrategy.
/// </summary>
public partial class QdrantVectorStore : IVectorStore, IAsyncDisposable
{
    private readonly QdrantClient _client;
    private readonly QdrantOptions _options;
    private readonly ILogger<QdrantVectorStore> _logger;
    private bool _collectionInitialized;

    // Dynamic dimension adaptation fields
    private string? _resolvedCollectionName;
    private int? _detectedDimension;
    private EmbeddingIdentity? _boundIdentity;
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
    /// With ModelFingerprint strategy, uses <see cref="BoundIdentity"/> fingerprint.
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
#pragma warning disable CS0618 // DimensionSuffix is obsolete
            var collectionName = _options.NamingStrategy switch
            {
                CollectionNamingStrategy.ModelFingerprint when _boundIdentity is not null
                    => $"{_options.BaseCollectionName}_{_boundIdentity.Fingerprint}",
                CollectionNamingStrategy.ModelFingerprint
                    => $"{_options.BaseCollectionName}_{dimension}",  // fallback if no identity bound yet
                CollectionNamingStrategy.DimensionSuffix
                    => $"{_options.BaseCollectionName}_{dimension}",
                _ => _options.BaseCollectionName
            };
#pragma warning restore CS0618

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

                LogCollectionReady(_logger, _resolvedCollectionName!, dimension, _options.NamingStrategy);
            }
            catch (Exception ex)
            {
                // Log warning but still set collection name (assume exists)
                LogCollectionInitFailed(_logger, ex, collectionName);

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

        LogCollectionCreated(_logger, _resolvedCollectionName!, dimension);
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

    /// <inheritdoc />
    public string? ResolvedStoreName => _resolvedCollectionName;

    /// <inheritdoc />
    public int? DetectedDimension => _detectedDimension;

    /// <inheritdoc />
    public EmbeddingIdentity? BoundIdentity => _boundIdentity;

    /// <summary>
    /// Binds an embedding identity to this store.
    /// Once bound, the collection name is determined by the identity's fingerprint (when using ModelFingerprint strategy).
    /// If the store was previously bound to a different identity, throws <see cref="EmbeddingModelMismatchException"/>.
    /// </summary>
    public void BindIdentity(EmbeddingIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (_boundIdentity is not null && _boundIdentity != identity)
        {
            throw new EmbeddingModelMismatchException(_boundIdentity, identity);
        }

        if (_boundIdentity is null)
        {
            _boundIdentity = identity;
            // Reset initialization state to force re-resolution with the new identity
            _collectionInitialized = false;
            LogIdentityBound(_logger, identity.Provider, identity.Model, identity.Dimension);
        }
    }

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

        LogChunkStored(_logger, chunk.Id, chunk.DocumentId);
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
            LogNoChunksToStore(_logger);
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

        LogBatchStored(_logger, validChunks.Count, dimension);
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
                LogInvalidChunkIdFormat(_logger, id);
                return null;
            }

            var points = await _client.RetrieveAsync(
                collectionName: _resolvedCollectionName!,
                id: guid,
                withPayload: true,
                withVectors: true,
                cancellationToken: cancellationToken);

            var point = points.Count > 0 ? points[0] : null;
            if (point == null) return null;

            return MapPointToChunk(point);
        }
        catch (Exception ex)
        {
            LogRetrieveChunkFailed(_logger, ex, id);
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

                var point = points.Count > 0 ? points[0] : null;
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
        foreach (var kv in payload.Where(p => p.Key.StartsWith("prop_", StringComparison.Ordinal)))
        {
            var key = kv.Key[5..]; // Remove "prop_" prefix
            chunk.AddProperty(key, GetPayloadString(payload, kv.Key));
        }

        // Extract metadata
        chunk.Metadata = new Dictionary<string, object>();
        foreach (var kv in payload.Where(p => p.Key.StartsWith("meta_", StringComparison.Ordinal)))
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
        Dictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default)
    {
        // Dynamic dimension adaptation: switch collection if dimension changed
        // (e.g., user changed embedding model in settings without restart)
        if (_detectedDimension.HasValue && queryEmbedding.Length != _detectedDimension.Value)
        {
            LogDimensionSwitch(_logger, _detectedDimension.Value, queryEmbedding.Length);
        }

        // Ensure collection exists for this dimension (creates if needed, switches if dimension changed)
        await EnsureCollectionForDimensionAsync(queryEmbedding.Length, cancellationToken);

        // If collection doesn't exist and auto-creation is disabled, return empty results
        if (_resolvedCollectionName == null)
        {
            return [];
        }

        // Verify the collection actually exists (may not if just switching to a new dimension)
        try
        {
            var collections = await _client.ListCollectionsAsync(cancellationToken);
            if (!collections.Any(c => c == _resolvedCollectionName))
            {
                LogCollectionNotFound(_logger, _resolvedCollectionName);
                return [];
            }
        }
        catch (Exception ex)
        {
            LogCollectionCheckFailed(_logger, ex, _resolvedCollectionName);
            return [];
        }

        var qdrantFilter = BuildQdrantFilter(filters);

        var results = await _client.SearchAsync(
            collectionName: _resolvedCollectionName!,
            vector: queryEmbedding,
            filter: qdrantFilter,
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

    /// <summary>
    /// Converts a filter dictionary to a Qdrant Filter with Must conditions.
    /// Keys are mapped to Qdrant payload field names:
    /// - Standard fields (document_id, chunk_index, etc.) → used directly
    /// - Keys with prop_/meta_ prefix → used as-is
    /// - Other keys → prefixed with meta_ (consistent with metadata storage convention)
    /// </summary>
    private static Filter? BuildQdrantFilter(Dictionary<string, object>? filters)
    {
        if (filters == null || filters.Count == 0)
            return null;

        var filter = new Filter();
        foreach (var (key, value) in filters)
        {
            var payloadKey = key switch
            {
                "document_id" or "content" or "chunk_index" or "total_chunks"
                    or "token_count" or "created_at" => key,
                _ when key.StartsWith("prop_", StringComparison.Ordinal) => key,
                _ when key.StartsWith("meta_", StringComparison.Ordinal) => key,
                _ => $"meta_{key}"
            };

            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = payloadKey,
                    Match = new Match { Keyword = value?.ToString() ?? string.Empty }
                }
            });
        }

        return filter;
    }

    private static DocumentChunk MapScoredPointToChunk(ScoredPoint point)
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
        foreach (var kv in payload.Where(p => p.Key.StartsWith("prop_", StringComparison.Ordinal)))
        {
            var key = kv.Key[5..];
            chunk.AddProperty(key, GetPayloadString(payload, kv.Key));
        }

        // Extract metadata
        chunk.Metadata = new Dictionary<string, object>();
        foreach (var kv in payload.Where(p => p.Key.StartsWith("meta_", StringComparison.Ordinal)))
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
            LogInvalidDeleteId(_logger, id);
            return false;
        }

        await _client.DeleteAsync(
            collectionName: _resolvedCollectionName!,
            id: guid,
            cancellationToken: cancellationToken);

        LogChunkDeleted(_logger, id);
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

        LogDocumentChunksDeleted(_logger, documentId);
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
                LogClearNoCollection(_logger);
                return;
            }

            var collectionToClear = _resolvedCollectionName;

            try
            {
#pragma warning disable CA2016 // Qdrant SDK does not accept CancellationToken for DeleteCollectionAsync
                await _client.DeleteCollectionAsync(collectionToClear);
#pragma warning restore CA2016
                LogCollectionCleared(_logger, collectionToClear);
            }
            catch (Exception ex)
            {
                LogDeleteCollectionFailed(_logger, ex, collectionToClear);
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

    private static string GetPayloadString(MapField<string, Value> payload, string key)
    {
        if (payload.TryGetValue(key, out var value))
        {
            return value.StringValue ?? string.Empty;
        }
        return string.Empty;
    }

    private static int GetPayloadInt(MapField<string, Value> payload, string key)
    {
        if (payload.TryGetValue(key, out var value))
        {
            return (int)value.IntegerValue;
        }
        return 0;
    }

    private static DateTime GetPayloadDateTime(MapField<string, Value> payload, string key)
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
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Qdrant collection ready: '{Collection}' (dim={Dimension}, strategy={Strategy})")]
    private static partial void LogCollectionReady(ILogger logger, string collection, int dimension, CollectionNamingStrategy strategy);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to initialize Qdrant collection '{Collection}' (assuming it exists)")]
    private static partial void LogCollectionInitFailed(ILogger logger, Exception exception, string collection);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created Qdrant collection '{CollectionName}' with dimension {Dimension}")]
    private static partial void LogCollectionCreated(ILogger logger, string collectionName, int dimension);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored chunk {ChunkId} for document {DocumentId}")]
    private static partial void LogChunkStored(ILogger logger, string chunkId, string documentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No chunks with embeddings to store")]
    private static partial void LogNoChunksToStore(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored {Count} chunks in batch (dim={Dimension})")]
    private static partial void LogBatchStored(ILogger logger, int count, int dimension);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invalid chunk ID format: {ChunkId}")]
    private static partial void LogInvalidChunkIdFormat(ILogger logger, string chunkId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to retrieve chunk {ChunkId}")]
    private static partial void LogRetrieveChunkFailed(ILogger logger, Exception exception, string chunkId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid chunk ID format for delete: {ChunkId}")]
    private static partial void LogInvalidDeleteId(ILogger logger, string chunkId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted chunk {ChunkId}")]
    private static partial void LogChunkDeleted(ILogger logger, string chunkId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted chunks for document {DocumentId}")]
    private static partial void LogDocumentChunksDeleted(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clear called but no collection initialized")]
    private static partial void LogClearNoCollection(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleared Qdrant collection '{CollectionName}'")]
    private static partial void LogCollectionCleared(ILogger logger, string collectionName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to delete Qdrant collection '{CollectionName}'")]
    private static partial void LogDeleteCollectionFailed(ILogger logger, Exception exception, string collectionName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Search dimension changed from {OldDimension} to {NewDimension}, switching collection")]
    private static partial void LogDimensionSwitch(ILogger logger, int oldDimension, int newDimension);

    [LoggerMessage(Level = LogLevel.Information, Message = "Collection '{CollectionName}' not found, returning empty results")]
    private static partial void LogCollectionNotFound(ILogger logger, string collectionName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to check collection '{CollectionName}' existence")]
    private static partial void LogCollectionCheckFailed(ILogger logger, Exception exception, string collectionName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Embedding identity bound: {Provider}:{Model} ({Dimension}d)")]
    private static partial void LogIdentityBound(ILogger logger, string provider, string model, int dimension);

    #endregion
}
