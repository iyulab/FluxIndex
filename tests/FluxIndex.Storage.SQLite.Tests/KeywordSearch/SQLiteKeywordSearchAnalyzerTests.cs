using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests.KeywordSearch;

/// <summary>
/// The analyzer seam on the relational keyword index (FluxIndex docket #28, part 2): the analyzer a
/// consumer supplies is the one that defines terms on <em>both</em> the index path and the query
/// path, the default is the previous behaviour, and the container's <c>ITextAnalyzer</c> reaches the
/// registered backend.
/// </summary>
public class SQLiteKeywordSearchAnalyzerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fluxindex-analyzer-{Guid.NewGuid():N}.db");

    private SQLiteKeywordSearchService Create(ITextAnalyzer? analyzer) =>
        new($"Data Source={_dbPath}", NullLogger<SQLiteKeywordSearchService>.Instance, analyzer);

    private static DocumentChunk Chunk(string id, string content) => DocumentChunk.Create(id, content, 0, 1);

    [Fact]
    public async Task Default_BareKoreanStem_DoesNotMatchItsInflectedForm()
    {
        // The measured defect: with the default analyzer 규정에 and 규정 are unrelated terms.
        using var service = Create(analyzer: null);
        await service.IndexChunksAsync([Chunk("d1", "이 규정에 따라 처리한다")], TestContext.Current.CancellationToken);

        var hits = await service.SearchAsync("규정", cancellationToken: TestContext.Current.CancellationToken);

        hits.Should().BeEmpty("DefaultTextAnalyzer keeps the previous behaviour — this is the gap the seam exists for");
    }

    [Fact]
    public async Task CjkBigram_BareKoreanStem_MatchesItsInflectedForm()
    {
        using var service = Create(CjkBigramTextAnalyzer.Instance);
        await service.IndexChunksAsync([
            Chunk("d1", "이 규정에 따라 처리한다"),
            Chunk("d2", "예산 집행 지침"),
        ], TestContext.Current.CancellationToken);

        var hits = await service.SearchAsync("규정", cancellationToken: TestContext.Current.CancellationToken);

        hits.Should().ContainSingle().Which.Chunk.DocumentId.Should().Be("d1");
    }

    [Fact]
    public async Task TheSameAnalyzerInstance_ServesIndexingAndQuerying()
    {
        // If the two paths could use different analyzers, a consumer could index with one and
        // query with another and never learn why nothing matches. Prove it is one instance.
        var analyzer = Substitute.For<ITextAnalyzer>();
        analyzer.Tokenize(Arg.Any<string>()).Returns(ci => DefaultTextAnalyzer.Instance.Tokenize(ci.Arg<string>()));
        using var service = Create(analyzer);

        await service.IndexChunksAsync([Chunk("d1", "transaction boundaries")], TestContext.Current.CancellationToken);
        await service.SearchAsync("transaction", cancellationToken: TestContext.Current.CancellationToken);

        analyzer.Received().Tokenize("transaction boundaries");
        analyzer.Received().Tokenize("transaction");
    }

    [Fact]
    public void RegisteredAnalyzer_ReachesTheBackendThroughTheContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITextAnalyzer>(CjkBigramTextAnalyzer.Instance);
        services.AddSingleton<SQLiteKeywordSearchService>(sp => new SQLiteKeywordSearchService(
            $"Data Source={_dbPath}",
            sp.GetRequiredService<ILogger<SQLiteKeywordSearchService>>(),
            sp.GetService<ITextAnalyzer>()));
        using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<SQLiteKeywordSearchService>();

        service.Tokenize("규정에").Should().Equal(["규정", "정에"], "the container's analyzer, not the default, defines the terms");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"));
        try { File.Delete(_dbPath); } catch (IOException) { }
    }
}
