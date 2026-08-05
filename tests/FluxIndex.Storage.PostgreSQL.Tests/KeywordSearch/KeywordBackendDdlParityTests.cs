using FluentAssertions;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Storage.PostgreSQL.KeywordSearch;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests.KeywordSearch;

/// <summary>
/// The BM25 implementation is shared (<see cref="RelationalKeywordSearchService"/>) and queries the
/// index relations <b>by name</b>: every backend's DDL therefore has to declare the same tables with
/// the same columns. A dialect that renames or drops one compiles fine and fails at runtime.
/// <para>
/// This is a structural check, not a substitute for executing the SQL — the runtime proof is the
/// Docker-gated roundtrip test. It is what can be verified without a database, and it covers the
/// failure mode most likely to be introduced when a new backend is added.
/// </para>
/// </summary>
public class KeywordBackendDdlParityTests
{
    private static string DdlOf(RelationalKeywordSearchService service)
    {
        var property = typeof(RelationalKeywordSearchService)
            .GetProperty("SchemaDdl", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (string)property.GetValue(service)!;
    }

    private static RelationalKeywordSearchService Sqlite() =>
        new SQLiteKeywordSearchService("Data Source=:memory:", NullLogger<SQLiteKeywordSearchService>.Instance);

    private static RelationalKeywordSearchService Postgres() =>
        new PostgresKeywordSearchService(
            "Host=localhost;Database=flux;Username=u;Password=p",
            NullLogger<PostgresKeywordSearchService>.Instance);

    /// <summary>
    /// Table and column names only — types are deliberately excluded, since those are exactly what a
    /// dialect is allowed to differ on (<c>TEXT</c> vs <c>text</c>, <c>REAL</c> vs
    /// <c>double precision</c>).
    /// </summary>
    private static Dictionary<string, List<string>> RelationsIn(string ddl)
    {
        var relations = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var match in Regex.Matches(
            ddl,
            @"CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+(?<table>\w+)\s*\((?<body>[^;]*)\)\s*;",
            RegexOptions.IgnoreCase | RegexOptions.Singleline).Cast<Match>())
        {
            var columns = new List<string>();
            foreach (var line in match.Groups["body"].Value.Split(','))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;

                // Table-level constraints are not columns.
                if (trimmed.StartsWith("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(")", StringComparison.Ordinal))
                    continue;

                var name = trimmed.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];
                if (!name.Equals("REFERENCES", StringComparison.OrdinalIgnoreCase))
                    columns.Add(name);
            }

            relations[match.Groups["table"].Value] = columns;
        }

        return relations;
    }

    [Fact]
    public void EveryBackend_DeclaresTheSameIndexRelations()
    {
        var sqlite = RelationsIn(DdlOf(Sqlite()));
        var postgres = RelationsIn(DdlOf(Postgres()));

        sqlite.Keys.Should().BeEquivalentTo(
            new[] { "bm25_terms", "bm25_postings", "bm25_chunks", "bm25_chunk_metadata", "bm25_statistics" },
            "the parser must actually find the relations, otherwise the comparison below is vacuous");
        postgres.Keys.Should().BeEquivalentTo(sqlite.Keys);
    }

    [Fact]
    public void EveryBackend_DeclaresTheSameColumnsPerRelation()
    {
        var sqlite = RelationsIn(DdlOf(Sqlite()));
        var postgres = RelationsIn(DdlOf(Postgres()));

        foreach (var (table, columns) in sqlite)
        {
            columns.Should().NotBeEmpty($"{table} must have parsed columns for this test to mean anything");
            postgres[table].Should().BeEquivalentTo(columns,
                $"the shared BM25 implementation reads {table} by column name on every backend");
        }
    }

    [Fact]
    public void EveryBackend_DeclaresTheIndexesTheSharedQueriesRelyOn()
    {
        var sqliteDdl = DdlOf(Sqlite());
        var postgresDdl = DdlOf(Postgres());

        foreach (var index in new[]
                 {
                     "idx_bm25_terms_term",
                     "idx_bm25_postings_chunk",
                     "idx_bm25_chunks_document"
                 })
        {
            sqliteDdl.Should().Contain(index);
            postgresDdl.Should().Contain(index);
        }
    }
}
