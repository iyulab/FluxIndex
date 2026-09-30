using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Contract;

/// <summary>
/// Shared contract suite for <see cref="IKeywordSearchService.ReassignDocumentAsync"/> — the keyword-index side of
/// moving a document to a new id: chunks are found under the new document and ids, not the old ones; metadata updates
/// reach the filter dimension; rejected calls leave the index as it was. Derive a concrete class per index.
/// </summary>
public abstract class KeywordSearchReassignContractSuite
{
    private const string OldDocument = "doc-old";
    private const string NewDocument = "doc-new";
    private const string OtherDocument = "doc-other";

    /// <summary>Creates a fresh, empty keyword index.</summary>
    protected abstract Task<IKeywordSearchService> CreateServiceAsync();

    /// <summary>
    /// Whether the index scores metadata fields (<c>file_name</c> by default) — true for the relational indexes; the
    /// in-memory index scores the body only.
    /// </summary>
    protected virtual bool ScoresFileNameField => false;

    private static readonly Dictionary<string, string> Map = new()
    {
        ["old-a"] = "new-a",
        ["old-b"] = "new-b",
    };

    private static DocumentChunk Chunk(string id, string documentId, string content, int chunkIndex, string fileName) => new()
    {
        Id = id,
        DocumentId = documentId,
        ChunkIndex = chunkIndex,
        Content = content,
        TokenCount = 3,
        Metadata = new Dictionary<string, object>
        {
            ["file_name"] = fileName,
            ["origin"] = "contract",
            ["obsolete"] = "remove me"
        }
    };

    private static async Task<IKeywordSearchService> Seed(IKeywordSearchService service)
    {
        await service.IndexChunksAsync(
        [
            Chunk("old-a", OldDocument, "quartz lantern harbor", 0, "voyage.txt"),
            Chunk("old-b", OldDocument, "quartz meadow", 1, "voyage.txt"),
            Chunk("other-a", OtherDocument, "quartz orchard", 0, "orchard.txt"),
        ], TestContext.Current.CancellationToken);
        return service;
    }

    private static async Task<List<string>> Search(IKeywordSearchService service, string query, KeywordSearchOptions options) =>
        (await service.SearchAsync(query, options, TestContext.Current.CancellationToken))
            .Select(r => r.Chunk.Id).Order(StringComparer.Ordinal).ToList();

    private static async Task<List<string>> IdsOf(IKeywordSearchService service, string documentId) =>
        (await service.GetChunkIdsByDocumentIdAsync(documentId, TestContext.Current.CancellationToken))
            .Order(StringComparer.Ordinal).ToList();

    [Fact]
    public async Task Reassign_MovesTheChunks_SoTheNewScopeFindsThemAndTheOldScopeDoesNot()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await Seed(await CreateServiceAsync());
        var before = await service.GetStatisticsAsync(ct);

        var moved = await service.ReassignDocumentAsync(OldDocument, NewDocument, Map, cancellationToken: ct);

        Assert.Equal(2, moved);
        Assert.Equal(["new-a", "new-b"], await Search(service, "quartz", new KeywordSearchOptions { DocumentIdFilter = NewDocument }));
        Assert.Empty(await Search(service, "quartz", new KeywordSearchOptions { DocumentIdFilter = OldDocument }));
        Assert.Equal(
            ["new-a", "new-b"],
            await Search(service, "quartz", new KeywordSearchOptions
            {
                MetadataFilter = new Dictionary<string, object> { [FilterKeys.DocumentId] = NewDocument }
            }));
        Assert.Equal(["new-a", "new-b"], await IdsOf(service, NewDocument));
        Assert.Empty(await IdsOf(service, OldDocument));
        Assert.Equal(["other-a"], await IdsOf(service, OtherDocument));

        var hit = Assert.Single(await service.SearchAsync("lantern", cancellationToken: ct));
        Assert.Equal("new-a", hit.Chunk.Id);
        Assert.Equal(NewDocument, hit.Chunk.DocumentId);
        Assert.Equal("quartz lantern harbor", hit.Chunk.Content);

        var after = await service.GetStatisticsAsync(ct);
        Assert.Equal(before.TotalDocuments, after.TotalDocuments);
    }

    [Fact]
    public async Task Reassign_AppliesMetadataUpdates_ToTheFilterDimension()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await Seed(await CreateServiceAsync());

        await service.ReassignDocumentAsync(
            OldDocument,
            NewDocument,
            Map,
            new Dictionary<string, object?> { ["file_name"] = "renamed.txt", ["obsolete"] = null },
            ct);

        Assert.Equal(
            ["new-a", "new-b"],
            await Search(service, "quartz", new KeywordSearchOptions
            {
                MetadataFilter = new Dictionary<string, object> { ["file_name"] = "renamed.txt" }
            }));
        Assert.Empty(await Search(service, "quartz", new KeywordSearchOptions
        {
            MetadataFilter = new Dictionary<string, object> { ["file_name"] = "voyage.txt" }
        }));
        Assert.Empty(await Search(service, "quartz", new KeywordSearchOptions
        {
            MetadataFilter = new Dictionary<string, object> { ["obsolete"] = "remove me" },
            DocumentIdFilter = NewDocument
        }));

        var hit = Assert.Single(await service.SearchAsync("lantern", cancellationToken: ct));
        Assert.NotNull(hit.Chunk.Metadata);
        Assert.Equal("renamed.txt", hit.Chunk.Metadata["file_name"]?.ToString());
        Assert.Equal("contract", hit.Chunk.Metadata["origin"]?.ToString());
        Assert.False(hit.Chunk.Metadata.ContainsKey("obsolete"));
    }

    [Fact]
    public async Task Reassign_AScoredFileNameField_FollowsTheUpdate()
    {
        Assert.SkipUnless(ScoresFileNameField, "This index scores the body only.");
        var ct = TestContext.Current.CancellationToken;
        var service = await Seed(await CreateServiceAsync());

        await service.ReassignDocumentAsync(
            OldDocument, NewDocument, Map, new Dictionary<string, object?> { ["file_name"] = "itinerary.txt" }, ct);

        // Terms that only ever appeared in the file name: the new one matches, the old one no longer does.
        Assert.Equal(["new-a", "new-b"], await Search(service, "itinerary", new KeywordSearchOptions()));
        Assert.Empty(await Search(service, "voyage", new KeywordSearchOptions()));
    }

    [Fact]
    public async Task Reassign_AMissingMapEntry_Throws_AndChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await Seed(await CreateServiceAsync());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.ReassignDocumentAsync(
            OldDocument, NewDocument, new Dictionary<string, string> { ["old-a"] = "new-a" }, cancellationToken: ct));

        Assert.Contains("old-b", ex.Message, StringComparison.Ordinal);
        Assert.Equal(["old-a", "old-b"], await IdsOf(service, OldDocument));
        Assert.Empty(await IdsOf(service, NewDocument));
        Assert.Equal(["old-a", "old-b"], await Search(service, "quartz", new KeywordSearchOptions { DocumentIdFilter = OldDocument }));
    }

    [Fact]
    public async Task Reassign_ToADocumentThatHasChunks_Throws_AndChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await Seed(await CreateServiceAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReassignDocumentAsync(OldDocument, OtherDocument, Map, cancellationToken: ct));

        Assert.Equal(["old-a", "old-b"], await IdsOf(service, OldDocument));
        Assert.Equal(["other-a"], await IdsOf(service, OtherDocument));
    }

    [Fact]
    public async Task Reassign_AnUnknownDocument_MovesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await Seed(await CreateServiceAsync());

        Assert.Equal(0, await service.ReassignDocumentAsync("doc-missing", NewDocument, Map, cancellationToken: ct));
        Assert.Empty(await IdsOf(service, NewDocument));
    }
}
