using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// <see cref="BM25SparseRetriever"/> 회귀. 이 구현은 빌더가 등록하는 <b>기본</b> sparse leg 이므로
/// 모든 소비자가 실제로 타는 경로인데, 0.21.5 시점까지 리포에 직접 테스트가 0건이었다.
/// </summary>
public class BM25SparseRetrieverTests
{
    private static BM25SparseRetriever CreateRetriever()
        => new(NullLogger<BM25SparseRetriever>.Instance);

    private static DocumentChunk Chunk(string documentId, string content)
        => DocumentChunk.Create(documentId, content, 0, 1);

    private static async Task<BM25SparseRetriever> IndexedWith(params DocumentChunk[] chunks)
    {
        var retriever = CreateRetriever();
        foreach (var chunk in chunks)
        {
            await retriever.IndexChunkAsync(chunk);
        }

        return retriever;
    }

    [Fact]
    public async Task Search_ReturnsTheChunkContainingTheTerm()
    {
        using var retriever = await IndexedWith(
            Chunk("doc-1", "transaction boundaries in distributed storage"),
            Chunk("doc-2", "unrelated content about photography"));

        var results = await retriever.SearchAsync("transaction");

        results.Should().ContainSingle();
        results[0].Chunk.DocumentId.Should().Be("doc-1");
    }

    /// <summary>
    /// 회귀: BM25 의 비평활 Robertson IDF <c>log((N-df+0.5)/(df+0.5))</c> 는 df 가 문서의 과반이면
    /// <b>음수</b>가 되고, <see cref="KeywordSearchOptions.MinScore"/> 기본값 0 이 그 결과를 통째로 버린다.
    /// 즉 코퍼스에 흔한 용어일수록 키워드 레그가 조용히 아무것도 기여하지 않는다.
    /// Lucene 과 동일한 평활 IDF <c>log(1 + (N-df+0.5)/(df+0.5))</c> 는 항상 양수다.
    /// </summary>
    [Fact]
    public async Task Search_ForATermPresentInEveryDocument_StillReturnsResults()
    {
        using var retriever = await IndexedWith(
            Chunk("doc-1", "provisioning schema relations"),
            Chunk("doc-2", "provisioning cache tables"),
            Chunk("doc-3", "provisioning graph edges"));

        var results = await retriever.SearchAsync("provisioning");

        results.Should().HaveCount(3,
            "a term common to the whole corpus must not be silently dropped by a negative IDF");
        results.Should().OnlyContain(r => r.Score > 0);
    }

    [Fact]
    public async Task Search_ForATermPresentInMostDocuments_RanksThemAll()
    {
        using var retriever = await IndexedWith(
            Chunk("doc-1", "keyword leg keyword leg keyword leg"),
            Chunk("doc-2", "keyword leg mentioned once"),
            Chunk("doc-3", "entirely different subject matter"));

        var results = await retriever.SearchAsync("keyword");

        results.Should().HaveCount(2);
        results[0].Chunk.DocumentId.Should().Be("doc-1", "term frequency must still order the results");
    }

    [Fact]
    public async Task GetIndexStatistics_ReflectsTheIndexedChunks()
    {
        using var retriever = await IndexedWith(
            Chunk("doc-1", "alpha beta gamma"),
            Chunk("doc-2", "beta gamma delta"));

        var statistics = await retriever.GetStatisticsAsync();

        statistics.TotalDocuments.Should().Be(2);
    }
}
