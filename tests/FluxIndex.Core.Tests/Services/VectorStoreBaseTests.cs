using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

public class VectorStoreBaseTests
{
    private readonly TestVectorStore _store;

    public VectorStoreBaseTests()
    {
        _store = new TestVectorStore();
    }

    #region StoreAsync Tests

    [Fact]
    public async Task StoreAsync_ValidChunk_CallsStoreCoreAsync()
    {
        // Arrange
        var chunk = CreateChunk("doc-1", "Test content");

        // Act
        var id = await _store.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(id);
        Assert.True(_store.StoreCoreWasCalled);
    }

    [Fact]
    public async Task StoreAsync_PreparesMetadata()
    {
        // Arrange
        var chunk = CreateChunk("doc-1", "Test content");
        chunk.Metadata = null;

        // Act
        await _store.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(chunk.Metadata);
        Assert.True(chunk.Metadata.ContainsKey(MetadataHelper.StandardKeys.DocumentId));
        Assert.True(chunk.Metadata.ContainsKey(MetadataHelper.StandardKeys.ChunkIndex));
    }

    [Fact]
    public async Task StoreAsync_NullChunk_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.StoreAsync(null!, TestContext.Current.CancellationToken));
    }

    #endregion

    #region StoreBatchAsync Tests

    [Fact]
    public async Task StoreBatchAsync_MultipleChunks_ReturnsAllIds()
    {
        // Arrange
        var chunks = new[]
        {
            CreateChunk("doc-1", "Content 1"),
            CreateChunk("doc-1", "Content 2"),
            CreateChunk("doc-2", "Content 3")
        };

        // Act
        var ids = (await _store.StoreBatchAsync(chunks, TestContext.Current.CancellationToken)).ToList();

        // Assert
        Assert.Equal(3, ids.Count);
        Assert.All(ids, id => Assert.NotEmpty(id));
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_ExistingId_ReturnsChunk()
    {
        // Arrange
        var chunk = CreateChunk("doc-1", "Test content");
        var id = await _store.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Act
        var retrieved = await _store.GetAsync(id, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(chunk.Content, retrieved.Content);
    }

    [Fact]
    public async Task GetAsync_EmptyId_ReturnsNull()
    {
        // Act
        var result = await _store.GetAsync("", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_DelegatesToGetAsync()
    {
        // Arrange
        var chunk = CreateChunk("doc-1", "Test content");
        var id = await _store.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Act
        var retrieved = await _store.GetByIdAsync(id, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrieved);
    }

    #endregion

    #region SearchAsync Tests

    [Fact]
    public async Task SearchAsync_NullEmbedding_ReturnsEmpty()
    {
        // Act
        var results = await _store.SearchAsync(null!, topK: 10, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_EmptyEmbedding_ReturnsEmpty()
    {
        // Act
        var results = await _store.SearchAsync([], topK: 10, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ZeroTopK_ReturnsEmpty()
    {
        // Arrange
        var embedding = new float[] { 1, 2, 3 };

        // Act
        var results = await _store.SearchAsync(embedding, topK: 0, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ValidQuery_FiltersAndSortsResults()
    {
        // Arrange
        await _store.StoreAsync(CreateChunkWithEmbedding("chunk-1", new[] { 0.9f, 0.1f }), TestContext.Current.CancellationToken);
        await _store.StoreAsync(CreateChunkWithEmbedding("chunk-2", new[] { 0.5f, 0.5f }), TestContext.Current.CancellationToken);
        await _store.StoreAsync(CreateChunkWithEmbedding("chunk-3", new[] { 0.1f, 0.9f }), TestContext.Current.CancellationToken);

        // Act
        var results = (await _store.SearchAsync(new[] { 0.9f, 0.1f }, topK: 10, minScore: 0.5f, cancellationToken: TestContext.Current.CancellationToken)).ToList();

        // Assert
        Assert.NotEmpty(results);
        // Results should be sorted by similarity (descending)
        if (results.Count > 1)
        {
            var scores = results.Select(r => r.Score ?? 0).ToList();
            Assert.True(scores.SequenceEqual(scores.OrderByDescending(s => s)));
        }
    }

    [Fact]
    public async Task SearchAsync_ForwardsFiltersToSearchCore()
    {
        // Arrange
        await _store.StoreAsync(CreateChunkWithEmbedding("chunk-1", new[] { 0.9f, 0.1f }), TestContext.Current.CancellationToken);
        var filters = new Dictionary<string, object> { ["workspace_id"] = "ws-1" };

        // Act
        await _store.SearchAsync(new[] { 0.9f, 0.1f }, topK: 5, minScore: 0f, filters: filters, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — stores need the filters to push them down before internal candidate trimming
        Assert.NotNull(_store.LastSearchFilters);
        Assert.Equal("ws-1", _store.LastSearchFilters!["workspace_id"]);
    }

    [Fact]
    public async Task SearchAsync_MetadataFilter_AppliedBeforeTopKTrim()
    {
        // Arrange — the 2 highest-scoring chunks belong to ANOTHER tenant. With topK=2, the old
        // trim-then-filter order returned 0 results for the target tenant (recall collapse).
        var query = new[] { 1.0f, 0.0f };

        var otherA = CreateChunkWithEmbedding("other-a", new[] { 1.0f, 0.0f });   // score 1.0
        otherA.Metadata = new Dictionary<string, object> { ["workspace_id"] = "ws-other" };
        var otherB = CreateChunkWithEmbedding("other-b", new[] { 0.99f, 0.14f }); // ~0.99
        otherB.Metadata = new Dictionary<string, object> { ["workspace_id"] = "ws-other" };
        var target = CreateChunkWithEmbedding("target", new[] { 0.9f, 0.44f });   // ~0.9
        target.Metadata = new Dictionary<string, object> { ["workspace_id"] = "ws-target" };

        await _store.StoreAsync(otherA, TestContext.Current.CancellationToken);
        await _store.StoreAsync(otherB, TestContext.Current.CancellationToken);
        await _store.StoreAsync(target, TestContext.Current.CancellationToken);

        var filters = new Dictionary<string, object> { ["workspace_id"] = "ws-target" };

        // Act
        var results = (await _store.SearchAsync(query, topK: 2, minScore: 0f, filters: filters, cancellationToken: TestContext.Current.CancellationToken)).ToList();

        // Assert — the target-tenant chunk must survive even though it is not in the global top-2
        var match = Assert.Single(results);
        Assert.Equal("target", match.Id);
    }

    [Fact]
    public async Task SearchAsync_MetadataFilter_NoMatch_ReturnsEmpty()
    {
        // Arrange
        var chunk = CreateChunkWithEmbedding("chunk-1", new[] { 0.9f, 0.1f });
        chunk.Metadata = new Dictionary<string, object> { ["workspace_id"] = "ws-1" };
        await _store.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Act
        var results = await _store.SearchAsync(new[] { 0.9f, 0.1f }, topK: 5, minScore: 0f, filters: new Dictionary<string, object> { ["workspace_id"] = "ws-absent" }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region MatchesMetadataFilter / NormalizeFilterValue Tests

    [Fact]
    public void MatchesMetadataFilter_BoolSurvivesJsonRoundTrip()
    {
        // Arrange — stored side deserialized from JSON (JsonElement true), filter side .NET bool.
        // Without normalization "True" (bool.ToString) != "true" (JSON text) and the match fails.
        using var doc = System.Text.Json.JsonDocument.Parse("""{"flag": true}""");
        var metadata = new Dictionary<string, object> { ["flag"] = doc.RootElement.GetProperty("flag").Clone() };
        var filters = new Dictionary<string, object> { ["flag"] = true };

        // Act & Assert
        Assert.True(VectorStoreBase.MatchesMetadataFilter(metadata, filters));
    }

    [Fact]
    public void MatchesMetadataFilter_StringEquality_IsOrdinal()
    {
        var metadata = new Dictionary<string, object> { ["workspace_id"] = "WS-1" };

        Assert.True(VectorStoreBase.MatchesMetadataFilter(
            metadata, new Dictionary<string, object> { ["workspace_id"] = "WS-1" }));
        Assert.False(VectorStoreBase.MatchesMetadataFilter(
            metadata, new Dictionary<string, object> { ["workspace_id"] = "ws-1" }));
    }

    [Fact]
    public void NormalizeFilterValue_UsesInvariantCultureAndJsonBoolText()
    {
        Assert.Equal("true", VectorStoreBase.NormalizeFilterValue(true));
        Assert.Equal("false", VectorStoreBase.NormalizeFilterValue(false));
        Assert.Equal("3.5", VectorStoreBase.NormalizeFilterValue(3.5));
        Assert.Equal("42", VectorStoreBase.NormalizeFilterValue(42));
        Assert.Null(VectorStoreBase.NormalizeFilterValue(null));
    }

    [Fact]
    public void MatchesMetadataFilter_CollectionValue_MatchesAnyElement()
    {
        // MatchAny: filter value document_id ∈ {hash1, hash2}
        var metadata = new Dictionary<string, object> { ["document_id"] = "hash2" };
        var filters = new Dictionary<string, object>
        {
            ["document_id"] = new List<string> { "hash1", "hash2", "hash3" }
        };

        Assert.True(VectorStoreBase.MatchesMetadataFilter(metadata, filters));

        var nonMatching = new Dictionary<string, object> { ["document_id"] = "hash9" };
        Assert.False(VectorStoreBase.MatchesMetadataFilter(nonMatching, filters));
    }

    [Fact]
    public void MatchesMetadataFilter_JsonElementArray_MatchesAnyElement()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""["a", "b"]""");
        var metadata = new Dictionary<string, object> { ["tag"] = "b" };
        var filters = new Dictionary<string, object> { ["tag"] = doc.RootElement.Clone() };

        Assert.True(VectorStoreBase.MatchesMetadataFilter(metadata, filters));
    }

    [Fact]
    public void MatchesMetadataFilter_CollectionOfNumbers_NormalizesLikeScalars()
    {
        // Stored side deserialized from JSON (JsonElement number) vs .NET int list on the filter side.
        using var doc = System.Text.Json.JsonDocument.Parse("""{"chunk_index": 2}""");
        var metadata = new Dictionary<string, object>
        {
            ["chunk_index"] = doc.RootElement.GetProperty("chunk_index").Clone()
        };
        var filters = new Dictionary<string, object> { ["chunk_index"] = new[] { 1, 2 } };

        Assert.True(VectorStoreBase.MatchesMetadataFilter(metadata, filters));
    }

    [Fact]
    public void ExpandFilterValue_Scalar_YieldsSingleAlternative()
    {
        Assert.Equal(["v1"], VectorStoreBase.ExpandFilterValue("k", "v1"));
        Assert.Equal(["42"], VectorStoreBase.ExpandFilterValue("k", 42));
        Assert.Equal(["true"], VectorStoreBase.ExpandFilterValue("k", true));
        Assert.Equal(new string?[] { null }, VectorStoreBase.ExpandFilterValue("k", null));
    }

    [Fact]
    public void ExpandFilterValue_Collection_YieldsAllAlternatives()
    {
        Assert.Equal(
            ["a", "b"],
            VectorStoreBase.ExpandFilterValue("k", new List<string> { "a", "b" }));
        Assert.Equal(
            ["1", "2"],
            VectorStoreBase.ExpandFilterValue("k", new[] { 1, 2 }));
    }

    [Fact]
    public void ExpandFilterValue_EmptyCollection_Throws()
    {
        // Previously silently un-matchable (zero results, no signal) — now fail-loud.
        var ex = Assert.Throws<ArgumentException>(
            () => VectorStoreBase.ExpandFilterValue("k", new List<string>()));
        Assert.Contains("empty collection", ex.Message);
    }

    [Fact]
    public void ExpandFilterValue_ArbitraryObject_Throws()
    {
        // Previously degraded to ToString() type name and matched nothing.
        Assert.Throws<ArgumentException>(
            () => VectorStoreBase.ExpandFilterValue("k", new object()));
        Assert.Throws<ArgumentException>(
            () => VectorStoreBase.ExpandFilterValue("k", new Dictionary<string, string>()));
    }

    [Fact]
    public void ExpandFilterValue_NestedCollection_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => VectorStoreBase.ExpandFilterValue("k", new List<object> { new List<string> { "a" } }));

        using var doc = System.Text.Json.JsonDocument.Parse("""[["nested"]]""");
        Assert.Throws<ArgumentException>(
            () => VectorStoreBase.ExpandFilterValue("k", doc.RootElement.Clone()));
    }

    [Fact]
    public void ExpandFilterValue_JsonObject_Throws()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{"nested": 1}""");
        Assert.Throws<ArgumentException>(
            () => VectorStoreBase.ExpandFilterValue("k", doc.RootElement.Clone()));
    }

    [Fact]
    public async Task SearchAsync_CollectionFilter_RestrictsToAnyOfValues()
    {
        // End-to-end through the base backstop: documentId ∈ {doc-1, doc-3}.
        // StoreAsync prepares metadata (fills documentId from DocumentId); the test store has no
        // native pushdown, so this exercises the backstop's MatchAny path.
        foreach (var docId in new[] { "doc-1", "doc-2", "doc-3" })
        {
            var chunk = CreateChunk(docId, $"content of {docId}");
            chunk.Embedding = [0.1f, 0.2f, 0.3f];
            await _store.StoreAsync(chunk, TestContext.Current.CancellationToken);
        }

        var results = await _store.SearchAsync([0.1f, 0.2f, 0.3f], topK: 10, minScore: 0.0f, filters: new Dictionary<string, object>
            {
                [MetadataHelper.StandardKeys.DocumentId] = new List<string> { "doc-1", "doc-3" }
            }, cancellationToken: TestContext.Current.CancellationToken);

        var ids = results.Select(r => r.DocumentId).OrderBy(x => x).ToList();
        Assert.Equal(["doc-1", "doc-3"], ids);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_ExistingId_ReturnsTrue()
    {
        // Arrange
        var chunk = CreateChunk("doc-1", "Test content");
        var id = await _store.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Act
        var deleted = await _store.DeleteAsync(id, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(deleted);
    }

    [Fact]
    public async Task DeleteAsync_EmptyId_ReturnsFalse()
    {
        // Act
        var deleted = await _store.DeleteAsync("", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(deleted);
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_ExistingChunk_SetsUpdatedTimestamp()
    {
        // Arrange
        var chunk = CreateChunk("doc-1", "Test content");
        var id = await _store.StoreAsync(chunk, TestContext.Current.CancellationToken);
        chunk.Id = id;
        chunk.Content = "Updated content";

        // Act
        var updated = await _store.UpdateAsync(chunk, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(updated);
        Assert.True(chunk.Metadata!.ContainsKey(MetadataHelper.StandardKeys.UpdatedAt));
    }

    [Fact]
    public async Task UpdateAsync_NullChunk_ReturnsFalse()
    {
        // Act
        var updated = await _store.UpdateAsync(null!, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(updated);
    }

    [Fact]
    public async Task UpdateAsync_EmptyId_ReturnsFalse()
    {
        // Arrange
        var chunk = CreateChunk("doc-1", "Test content");
        chunk.Id = "";

        // Act
        var updated = await _store.UpdateAsync(chunk, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(updated);
    }

    #endregion

    #region ExistsAsync Tests

    [Fact]
    public async Task ExistsAsync_ExistingId_ReturnsTrue()
    {
        // Arrange
        var chunk = CreateChunk("doc-1", "Test content");
        var id = await _store.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Act
        var exists = await _store.ExistsAsync(id, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_EmptyId_ReturnsFalse()
    {
        // Act
        var exists = await _store.ExistsAsync("", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(exists);
    }

    #endregion

    #region Count/Clear Tests

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount()
    {
        // Arrange
        await _store.StoreAsync(CreateChunk("doc-1", "Content 1"), TestContext.Current.CancellationToken);
        await _store.StoreAsync(CreateChunk("doc-1", "Content 2"), TestContext.Current.CancellationToken);

        // Act
        var count = await _store.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetCountAsync_DelegatesToCountAsync()
    {
        // Arrange
        await _store.StoreAsync(CreateChunk("doc-1", "Content 1"), TestContext.Current.CancellationToken);

        // Act
        var count = await _store.GetCountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllChunks()
    {
        // Arrange
        await _store.StoreAsync(CreateChunk("doc-1", "Content 1"), TestContext.Current.CancellationToken);
        await _store.StoreAsync(CreateChunk("doc-2", "Content 2"), TestContext.Current.CancellationToken);

        // Act
        await _store.ClearAsync(TestContext.Current.CancellationToken);
        var count = await _store.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, count);
    }

    #endregion

    #region GetByDocumentIdAsync Tests

    [Fact]
    public async Task GetByDocumentIdAsync_ReturnsChunksForDocument()
    {
        // Arrange
        await _store.StoreAsync(CreateChunk("doc-1", "Content 1"), TestContext.Current.CancellationToken);
        await _store.StoreAsync(CreateChunk("doc-1", "Content 2"), TestContext.Current.CancellationToken);
        await _store.StoreAsync(CreateChunk("doc-2", "Content 3"), TestContext.Current.CancellationToken);

        // Act
        var chunks = (await _store.GetByDocumentIdAsync("doc-1", TestContext.Current.CancellationToken)).ToList();

        // Assert
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c => Assert.Equal("doc-1", c.DocumentId));
    }

    [Fact]
    public async Task GetByDocumentIdAsync_EmptyId_ReturnsEmpty()
    {
        // Act
        var chunks = await _store.GetByDocumentIdAsync("", TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(chunks);
    }

    #endregion

    #region Helper Methods

    private static DocumentChunk CreateChunk(string documentId, string content, int chunkIndex = 0)
    {
        return new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = documentId,
            Content = content,
            ChunkIndex = chunkIndex
        };
    }

    private static DocumentChunk CreateChunkWithEmbedding(string id, float[] embedding)
    {
        return new DocumentChunk
        {
            Id = id,
            DocumentId = "doc",
            Content = "content",
            Embedding = embedding
        };
    }

    #endregion

    #region Test Implementation

    /// <summary>
    /// Concrete test implementation of VectorStoreBase for testing.
    /// Uses in-memory storage.
    /// </summary>
    private class TestVectorStore : VectorStoreBase
    {
        private readonly Dictionary<string, DocumentChunk> _storage = new();

        public bool StoreCoreWasCalled { get; private set; }

        protected override Task<string> StoreCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
        {
            StoreCoreWasCalled = true;
            var id = chunk.Id ?? Guid.NewGuid().ToString();
            chunk.Id = id;
            _storage[id] = chunk;
            return Task.FromResult(id);
        }

        protected override Task<DocumentChunk?> GetCoreAsync(string id, CancellationToken cancellationToken)
        {
            _storage.TryGetValue(id, out var chunk);
            return Task.FromResult(chunk);
        }

        /// <summary>
        /// Filters received by the last SearchCoreAsync call (null when none were passed).
        /// </summary>
        public Dictionary<string, object>? LastSearchFilters { get; private set; }

        /// <summary>
        /// Simulates a store WITHOUT native filter pushdown: records the filters but does not
        /// apply them, so tests can verify the base-class backstop ordering.
        /// </summary>
        protected override Task<IEnumerable<VectorSearchResult>> SearchCoreAsync(
            float[] queryEmbedding, int topK, Dictionary<string, object>? filters, CancellationToken cancellationToken)
        {
            LastSearchFilters = filters;

            var results = _storage.Values
                .Where(c => c.Embedding != null)
                .Select(c => new VectorSearchResult(c, ComputeCosineSimilarity(queryEmbedding, c.Embedding)))
                .OrderByDescending(r => r.Score)
                .Take(topK * 2); // Get more to allow filtering

            return Task.FromResult(results);
        }

        protected override Task<bool> DeleteCoreAsync(string id, CancellationToken cancellationToken)
        {
            return Task.FromResult(_storage.Remove(id));
        }

        protected override Task<bool> UpdateCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
        {
            if (!_storage.ContainsKey(chunk.Id))
                return Task.FromResult(false);

            _storage[chunk.Id] = chunk;
            return Task.FromResult(true);
        }

        protected override Task<IEnumerable<DocumentChunk>> GetByDocumentIdCoreAsync(
            string documentId, CancellationToken cancellationToken)
        {
            var chunks = _storage.Values.Where(c => c.DocumentId == documentId);
            return Task.FromResult(chunks);
        }

        protected override Task<bool> DeleteByDocumentIdCoreAsync(
            string documentId, CancellationToken cancellationToken)
        {
            var toRemove = _storage.Where(kvp => kvp.Value.DocumentId == documentId).Select(kvp => kvp.Key).ToList();
            foreach (var key in toRemove)
                _storage.Remove(key);
            return Task.FromResult(toRemove.Any());
        }

        protected override Task<int> CountCoreAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_storage.Count);
        }

        protected override Task ClearCoreAsync(CancellationToken cancellationToken)
        {
            _storage.Clear();
            return Task.CompletedTask;
        }
    }

    #endregion
}
