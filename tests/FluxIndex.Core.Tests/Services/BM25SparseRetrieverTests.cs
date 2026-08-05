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

    // === Scope options ===
    //
    // This backend is the default sparse leg, so an option it accepts and ignores is worse here than
    // anywhere else: every consumer who has not configured a SQL backend is on this path. The two
    // implementations must answer the same options the same way or IKeywordSearchService is not a
    // contract, only a shape.

    private static DocumentChunk Tagged(string documentId, string content, string tenant)
    {
        var chunk = DocumentChunk.Create(documentId, content, 0, 1);
        chunk.Metadata = new Dictionary<string, object> { ["tenant"] = tenant };
        return chunk;
    }

    [Fact]
    public async Task Search_MetadataFilter_RestrictsToMatchingChunks()
    {
        using var retriever = await IndexedWith(
            Tagged("doc-a", "provisioning schema", "alpha"),
            Tagged("doc-b", "provisioning schema", "beta"));

        var results = await retriever.SearchAsync("provisioning", new KeywordSearchOptions
        {
            MetadataFilter = new Dictionary<string, object> { ["tenant"] = "alpha" }
        });

        results.Should().ContainSingle().Which.Chunk.DocumentId.Should().Be("doc-a");
    }

    /// <summary>
    /// The same claim the SQL backends make: the restriction is applied before truncation. Here the
    /// wanted document is deliberately the weakest match and MaxResults excludes it globally.
    /// </summary>
    [Fact]
    public async Task Search_MetadataFilter_IsAppliedBeforeTruncation()
    {
        var chunks = new List<DocumentChunk>();
        for (var i = 0; i < 5; i++)
            chunks.Add(Tagged($"other-{i}", "provisioning provisioning provisioning", "beta"));
        chunks.Add(Tagged("ours", "provisioning among several unrelated trailing words here", "alpha"));

        using var retriever = await IndexedWith([.. chunks]);

        var unfiltered = await retriever.SearchAsync("provisioning", new KeywordSearchOptions { MaxResults = 2 });
        unfiltered.Should().NotContain(r => r.Chunk.DocumentId == "ours",
            "otherwise a post-filter would pass this test too");

        var filtered = await retriever.SearchAsync("provisioning", new KeywordSearchOptions
        {
            MaxResults = 2,
            MetadataFilter = new Dictionary<string, object> { ["tenant"] = "alpha" }
        });

        filtered.Should().ContainSingle().Which.Chunk.DocumentId.Should().Be("ours");
    }

    /// <summary>
    /// <c>DocumentIdFilter</c> was declared on the contract and honoured only by the SQL backends;
    /// this one accepted it and searched the whole index.
    /// </summary>
    [Fact]
    public async Task Search_DocumentIdFilter_IsHonoured()
    {
        using var retriever = await IndexedWith(
            Chunk("doc-a", "provisioning schema"),
            Chunk("doc-b", "provisioning schema"));

        var results = await retriever.SearchAsync("provisioning", new KeywordSearchOptions
        {
            DocumentIdFilter = "doc-a"
        });

        results.Should().ContainSingle().Which.Chunk.DocumentId.Should().Be("doc-a");
    }

    [Fact]
    public async Task DeleteByFilter_RemovesMatchingChunksOnly()
    {
        using var retriever = await IndexedWith(
            Tagged("doc-a1", "provisioning", "alpha"),
            Tagged("doc-a2", "provisioning", "alpha"),
            Tagged("doc-b1", "provisioning", "beta"));

        var removed = await retriever.DeleteByFilterAsync(
            new Dictionary<string, object> { ["tenant"] = "alpha" });

        removed.Should().Be(2);
        (await retriever.SearchAsync("provisioning"))
            .Select(r => r.Chunk.DocumentId).Should().BeEquivalentTo(["doc-b1"]);
    }

    [Fact]
    public async Task DeleteByFilter_EmptyFilter_Throws()
    {
        using var retriever = await IndexedWith(Chunk("doc-a", "provisioning"));

        var act = () => retriever.DeleteByFilterAsync(new Dictionary<string, object>());

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
