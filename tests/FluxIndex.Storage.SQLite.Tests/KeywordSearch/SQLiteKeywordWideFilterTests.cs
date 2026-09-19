using System.Diagnostics;
using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests.KeywordSearch;

/// <summary>
/// A metadata filter with many accepted values — a consumer scoping a search to every document of a vault
/// passes thousands — is resolved to its chunk set once per search. Carried in SQL it was evaluated per
/// posting row: 30 ms unfiltered, 9 s with 6 000 accepted ids. The wide and the narrow path must return the
/// same results, and the value list must not be bounded by the backend's parameter limit.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteKeywordWideFilterTests : IDisposable
{
    private readonly List<string> _paths = [];

    private SQLiteKeywordSearchService Create()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluxindex-kw-wide-{Guid.NewGuid():N}.db");
        _paths.Add(path);
        return new SQLiteKeywordSearchService($"Data Source={path}", NullLogger<SQLiteKeywordSearchService>.Instance);
    }

    private static DocumentChunk Chunk(int i, string content, string group)
    {
        var chunk = DocumentChunk.Create("doc" + i, content, 0, 1);
        chunk.Id = "c" + i;
        chunk.Metadata = new Dictionary<string, object>(StringComparer.Ordinal) { ["document_id"] = "doc" + i, ["group"] = group };
        return chunk;
    }

    private static Dictionary<string, object> Filter(IEnumerable<string> documentIds, string? group = null)
    {
        var filter = new Dictionary<string, object>(StringComparer.Ordinal) { ["document_id"] = documentIds.ToHashSet() };
        if (group is not null)
            filter["group"] = group;
        return filter;
    }

    [Fact]
    public async Task AWideFilter_ReturnsWhatTheSameFilterReturnsNarrow()
    {
        var ct = TestContext.Current.CancellationToken;
        using var service = Create();
        await service.IndexChunksAsync(
            Enumerable.Range(0, 40).Select(i => Chunk(i, i % 2 == 0 ? "alpha launch date" : "alpha budget review", i % 4 == 0 ? "a" : "b")), ct);

        // The accepted documents are the same twenty; the wide filter pads the list with ids that match
        // nothing, which moves it past the threshold without changing what it accepts.
        var accepted = Enumerable.Range(0, 20).Select(i => "doc" + i).ToList();
        var padded = accepted.Concat(Enumerable.Range(1000, 400).Select(i => "doc" + i)).ToList();

        foreach (var group in new[] { null, "a" })
        {
            var narrow = await service.SearchAsync("alpha launch", new KeywordSearchOptions { MaxResults = 50, MetadataFilter = Filter(accepted, group) }, ct);
            var wide = await service.SearchAsync("alpha launch", new KeywordSearchOptions { MaxResults = 50, MetadataFilter = Filter(padded, group) }, ct);

            narrow.Should().NotBeEmpty();
            narrow.Select(r => r.Chunk.Id).Should().OnlyContain(id => int.Parse(id.Substring(1)) < 20);
            wide.Select(r => (r.Chunk.Id, r.Score)).Should().Equal(narrow.Select(r => (r.Chunk.Id, r.Score)));
        }
    }

    [Fact]
    public async Task AWideFilterThatAcceptsNothing_ReturnsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var service = Create();
        await service.IndexChunksAsync(Enumerable.Range(0, 5).Select(i => Chunk(i, "alpha launch date", "a")), ct);

        var results = await service.SearchAsync("alpha",
            new KeywordSearchOptions { MetadataFilter = Filter(Enumerable.Range(1000, 400).Select(i => "doc" + i)) }, ct);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task AFilterWiderThanTheParameterLimit_StillSearches()
    {
        var ct = TestContext.Current.CancellationToken;
        using var service = Create();
        await service.IndexChunksAsync(Enumerable.Range(0, 5).Select(i => Chunk(i, "alpha launch date", "a")), ct);

        // SQLite binds at most 32 766 parameters per statement.
        var results = await service.SearchAsync("alpha",
            new KeywordSearchOptions { MaxResults = 10, MetadataFilter = Filter(Enumerable.Range(0, 40_000).Select(i => "doc" + i)) }, ct);

        results.Should().HaveCount(5);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task ScopingASearchToEveryDocument_CostsAboutWhatTheUnscopedSearchCosts()
    {
        var ct = TestContext.Current.CancellationToken;
        using var service = Create();
        const int entries = 6000;
        var random = new Random(11);
        var vocabulary = Enumerable.Range(0, 3000).Select(i => "term" + i).ToArray();
        var chunks = Enumerable.Range(0, entries).Select(i => Chunk(
            i, "alpha launch date " + string.Join(' ', Enumerable.Range(0, 60).Select(_ => vocabulary[random.Next(vocabulary.Length)])), "a")).ToList();
        foreach (var batch in chunks.Chunk(500))
            await service.IndexChunksAsync(batch, ct);

        var everyDocument = Filter(Enumerable.Range(0, entries).Select(i => "doc" + i));
        async Task<TimeSpan> MeasureAsync(KeywordSearchOptions options)
        {
            await service.SearchAsync("Alpha launch date", options, ct);
            var started = Stopwatch.GetTimestamp();
            (await service.SearchAsync("Alpha launch date", options, ct)).Should().HaveCount(10);
            return Stopwatch.GetElapsedTime(started);
        }

        var unscoped = await MeasureAsync(new KeywordSearchOptions { MaxResults = 10 });
        var scoped = await MeasureAsync(new KeywordSearchOptions { MaxResults = 10, MetadataFilter = everyDocument });

        scoped.Should().BeLessThan(unscoped * 25,
            $"unscoped {unscoped.TotalMilliseconds:F0} ms, scoped to every document {scoped.TotalMilliseconds:F0} ms (the correlated filter was about 300x)");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _paths)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
