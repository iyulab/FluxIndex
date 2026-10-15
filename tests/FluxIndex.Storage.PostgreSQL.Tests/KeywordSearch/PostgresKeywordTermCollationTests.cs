using AwesomeAssertions;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.PostgreSQL.KeywordSearch;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests.KeywordSearch;

/// <summary>
/// <c>bm25_terms.term</c> is compared by bytes (<c>COLLATE "C"</c>): a linguistic database collation made every term insert
/// and document-frequency update pay a locale comparison per index level. A table created by an earlier release, under the
/// database's collation, is converted on first open and keeps answering the same searches.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgresKeywordTermCollationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private PostgresKeywordSearchService Open() =>
        new(_container.GetConnectionString(), NullLogger<PostgresKeywordSearchService>.Instance, CjkBigramTextAnalyzer.Instance);

    private async Task<string> TermCollationAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT c.collname FROM pg_attribute a JOIN pg_collation c ON c.oid = a.attcollation " +
            "WHERE a.attrelid = 'bm25_terms'::regclass AND a.attname = 'term'",
            connection);
        return (string)(await command.ExecuteScalarAsync(ct))!;
    }

    private async Task ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    [Fact]
    public async Task A_new_index_compares_terms_by_bytes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var service = Open();
        await service.EnsureSchemaAsync(ct);

        (await TermCollationAsync(ct)).Should().Be("C");
    }

    [Fact]
    public async Task An_index_created_under_the_database_collation_is_converted_on_open_and_still_finds_its_terms()
    {
        var ct = TestContext.Current.CancellationToken;
        var chunk = DocumentChunk.Create("doc", "개인정보 보호 규정에 따라 처리한다", 0, 1);
        using (var earlier = Open())
            await earlier.IndexChunksAsync([chunk], ct);

        // As an earlier release left it: the database's own collation on the term column.
        await ExecuteAsync("ALTER TABLE bm25_terms ALTER COLUMN term TYPE text COLLATE \"default\"", ct);
        (await TermCollationAsync(ct)).Should().Be("default");

        using var reopened = Open();
        var hits = await reopened.SearchAsync("정보", cancellationToken: ct);

        (await TermCollationAsync(ct)).Should().Be("C");
        hits.Select(h => h.Chunk.Id).Should().Equal(chunk.Id);
    }
}
