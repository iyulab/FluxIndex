using System.Text.Json;
using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.SDK.Services;
using Xunit;
using DocumentChunkEntity = FluxIndex.Core.Domain.Entities.DocumentChunk;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Guards the SDK surface against the metadata-filter contract defects reported by All.Manual
/// on FluxIndex 0.18.0: the convenience indexing overloads dropped metadata before it reached the
/// chunk rows that filters actually read, and <c>Retriever</c> compared filter values with raw
/// object equality, which a JSON round-trip (jsonb / JSON column) breaks.
/// </summary>
public class SdkFilterContractTests
{
    private const string TenantKey = "workspace_id";

    /// <summary>
    /// Stands in for a persistent store (PostgreSQL/SQLite) after a process restart: it applies the
    /// filter itself under the canonical <see cref="VectorStoreBase.MatchesMetadataFilter"/>
    /// semantics, and hands back metadata materialized as <see cref="JsonElement"/> the way a JSON
    /// column round-trip does. An <see cref="InMemoryVectorStore"/> keeps the original CLR strings
    /// and so cannot reproduce the defect — the JsonElement round-trip is the whole point.
    /// </summary>
    private sealed class JsonRoundTripVectorStore : IVectorStore
    {
        private readonly List<DocumentChunkEntity> _chunks = new();

        public Task<string> StoreAsync(DocumentChunkEntity chunk, CancellationToken cancellationToken = default)
        {
            _chunks.Add(RoundTrip(chunk));
            return Task.FromResult(chunk.Id);
        }

        public Task<IEnumerable<DocumentChunkEntity>> SearchAsync(
            float[] queryEmbedding,
            int topK = 10,
            float minScore = 0.0f,
            Dictionary<string, object>? filters = null,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<DocumentChunkEntity> hits = _chunks;
            if (filters is { Count: > 0 })
                hits = hits.Where(c => VectorStoreBase.MatchesMetadataFilter(c.Metadata, filters));

            return Task.FromResult(hits.Take(topK).ToList().AsEnumerable());
        }

        /// <summary>Simulates the JSON column round-trip: values come back as JsonElement.</summary>
        private static DocumentChunkEntity RoundTrip(DocumentChunkEntity chunk)
        {
            if (chunk.Metadata is null)
                return chunk;

            var json = JsonSerializer.Serialize(chunk.Metadata);
            chunk.Metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
            chunk.Score = 1.0f;
            return chunk;
        }

        public Task<IEnumerable<string>> StoreBatchAsync(IEnumerable<DocumentChunkEntity> chunks, CancellationToken cancellationToken = default)
        {
            var ids = new List<string>();
            foreach (var chunk in chunks)
            {
                _chunks.Add(RoundTrip(chunk));
                ids.Add(chunk.Id);
            }
            return Task.FromResult(ids.AsEnumerable());
        }

        public Task<DocumentChunkEntity?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_chunks.FirstOrDefault(c => c.Id == id));
        public Task<IEnumerable<DocumentChunkEntity>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
            => Task.FromResult(_chunks.Where(c => c.DocumentId == documentId).AsEnumerable());
        public Task<IEnumerable<DocumentChunkEntity>> GetChunksByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
            => Task.FromResult(_chunks.Where(c => ids.Contains(c.Id)).AsEnumerable());
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_chunks.RemoveAll(c => c.Id == id) > 0);
        public Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
            => Task.FromResult(_chunks.RemoveAll(c => c.DocumentId == documentId) > 0);
        public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_chunks.Any(c => c.Id == id));
        public Task<DocumentChunkEntity?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => GetAsync(id, cancellationToken);
        public Task<bool> UpdateAsync(DocumentChunkEntity chunk, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
        public Task<int> CountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_chunks.Count);
        public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_chunks.Count);
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            _chunks.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class ConstantEmbeddingService : IEmbeddingService
    {
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(new[] { 1.0f, 0.0f, 0.0f });
        public Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
            => Task.FromResult(texts.Select(_ => new[] { 1.0f, 0.0f, 0.0f }));
        public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(text.Length / 4);
        public int GetEmbeddingDimension() => 3;
        public string GetModelName() => "test-constant";
        public int GetMaxTokens() => 512;
        public EmbeddingIdentity GetIdentity()
            => new() { Provider = "Test", Model = "test-constant", Dimension = 3 };
    }

    private static Retriever CreateRetriever(IVectorStore store)
        => new(store, new InMemoryDocumentRepository(), new ConstantEmbeddingService(), new RetrieverOptions());

    /// <summary>
    /// The reported defect: the store applies the filter and returns the row, then the SDK re-applies
    /// the filter with raw object equality — <c>JsonElement("ws-a").Equals("ws-a")</c> is false — and
    /// silently drops every result. Same process passes (change-tracked CLR strings), restarted
    /// process returns 0.
    /// </summary>
    [Fact]
    public async Task SearchAsync_WithFilter_MatchesMetadataThatRoundTrippedThroughJson()
    {
        var store = new JsonRoundTripVectorStore();
        var chunk = DocumentChunkEntity.Create("doc-1", "tenant a content", 0, 1);
        chunk.Metadata = new Dictionary<string, object> { [TenantKey] = "ws-a" };
        await store.StoreAsync(chunk);

        var results = await CreateRetriever(store).SearchAsync(
            "content",
            maxResults: 10,
            minScore: 0f,
            filter: new Dictionary<string, object> { [TenantKey] = "ws-a" });

        results.Should().HaveCount(1, "the store matched the filter and the SDK must not discard the row over a JSON round-trip");
    }

    /// <summary>A non-matching tenant must still be excluded — the fix must not simply stop filtering.</summary>
    [Fact]
    public async Task SearchAsync_WithFilter_ExcludesNonMatchingMetadata()
    {
        var store = new JsonRoundTripVectorStore();
        var chunk = DocumentChunkEntity.Create("doc-1", "tenant a content", 0, 1);
        chunk.Metadata = new Dictionary<string, object> { [TenantKey] = "ws-a" };
        await store.StoreAsync(chunk);

        var results = await CreateRetriever(store).SearchAsync(
            "content",
            maxResults: 10,
            minScore: 0f,
            filter: new Dictionary<string, object> { [TenantKey] = "ws-b" });

        results.Should().BeEmpty();
    }

    /// <summary>Non-string values (bool/number) must survive the round-trip comparison too.</summary>
    [Fact]
    public async Task SearchAsync_WithFilter_MatchesNonStringMetadataAcrossJsonRoundTrip()
    {
        var store = new JsonRoundTripVectorStore();
        var chunk = DocumentChunkEntity.Create("doc-1", "published content", 0, 1);
        chunk.Metadata = new Dictionary<string, object> { ["published"] = true, ["version"] = 3 };
        await store.StoreAsync(chunk);

        var results = await CreateRetriever(store).SearchAsync(
            "content",
            maxResults: 10,
            minScore: 0f,
            filter: new Dictionary<string, object> { ["published"] = true, ["version"] = 3 });

        results.Should().HaveCount(1);
    }

    private static Indexer CreateIndexer(IVectorStore store)
        => new(store, new InMemoryDocumentRepository(), new ConstantEmbeddingService(),
            new FixedContentChunkingService(), new IndexerOptions());

    /// <summary>Chunking is not under test here — one chunk per document keeps the metadata assertions direct.</summary>
    private sealed class FixedContentChunkingService : IChunkingService
    {
        public Task<IEnumerable<DocumentChunkEntity>> ChunkDocumentAsync(
            string content, int chunkSize = 512, int chunkOverlap = 64, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<DocumentChunkEntity>>(
                new[] { DocumentChunkEntity.Create(string.Empty, content, 0, 1) });

        public IEnumerable<string> ChunkText(string text, int chunkSize = 512, int chunkOverlap = 64)
            => new[] { text };
    }

    /// <summary>
    /// The convenience overload's whole purpose is to carry metadata. Filters read chunk metadata,
    /// so metadata that only ever reaches the Document entity is invisible to every filter — and the
    /// document repository is in-memory-only, so it does not survive a restart either.
    /// </summary>
    [Fact]
    public async Task IndexDocumentAsync_ConvenienceOverload_PutsMetadataWhereFiltersReadIt()
    {
        var store = new JsonRoundTripVectorStore();

        await CreateIndexer(store).IndexDocumentAsync(
            "tenant a content",
            "doc-1",
            new Dictionary<string, object> { [TenantKey] = "ws-a" });

        var stored = await store.GetByDocumentIdAsync("doc-1");
        stored.Should().ContainSingle()
            .Which.Metadata.Should().ContainKey(TenantKey);
    }

    /// <summary>End-to-end acceptance: index with metadata, find it by filter — no entity-API assembly.</summary>
    [Fact]
    public async Task IndexDocumentAsync_ConvenienceOverload_ThenSearchWithFilter_FindsTheDocument()
    {
        var store = new JsonRoundTripVectorStore();
        await CreateIndexer(store).IndexDocumentAsync(
            "tenant a content",
            "doc-1",
            new Dictionary<string, object> { [TenantKey] = "ws-a" });

        var results = await CreateRetriever(store).SearchAsync(
            "content",
            maxResults: 10,
            minScore: 0f,
            filter: new Dictionary<string, object> { [TenantKey] = "ws-a" });

        results.Should().ContainSingle().Which.DocumentChunk.DocumentId.Should().Be("doc-1");
    }

    /// <summary>Metadata carried on the chunk model must not be dropped by the model→entity conversion.</summary>
    [Fact]
    public async Task IndexChunksAsync_PreservesPerChunkMetadata()
    {
        var store = new JsonRoundTripVectorStore();
        var chunk = new FluxIndex.Core.Domain.Models.CacheDocumentChunk
        {
            DocumentId = "doc-1",
            Content = "chunk content",
            ChunkIndex = 0,
            TotalChunks = 1,
            Metadata = new Dictionary<string, object> { ["section"] = "intro" }
        };

        await CreateIndexer(store).IndexChunksAsync(new[] { chunk }, "doc-1");

        var stored = await store.GetByDocumentIdAsync("doc-1");
        stored.Should().ContainSingle()
            .Which.Metadata.Should().ContainKey("section");
    }

    /// <summary>
    /// Document-level metadata is the base layer; a chunk's own metadata is the more specific scope
    /// and must win on key collision.
    /// </summary>
    [Fact]
    public async Task IndexChunksAsync_ChunkMetadata_WinsOverDocumentMetadata()
    {
        var store = new JsonRoundTripVectorStore();
        var chunk = new FluxIndex.Core.Domain.Models.CacheDocumentChunk
        {
            DocumentId = "doc-1",
            Content = "chunk content",
            ChunkIndex = 0,
            TotalChunks = 1,
            Metadata = new Dictionary<string, object> { ["scope"] = "chunk" }
        };

        await CreateIndexer(store).IndexChunksAsync(
            new[] { chunk },
            "doc-1",
            new Dictionary<string, object> { ["scope"] = "document", [TenantKey] = "ws-a" });

        var stored = (await store.GetByDocumentIdAsync("doc-1")).Single();
        VectorStoreBase.NormalizeFilterValue(stored.Metadata!["scope"]).Should().Be("chunk");
        VectorStoreBase.NormalizeFilterValue(stored.Metadata![TenantKey]).Should().Be("ws-a");
    }
}
