using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Contract;

/// <summary>
/// Shared contract for the field dimension of the relational keyword index: a metadata value such as a title or a file name is a scored field, so a query term that
/// appears only there still retrieves the chunk; the set of fields is configuration that the index
/// path and the query path share; and an index with no field postings ranks exactly as it did before
/// fields existed. Derive a concrete class per relational backend and implement
/// <see cref="CreateServiceAsync"/>.
/// </summary>
public abstract class KeywordSearchFieldContractSuite
{
    /// <summary>Creates a fresh, empty keyword index configured with the given fields (default when null).</summary>
    protected abstract Task<IKeywordSearchService> CreateServiceAsync(KeywordFieldOptions? fields);

    /// <summary>
    /// Opens a second service over the store the last <see cref="CreateServiceAsync"/> call created, without
    /// clearing it, as a host restarted under a different configuration would.
    /// </summary>
    protected abstract Task<IKeywordSearchService> ReopenAsync(KeywordFieldOptions? fields);

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
    public async Task ADocumentIdFilter_MatchesTheChunksDocument_EvenWithoutAMetadataCopy()
    {
        // The hybrid search hands its filter to both legs. The vector stores resolve document_id to
        // the chunk's own DocumentId; the keyword leg must too, or a chunk indexed without a metadata
        // copy of its document id is invisible to every document-scoped keyword search.
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync(fields: null);

        await service.IndexChunksAsync([
            ChunkOf("doc-a", "a-body", "budget in the body", ("file_name", "a.txt")),
            ChunkOf("doc-b", "b-title", "body of b", ("title", "budget review")),
            ChunkOf("doc-c", "c-body", "budget outside the scope"),
        ], ct);

        var hits = await service.SearchAsync(
            "budget",
            new KeywordSearchOptions
            {
                MetadataFilter = new Dictionary<string, object> { [FilterKeys.DocumentId] = new[] { "doc-a", "doc-b" } }
            },
            ct);

        Assert.Equal(["a-body", "b-title"], hits.Select(h => h.Chunk.Id).Order());
    }

    [Fact]
    public async Task ADocumentIdFilter_IgnoresAMetadataEntryOfTheSameName()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync(fields: null);

        await service.IndexChunksAsync([
            ChunkOf("doc-a", "stale-copy", "budget text", (FilterKeys.DocumentId, "doc-b")),
        ], ct);

        var inB = await service.SearchAsync(
            "budget",
            new KeywordSearchOptions { MetadataFilter = new Dictionary<string, object> { [FilterKeys.DocumentId] = "doc-b" } },
            ct);
        var inA = await service.SearchAsync(
            "budget",
            new KeywordSearchOptions { MetadataFilter = new Dictionary<string, object> { [FilterKeys.DocumentId] = "doc-a" } },
            ct);

        Assert.Empty(inB);
        Assert.Equal("stale-copy", Assert.Single(inA).Chunk.Id);
    }

    [Fact]
    public async Task DeleteByFilter_OnDocumentId_RemovesTheDocumentsChunks_EvenWithoutAMetadataCopy()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync(fields: null);

        await service.IndexChunksAsync([
            ChunkOf("doc-a", "a1", "budget one"),
            ChunkOf("doc-a", "a2", "budget two", ("tenant", "t")),
            ChunkOf("doc-b", "b1", "budget three"),
        ], ct);

        var deleted = await service.DeleteByFilterAsync(
            new Dictionary<string, object> { [FilterKeys.DocumentId] = "doc-a" }, ct);

        Assert.Equal(2, deleted);
        Assert.Equal("b1", Assert.Single(await service.SearchAsync("budget", cancellationToken: ct)).Chunk.Id);
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

    public static TheoryData<string> FieldConfigurations => ["default", "none", "title"];

    private static KeywordFieldOptions? FieldsFor(string configuration) => configuration switch
    {
        "none" => KeywordFieldOptions.None,
        "title" => new KeywordFieldOptions { Fields = [new KeywordField("title")] },
        _ => null,
    };

    [Theory]
    [MemberData(nameof(FieldConfigurations))]
    public async Task MaintainedDocumentFrequency_EqualsAFullRecount_AfterEveryKindOfWrite(string configuration)
    {
        // Document frequency is moved by what each write deleted and wrote. After every step below the
        // maintained value of every term ever written must equal what a recount from the posting rows
        // produces (OptimizeIndexAsync), whatever fields are configured.
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync(FieldsFor(configuration));
        var vocabulary = new HashSet<string>(StringComparer.Ordinal);

        DocumentChunk Track(DocumentChunk chunk)
        {
            vocabulary.UnionWith(service.Tokenize(chunk.Content));
            foreach (var value in chunk.Metadata?.Values.AsEnumerable() ?? [])
                vocabulary.UnionWith(service.Tokenize(value?.ToString() ?? string.Empty));
            return chunk;
        }

        async Task AssertMatchesRecountAsync(string step)
        {
            var maintained = await service.GetDocumentFrequenciesAsync(vocabulary, ct);
            await service.OptimizeIndexAsync(ct);
            var recounted = await service.GetDocumentFrequenciesAsync(vocabulary, ct);
            Assert.True(
                recounted.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(maintained.OrderBy(p => p.Key, StringComparer.Ordinal)),
                $"after {step}: " + string.Join(", ", recounted
                    .Where(p => maintained[p.Key] != p.Value)
                    .Select(p => $"{p.Key} maintained {maintained[p.Key]} recounted {p.Value}")));
        }

        // A batch: body and field terms overlapping, the same chunk id twice (the later write wins), and a chunk
        // whose body has no terms, so its title is never indexed.
        await service.IndexChunksAsync([
            Track(ChunkOf("doc-a", "a1", "river delta survey", ("title", "river survey"), ("file_name", "delta.txt"))),
            Track(ChunkOf("doc-a", "a2", "harbor river crane")),
            Track(ChunkOf("doc-a", "a2", "harbor dredge", ("title", "crane log"))),
            Track(ChunkOf("doc-b", "b1", "crane river ledger", ("title", "ledger"))),
            Track(ChunkOf("doc-b", "b2", "silt ledger survey", ("file_name", "silt.md"))),
            Track(ChunkOf("doc-b", "b3", "ledger survey", ("title", "survey notes"))),
            Track(ChunkOf("doc-c", "c1", "--- !!!", ("title", "phantom"))),
            Track(ChunkOf("doc-c", "c2", "phantom river")),
        ], ct);
        await AssertMatchesRecountAsync("the first batch");

        // Replace one chunk: some terms kept, some dropped, some new, one moving from the title to the body.
        await service.IndexChunkAsync(Track(ChunkOf("doc-a", "a1", "river survey estuary", ("title", "delta estuary"))), ct);
        await AssertMatchesRecountAsync("a single-chunk replace");

        // Re-index a chunk to content with no terms: every term it held loses it.
        await service.IndexChunkAsync(Track(ChunkOf("doc-b", "b2", "... ???", ("title", "silt"))), ct);
        await AssertMatchesRecountAsync("a re-index to no terms");

        // Replace a document: one chunk rewritten, one stale chunk removed, one new chunk.
        await service.ReplaceDocumentsAsync(["doc-a"], [
            Track(ChunkOf("doc-a", "a1", "estuary dredge", ("title", "river"))),
            Track(ChunkOf("doc-a", "a3", "harbor crane river", ("file_name", "harbor.pdf"))),
        ], ct);
        await AssertMatchesRecountAsync("a document replace");

        // Move a document under new ids with a changed title.
        vocabulary.UnionWith(service.Tokenize("ledger archive"));
        await service.ReassignDocumentAsync(
            "doc-b",
            "doc-d",
            new Dictionary<string, string> { ["b1"] = "d1", ["b2"] = "d2", ["b3"] = "d3" },
            new Dictionary<string, object?> { ["title"] = "ledger archive" },
            ct);
        await AssertMatchesRecountAsync("a reassignment");

        await service.DeleteChunkAsync("a3", ct);
        await service.DeleteByDocumentIdAsync("doc-c", ct);
        await AssertMatchesRecountAsync("deletions");

        // The control: concrete counts, so a store that maintained and recounted nothing could not pass.
        // a1 holds "river" in its title only and d1 in its body; "archive" is only ever in the titles of d1 and d3.
        var final = await service.GetDocumentFrequenciesAsync(["river", "crane", "ledger", "archive", "phantom"], ct);
        var titled = configuration != "none";
        Assert.Equal(titled ? 2 : 1, final["river"]);
        Assert.Equal(1, final["crane"]);
        Assert.Equal(2, final["ledger"]);
        Assert.Equal(titled ? 2 : 0, final["archive"]);
        Assert.Equal(0, final["phantom"]);
    }

    [Fact]
    public async Task OpeningUnderADifferentFieldSet_RecountsEveryTerm_AcrossManyTermIdRanges()
    {
        // The first open under a different field set recounts every term. The recount walks term ids in ranges
        // (so no single statement grows with the table); enough terms to span several ranges on every backend,
        // with terms whose count changes and terms that disappear interleaved in id order, so a range edge that
        // skipped or repeated a row would show here.
        var ct = TestContext.Current.CancellationToken;
        const int Codes = 5_500;
        var shared = new List<string>(Codes);
        var titleOnly = new List<string>(Codes);
        for (var i = 0; i < Codes; i++)
        {
            var code = string.Concat(Enumerable.Range(0, 4).Select(p => (char)('a' + i / (int)Math.Pow(26, 3 - p) % 26)));
            shared.Add(code + "q");
            titleOnly.Add(code + "x");
        }

        // Titles are split over many chunks: one title holding every term would be a single filterable metadata
        // value of ~66 KB, which is a different limit than the one under test.
        var fielded = await CreateServiceAsync(fields: null);
        await fielded.IndexChunksAsync([
            Chunk("body", string.Join(' ', shared)),
            .. shared.Zip(titleOnly, (s, t) => $"{s} {t}").Chunk(50).Select((pairs, k) =>
                Chunk($"titled-{k}", "plain", ("title", string.Join(' ', pairs)))),
        ], ct);
        var before = await fielded.GetDocumentFrequenciesAsync([shared[0], shared[^1], titleOnly[0], titleOnly[^1]], ct);
        Assert.Equal([2, 2, 1, 1], new[] { before[shared[0]], before[shared[^1]], before[titleOnly[0]], before[titleOnly[^1]] });

        var bodyOnly = await ReopenAsync(KeywordFieldOptions.None);
        var after = await bodyOnly.GetDocumentFrequenciesAsync([.. shared, .. titleOnly], ct);

        Assert.Empty(shared.Where(term => after[term] != 1));
        Assert.Empty(titleOnly.Where(term => after[term] != 0));
        Assert.Equal((Codes + 49) / 50, (await bodyOnly.GetDocumentFrequenciesAsync(["plain"], ct))["plain"]);
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
