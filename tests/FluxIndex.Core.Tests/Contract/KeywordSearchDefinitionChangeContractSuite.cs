using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
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
    /// it, as a host restarted under a different configuration would. Logs to <paramref name="logger"/> when given.
    /// </summary>
    protected abstract Task<IKeywordSearchService> ReopenAsync(ITextAnalyzer analyzer, KeywordFieldOptions? fields = null, ILogger? logger = null);

    /// <summary>
    /// Removes the analyzer the last store recorded, so it looks like an index written by a release that did not
    /// record one.
    /// </summary>
    protected abstract Task ForgetRecordedAnalyzerAsync();

    /// <summary>
    /// Writes a row (value 1) of the last store's statistics relation under <paramref name="key"/>, as a host stopped
    /// part-way through a rebuild would have left it.
    /// </summary>
    protected abstract Task WriteStatisticAsync(string key);

    /// <summary>The rebuild marker key for an analyzer under the default field set.</summary>
    private static string RebuildTarget(string analyzerIdentity) => $"rebuild_target:{analyzerIdentity}|file_name,title";

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

        var service = await ReopenAsync(CjkBigramTextAnalyzer.Instance);
        await service.WaitForMaintenanceAsync(ct);
        var reopened = await RankAsync(service, "정보 규정", ct);

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
        await (await ReopenAsync(analyzer)).WaitForMaintenanceAsync(ct);

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
        await upgraded.WaitForMaintenanceAsync(ct);
        Assert.Equal(["1"], (await RankAsync(upgraded, "정보", ct)).Select(r => r.Id));

        // Recorded now: the next open with the same analyzer analyzes only the query.
        var analyzer = new RecordingAnalyzer(CjkBigramTextAnalyzer.Instance);
        await (await ReopenAsync(analyzer)).SearchAsync("규정", cancellationToken: ct);
        Assert.Equal(["규정"], analyzer.Inputs);
    }

    [Fact]
    public async Task AStoreThatRecordsNoAnalyzer_ButHoldsTheConfiguredAnalyzersTerms_IsRecordedWithoutReTokenizing()
    {
        // Written by an earlier release under the analyzer the host still registers: a sample of its chunks already holds
        // exactly the terms that analyzer produces, so re-tokenizing it would rewrite the same rows.
        var ct = TestContext.Current.CancellationToken;
        var built = await CreateServiceAsync(CjkBigramTextAnalyzer.Instance);
        await built.IndexChunksAsync(KoreanCorpus(), ct);
        await ForgetRecordedAnalyzerAsync();

        var log = new ListLogger();
        var reopened = await ReopenAsync(CjkBigramTextAnalyzer.Instance, logger: log);
        Assert.Equal(["1"], (await RankAsync(reopened, "정보", ct)).Select(r => r.Id));

        Assert.DoesNotContain(log.Messages, m => m.Level >= LogLevel.Warning);
        Assert.Contains(log.Messages, m => m.Text.Contains("3 sampled chunks hold exactly the terms", StringComparison.Ordinal));

        // Recorded: the next open analyzes only the query.
        var analyzer = new RecordingAnalyzer(CjkBigramTextAnalyzer.Instance);
        await (await ReopenAsync(analyzer)).SearchAsync("규정", cancellationToken: ct);
        Assert.Equal(["규정"], analyzer.Inputs);
    }

    [Fact]
    public async Task AStoreThatRecordsNoAnalyzer_WithOneChunkFromAnotherAnalyzer_IsRebuilt()
    {
        // A host that changed its analyzer before analyzers were recorded left a store of mixed terms. Under one identity,
        // chunk 1 is written whole-word and chunks 2-3 as bigrams; the sample finds chunk 1 and the store is rebuilt.
        var ct = TestContext.Current.CancellationToken;
        var corpus = KoreanCorpus();
        var wholeWord = await CreateServiceAsync(new RecordingAnalyzer(WordTextAnalyzer.Instance, "legacy/1"));
        await wholeWord.IndexChunksAsync([corpus[0]], ct);
        var bigrams = await ReopenAsync(new RecordingAnalyzer(CjkBigramTextAnalyzer.Instance, "legacy/1"));
        await bigrams.IndexChunksAsync([corpus[1], corpus[2]], ct);
        await ForgetRecordedAnalyzerAsync();

        var log = new ListLogger();
        var reopened = await ReopenAsync(CjkBigramTextAnalyzer.Instance, logger: log);
        await reopened.WaitForMaintenanceAsync(ct);

        Assert.Equal(["1"], (await RankAsync(reopened, "정보", ct)).Select(r => r.Id));
        Assert.Contains(log.Messages, m => m.Level == LogLevel.Warning && m.Text.Contains("sampled chunk 1 differs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SpellingsThatNormalizeToOneTerm_CountTogether()
    {
        // An analyzer that keeps case emits «Index» and «index» as two tokens; they are one term, held twice.
        var ct = TestContext.Current.CancellationToken;
        DocumentChunk[] corpus = [Chunk("a", "Index index lookup"), Chunk("b", "index table schema")];

        var keepsCase = await CreateServiceAsync(new SplittingAnalyzer("split/1"), KeywordFieldOptions.None);
        await keepsCase.IndexChunksAsync(corpus, ct);
        var cased = await RankAsync(keepsCase, "index", ct);

        var lower = await CreateServiceAsync(new SplittingAnalyzer("split/1", lowerCase: true), KeywordFieldOptions.None);
        await lower.IndexChunksAsync(corpus, ct);

        Assert.Equal(await RankAsync(lower, "index", ct), cased);
        Assert.Equal("a", cased[0].Id);
    }

    [Fact]
    public async Task Opening_StartsTheRebuildInTheBackground_AndReportsItsProgress()
    {
        var ct = TestContext.Current.CancellationToken;
        var corpus = KoreanCorpus();
        var built = await CreateServiceAsync(WordTextAnalyzer.Instance);
        await built.IndexChunksAsync(corpus, ct);

        // The rebuild is held inside its first batch; opening and searching must not wait for it.
        var gate = new GatedAnalyzer(CjkBigramTextAnalyzer.Instance, blockOn: corpus[0].Content);
        try
        {
            var reopened = await ReopenAsync(gate);
            await reopened.GetStatisticsAsync(ct);
            await gate.Reached.WaitAsync(TimeSpan.FromSeconds(30), ct);

            var running = (await reopened.GetStatisticsAsync(ct)).Rebuild;
            Assert.NotNull(running);
            Assert.Equal(KeywordIndexRebuildStatus.Running, running.Status);
            Assert.Equal(3, running.Total);
            Assert.Equal(0, running.Completed);
            Assert.Contains("fluxindex.word/1", running.Change, StringComparison.Ordinal);

            // Searches run meanwhile, against the index as it stands: still whole words, so «정보» matches nothing.
            Assert.Empty(await RankAsync(reopened, "정보", ct));

            gate.Release();
            await reopened.WaitForMaintenanceAsync(ct);

            Assert.Null((await reopened.GetStatisticsAsync(ct)).Rebuild);
            Assert.Equal(["1"], (await RankAsync(reopened, "정보", ct)).Select(r => r.Id));
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task AnInterruptedRebuild_ContinuesAfterItsLastCommittedBatch()
    {
        // A host stopped part-way through rebuilding toward bigrams: its last committed batch ended at chunk 1.
        var ct = TestContext.Current.CancellationToken;
        var corpus = KoreanCorpus();
        var built = await CreateServiceAsync(WordTextAnalyzer.Instance);
        await built.IndexChunksAsync(corpus, ct);
        await ForgetRecordedAnalyzerAsync();
        await WriteStatisticAsync(RebuildTarget(CjkBigramTextAnalyzer.Instance.Identity));
        await WriteStatisticAsync("rebuild_after:1");

        var analyzer = new RecordingAnalyzer(CjkBigramTextAnalyzer.Instance);
        await (await ReopenAsync(analyzer)).WaitForMaintenanceAsync(ct);

        Assert.DoesNotContain(corpus[0].Content, analyzer.Inputs);
        Assert.Contains(corpus[1].Content, analyzer.Inputs);
        Assert.Contains(corpus[2].Content, analyzer.Inputs);

        // Finished and recorded: the next open analyzes only the query.
        var next = new RecordingAnalyzer(CjkBigramTextAnalyzer.Instance);
        await (await ReopenAsync(next)).SearchAsync("규정", cancellationToken: ct);
        Assert.Equal(["규정"], next.Inputs);
    }

    [Fact]
    public async Task ARebuildRecordedTowardAnotherDefinition_IsDropped_AndTheStoreJudgedAfresh()
    {
        var ct = TestContext.Current.CancellationToken;
        var corpus = KoreanCorpus();
        var built = await CreateServiceAsync(WordTextAnalyzer.Instance);
        await built.IndexChunksAsync(corpus, ct);
        await ForgetRecordedAnalyzerAsync();
        await WriteStatisticAsync(RebuildTarget("other/1"));
        await WriteStatisticAsync("rebuild_after:1");

        var analyzer = new RecordingAnalyzer(CjkBigramTextAnalyzer.Instance);
        var reopened = await ReopenAsync(analyzer);
        await reopened.WaitForMaintenanceAsync(ct);

        // From the first chunk, not after the other definition's cursor.
        Assert.Contains(corpus[0].Content, analyzer.Inputs);
        Assert.Equal(["1"], (await RankAsync(reopened, "정보", ct)).Select(r => r.Id));
    }

    [Fact]
    public async Task AChunkRewrittenOrDeletedDuringTheRebuild_EndsAsTheWriterLeftIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var corpus = KoreanCorpus();
        var built = await CreateServiceAsync(WordTextAnalyzer.Instance);
        await built.IndexChunksAsync(corpus, ct);

        var gate = new GatedAnalyzer(CjkBigramTextAnalyzer.Instance, blockOn: corpus[0].Content);
        try
        {
            var rebuilding = await ReopenAsync(gate);
            await rebuilding.GetStatisticsAsync(ct);
            await gate.Reached.WaitAsync(TimeSpan.FromSeconds(30), ct);

            // Another instance of the host writes while the rebuild holds the batch those chunks are in.
            var writer = await ReopenAsync(CjkBigramTextAnalyzer.Instance);
            // On threads of their own: a driver whose async calls run synchronously would otherwise hold this one in its busy wait.
            var rewrite = Task.Run(() => writer.IndexChunksAsync([Chunk("2", "새로운 공지로 교체한다", ("title", "인사 규정"))], ct), ct);
            var delete = Task.Run(() => writer.DeleteChunkAsync("3", ct), ct);
            await Task.Delay(300, ct);

            gate.Release();
            await Task.WhenAll(rewrite, delete);
            await rebuilding.WaitForMaintenanceAsync(ct);
            await writer.WaitForMaintenanceAsync(ct);

            Assert.Equal(["2"], (await RankAsync(writer, "공지", ct)).Select(r => r.Id));
            Assert.Empty(await RankAsync(writer, "징계", ct));
            Assert.Empty(await RankAsync(writer, "회의록", ct));
            Assert.Equal(["1"], (await RankAsync(writer, "정보", ct)).Select(r => r.Id));
        }
        finally
        {
            gate.Release();
        }
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
        await reopened.WaitForMaintenanceAsync(ct);

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
        await reopened.WaitForMaintenanceAsync(ct);
        var hits = await reopened.SearchAsync(
            "규정",
            new KeywordSearchOptions { MetadataFilter = new Dictionary<string, object> { ["team"] = "ops" } },
            ct);

        Assert.Equal(["b"], hits.Select(h => h.Chunk.Id));
    }

    /// <summary>
    /// Delegates to another analyzer, and holds the first call that analyzes <paramref name="blockOn"/> until released -
    /// a rebuild caught inside its batch.
    /// </summary>
    private sealed class GatedAnalyzer(ITextAnalyzer inner, string blockOn) : ITextAnalyzer
    {
        private readonly ManualResetEventSlim _open = new(false);
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when the held text is being analyzed.</summary>
        public Task Reached => _reached.Task;

        public string Identity => inner.Identity;

        public void Release() => _open.Set();

        public IEnumerable<string> Tokenize(string text)
        {
            if (string.Equals(text, blockOn, StringComparison.Ordinal))
            {
                _reached.TrySetResult();
                _open.Wait(TimeSpan.FromSeconds(60));
            }

            return inner.Tokenize(text).ToList();
        }
    }

    /// <summary>Splits on spaces, keeping case unless told otherwise.</summary>
    private sealed class SplittingAnalyzer(string identity, bool lowerCase = false) : ITextAnalyzer
    {
        public string Identity { get; } = identity;

        public IEnumerable<string> Tokenize(string text)
            => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => lowerCase ? t.ToLowerInvariant() : t);
    }

    /// <summary>Keeps every message logged at Information or above.</summary>
    private sealed class ListLogger : ILogger
    {
        private readonly List<(LogLevel Level, string Text)> _messages = [];

        public IReadOnlyList<(LogLevel Level, string Text)> Messages
        {
            get { lock (_messages) return [.. _messages]; }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                lock (_messages) _messages.Add((logLevel, formatter(state, exception)));
            }
        }
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
