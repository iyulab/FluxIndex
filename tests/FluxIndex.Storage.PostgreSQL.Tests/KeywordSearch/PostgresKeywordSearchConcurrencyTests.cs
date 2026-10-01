using System.Collections.Concurrent;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.PostgreSQL.KeywordSearch;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests.KeywordSearch;

/// <summary>
/// Indexing and deleting run concurrently against one keyword index in any deployment that processes
/// more than one document at a time, and every transaction of either kind writes the shared term rows.
/// Two transactions that take those rows in different orders deadlock; PostgreSQL then kills one of
/// them. Indexing acquired its term rows in one fixed order, but deletion updated them in whatever
/// order the executor chose and had no retry, so a re-index that replaced a document's previous chunks
/// could fail outright under load.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgresKeywordSearchConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();
    private readonly RetryCountingLogger _logger = new();
    private PostgresKeywordSearchService _service = null!;

    // Shared vocabulary: every document uses most of it, so every pair of transactions contends on
    // hundreds of the same term rows — the situation any two documents in one language are in.
    private static readonly string[] Vocabulary =
        Enumerable.Range(0, 400).Select(i => $"term{i:D3}").ToArray();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _service = new PostgresKeywordSearchService(_container.GetConnectionString(), _logger);
        await _service.ClearIndexAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _service.Dispose();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Concurrent_reindex_swaps_over_shared_vocabulary_complete_without_a_deadlock_surfacing()
    {
        const int workers = 6;
        const int rounds = 25;
        var failures = new ConcurrentBag<Exception>();

        await Task.WhenAll(Enumerable.Range(0, workers).Select(worker => Task.Run(async () =>
        {
            var random = new Random(worker);
            IReadOnlyList<DocumentChunk> previous = [];

            for (var round = 0; round < rounds; round++)
            {
                try
                {
                    // The re-index swap a document pipeline performs: write the new generation, then
                    // remove the previous one chunk by chunk, then (sometimes) the whole document.
                    var documentId = $"doc-{worker}";
                    var generation = Enumerable.Range(0, 3)
                        .Select(index => DocumentChunk.Create(documentId, Text(random), index, 3))
                        .ToList();

                    await _service.IndexChunksAsync(generation);

                    // Half the writers drop the previous generation in one call, half chunk by chunk, so the
                    // batched and the single-chunk delete paths contend with each other and with indexing.
                    if (worker % 2 == 0)
                    {
                        await _service.DeleteChunksAsync(previous.Select(chunk => chunk.Id));
                    }
                    else
                    {
                        foreach (var chunk in previous)
                            await _service.DeleteChunkAsync(chunk.Id);
                    }

                    previous = generation;
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            try
            {
                await _service.DeleteByDocumentIdAsync($"doc-{worker}");
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }, TestContext.Current.CancellationToken)));

        failures.Select(ex => ex.Message).Should().BeEmpty("a lock conflict between two writers must be absorbed, not surfaced");

        // The retry absorbs a conflict; the acquisition order is what should keep one from happening at
        // all. Every writer takes the shared term rows in one order, so no cycle is left to retry.
        _logger.ConcurrencyRetries.Should().Be(0, "no deadlock should occur once every writer acquires term rows in one order");

        // Every document was removed, so no term row may survive: the cleanup of zero-frequency terms is
        // scoped to the rows a transaction touched and must still reach all of them.
        (await CountAsync("SELECT COUNT(*) FROM bm25_postings")).Should().Be(0);
        (await CountAsync("SELECT COUNT(*) FROM bm25_terms")).Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_writes_of_the_same_chunk_leave_document_frequency_equal_to_a_recount()
    {
        // Document frequency moves by what each transaction deleted and wrote. Two transactions writing the same
        // chunk id at once must not both count the rows the earlier one replaced, nor miss the rows the earlier
        // one committed: the delta is taken from the rows each DELETE actually removed.
        var ct = TestContext.Current.CancellationToken;
        using var first = new PostgresKeywordSearchService(_container.GetConnectionString(), _logger);
        using var second = new PostgresKeywordSearchService(_container.GetConnectionString(), _logger);
        // Initialized at the same time, as two workers starting together do (schema creation is serialized).
        await Task.WhenAll(first.EnsureSchemaAsync(ct), second.EnsureSchemaAsync(ct));
        var random = new Random(3);

        string Words(int from, int count) => string.Join(' ', Vocabulary.Skip(from).Take(count));
        DocumentChunk Chunk(string id, string content)
        {
            var chunk = DocumentChunk.Create("doc-" + id, content, 0, 1);
            chunk.Id = id;
            return chunk;
        }

        async Task RaceAsync(Func<Task> left, Func<Task> right)
        {
            using var start = new SemaphoreSlim(0, 2);
            var tasks = new[] { left, right }.Select(write => Task.Run(async () =>
            {
                await start.WaitAsync(ct);
                await write();
            }, ct)).ToArray();
            start.Release(2);
            await Task.WhenAll(tasks);
        }

        await _service.IndexChunksAsync([Chunk("bystander", Words(0, 120))], ct);

        for (var round = 0; round < 20; round++)
        {
            var id = $"shared-{round % 4}";

            // Overlapping terms: both replace the chunk, sharing part of the vocabulary.
            var offset = random.Next(0, 100);
            await RaceAsync(
                () => first.IndexChunkAsync(Chunk(id, Words(offset, 150)), ct),
                () => second.IndexChunkAsync(Chunk(id, Words(offset + 75, 150)), ct));

            // A chunk id neither has seen, written with disjoint terms.
            await RaceAsync(
                () => first.IndexChunkAsync(Chunk($"fresh-{round}", Words(0, 40)), ct),
                () => second.IndexChunkAsync(Chunk($"fresh-{round}", Words(200, 40)), ct));

            // Both delete the same chunk; then both bring it back.
            if (round % 5 == 4)
            {
                await RaceAsync(() => first.DeleteChunkAsync(id, ct), () => second.DeleteChunkAsync(id, ct));
                await RaceAsync(
                    () => first.IndexChunkAsync(Chunk(id, Words(10, 60)), ct),
                    () => second.IndexChunkAsync(Chunk(id, Words(40, 60)), ct));
            }
        }

        var mismatches = await MismatchedDocumentFrequenciesAsync();
        mismatches.Should().BeEmpty("the maintained document frequency must equal the count of chunks holding the term");
        (await CountAsync("SELECT COUNT(*) FROM bm25_terms")).Should().BeGreaterThan(0);

        // And the repair path agrees with both.
        var vocabulary = Vocabulary.ToList();
        var maintained = await _service.GetDocumentFrequenciesAsync(vocabulary, ct);
        await _service.OptimizeIndexAsync(ct);
        (await _service.GetDocumentFrequenciesAsync(vocabulary, ct)).Should().BeEquivalentTo(maintained);
    }

    /// <summary>Terms whose stored document frequency differs from the distinct chunks holding them, or that no chunk holds.</summary>
    private async Task<List<string>> MismatchedDocumentFrequenciesAsync()
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT t.term, t.document_frequency, COUNT(DISTINCT c.chunk_id)
            FROM bm25_terms t
            LEFT JOIN (SELECT term_id, chunk_id FROM bm25_postings
                       UNION SELECT term_id, chunk_id FROM bm25_field_postings) c ON c.term_id = t.id
            GROUP BY t.id, t.term, t.document_frequency
            HAVING t.document_frequency <> COUNT(DISTINCT c.chunk_id) OR COUNT(DISTINCT c.chunk_id) = 0
            """, connection);
        var mismatches = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            mismatches.Add($"{reader.GetString(0)}: stored {reader.GetInt32(1)}, chunks {reader.GetInt64(2)}");
        return mismatches;
    }

    private sealed class RetryCountingLogger : ILogger<PostgresKeywordSearchService>
    {
        private int _retries;

        public int ConcurrencyRetries => Volatile.Read(ref _retries);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning && formatter(state, exception).Contains("concurrency conflict", StringComparison.Ordinal))
                Interlocked.Increment(ref _retries);
        }
    }

    private static string Text(Random random) =>
        string.Join(' ', Vocabulary.OrderBy(_ => random.Next()).Take(300));

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
