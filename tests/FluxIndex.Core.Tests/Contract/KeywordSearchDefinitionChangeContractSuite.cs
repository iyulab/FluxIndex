using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Contract;

/// <summary>
/// Shared contract for how the relational keyword index treats its own definition: it records which analyzer and
/// field set built its terms, and opened with a different one it re-tokenizes its stored chunks before first use, so
/// stored terms and query terms come from the same definition. Opened with the same one it leaves the terms alone; a
/// store that records no analyzer (written by an earlier release) is rebuilt once. Derive a concrete class per
/// relational backend.
/// </summary>
public abstract class KeywordSearchDefinitionChangeContractSuite
{
    /// <summary>Creates a fresh, empty keyword index with the given analyzer and fields (default fields when null).</summary>
    protected abstract Task<IKeywordSearchService> CreateServiceAsync(ITextAnalyzer? analyzer, KeywordFieldOptions? fields = null);

    /// <summary>
    /// Opens a second service over the store the last <see cref="CreateServiceAsync"/> call created, without clearing
    /// it, as a host restarted under a different configuration would.
    /// </summary>
    protected abstract Task<IKeywordSearchService> ReopenAsync(ITextAnalyzer analyzer, KeywordFieldOptions? fields = null);

    /// <summary>
    /// Removes the analyzer the last store recorded, so it looks like an index written by a release that did not
    /// record one.
    /// </summary>
    protected abstract Task ForgetRecordedAnalyzerAsync();

    private static DocumentChunk Chunk(string id, string content, params (string Key, object Value)[] metadata)
    {
        var chunk = DocumentChunk.Create($"doc-{id}", content, 0, 1);
        chunk.Id = id;
        if (metadata.Length > 0)
            chunk.Metadata = metadata.ToDictionary(m => m.Key, m => m.Value, StringComparer.Ordinal);
        return chunk;
    }

    private static DocumentChunk[] KoreanCorpus() =>
    [
        Chunk("1", "개인정보 보호 규정에 따라 처리한다", ("title", "보안 지침")),
        Chunk("2", "이 규정을 위반하면 징계한다", ("title", "인사 규정")),
        Chunk("3", "회의록은 매주 공유한다", ("title", "운영 안내")),
    ];

    private static async Task<List<(string Id, double Score)>> RankAsync(IKeywordSearchService service, string query, CancellationToken ct)
        => (await service.SearchAsync(query, cancellationToken: ct)).Select(r => (r.Chunk.Id, Math.Round(r.Score, 9))).ToList();

    [Fact]
    public async Task ReopenedWithAnotherAnalyzer_ReTokenizes_AndRanksLikeAFreshIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        var built = await CreateServiceAsync(WordTextAnalyzer.Instance);
        await built.IndexChunksAsync(KoreanCorpus(), ct);
        // The default analyzer keeps «개인정보» whole, so «정보» (inside the word, not its prefix) matches nothing.
        Assert.Empty(await RankAsync(built, "정보", ct));

        var reopened = await RankAsync(await ReopenAsync(CjkBigramTextAnalyzer.Instance), "정보 규정", ct);

        var fresh = await CreateServiceAsync(CjkBigramTextAnalyzer.Instance);
        await fresh.IndexChunksAsync(KoreanCorpus(), ct);
        var expected = await RankAsync(fresh, "정보 규정", ct);

        Assert.Equal(["1", "2"], reopened.Select(r => r.Id));
        // The rebuilt terms, document frequencies and field postings equal those of an index built with the new analyzer.
        Assert.Equal(expected, reopened);
    }

    [Fact]
    public async Task ReopenedWithTheSameAnalyzer_DoesNotReTokenize()
    {
        var ct = TestContext.Current.CancellationToken;
        var built = await CreateServiceAsync(CjkBigramTextAnalyzer.Instance);
        await built.IndexChunksAsync(KoreanCorpus(), ct);

        var analyzer = new RecordingAnalyzer(CjkBigramTextAnalyzer.Instance);
        await (await ReopenAsync(analyzer)).SearchAsync("규정", cancellationToken: ct);

        Assert.Equal(["규정"], analyzer.Inputs);
    }

    [Fact]
    public async Task ReopenedWithAnAnalyzerOfTheSameTypeButAnotherIdentity_ReTokenizes()
    {
        // The identity, not the type, decides: a configurable analyzer states its configuration in it.
        var ct = TestContext.Current.CancellationToken;
        var built = await CreateServiceAsync(new RecordingAnalyzer(WordTextAnalyzer.Instance, "custom/1"));
        await built.IndexChunksAsync(KoreanCorpus(), ct);

        var analyzer = new RecordingAnalyzer(WordTextAnalyzer.Instance, "custom/2");
        await (await ReopenAsync(analyzer)).SearchAsync("회의록은", cancellationToken: ct);

        foreach (var chunk in KoreanCorpus())
            Assert.Contains(chunk.Content, analyzer.Inputs);
    }

    [Fact]
    public async Task AStoreThatRecordsNoAnalyzer_IsRebuiltOnce()
    {
        // An index written before analyzers were recorded, under the whole-word analyzer that was the default then.
        // Nothing says which analyzer built it, so it is rebuilt with the current one - once.
        var ct = TestContext.Current.CancellationToken;
        var built = await CreateServiceAsync(WordTextAnalyzer.Instance);
        await built.IndexChunksAsync(KoreanCorpus(), ct);
        await ForgetRecordedAnalyzerAsync();

        var upgraded = await ReopenAsync(CjkBigramTextAnalyzer.Instance);
        Assert.Equal(["1"], (await RankAsync(upgraded, "정보", ct)).Select(r => r.Id));

        // Recorded now: the next open with the same analyzer analyzes only the query.
        var analyzer = new RecordingAnalyzer(CjkBigramTextAnalyzer.Instance);
        await (await ReopenAsync(analyzer)).SearchAsync("규정", cancellationToken: ct);
        Assert.Equal(["규정"], analyzer.Inputs);
    }

    [Fact]
    public async Task WithNoAnalyzerRegistered_TheIndexUsesCjkBigrams()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync(analyzer: null);
        await service.IndexChunksAsync(KoreanCorpus(), ct);

        Assert.Equal(["1"], (await RankAsync(service, "정보", ct)).Select(r => r.Id));
    }

    [Fact]
    public async Task ReopenedWithAnotherFieldSet_IndexesTheNewFieldForStoredChunks()
    {
        var ct = TestContext.Current.CancellationToken;
        var built = await CreateServiceAsync(CjkBigramTextAnalyzer.Instance, KeywordFieldOptions.None);
        await built.IndexChunksAsync(KoreanCorpus(), ct);
        // «지침» is only in chunk 1's title, which a body-only index does not score.
        Assert.Empty(await RankAsync(built, "지침", ct));

        var reopened = await ReopenAsync(CjkBigramTextAnalyzer.Instance, new KeywordFieldOptions());

        Assert.Equal(["1"], (await RankAsync(reopened, "지침", ct)).Select(r => r.Id));
    }

    [Fact]
    public async Task Rebuild_KeepsMetadataFiltersWorking()
    {
        var ct = TestContext.Current.CancellationToken;
        var built = await CreateServiceAsync(WordTextAnalyzer.Instance);
        await built.IndexChunksAsync(
            [Chunk("a", "보안규정을 지킨다", ("team", "legal")), Chunk("b", "보안규정에 따른다", ("team", "ops"))], ct);

        var reopened = await ReopenAsync(CjkBigramTextAnalyzer.Instance);
        var hits = await reopened.SearchAsync(
            "규정",
            new KeywordSearchOptions { MetadataFilter = new Dictionary<string, object> { ["team"] = "ops" } },
            ct);

        Assert.Equal(["b"], hits.Select(h => h.Chunk.Id));
    }

    /// <summary>Delegates to another analyzer under a chosen identity and records every text it was asked to analyze.</summary>
    private sealed class RecordingAnalyzer(ITextAnalyzer inner, string? identity = null) : ITextAnalyzer
    {
        private readonly List<string> _inputs = [];

        public IReadOnlyList<string> Inputs
        {
            get { lock (_inputs) return [.. _inputs]; }
        }

        public string Identity { get; } = identity ?? inner.Identity;

        public IEnumerable<string> Tokenize(string text)
        {
            lock (_inputs) _inputs.Add(text);
            return inner.Tokenize(text).ToList();
        }
    }
}
