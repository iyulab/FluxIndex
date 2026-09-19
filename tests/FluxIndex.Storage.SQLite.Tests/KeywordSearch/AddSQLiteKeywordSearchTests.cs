using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests.KeywordSearch;

/// <summary>
/// <c>AddSQLiteKeywordSearch</c> for a container the builder does not assemble. What it has to get right is what a
/// hand-written registration forgets: the index goes in the vector store's database, and a registered analyzer
/// reaches it.
/// </summary>
[Collection("SQLite Tests")]
public sealed class AddSQLiteKeywordSearchTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fluxindex-kw-reg-{Guid.NewGuid():N}.db");

    private static DocumentChunk Chunk(string id, string content)
    {
        var chunk = DocumentChunk.Create("doc-" + id, content, 0, 1);
        chunk.Id = id;
        return chunk;
    }

    [Fact]
    public async Task WithoutAConnectionString_TheIndexGoesInTheRegisteredVectorStoresDatabase()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSQLiteVecVectorStore(o => o.DatabasePath = _path);
        services.AddSQLiteKeywordSearch();
        await using var provider = services.BuildServiceProvider();

        var keyword = provider.GetRequiredService<IKeywordSearchService>();
        keyword.Should().BeOfType<SQLiteKeywordSearchService>();
        await keyword.IndexChunkAsync(Chunk("1", "alpha beta"), TestContext.Current.CancellationToken);

        await using var connection = new SqliteConnection($"Data Source={_path}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM bm25_postings";
        Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(2);
    }

    [Fact]
    public async Task ARegisteredTextAnalyzer_ReachesTheIndex()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITextAnalyzer, CjkBigramTextAnalyzer>();
        services.AddSQLiteKeywordSearch($"Data Source={_path}");
        await using var provider = services.BuildServiceProvider();
        var keyword = provider.GetRequiredService<IKeywordSearchService>();
        var ct = TestContext.Current.CancellationToken;

        await keyword.IndexChunkAsync(Chunk("1", "월세공제 신청은 연말정산 기간에 한다"), ct);

        (await keyword.SearchAsync("월세", cancellationToken: ct)).Should().ContainSingle(
            "only the bigram analyzer matches 월세 inside 월세공제 — the default tokenizer keeps the compound whole");
    }

    [Fact]
    public void WithoutAConnectionStringOrAVectorStore_ResolvingSaysWhatIsMissing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSQLiteKeywordSearch();
        using var provider = services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<IKeywordSearchService>();

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*connection string*AddSQLiteVecVectorStore*");
    }

    [Fact]
    public void AKeywordServiceRegisteredEarlier_IsKept()
    {
        var earlier = Substitute.For<IKeywordSearchService>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(earlier);
        services.AddSQLiteKeywordSearch($"Data Source={_path}");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IKeywordSearchService>().Should().BeSameAs(earlier);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
