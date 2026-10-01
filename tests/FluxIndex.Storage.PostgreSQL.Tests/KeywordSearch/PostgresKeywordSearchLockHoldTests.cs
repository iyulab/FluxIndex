using System.Diagnostics;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.PostgreSQL.KeywordSearch;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests.KeywordSearch;

/// <summary>
/// A write transaction must not hold the shared term rows for its whole length. Indexing a large document
/// writes postings for every chunk; if the term rows it uses are locked when the transaction starts and only
/// released at commit, every other writer that shares a single word with it waits for the whole document —
/// and with a few large documents in flight at once that wait exceeds the command timeout. Two forms of the
/// wait exist and both are covered: a row lock on a term that already exists, and the unique-index wait on a
/// term another transaction has inserted but not committed (a fresh index, where all vocabulary is new).
/// </summary>
/// <remarks>
/// The connection carries <c>lock_timeout</c>, so a writer that waits for another writer's whole batch fails
/// at once with <c>55P03</c> instead of after the 30 s command timeout — the failure is deterministic rather
/// than a matter of how slow the machine is.
/// </remarks>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgresKeywordSearchLockHoldTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_writer_sharing_vocabulary_completes_while_a_large_batch_is_still_being_written(bool vocabularyExists)
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Options = "-c lock_timeout=2000",
        }.ConnectionString;
        using var large = new PostgresKeywordSearchService(connectionString, NullLogger<PostgresKeywordSearchService>.Instance);
        using var small = new PostgresKeywordSearchService(connectionString, NullLogger<PostgresKeywordSearchService>.Instance);
        await Task.WhenAll(large.EnsureSchemaAsync(ct), small.EnsureSchemaAsync(ct));

        var vocabulary = Enumerable.Range(0, 400).Select(i => $"word{i:D3}").ToArray();
        if (vocabularyExists)
            await small.IndexChunksAsync([DocumentChunk.Create("seed", string.Join(' ', vocabulary), 0, 1)], ct);

        var random = new Random(7);
        var batch = Enumerable.Range(0, 120)
            .Select(index => DocumentChunk.Create("large", string.Join(' ', vocabulary.OrderBy(_ => random.Next()).Take(300)), index, 120))
            .ToList();

        var largeWrite = Task.Run(() => large.IndexChunksAsync(batch, ct), ct);
        await WaitForLongRunningTransactionAsync(TimeSpan.FromSeconds(3), ct);

        var elapsed = Stopwatch.StartNew();
        await small.IndexChunksAsync([DocumentChunk.Create("small", string.Join(' ', vocabulary.Take(50)), 0, 1)], ct);
        elapsed.Stop();

        largeWrite.IsCompleted.Should().BeFalse(
            "the small write must finish while the large batch is still being written, not after it commits (took {0} ms)",
            elapsed.ElapsedMilliseconds);

        await largeWrite;
        (await small.SearchAsync("word001", new KeywordSearchOptions { MaxResults = 200 }, ct)).Select(r => r.Chunk.DocumentId).Should().Contain(["large", "small"]);
    }

    /// <summary>Waits until a transaction other than this connection's has been open for a while — the large write.</summary>
    private async Task WaitForLongRunningTransactionAsync(TimeSpan within, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(ct);
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < within)
        {
            await using var command = new NpgsqlCommand("""
                SELECT COUNT(*) FROM pg_stat_activity
                WHERE datname = current_database() AND pid <> pg_backend_pid()
                  AND xact_start IS NOT NULL AND now() - xact_start > interval '500 milliseconds'
                """, connection);
            if ((long)(await command.ExecuteScalarAsync(ct))! > 0)
                return;
            await Task.Delay(50, ct);
        }

        throw new TimeoutException("the large write never had a transaction open for 500 ms — the batch is too small to test the hold");
    }
}
