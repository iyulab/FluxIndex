using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests.KeywordSearch;

/// <summary>
/// <see cref="SQLiteKeywordSearchService"/> 라운드트립 회귀.
///
/// 이 서비스는 0.21.5 시점까지 리포 어디에서도 참조되지 않는 고아였다 — 등록도 테스트도 0.
/// hybrid keyword leg 영속화(0.22.0 B1)의 백엔드 후보이므로, 설계를 얹기 전에
/// "실제로 도는가"를 먼저 실측한다.
/// </summary>
public class SQLiteKeywordSearchServiceTests : IDisposable
{
    private readonly string _dbPath;

    public SQLiteKeywordSearchServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"fluxindex-keyword-{Guid.NewGuid():N}.db");
    }

    private SQLiteKeywordSearchService CreateService() =>
        new(
            $"Data Source={_dbPath}",
            NullLogger<SQLiteKeywordSearchService>.Instance);

    private static DocumentChunk Chunk(string documentId, string content, int index = 0, int total = 1)
        => DocumentChunk.Create(documentId, content, index, total);

    /// <summary>
    /// 핵심 질문: 인덱싱한 프로세스와 <b>다른 인스턴스</b>가 키워드 매치를 회수하는가.
    /// 재시작 후 hybrid 가 vector-only 로 강등되는 결함의 반대 명제다.
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

        // 재시작 시뮬레이션 — 인스턴스 상태를 전부 버린다.
        var reader = CreateService();
        var results = await reader.SearchAsync("transaction");
        reader.Dispose();

        results.Should().NotBeEmpty("the keyword index must survive the process that wrote it");
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

        results.Should().NotBeEmpty();
        results[0].Chunk.DocumentId.Should().Be("doc-3");
        results.Should().NotContain(r => r.Chunk.DocumentId == "doc-2");
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
    /// 두 소비자(AIMS, All.Manual) 모두 한국어 문서다. 영문 픽스처만으로 GREEN 을 만들면
    /// 소비자에게 무용한 채로 "해결"로 보인다 — 토크나이저의 실제 거동을 여기서 고정한다.
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

    /// <summary>
    /// 토크나이저는 <c>\W+</c> 분할이고 .NET 에서 한글은 <c>\w</c> 다 —
    /// 즉 한글 run 은 절대 분할되지 않는다. 부분 토큰 질의는 매치되지 않는다.
    /// 현행 거동을 고정하는 characterization 이며, CJK 착지 시 뒤집히는 것이 정상이다.
    /// </summary>
    [Fact]
    public async Task KoreanContent_IsNotRetrievable_ByAPartialToken_Characterization()
    {
        var service = CreateService();
        await service.IndexChunksAsync([Chunk("doc-ko", "착수계약서 검토 결과를 정리한 문서")]);

        var results = await service.SearchAsync("착수계");
        service.Dispose();

        results.Should().BeEmpty(
            "the tokenizer never splits a Hangul run, so a prefix query cannot match");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(
                new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"));
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // best effort — 임시 파일이 남아도 테스트 결과에 영향 없다.
        }
    }
}
