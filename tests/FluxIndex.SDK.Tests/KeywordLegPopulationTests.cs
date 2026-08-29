using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// 0.22.0 B1 의 계약: <b>인덱싱 API 가 키워드(sparse) 인덱스를 채운다.</b>
///
/// 0.21.5 까지 <c>Indexer</c> 는 벡터 스토어만 썼고 키워드 인덱스를 채우는 공식 경로가 없었다
/// (`Indexer` 의존성 9개에 <see cref="IKeywordSearchService"/> 부재). 그 결과 hybrid 의 키워드 레그는
/// "이 프로세스에서 무엇을 검색했는가"에만 채워져 재시작 후 사실상 vector-only 로 강등됐다.
/// </summary>
public class KeywordLegPopulationTests
{
    private static IFluxIndexContext BuildContext()
        => FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .UseInMemoryEmbedding()
            .AddSQLiteStorage()
            .Build();

    [Fact]
    public async Task IndexDocument_PopulatesTheKeywordIndex()
    {
        var context = BuildContext();
        var keywordSearch = context.ServiceProvider.GetRequiredService<IKeywordSearchService>();

        await context.Indexer.IndexDocumentAsync("Distributed systems require careful transaction boundaries", "doc-1", cancellationToken: TestContext.Current.CancellationToken);

        var results = await keywordSearch.SearchAsync("transaction", cancellationToken: TestContext.Current.CancellationToken);

        results.Should().NotBeEmpty(
            "the indexing API must fill the keyword leg — nothing else does, so hybrid search " +
            "would otherwise be vector-only for every document ever indexed");
        results.Should().ContainSingle(r => r.Chunk.DocumentId == "doc-1");
    }

    [Fact]
    public async Task DeleteDocument_RemovesItFromTheKeywordIndex()
    {
        var context = BuildContext();
        var keywordSearch = context.ServiceProvider.GetRequiredService<IKeywordSearchService>();

        await context.Indexer.IndexDocumentAsync("retention policy for archived records", "doc-1", cancellationToken: TestContext.Current.CancellationToken);
        await context.Indexer.IndexDocumentAsync("retention of build artifacts", "doc-2", cancellationToken: TestContext.Current.CancellationToken);

        await context.Indexer.DeleteByDocumentIdAsync("doc-1", TestContext.Current.CancellationToken);

        var results = await keywordSearch.SearchAsync("retention", cancellationToken: TestContext.Current.CancellationToken);

        results.Should().NotContain(r => r.Chunk.DocumentId == "doc-1",
            "a deleted document must not keep matching through the keyword leg");
        results.Should().Contain(r => r.Chunk.DocumentId == "doc-2");
    }

    /// <summary>
    /// Characterization: 같은 <c>documentId</c> 로 <c>IndexDocumentAsync</c> 를 두 번 부르면
    /// 청크 ID 가 매번 새 Guid 이므로 <b>두 레그 모두</b> 중복 항목을 갖는다. 키워드 레그 한정 문제가
    /// 아니라 파이프라인 전체의 기존 거동이며, 두 레그가 <b>서로 어긋나지 않는다</b>는 것이 여기서 고정하는 것이다.
    /// 중복 자체의 처리(업서트 여부)는 별도 판단 대상이다.
    /// </summary>
    [Fact]
    public async Task IndexingTheSameDocumentIdTwice_DuplicatesBothLegsEqually()
    {
        var context = BuildContext();
        var keywordSearch = context.ServiceProvider.GetRequiredService<IKeywordSearchService>();
        var vectorStore = context.ServiceProvider.GetRequiredService<IVectorStore>();

        await context.Indexer.IndexDocumentAsync("provisioning contract", "doc-1", cancellationToken: TestContext.Current.CancellationToken);
        await context.Indexer.IndexDocumentAsync("provisioning contract", "doc-1", cancellationToken: TestContext.Current.CancellationToken);

        var keywordResults = await keywordSearch.SearchAsync("provisioning", cancellationToken: TestContext.Current.CancellationToken);
        var storedChunks = await vectorStore.GetByDocumentIdAsync("doc-1", TestContext.Current.CancellationToken);

        keywordResults.Should().HaveCount(storedChunks.Count(),
            "the keyword leg must hold exactly the chunks the vector store holds — a mismatch is how " +
            "hybrid search starts returning results one leg cannot explain");
    }

    /// <summary>
    /// <c>UpdateDocumentAsync</c> 는 벡터 청크를 지우고 다시 쓴다. 키워드 레그가 대칭으로 정리되지 않으면
    /// 구 본문의 postings 가 남아 이미 존재하지 않는 텍스트로 매치된다.
    /// </summary>
    [Fact]
    public async Task UpdateDocument_RemovesTheOldContentFromTheKeywordIndex()
    {
        var context = BuildContext();
        var keywordSearch = context.ServiceProvider.GetRequiredService<IKeywordSearchService>();

        await context.Indexer.IndexDocumentAsync("superseded provisioning contract", "doc-1", cancellationToken: TestContext.Current.CancellationToken);

        var replacement = Core.Domain.Entities.Document.Create("doc-1");
        replacement.Content = "replacement text about embeddings";
        replacement.AddChunk(Core.Domain.Entities.DocumentChunk.Create(
            "doc-1", "replacement text about embeddings", 0, 1));
        await context.Indexer.UpdateDocumentAsync("doc-1", replacement, TestContext.Current.CancellationToken);

        var staleResults = await keywordSearch.SearchAsync("superseded", cancellationToken: TestContext.Current.CancellationToken);
        var freshResults = await keywordSearch.SearchAsync("embeddings", cancellationToken: TestContext.Current.CancellationToken);

        staleResults.Should().BeEmpty("the replaced content must not keep matching");
        freshResults.Should().ContainSingle(r => r.Chunk.DocumentId == "doc-1");
    }

    private static IFluxIndexContext BuildContextWithoutKeywordIndexing()
        => FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .UseInMemoryEmbedding()
            .AddSQLiteStorage()
            .WithIndexerOptions(options => options.IndexKeyword = false)
            .Build();

    [Fact]
    public async Task IndexKeywordDisabled_AddsNothingToTheKeywordIndex()
    {
        var context = BuildContextWithoutKeywordIndexing();
        var keywordSearch = context.ServiceProvider.GetRequiredService<IKeywordSearchService>();

        await context.Indexer.IndexDocumentAsync("transaction boundaries", "doc-1", cancellationToken: TestContext.Current.CancellationToken);

        var results = await keywordSearch.SearchAsync("transaction", cancellationToken: TestContext.Current.CancellationToken);

        results.Should().BeEmpty(
            "opting out stops the indexer writing to the keyword index — which also means keyword " +
            "and hybrid search have nothing to match against; it is not a compatibility switch");
    }

    /// <summary>
    /// 옵트아웃은 "추가를 멈춘다"이고 "제거를 멈춘다"가 아니다. 삭제까지 게이트하면 영속 인덱스에
    /// 삭제된 문서의 postings 가 <b>영구히</b> 남아 계속 매치된다 — 옵션 하나로 만들어지는 유령 매치다.
    /// </summary>
    [Fact]
    public async Task IndexKeywordDisabled_StillPropagatesDeletions()
    {
        // 인덱스에 항목이 있는 상태를 먼저 만든다(옵션 on).
        var writer = BuildContext();
        await writer.Indexer.IndexDocumentAsync("retention policy for archived records", "doc-1", cancellationToken: TestContext.Current.CancellationToken);

        var keywordSearch = writer.ServiceProvider.GetRequiredService<IKeywordSearchService>();
        (await keywordSearch.SearchAsync("retention", cancellationToken: TestContext.Current.CancellationToken)).Should().NotBeEmpty();

        // 같은 인덱스를 공유하는 인덱서를 옵션 off 로 만들어 삭제시킨다.
        var indexerWithoutKeywordWrites = new Indexer(
            writer.ServiceProvider.GetRequiredService<IVectorStore>(),
            writer.ServiceProvider.GetRequiredService<IDocumentRepository>(),
            writer.ServiceProvider.GetRequiredService<IEmbeddingService>(),
            writer.ServiceProvider.GetRequiredService<IChunkingService>(),
            new IndexerOptions { IndexKeyword = false },
            keywordSearchService: keywordSearch);

        await indexerWithoutKeywordWrites.DeleteByDocumentIdAsync("doc-1", TestContext.Current.CancellationToken);

        (await keywordSearch.SearchAsync("retention", cancellationToken: TestContext.Current.CancellationToken)).Should().BeEmpty(
            "a deleted document must not keep matching just because keyword indexing was turned off");
    }

    /// <summary>
    /// 인덱서가 쓴 인덱스와 검색이 읽는 인덱스가 <b>같은 인스턴스</b>여야 한다. 기본 구현은
    /// 프로세스 메모리에 인덱스를 들고 있어, scoped 수명이면 스코프마다 빈 인덱스를 받는다.
    /// </summary>
    [Fact]
    public void KeywordSearchService_IsASingleInstanceAcrossScopes()
    {
        var context = BuildContext();

        var fromRoot = context.ServiceProvider.GetRequiredService<IKeywordSearchService>();
        using var scope = context.ServiceProvider.CreateScope();
        var fromScope = scope.ServiceProvider.GetRequiredService<IKeywordSearchService>();

        fromScope.Should().BeSameAs(fromRoot);
    }
}
