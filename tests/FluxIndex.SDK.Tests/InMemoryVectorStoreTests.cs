using FluentAssertions;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.SDK.Services;
using Xunit;

namespace FluxIndex.SDK.Tests;

public class InMemoryVectorStoreTests
{
    private static DocumentChunk CreateChunk(string documentId, string workspaceId, float[] embedding, int chunkIndex = 0)
    {
        return new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = documentId,
            ChunkIndex = chunkIndex,
            Content = $"content {documentId}#{chunkIndex}",
            Embedding = embedding,
            Metadata = new Dictionary<string, object> { ["workspace_id"] = workspaceId }
        };
    }

    [Fact]
    public async Task DeleteByFilterAsync_RemovesOnlyMatchingChunks()
    {
        // Arrange
        var store = new InMemoryVectorStore();
        await store.StoreAsync(CreateChunk("other-doc", "ws-other", [1f, 0f], 0));
        await store.StoreAsync(CreateChunk("other-doc", "ws-other", [0f, 1f], 1));
        await store.StoreAsync(CreateChunk("target-doc", "ws-target", [1f, 1f]));

        // Act
        var deleted = await store.DeleteByFilterAsync(
            new Dictionary<string, object> { ["workspace_id"] = "ws-other" });

        // Assert
        deleted.Should().Be(2);
        (await store.CountAsync()).Should().Be(1);
        var remaining = await store.GetByDocumentIdAsync("target-doc");
        remaining.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteByFilterAsync_NoMatch_ReturnsZero()
    {
        var store = new InMemoryVectorStore();
        await store.StoreAsync(CreateChunk("doc-1", "ws-1", [1f, 0f]));

        var deleted = await store.DeleteByFilterAsync(
            new Dictionary<string, object> { ["workspace_id"] = "ws-absent" });

        deleted.Should().Be(0);
        (await store.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeleteByFilterAsync_EmptyFilter_Throws()
    {
        var store = new InMemoryVectorStore();

        var act = () => store.DeleteByFilterAsync(new Dictionary<string, object>());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SearchAsync_MetadataFilter_AppliedBeforeTrim()
    {
        // Arrange — other-tenant chunks dominate similarity; with topK=1 the target only
        // survives if the filter is applied before the internal candidate trim.
        var store = new InMemoryVectorStore();
        for (var i = 0; i < 4; i++)
            await store.StoreAsync(CreateChunk($"other-{i}", "ws-other", [1f, 0.01f * i], i));
        await store.StoreAsync(CreateChunk("target-doc", "ws-target", [0f, 1f]));

        // Act
        var results = (await store.SearchAsync(
            [1f, 0f], topK: 1, minScore: -1f,
            filters: new Dictionary<string, object> { ["workspace_id"] = "ws-target" })).ToList();

        // Assert
        results.Should().ContainSingle();
        results[0].DocumentId.Should().Be("target-doc");
    }
}
