using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests.KeywordSearch;

/// <summary>
/// Runs the shared keyword-field contract suite against the SQLite keyword index, plus the facts that
/// need the backend itself: an index created before the field relation existed opens and ranks as
/// before, the consumer's analyzer is the one that tokenizes field text, and a registered
/// <see cref="KeywordFieldOptions"/> reaches the backend through the container.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteKeywordSearchFieldTests : KeywordSearchFieldContractSuite, IDisposable
{
    private readonly List<string> _paths = [];

    private string NewPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluxindex-kw-fields-{Guid.NewGuid():N}.db");
        _paths.Add(path);
        return path;
    }

    private SQLiteKeywordSearchService Create(string path, KeywordFieldOptions? fields, ITextAnalyzer? analyzer = null) =>
        new($"Data Source={path}", NullLogger<SQLiteKeywordSearchService>.Instance, analyzer, fields);

    protected override Task<IKeywordSearchService> CreateServiceAsync(KeywordFieldOptions? fields)
        => Task.FromResult<IKeywordSearchService>(Create(NewPath(), fields));

    private static DocumentChunk Chunk(string id, string content, params (string Key, object Value)[] metadata)
    {
        var chunk = DocumentChunk.Create("doc-1", content, 0, 1);
        chunk.Id = id;
        if (metadata.Length > 0)
            chunk.Metadata = metadata.ToDictionary(m => m.Key, m => m.Value, StringComparer.Ordinal);
        return chunk;
    }

    [Fact]
    public async Task AnIndexBuiltBeforeFieldsExisted_OpensAndRanksAsBefore()
    {
        // Simulate the pre-field schema: index a corpus, then drop the field relation the way a
        // database from an earlier version simply would not have it. A new service must add the
        // relation back (IF NOT EXISTS), not fail, and score the untouched body postings exactly as
        // the previous version did.
        var ct = TestContext.Current.CancellationToken;
        var path = NewPath();
        DocumentChunk[] corpus =
        [
            Chunk("1", "alpha beta gamma alpha"),
            Chunk("2", "beta gamma delta"),
            Chunk("3", "alpha delta epsilon zeta eta"),
        ];

        IReadOnlyList<KeywordSearchResult> before;
        using (var first = Create(path, KeywordFieldOptions.None))
        {
            await first.IndexChunksAsync(corpus, ct);
            before = await first.SearchAsync("alpha delta", cancellationToken: ct);
        }

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync(ct);
            await using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE bm25_field_postings; DELETE FROM bm25_statistics WHERE key LIKE 'avg_field_length:%';";
            await drop.ExecuteNonQueryAsync(ct);
        }

        using var second = Create(path, fields: null);
        var after = await second.SearchAsync("alpha delta", cancellationToken: ct);

        after.Select(r => r.Chunk.Id).Should().Equal(before.Select(r => r.Chunk.Id));
        after.Select(r => r.Score).Should().Equal(before.Select(r => r.Score));

        // And the relation is back: a re-indexed chunk with a title is found through it.
        await second.IndexChunksAsync([Chunk("4", "body", ("title", "kappa"))], ct);
        (await second.SearchAsync("kappa", cancellationToken: ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task TheConsumersAnalyzer_TokenizesFieldTextToo()
    {
        var analyzer = Substitute.For<ITextAnalyzer>();
        analyzer.Tokenize(Arg.Any<string>()).Returns(ci => DefaultTextAnalyzer.Instance.Tokenize(ci.Arg<string>()));
        using var service = Create(NewPath(), fields: null, analyzer);

        await service.IndexChunksAsync([Chunk("c", "transaction boundaries", ("title", "ledger design"))], TestContext.Current.CancellationToken);

        analyzer.Received().Tokenize("transaction boundaries");
        analyzer.Received().Tokenize("ledger design");
    }

    [Fact]
    public async Task CjkBigram_MatchesAnInflectedTitleFromItsBareStem()
    {
        using var service = Create(NewPath(), fields: null, CjkBigramTextAnalyzer.Instance);

        await service.IndexChunksAsync([Chunk("c", "본문에는 다른 말만 있다", ("title", "규정집 2026"))], TestContext.Current.CancellationToken);

        (await service.SearchAsync("규정", cancellationToken: TestContext.Current.CancellationToken))
            .Should().ContainSingle().Which.Chunk.Id.Should().Be("c");
    }

    [Fact]
    public async Task AFieldWithSeveralValues_IndexesAllOfThem()
    {
        using var service = Create(NewPath(), new KeywordFieldOptions { Fields = [new KeywordField("tags")] });

        await service.IndexChunksAsync([Chunk("c", "body", ("tags", new[] { "alpha", "omega" }))], TestContext.Current.CancellationToken);

        (await service.SearchAsync("omega", cancellationToken: TestContext.Current.CancellationToken)).Should().ContainSingle();
    }

    [Fact]
    public void RegisteredFieldOptions_ReachTheBackendThroughTheContainer()
    {
        var path = NewPath();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new KeywordFieldOptions { Fields = [new KeywordField("subject")] });
        services.AddSingleton<SQLiteKeywordSearchService>(sp => new SQLiteKeywordSearchService(
            $"Data Source={path}",
            sp.GetRequiredService<ILogger<SQLiteKeywordSearchService>>(),
            sp.GetService<ITextAnalyzer>(),
            sp.GetService<KeywordFieldOptions>()));

        using var provider = services.BuildServiceProvider();
        using var service = provider.GetRequiredService<SQLiteKeywordSearchService>();

        var ct = TestContext.Current.CancellationToken;
        service.IndexChunksAsync([Chunk("c", "body", ("subject", "lambda"), ("title", "mu"))], ct).GetAwaiter().GetResult();
        service.SearchAsync("lambda", cancellationToken: ct).GetAwaiter().GetResult().Should().ContainSingle();
        service.SearchAsync("mu", cancellationToken: ct).GetAwaiter().GetResult().Should().BeEmpty("title is not in the registered field set");
    }

    [Fact]
    public async Task DocumentFrequency_CountsOnlyTheConfiguredFields_AfterTheConfigurationShrinks()
    {
        // Two chunks hold "zeta": one in its title only, one in its body. With the title field on,
        // df is 2. Re-indexing the body chunk under a body-only configuration must recompute df from
        // the rows that configuration reads — 1 — not from every field row still in the table.
        var ct = TestContext.Current.CancellationToken;
        var path = NewPath();

        using (var fielded = Create(path, fields: null))
        {
            await fielded.IndexChunksAsync([
                Chunk("in-title", "body without the word", ("title", "zeta")),
                Chunk("in-body", "zeta appears in the body"),
            ], ct);
            fielded.GetIDF("zeta").Should().BeApproximately(Math.Log(1 + (2 - 2 + 0.5) / (2 + 0.5)), 1e-9, "both chunks count while the title field is on");
        }

        using var bodyOnly = Create(path, KeywordFieldOptions.None);
        await bodyOnly.IndexChunksAsync([Chunk("in-body", "zeta appears in the body")], ct);

        bodyOnly.GetIDF("zeta").Should().BeApproximately(Math.Log(1 + (2 - 1 + 0.5) / (1 + 0.5)), 1e-9,
            "the title-only chunk no longer counts once title is not a configured field");
    }

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
