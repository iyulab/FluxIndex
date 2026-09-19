using AwesomeAssertions;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests.KeywordSearch;

/// <summary>
/// A keyword-index write must cost what it writes, not the size of the index. Two statements used to
/// read the whole posting tables on every write — the document-frequency update (a term filter the
/// planner did not push into a <c>UNION ALL</c>) and the statistics recount — so indexing a vault entry
/// by entry cost its square. The plan is asserted rather than a duration: a timing threshold tells a
/// slow machine from a slow query only by luck.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteKeywordWriteCostTests : IDisposable
{
    private static readonly KeywordFieldOptions TitleField = new() { Fields = [new KeywordField("title")] };

    private readonly List<string> _paths = [];

    private string NewPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluxindex-kw-cost-{Guid.NewGuid():N}.db");
        _paths.Add(path);
        return path;
    }

    private static SQLiteKeywordSearchService Create(string path, KeywordFieldOptions? fields) =>
        new($"Data Source={path}", NullLogger<SQLiteKeywordSearchService>.Instance, analyzer: null, fields);

    private static DocumentChunk Chunk(string id, string content, string? title = null)
    {
        var chunk = DocumentChunk.Create("doc-" + id, content, 0, 1);
        chunk.Id = id;
        if (title is not null)
            chunk.Metadata = new Dictionary<string, object>(StringComparer.Ordinal) { ["title"] = title };
        return chunk;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentFrequencyUpdate_ReadsPostingsByTermId_NeverTheWholeTable(bool withField)
    {
        var ct = TestContext.Current.CancellationToken;
        var path = NewPath();
        using (var service = Create(path, withField ? TitleField : KeywordFieldOptions.None))
            await service.IndexChunksAsync([Chunk("1", "alpha beta", "gamma"), Chunk("2", "beta delta", "alpha")], ct);

        var fieldPredicate = withField ? "f.field IN ('title')" : null;
        var current = await PlanAsync(path, RelationalKeywordSearchService.BuildDocumentFrequencyUpdateSql(fieldPredicate, "bm25_terms.id IN (1, 2)"), ct);

        // The control: the statement this replaced, on the same database. It must show the scan, or a
        // clean plan above would only mean the check cannot see one.
        var replaced = await PlanAsync(path, """
            UPDATE bm25_terms
            SET document_frequency =
                (SELECT COUNT(DISTINCT u.chunk_id) FROM (
                    SELECT chunk_id, term_id FROM bm25_postings
                    UNION ALL SELECT chunk_id, term_id FROM bm25_field_postings WHERE field IN ('title')) u
                 WHERE u.term_id = bm25_terms.id)
            WHERE bm25_terms.id IN (1, 2);
            """, ct);
        replaced.Should().Contain(step => step.StartsWith("SCAN", StringComparison.Ordinal),
            "the control statement reads the posting tables in full; if this fails the plan check is blind");

        current.Should().NotBeEmpty();
        current.Where(step => step.StartsWith("SCAN", StringComparison.Ordinal)).Should().BeEmpty(
            $"every table the update touches is reached through an index; plan was: {string.Join(" | ", current)}");
    }

    [Fact]
    public async Task IncrementalStatistics_MatchARecountFromTheRows_AfterReplaceDeleteAndEmptying()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = NewPath();
        using var service = Create(path, TitleField);

        await service.IndexChunksAsync(
        [
            Chunk("1", "alpha beta gamma alpha", "first title"),
            Chunk("2", "beta gamma delta", "second"),
            Chunk("3", "alpha delta epsilon zeta eta"),
            Chunk("4", "theta iota", "a much longer title than the others"),
        ], ct);
        await service.IndexChunkAsync(Chunk("2", "beta", "second title now longer"), ct);            // replace, shorter body
        await service.IndexChunkAsync(Chunk("3", "alpha delta epsilon zeta eta kappa", "gains a title"), ct); // replace, gains a field
        await service.IndexChunkAsync(Chunk("4", "theta iota"), ct);                                    // replace, loses its field
        await service.DeleteChunkAsync("1", ct);
        await service.IndexChunkAsync(Chunk("5", "lambda mu nu", "fifth"), ct);

        var incremental = await ReadStatisticsAsync(path, ct);
        await service.OptimizeIndexAsync(ct); // recounts from the rows
        var recounted = await ReadStatisticsAsync(path, ct);

        incremental["total_documents"].Should().Be(4);
        incremental.Should().BeEquivalentTo(recounted, "statistics moved by deltas must equal statistics derived from the rows");
    }

    [Fact]
    public async Task AStoreWithoutTheTotals_IsRecountedOnTheNextWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = NewPath();
        using var service = Create(path, TitleField);
        await service.IndexChunksAsync([Chunk("1", "alpha beta", "one"), Chunk("2", "beta gamma delta", "two words")], ct);

        // What an index written by an earlier release holds: the averages, none of the totals.
        await ExecuteAsync(path, "DELETE FROM bm25_statistics WHERE key = 'total_doc_length' OR key LIKE 'field_%'", ct);

        await service.IndexChunkAsync(Chunk("3", "epsilon", "three"), ct);

        var statistics = await ReadStatisticsAsync(path, ct);
        statistics["total_documents"].Should().Be(3);
        statistics["total_doc_length"].Should().Be(6);
        statistics["avg_doc_length"].Should().Be(2);
        statistics["field_doc_count:title"].Should().Be(3);
        statistics["field_total_length:title"].Should().Be(4);
    }

    [Fact]
    public async Task ReindexingAChunkToContentWithNoTerms_RemovesItsOldPostings()
    {
        var ct = TestContext.Current.CancellationToken;
        using var service = Create(NewPath(), KeywordFieldOptions.None);
        await service.IndexChunksAsync([Chunk("1", "alpha beta"), Chunk("2", "gamma")], ct);

        await service.IndexChunkAsync(Chunk("1", "--- ... !!!"), ct); // non-empty, but no terms

        (await service.SearchAsync("alpha", cancellationToken: ct)).Should().BeEmpty(
            "the chunk no longer holds that text, so its old postings must not keep matching");
        (await service.SearchAsync("gamma", cancellationToken: ct)).Should().ContainSingle();
    }

    /// <summary>
    /// The shape the plan test protects, measured: entries written one at a time, the way a repair or a
    /// folder scan writes them. Compares the run against itself, so a slow machine moves both halves.
    /// </summary>
    [Fact]
    [Trait("Category", "Performance")]
    public async Task WritingEntryByEntry_CostsTheSamePerEntry_AsTheIndexGrows()
    {
        var ct = TestContext.Current.CancellationToken;
        using var service = Create(NewPath(), TitleField);
        const int entries = 1500;
        const int window = 150;
        var random = new Random(7);
        var vocabulary = Enumerable.Range(0, 4000).Select(i => "term" + i).ToArray();

        var first = TimeSpan.Zero;
        var last = TimeSpan.Zero;
        for (var i = 0; i < entries; i++)
        {
            var body = string.Join(' ', Enumerable.Range(0, 70).Select(_ => vocabulary[random.Next(vocabulary.Length)]));
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            await service.IndexChunkAsync(Chunk("c" + i, body, "title " + vocabulary[random.Next(vocabulary.Length)]), ct);
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
            if (i < window) first += elapsed;
            if (i >= entries - window) last += elapsed;
        }

        TestContext.Current.SendDiagnosticMessage(
            $"first {window}: {first.TotalMilliseconds / window:F2} ms/entry, last {window}: {last.TotalMilliseconds / window:F2} ms/entry");
        last.Should().BeLessThan(first * 3, $"per-entry cost must not grow with the index (first {first.TotalMilliseconds / window:F2} ms, last {last.TotalMilliseconds / window:F2} ms)");
    }

    private static async Task<List<string>> PlanAsync(string path, string sql, CancellationToken ct)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        var steps = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            steps.Add(reader.GetString(3));
        return steps;
    }

    private static async Task<Dictionary<string, double>> ReadStatisticsAsync(string path, CancellationToken ct)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM bm25_statistics";
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            values[reader.GetString(0)] = reader.GetDouble(1);
        return values;
    }

    private static async Task ExecuteAsync(string path, string sql, CancellationToken ct)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
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
