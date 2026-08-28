using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.PostgreSQL.KeywordSearch;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests.KeywordSearch;

/// <summary>
/// Integration test (requires Docker) proving the PostgreSQL keyword index actually runs: its DDL is
/// valid, its upserts and array predicate execute, and the index outlives the instance that wrote it.
/// <para>
/// This is the runtime proof the Docker-free parity and wiring tests cannot give. The consumers that
/// reported the original defect run PostgreSQL, so "the code compiles and the schema matches SQLite"
/// is not sufficient evidence for them.
/// </para>
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public class PostgresKeywordSearchIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private PostgresKeywordSearchService CreateService() =>
        new(_container.GetConnectionString(), NullLogger<PostgresKeywordSearchService>.Instance);

    private static DocumentChunk Chunk(string documentId, string content, int index = 0, int total = 1)
        => DocumentChunk.Create(documentId, content, index, total);

    /// <summary>
    /// The proposition the whole track exists for: a process that did not do the indexing can still
    /// retrieve keyword matches.
    /// </summary>
    [Fact]
    public async Task IndexedChunks_AreRetrievable_FromASeparateInstance()
    {
        var writer = CreateService();
        await writer.IndexChunksAsync(
        [
            Chunk("doc-1", "The quick brown fox jumps over the lazy dog"),
            Chunk("doc-2", "Distributed systems require careful transaction boundaries")
        ]);
        writer.Dispose();

        var reader = CreateService();
        var results = await reader.SearchAsync("transaction");
        reader.Dispose();

        results.Should().ContainSingle(r => r.Chunk.DocumentId == "doc-2");
    }

    [Fact]
    public async Task Search_RanksTheChunkContainingTheTerm_Highest()
    {
        var service = CreateService();
        await service.IndexChunksAsync(
        [
            Chunk("doc-1", "provisioning schema tables relations"),
            Chunk("doc-2", "unrelated content about embeddings and vectors"),
            Chunk("doc-3", "schema provisioning is the topic of provisioning here")
        ]);

        var results = await service.SearchAsync("provisioning");
        service.Dispose();

        results[0].Chunk.DocumentId.Should().Be("doc-3");
        results.Should().NotContain(r => r.Chunk.DocumentId == "doc-2");
    }

    /// <summary>
    /// Re-indexing replaces postings rather than layering them, and document frequency is recomputed
    /// from the postings that exist — the array predicate is what applies that update on this backend.
    /// </summary>
    [Fact]
    public async Task ReindexingTheSameChunk_DoesNotDriftDocumentFrequency()
    {
        // The same chunk — same id. `DocumentChunk.Create` mints a new Guid per call, so building it
        // twice would be two different chunks and would test nothing about re-indexing.
        var chunk = Chunk("doc-1", "provisioning schema relations");

        var service = CreateService();
        await service.IndexChunksAsync([chunk]);
        await service.IndexChunksAsync([chunk]);

        var idf = service.GetIDF("provisioning");
        var statistics = await service.GetStatisticsAsync();
        service.Dispose();

        statistics.TotalDocuments.Should().Be(1, "re-indexing replaces the chunk's postings, not adds to them");
        idf.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Characterization of current behavior, not an endorsement: indexing the *same document* through
    /// two freshly created chunks produces two entries, because chunk identity is a per-instance Guid
    /// rather than derived from (document, index). Whether re-indexing a document should first delete
    /// its previous chunks is an open policy question, so this pins what happens today and will fail
    /// loudly if that policy changes.
    /// </summary>
    [Fact]
    public async Task IndexingTheSameDocumentTwiceAsNewChunks_StoresBoth_Characterization()
    {
        var service = CreateService();
        await service.IndexChunksAsync([Chunk("doc-1", "provisioning schema relations")]);
        await service.IndexChunksAsync([Chunk("doc-1", "provisioning schema relations")]);

        var statistics = await service.GetStatisticsAsync();
        service.Dispose();

        statistics.TotalDocuments.Should().Be(2,
            "chunk ids are per-instance Guids, so the second call is a second chunk of the same document");
    }

    [Fact]
    public async Task DeleteByDocumentId_RemovesTheDocumentFromSubsequentSearches()
    {
        var service = CreateService();
        await service.IndexChunksAsync(
        [
            Chunk("doc-1", "orphaned keyword index backend candidate"),
            Chunk("doc-2", "candidate backend for the keyword leg")
        ]);

        await service.DeleteByDocumentIdAsync("doc-1");

        var results = await service.SearchAsync("candidate");
        service.Dispose();

        results.Should().NotContain(r => r.Chunk.DocumentId == "doc-1",
            "a deleted document must not produce ghost matches");
        results.Should().Contain(r => r.Chunk.DocumentId == "doc-2");
    }

    /// <summary>
    /// Both reporting consumers index Korean documents. An English-only fixture would go green while
    /// being useless to them.
    /// </summary>
    [Fact]
    public async Task KoreanContent_IsRetrievable_ByAWholeToken()
    {
        var service = CreateService();
        await service.IndexChunksAsync(
        [
            Chunk("doc-ko", "착수계약서 검토 결과를 정리한 문서"),
            Chunk("doc-en", "review of the engagement contract")
        ]);

        var results = await service.SearchAsync("착수계약서");
        service.Dispose();

        results.Should().ContainSingle(r => r.Chunk.DocumentId == "doc-ko");
    }

    [Fact]
    public async Task Search_WithDocumentIdFilter_ReturnsOnlyThatDocument()
    {
        var service = CreateService();
        await service.IndexChunksAsync(
        [
            Chunk("doc-1", "provisioning schema tables relations"),
            Chunk("doc-2", "provisioning is also discussed in this other document")
        ]);

        var results = await service.SearchAsync(
            "provisioning",
            new KeywordSearchOptions { DocumentIdFilter = "doc-2" });
        service.Dispose();

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(r => r.Chunk.DocumentId == "doc-2");
    }

    /// <summary>
    /// Exercises the array predicate with more term ids than a single inlined statement would
    /// comfortably carry — the reason this backend leaves the batch size unbounded.
    /// </summary>
    [Fact]
    public async Task Indexing_ManyDistinctTerms_KeepsEveryTermSearchable()
    {
        const int termCount = 6_000;
        var content = string.Join(' ', Enumerable.Range(1, termCount).Select(i => $"t{i:D5}"));

        var service = CreateService();
        await service.IndexChunksAsync([Chunk("doc-wide", content)]);

        foreach (var term in new[] { "t00001", "t03000", "t05999" })
        {
            var results = await service.SearchAsync(term);
            results.Should().ContainSingle($"'{term}' was indexed and its document frequency must be set")
                .Which.Chunk.DocumentId.Should().Be("doc-wide");
        }

        service.Dispose();
    }

    /// <summary>
    /// The index must be usable in a database that already holds other tables — both reporting
    /// consumers point FluxIndex at a database shared with their application schema.
    /// </summary>
    [Fact]
    public async Task EnsureSchema_InADatabaseThatAlreadyHasTables_Succeeds()
    {
        await using (var connection = new Npgsql.NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS consumer_owned_table (id integer PRIMARY KEY)";
            await command.ExecuteNonQueryAsync();
        }

        var service = CreateService();
        var act = async () => await service.EnsureSchemaAsync();

        await act.Should().NotThrowAsync();
        await service.IndexChunksAsync([Chunk("doc-1", "shared database provisioning")]);
        (await service.SearchAsync("provisioning")).Should().NotBeEmpty();
        service.Dispose();
    }
}
