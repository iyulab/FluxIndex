using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Contract;

/// <summary>
/// Shared contract for the field dimension of the relational keyword index (FluxIndex docket #28,
/// part 1): a metadata value such as a title or a file name is a scored field, so a query term that
/// appears only there still retrieves the chunk; the set of fields is configuration that the index
/// path and the query path share; and an index with no field postings ranks exactly as it did before
/// fields existed. Derive a concrete class per relational backend and implement
/// <see cref="CreateServiceAsync"/>.
/// </summary>
public abstract class KeywordSearchFieldContractSuite
{
    /// <summary>Creates a fresh, empty keyword index configured with the given fields (default when null).</summary>
    protected abstract Task<IKeywordSearchService> CreateServiceAsync(KeywordFieldOptions? fields);

    private static DocumentChunk Chunk(string id, string content, params (string Key, object Value)[] metadata)
        => ChunkOf("doc-1", id, content, metadata);

    private static DocumentChunk ChunkOf(string documentId, string id, string content, params (string Key, object Value)[] metadata)
    {
        var chunk = DocumentChunk.Create(documentId, content, 0, 1);
        chunk.Id = id;
        if (metadata.Length > 0)
            chunk.Metadata = metadata.ToDictionary(m => m.Key, m => m.Value, StringComparer.Ordinal);
        return chunk;
    }

    [Fact]
    public async Task ATermThatAppearsOnlyInTheTitle_RetrievesTheChunk_WithTheDefaultFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync(fields: null);

        await service.IndexChunksAsync([
            Chunk("titled", "the quarterly numbers are attached below", ("title", "procurement guideline")),
            Chunk("plain", "another chunk about numbers"),
        ], ct);

        var hits = await service.SearchAsync("procurement", cancellationToken: ct);

        var hit = Assert.Single(hits);
        Assert.Equal("titled", hit.Chunk.Id);
        Assert.Contains("procurement", hit.MatchedTerms);
    }

    [Fact]
    public async Task ATermThatAppearsOnlyInTheFileName_RetrievesTheChunk_WithTheDefaultFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync(fields: null);

        await service.IndexChunksAsync([
            Chunk("named", "body text without the name", ("file_name", "ZS11-HA.txt")),
        ], ct);

        var hits = await service.SearchAsync("ZS11-HA", cancellationToken: ct);

        Assert.Equal("named", Assert.Single(hits).Chunk.Id);
    }

    [Fact]
    public async Task ATermThatAppearsOnlyInTheTitle_IsNotRetrieved_WithNoFields()
    {
        // The control: the same chunk, the same query, body-only configuration. If this passed too,
        // the fact above would be proving nothing about fields.
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync(KeywordFieldOptions.None);

        await service.IndexChunksAsync([
            Chunk("titled", "the quarterly numbers are attached below", ("title", "procurement guideline")),
        ], ct);

        var hits = await service.SearchAsync("procurement", cancellationToken: ct);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task AFieldNotConfigured_IsNotScored()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync(new KeywordFieldOptions { Fields = [new KeywordField("title")] });

        await service.IndexChunksAsync([
            Chunk("c", "body", ("title", "alpha report"), ("file_name", "omega.pdf")),
        ], ct);

        Assert.Single(await service.SearchAsync("alpha", cancellationToken: ct));
        Assert.Empty(await service.SearchAsync("omega", cancellationToken: ct));
    }

    [Fact]
    public async Task ADocumentScopedQuery_DoesNotSurfaceATitleHitFromAnotherDocument()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync(fields: null);

        await service.IndexChunksAsync([
            ChunkOf("doc-a", "a", "body of a", ("title", "budget plan")),
            ChunkOf("doc-b", "b", "body of b", ("title", "budget review")),
        ], ct);

        var hits = await service.SearchAsync("budget", new KeywordSearchOptions { DocumentIdFilter = "doc-b" }, ct);

        Assert.Equal("b", Assert.Single(hits).Chunk.Id);
    }

    [Fact]
    public async Task AMetadataFilteredQuery_AppliesToFieldHitsToo()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync(fields: null);

        await service.IndexChunksAsync([
            Chunk("a", "body of a", ("title", "budget plan"), ("tenant", "one")),
            Chunk("b", "body of b", ("title", "budget review"), ("tenant", "two")),
        ], ct);

        var hits = await service.SearchAsync(
            "budget",
            new KeywordSearchOptions { MetadataFilter = new Dictionary<string, object> { ["tenant"] = "two" } },
            ct);

        Assert.Equal("b", Assert.Single(hits).Chunk.Id);
    }

    [Fact]
    public async Task ATermInTheTitleAndTheBody_CountsAsOneDocumentForIdf()
    {
        // Two chunks; the term is in one of them, in both its title and its body. Document frequency
        // must be 1, not 2 — a sum over posting rows would count the chunk twice and halve the IDF.
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync(fields: null);

        await service.IndexChunksAsync([
            Chunk("both", "the tender opens on monday", ("title", "tender notice")),
            Chunk("other", "an unrelated chunk"),
        ], ct);

        var expectedIdf = Math.Log(1 + (2 - 1 + 0.5) / (1 + 0.5));
        Assert.Equal(expectedIdf, service.GetIDF("tender"), precision: 9);
    }

    [Fact]
    public async Task ReindexingWithoutTheTitle_DropsTheFieldHit()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync(fields: null);

        await service.IndexChunksAsync([Chunk("c", "body only", ("title", "vanishing"))], ct);
        Assert.Single(await service.SearchAsync("vanishing", cancellationToken: ct));

        await service.IndexChunksAsync([Chunk("c", "body only")], ct);

        Assert.Empty(await service.SearchAsync("vanishing", cancellationToken: ct));
    }

    [Fact]
    public async Task DeletingTheChunk_DropsTheFieldHit()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync(fields: null);

        await service.IndexChunksAsync([Chunk("c", "body only", ("title", "vanishing"))], ct);
        await service.DeleteChunkAsync("c", ct);

        Assert.Empty(await service.SearchAsync("vanishing", cancellationToken: ct));
        Assert.Equal(0, (await service.GetStatisticsAsync(ct)).TotalDocuments);
    }

    [Fact]
    public async Task ABodyOnlyCorpus_ScoresExactlyAsWithNoFields()
    {
        // Chunks without the field keys produce no field postings, so the scoring must take the
        // body-only path and reproduce the previous ranking bit for bit — this is the promise that an
        // index built before fields existed keeps ranking as it did.
        var ct = TestContext.Current.CancellationToken;
        DocumentChunk[] corpus =
        [
            Chunk("1", "alpha beta gamma alpha"),
            Chunk("2", "beta gamma delta"),
            Chunk("3", "alpha delta epsilon zeta eta"),
        ];

        var withFields = await CreateServiceAsync(fields: null);
        await withFields.IndexChunksAsync(corpus, ct);
        var fielded = await withFields.SearchAsync("alpha delta", cancellationToken: ct);

        var bodyOnly = await CreateServiceAsync(KeywordFieldOptions.None);
        await bodyOnly.IndexChunksAsync(corpus, ct);
        var plain = await bodyOnly.SearchAsync("alpha delta", cancellationToken: ct);

        Assert.Equal(plain.Select(r => r.Chunk.Id), fielded.Select(r => r.Chunk.Id));
        Assert.Equal(plain.Select(r => r.Score), fielded.Select(r => r.Score));
    }

    [Fact]
    public async Task AHeavierFieldWeight_RanksTheTitleMatchAboveABodyMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync(new KeywordFieldOptions { Fields = [new KeywordField("title", Weight: 5.0)] });

        await service.IndexChunksAsync([
            Chunk("in-body", "the audit found the audit trail complete and the audit closed"),
            Chunk("in-title", "a long paragraph that talks about something else entirely for a while", ("title", "audit")),
        ], ct);

        var hits = await service.SearchAsync("audit", cancellationToken: ct);

        Assert.Equal(["in-title", "in-body"], hits.Select(h => h.Chunk.Id));
    }
}
