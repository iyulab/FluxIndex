using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Storage.SQLite;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// B1 Acceptance #1 — <b>사용자 가시 행동</b>: 한 프로세스가 인덱싱하고, 재시작한 프로세스가
/// 키워드 매치로 그 문서를 회수한다.
///
/// 0.21.5 까지 <c>IKeywordSearchService</c> 는 무조건 in-memory BM25 였고 영속 구현은 등록되지 않았다.
/// 그래서 재시작 후 hybrid 는 사실상 vector-only 였다(0.21.5 가 경고로 가시화한 상태).
/// 여기서 "프로세스 재시작"은 컨텍스트를 버리고 같은 데이터베이스 파일로 새로 세우는 것으로 모형화한다 —
/// 인메모리 상태를 전부 버리는 것이 요점이다.
/// </summary>
public class KeywordLegPersistenceTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"fluxindex_keywordleg_{Guid.NewGuid():N}.db");

    private IFluxIndexContext NewProcess()
        => FluxIndexContext.CreateBuilder()
            .UseSQLite(_dbPath)
            .UseInMemoryEmbedding()
            .AddSQLiteStorage()
            .Build();

    [Fact]
    public async Task ADocumentIndexedByOneProcess_IsKeywordSearchableByTheNext()
    {
        var writer = NewProcess();
        await writer.Indexer.IndexDocumentAsync(
            "Distributed transaction boundaries in the provisioning layer", "doc-1");
        (writer as IDisposable)?.Dispose();

        var reader = NewProcess();
        var keywordSearch = reader.ServiceProvider.GetRequiredService<IKeywordSearchService>();
        var results = await keywordSearch.SearchAsync("provisioning");
        (reader as IDisposable)?.Dispose();

        results.Should().ContainSingle(r => r.Chunk.DocumentId == "doc-1",
            "the keyword leg must survive the process that indexed it");
    }

    [Fact]
    public async Task KoreanDocumentIndexedByOneProcess_IsKeywordSearchableByTheNext()
    {
        var writer = NewProcess();
        await writer.Indexer.IndexDocumentAsync("착수계약서 검토 결과를 정리한 문서", "doc-ko");
        (writer as IDisposable)?.Dispose();

        var reader = NewProcess();
        var keywordSearch = reader.ServiceProvider.GetRequiredService<IKeywordSearchService>();
        var results = await keywordSearch.SearchAsync("착수계약서");
        (reader as IDisposable)?.Dispose();

        results.Should().ContainSingle(r => r.Chunk.DocumentId == "doc-ko",
            "both dogfooding consumers index Korean documents — an English-only fixture would pass " +
            "while leaving them no better off");
    }

    [Fact]
    public void Build_OnTheSQLitePath_RegistersThePersistentKeywordBackend()
    {
        var context = NewProcess();

        var keywordSearch = context.ServiceProvider.GetRequiredService<IKeywordSearchService>();

        keywordSearch.Should().BeOfType<SQLiteKeywordSearchService>(
            "the SDK's in-memory fallback must not win over the storage package's registration — " +
            "storage registrations run first, so the fallback has to be a TryAdd");
        (context as IDisposable)?.Dispose();
    }

    [Fact]
    public void Build_ProvisionsTheKeywordIndexSchema()
    {
        var context = NewProcess();
        (context as IDisposable)?.Dispose();

        var tables = new List<string>();
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            tables.Add(reader.GetString(0));

        tables.Should().Contain("bm25_terms");
        tables.Should().Contain("bm25_postings");
        tables.Should().Contain("bm25_chunks");
    }

    [Fact]
    public async Task ADocumentDeletedByOneProcess_StaysDeletedForTheNext()
    {
        var writer = NewProcess();
        await writer.Indexer.IndexDocumentAsync("retention policy for archived records", "doc-1");
        await writer.Indexer.IndexDocumentAsync("retention of build artifacts", "doc-2");
        await writer.Indexer.DeleteByDocumentIdAsync("doc-1");
        (writer as IDisposable)?.Dispose();

        var reader = NewProcess();
        var keywordSearch = reader.ServiceProvider.GetRequiredService<IKeywordSearchService>();
        var results = await keywordSearch.SearchAsync("retention");
        (reader as IDisposable)?.Dispose();

        results.Should().NotContain(r => r.Chunk.DocumentId == "doc-1",
            "a persisted keyword index makes stale postings outlive the process too");
        results.Should().Contain(r => r.Chunk.DocumentId == "doc-2");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        foreach (var path in new[] { _dbPath, Path.ChangeExtension(_dbPath, null) + "-entitygraph.db" })
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException) { }
        }
    }
}
