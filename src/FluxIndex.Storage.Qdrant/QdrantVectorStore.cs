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
/// </summary>
public class QdrantVectorStore : IVectorStore, IAsyncDisposable
{
    private readonly QdrantClient _client;
    private readonly QdrantOptions _options;
    private readonly ILogger<QdrantVectorStore> _logger;
    private bool _collectionInitialized;

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

    private async Task EnsureCollectionAsync(CancellationToken ct)
    {
        if (_collectionInitialized) return;

        try
        {
            var collections = await _client.ListCollectionsAsync(ct);
            var exists = collections.Any(c => c == _options.CollectionName);

            if (!exists && _options.CreateCollectionOnStartup)
            {
                var distance = _options.DistanceMetric switch
                {
                    QdrantDistanceMetric.Cosine => Distance.Cosine,
                    QdrantDistanceMetric.Euclid => Distance.Euclid,
                    QdrantDistanceMetric.Dot => Distance.Dot,
                    _ => Distance.Cosine
                };

                await _client.CreateCollectionAsync(
                    collectionName: _options.CollectionName,
                    vectorsConfig: new VectorParams
                    {
                        Size = (ulong)_options.VectorSize,
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
                    collectionName: _options.CollectionName,
                    fieldName: "document_id",
                    schemaType: PayloadSchemaType.Keyword,
                    cancellationToken: ct);

                await _client.CreatePayloadIndexAsync(
                    collectionName: _options.CollectionName,
                    fieldName: "chunk_index",
                    schemaType: PayloadSchemaType.Integer,
                    cancellationToken: ct);

                _logger.LogInformation("Created Qdrant collection '{CollectionName}' with dimension {Dimension}",
                    _options.CollectionName, _options.VectorSize);
            }

            _collectionInitialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Qdrant collection (may already exist)");
            _collectionInitialized = true; // Assume it exists
        }
    }

    #region Store Operations

    public async Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(cancellationToken);

        if (chunk.Embedding == null || chunk.Embedding.Length == 0)
        {
            throw new ArgumentException("Chunk must have embedding", nameof(chunk));
        }

        var point = CreatePointFromChunk(chunk);

        await _client.UpsertAsync(
            collectionName: _options.CollectionName,
            points: [point],
            cancellationToken: cancellationToken);

        _logger.LogDebug("Stored chunk {ChunkId} for document {DocumentId}", chunk.Id, chunk.DocumentId);
        return chunk.Id;
    }

    public async Task<IEnumerable<string>> StoreBatchAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(cancellationToken);

        var chunkList = chunks.ToList();
        if (chunkList.Count == 0) return [];

        var validChunks = chunkList.Where(c => c.Embedding != null && c.Embedding.Length > 0).ToList();
        if (validChunks.Count == 0)
        {
            _logger.LogWarning("No chunks with embeddings to store");
            return [];
        }

        var points = validChunks.Select(CreatePointFromChunk).ToList();

        await _client.UpsertAsync(
            collectionName: _options.CollectionName,
            points: points,
            cancellationToken: cancellationToken);

        _logger.LogDebug("Stored {Count} chunks in batch", validChunks.Count);
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
                collectionName: _options.CollectionName,
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
            collectionName: _options.CollectionName,
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
                    collectionName: _options.CollectionName,
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
        await EnsureCollectionAsync(cancellationToken);

        var results = await _client.SearchAsync(
            collectionName: _options.CollectionName,
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
        await EnsureCollectionAsync(cancellationToken);

        if (chunk.Embedding == null || chunk.Embedding.Length == 0)
        {
            throw new ArgumentException("Chunk must have embedding for update", nameof(chunk));
        }

        var point = CreatePointFromChunk(chunk);

        await _client.UpsertAsync(
            collectionName: _options.CollectionName,
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
            collectionName: _options.CollectionName,
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
            collectionName: _options.CollectionName,
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

        var info = await _client.GetCollectionInfoAsync(_options.CollectionName, cancellationToken);
        return (int)info.PointsCount;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DeleteCollectionAsync(_options.CollectionName);
            _collectionInitialized = false;
            _logger.LogInformation("Cleared Qdrant collection '{CollectionName}'", _options.CollectionName);

            // Recreate collection
            await EnsureCollectionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear Qdrant collection");
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
        return ValueTask.CompletedTask;
    }
}
