using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.PostgreSQL.KeywordSearch;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests.KeywordSearch;

/// <summary>
/// Executes the same corpus and the same queries against <b>both</b> SQL keyword backends and compares
/// the results. The backends share one BM25 implementation, so "ranking means the same thing whichever
/// store you configured" ought to hold — but sharing code is an argument, not evidence, and the parts
/// that are not shared (schema types, upsert syntax, the id-list predicate, how each provider maps
/// values) are exactly where an equivalence can break.
/// <para>
/// Requires Docker for the PostgreSQL side, hence Integration. This is the test that would catch a
/// dialect change that silently alters scoring — for example a numeric type that truncates a term
/// frequency, or a collation that makes term lookup case-sensitive on one backend only.
/// </para>
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public class KeywordBackendEquivalenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().Build();
    private readonly string _sqlitePath =
        Path.Combine(Path.GetTempPath(), $"fluxindex-equiv-{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(
            new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_sqlitePath}"));
        try { if (File.Exists(_sqlitePath)) File.Delete(_sqlitePath); } catch (IOException) { }
    }

    /// <summary>
    /// Chunks are built once and indexed into both backends, so chunk ids are identical on both sides
    /// and the comparison is about ranking rather than about identity.
    /// </summary>
    private static DocumentChunk[] Corpus() =>
    [
        Tagged("doc-contract", "provisioning schema tables relations", "alpha", 7),
        Tagged("doc-repeat", "schema provisioning is the topic of provisioning here", "alpha", 7),
        Tagged("doc-other", "unrelated content about embeddings and vectors", "beta", 9),
        Tagged("doc-common", "provisioning appears here too, with schema and relations", "beta", 9),
        Tagged("doc-ko", "착수계약서 검토 결과를 정리한 문서", "alpha", 9),
    ];

    /// <summary>
    /// Carries a string and a number so the filter comparison covers value formatting, not only
    /// predicate shape — a backend that rendered the number differently would match nothing, and the
    /// two providers do not have to agree on how they bind an int for that to happen.
    /// </summary>
    private static DocumentChunk Tagged(string documentId, string content, string tenant, int rank)
    {
        var chunk = DocumentChunk.Create(documentId, content, 0, 1);
        chunk.Metadata = new Dictionary<string, object> { ["tenant"] = tenant, ["rank"] = rank };
        return chunk;
    }

    private async Task<(IReadOnlyList<KeywordSearchResult> Sqlite, IReadOnlyList<KeywordSearchResult> Postgres)>
        SearchBothAsync(string query, KeywordSearchOptions? options = null)
    {
        var corpus = Corpus();

        var sqlite = new SQLiteKeywordSearchService(
            $"Data Source={_sqlitePath}", NullLogger<SQLiteKeywordSearchService>.Instance);
        var postgres = new PostgresKeywordSearchService(
            _container.GetConnectionString(), NullLogger<PostgresKeywordSearchService>.Instance);

        try
        {
            await sqlite.ClearIndexAsync();
            await postgres.ClearIndexAsync();
            await sqlite.IndexChunksAsync(corpus);
            await postgres.IndexChunksAsync(corpus);

            return (await sqlite.SearchAsync(query, options), await postgres.SearchAsync(query, options));
        }
        finally
        {
            sqlite.Dispose();
            postgres.Dispose();
        }
    }

    [Fact]
    public async Task BothBackends_RankTheSameCorpusIdentically()
    {
        var (sqlite, postgres) = await SearchBothAsync("provisioning schema");

        sqlite.Should().NotBeEmpty("otherwise the comparison below is vacuous");
        postgres.Select(r => r.Chunk.Id).Should().Equal(
            sqlite.Select(r => r.Chunk.Id),
            "the shared BM25 implementation must produce the same order on both backends");
    }

    [Fact]
    public async Task BothBackends_ProduceTheSameScores()
    {
        var (sqlite, postgres) = await SearchBothAsync("provisioning schema");

        sqlite.Should().NotBeEmpty();
        postgres.Should().HaveSameCount(sqlite);

        var sqliteById = sqlite.ToDictionary(r => r.Chunk.Id, StringComparer.Ordinal);
        foreach (var result in postgres)
        {
            // Same inputs through the same arithmetic — the tolerance covers double formatting through
            // two providers, not a difference in how the score is computed.
            result.Score.Should().BeApproximately(sqliteById[result.Chunk.Id].Score, 1e-9,
                $"score for {result.Chunk.DocumentId} must not depend on the storage backend");
        }
    }

    [Fact]
    public async Task BothBackends_ReportTheSameMatchedTermsAndDocumentLengths()
    {
        var (sqlite, postgres) = await SearchBothAsync("provisioning schema");

        // Without this the loop below can be empty and the test passes having compared nothing.
        postgres.Should().NotBeEmpty();

        var sqliteById = sqlite.ToDictionary(r => r.Chunk.Id, StringComparer.Ordinal);
        foreach (var result in postgres)
        {
            var expected = sqliteById[result.Chunk.Id];
            result.MatchedTerms.Should().BeEquivalentTo(expected.MatchedTerms);
            result.DocumentLength.Should().Be(expected.DocumentLength);
            result.TermFrequencies.Should().BeEquivalentTo(expected.TermFrequencies);
        }
    }

    /// <summary>
    /// Term lookup must not depend on a backend collation: SQLite's schema still declares NOCASE on the
    /// term column while PostgreSQL uses a plain unique text column, and case handling was moved into
    /// managed code precisely so the two cannot diverge.
    /// </summary>
    [Fact]
    public async Task BothBackends_MatchQueryTermsRegardlessOfCase()
    {
        var (sqlite, postgres) = await SearchBothAsync("PROVISIONING");

        sqlite.Should().NotBeEmpty("the corpus is indexed lower-cased, so an upper-case query must match");
        postgres.Select(r => r.Chunk.Id).Should().Equal(sqlite.Select(r => r.Chunk.Id));
    }

    [Fact]
    public async Task BothBackends_HandleKoreanTokensIdentically()
    {
        var (sqlite, postgres) = await SearchBothAsync("착수계약서");

        sqlite.Should().ContainSingle().Which.Chunk.DocumentId.Should().Be("doc-ko");
        postgres.Select(r => r.Chunk.Id).Should().Equal(sqlite.Select(r => r.Chunk.Id));
    }

    [Fact]
    public async Task BothBackends_ApplyDocumentScopeIdentically()
    {
        var (sqlite, postgres) = await SearchBothAsync(
            "provisioning",
            new KeywordSearchOptions { DocumentIdFilter = "doc-common" });

        // OnlyContain is satisfied by an empty collection, so assert there is something to scope first.
        sqlite.Should().NotBeEmpty();
        sqlite.Should().OnlyContain(r => r.Chunk.DocumentId == "doc-common");
        postgres.Select(r => r.Chunk.Id).Should().Equal(sqlite.Select(r => r.Chunk.Id));
    }

    // === Metadata filter dimension ===
    //
    // The filter is normalised into its own relation and matched with an EXISTS predicate, which is
    // plain SQL on both dialects — but "plain on both" is the claim, and the claim is what these
    // check. Value formatting is the part most likely to diverge: it is the one place where the two
    // providers would otherwise each decide how to render a value.

    [Fact]
    public async Task BothBackends_ApplyMetadataFilterIdentically()
    {
        var (sqlite, postgres) = await SearchBothAsync(
            "provisioning schema",
            new KeywordSearchOptions
            {
                MetadataFilter = new Dictionary<string, object> { ["tenant"] = "alpha" }
            });

        sqlite.Should().NotBeEmpty("otherwise the order comparison below is vacuous");
        sqlite.Should().OnlyContain(r => r.Chunk.DocumentId != "doc-other" && r.Chunk.DocumentId != "doc-common");
        postgres.Select(r => r.Chunk.Id).Should().Equal(
            sqlite.Select(r => r.Chunk.Id),
            "a filter selects rows; the surviving order must still be backend-independent");
    }

    [Fact]
    public async Task BothBackends_MatchNumericMetadataIdentically()
    {
        var (sqlite, postgres) = await SearchBothAsync(
            "provisioning schema",
            new KeywordSearchOptions
            {
                // Supplied as an int, stored as text. Both backends must reach the same rows without
                // the caller knowing how the index represents a number.
                MetadataFilter = new Dictionary<string, object> { ["rank"] = 7 }
            });

        sqlite.Should().NotBeEmpty("a numeric filter that silently matches nothing would pass vacuously");
        postgres.Select(r => r.Chunk.Id).Should().Equal(sqlite.Select(r => r.Chunk.Id));
    }

    [Fact]
    public async Task BothBackends_ApplyMatchAnyIdentically()
    {
        var (sqlite, postgres) = await SearchBothAsync(
            "provisioning schema",
            new KeywordSearchOptions
            {
                MetadataFilter = new Dictionary<string, object> { ["tenant"] = new[] { "alpha", "beta" } }
            });

        var (unfiltered, _) = await SearchBothAsync("provisioning schema");

        // Compared by document id, not chunk id: each call re-indexes a freshly built corpus, so chunk
        // ids are new Guids every time and only agree within a single call.
        sqlite.Should().NotBeEmpty();
        sqlite.Select(r => r.Chunk.DocumentId).Should().Equal(
            unfiltered.Select(r => r.Chunk.DocumentId),
            "match-any over every tenant present is equivalent to no filter at all");
        postgres.Select(r => r.Chunk.Id).Should().Equal(sqlite.Select(r => r.Chunk.Id));
    }

    [Fact]
    public async Task BothBackends_DeleteByFilterRemoveTheSameChunks()
    {
        var corpus = Corpus();
        var filter = new Dictionary<string, object> { ["tenant"] = "alpha" };

        var sqlite = new SQLiteKeywordSearchService(
            $"Data Source={_sqlitePath}", NullLogger<SQLiteKeywordSearchService>.Instance);
        var postgres = new PostgresKeywordSearchService(
            _container.GetConnectionString(), NullLogger<PostgresKeywordSearchService>.Instance);

        try
        {
            await sqlite.ClearIndexAsync();
            await postgres.ClearIndexAsync();
            await sqlite.IndexChunksAsync(corpus);
            await postgres.IndexChunksAsync(corpus);

            var sqliteRemoved = await sqlite.DeleteByFilterAsync(filter);
            var postgresRemoved = await postgres.DeleteByFilterAsync(filter);

            sqliteRemoved.Should().BeGreaterThan(0, "otherwise both backends agreeing on zero proves nothing");
            postgresRemoved.Should().Be(sqliteRemoved);

            var sqliteLeft = await sqlite.SearchAsync("provisioning schema");
            var postgresLeft = await postgres.SearchAsync("provisioning schema");

            sqliteLeft.Should().NotBeEmpty("the other tenant's rows must survive");
            postgresLeft.Select(r => r.Chunk.Id).Should().Equal(sqliteLeft.Select(r => r.Chunk.Id));
        }
        finally
        {
            sqlite.Dispose();
            postgres.Dispose();
        }
    }
}
