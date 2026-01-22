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
        var id = await _store.StoreAsync(chunk);

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
        await _store.StoreAsync(chunk);

        // Assert
        Assert.NotNull(chunk.Metadata);
        Assert.True(chunk.Metadata.ContainsKey(MetadataHelper.StandardKeys.DocumentId));
        Assert.True(chunk.Metadata.ContainsKey(MetadataHelper.StandardKeys.ChunkIndex));
    }

    [Fact]
    public async Task StoreAsync_NullChunk_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.StoreAsync(null!));
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
        var ids = (await _store.StoreBatchAsync(chunks)).ToList();

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
        var id = await _store.StoreAsync(chunk);

        // Act
        var retrieved = await _store.GetAsync(id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(chunk.Content, retrieved.Content);
    }

    [Fact]
    public async Task GetAsync_EmptyId_ReturnsNull()
    {
        // Act
        var result = await _store.GetAsync("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_DelegatesToGetAsync()
    {
        // Arrange
        var chunk = CreateChunk("doc-1", "Test content");
        var id = await _store.StoreAsync(chunk);

        // Act
        var retrieved = await _store.GetByIdAsync(id);

        // Assert
        Assert.NotNull(retrieved);
    }

    #endregion

    #region SearchAsync Tests

    [Fact]
    public async Task SearchAsync_NullEmbedding_ReturnsEmpty()
    {
        // Act
        var results = await _store.SearchAsync(null!, topK: 10);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_EmptyEmbedding_ReturnsEmpty()
    {
        // Act
        var results = await _store.SearchAsync([], topK: 10);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ZeroTopK_ReturnsEmpty()
    {
        // Arrange
        var embedding = new float[] { 1, 2, 3 };

        // Act
        var results = await _store.SearchAsync(embedding, topK: 0);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ValidQuery_FiltersAndSortsResults()
    {
        // Arrange
        await _store.StoreAsync(CreateChunkWithEmbedding("chunk-1", new[] { 0.9f, 0.1f }));
        await _store.StoreAsync(CreateChunkWithEmbedding("chunk-2", new[] { 0.5f, 0.5f }));
        await _store.StoreAsync(CreateChunkWithEmbedding("chunk-3", new[] { 0.1f, 0.9f }));

        // Act
        var results = (await _store.SearchAsync(new[] { 0.9f, 0.1f }, topK: 10, minScore: 0.5f)).ToList();

        // Assert
        Assert.NotEmpty(results);
        // Results should be sorted by similarity (descending)
        if (results.Count > 1)
        {
            var scores = results.Select(r => r.Score ?? 0).ToList();
            Assert.True(scores.SequenceEqual(scores.OrderByDescending(s => s)));
        }
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_ExistingId_ReturnsTrue()
    {
        // Arrange
        var chunk = CreateChunk("doc-1", "Test content");
        var id = await _store.StoreAsync(chunk);

        // Act
        var deleted = await _store.DeleteAsync(id);

        // Assert
        Assert.True(deleted);
    }

    [Fact]
    public async Task DeleteAsync_EmptyId_ReturnsFalse()
    {
        // Act
        var deleted = await _store.DeleteAsync("");

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
        var id = await _store.StoreAsync(chunk);
        chunk.Id = id;
        chunk.Content = "Updated content";

        // Act
        var updated = await _store.UpdateAsync(chunk);

        // Assert
        Assert.True(updated);
        Assert.True(chunk.Metadata!.ContainsKey(MetadataHelper.StandardKeys.UpdatedAt));
    }

    [Fact]
    public async Task UpdateAsync_NullChunk_ReturnsFalse()
    {
        // Act
        var updated = await _store.UpdateAsync(null!);

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
        var updated = await _store.UpdateAsync(chunk);

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
        var id = await _store.StoreAsync(chunk);

        // Act
        var exists = await _store.ExistsAsync(id);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_EmptyId_ReturnsFalse()
    {
        // Act
        var exists = await _store.ExistsAsync("");

        // Assert
        Assert.False(exists);
    }

    #endregion

    #region Count/Clear Tests

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount()
    {
        // Arrange
        await _store.StoreAsync(CreateChunk("doc-1", "Content 1"));
        await _store.StoreAsync(CreateChunk("doc-1", "Content 2"));

        // Act
        var count = await _store.CountAsync();

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetCountAsync_DelegatesToCountAsync()
    {
        // Arrange
        await _store.StoreAsync(CreateChunk("doc-1", "Content 1"));

        // Act
        var count = await _store.GetCountAsync();

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllChunks()
    {
        // Arrange
        await _store.StoreAsync(CreateChunk("doc-1", "Content 1"));
        await _store.StoreAsync(CreateChunk("doc-2", "Content 2"));

        // Act
        await _store.ClearAsync();
        var count = await _store.CountAsync();

        // Assert
        Assert.Equal(0, count);
    }

    #endregion

    #region GetByDocumentIdAsync Tests

    [Fact]
    public async Task GetByDocumentIdAsync_ReturnsChunksForDocument()
    {
        // Arrange
        await _store.StoreAsync(CreateChunk("doc-1", "Content 1"));
        await _store.StoreAsync(CreateChunk("doc-1", "Content 2"));
        await _store.StoreAsync(CreateChunk("doc-2", "Content 3"));

        // Act
        var chunks = (await _store.GetByDocumentIdAsync("doc-1")).ToList();

        // Assert
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c => Assert.Equal("doc-1", c.DocumentId));
    }

    [Fact]
    public async Task GetByDocumentIdAsync_EmptyId_ReturnsEmpty()
    {
        // Act
        var chunks = await _store.GetByDocumentIdAsync("");

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

        protected override Task<IEnumerable<VectorSearchResult>> SearchCoreAsync(
            float[] queryEmbedding, int topK, CancellationToken cancellationToken)
        {
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
