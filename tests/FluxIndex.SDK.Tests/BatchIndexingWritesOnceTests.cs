using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.SDK;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// The batch APIs prepare each document on its own and write them together (one replacement per store). The result must
/// be the index the per-document path builds — same chunks, same keyword scores — and it must replace like it does.
/// Until 0.66.0 both batch APIs indexed one document at a time, one keyword transaction per document.
/// </summary>
public sealed class BatchIndexingWritesOnceTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly List<string> _paths = [];

    public void Dispose()
    {
        FluxIndex.Tests.Shared.SqliteTestPools.Release(_paths);
        foreach (var path in _paths.SelectMany(p => new[] { p, p + "-wal", p + "-shm" }))
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    private FluxIndexContext NewContext()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluxindex-batch-{Guid.NewGuid():N}.db");
        _paths.Add(path);
        return (FluxIndexContext)FluxIndexContext.CreateBuilder()
            .UseSQLite(path)
            .AddSQLiteStorage()
            .ConfigureServices(s => s.AddSingleton<ITextAnalyzer>(CjkBigramTextAnalyzer.Instance))
            .SuppressStartupMessages()
            .Build();
    }

    // Non-trivial on purpose: shared vocabulary (document frequency > 1), Korean bigrams, a long document that splits
    // into several chunks, and metadata that filters read.
    private static List<(string Id, string Content, Dictionary<string, object>? Metadata)> Corpus() =>
    [
        ("d1", "노트북 배터리가 금방 닳아요. 충전기를 바꿔도 같습니다.", new() { ["dept"] = "it" }),
        ("d2", "노트북 화면이 업데이트 뒤에 깜박입니다.", new() { ["dept"] = "it" }),
        ("d3", "사무실 프린터 토너 교체 요청", new() { ["dept"] = "ops" }),
        ("d4", string.Join(" ", Enumerable.Range(0, 300).Select(i => $"배터리{i % 7} 점검{i}")), new() { ["dept"] = "it" }),
        ("d5", "Laptop battery drains quickly after the update", null),
    ];

    private static readonly string[] Queries = ["배터리", "노트북", "프린터 토너", "battery update", "점검3"];

    [Fact]
    public async Task BatchIndex_ProducesTheSameIndex_AsIndexingOneByOne()
    {
        await using var single = NewContext();
        foreach (var (id, content, metadata) in Corpus())
            await single.Indexer.IndexDocumentAsync(content, id, metadata, Ct);

        await using var batch = NewContext();
        var result = await batch.Indexer.IndexDocumentsBatchAsync(Corpus(), cancellationToken: Ct);
        result.SuccessfulDocuments.Should().Be(5);
        result.FailedDocuments.Should().Be(0);

        foreach (var query in Queries)
        {
            var expected = Ranked(await single.Retriever.KeywordSearchAsync(query, 20, cancellationToken: Ct));
            var actual = Ranked(await batch.Retriever.KeywordSearchAsync(query, 20, cancellationToken: Ct));

            expected.Should().NotBeEmpty($"'{query}' matches the corpus");
            actual.Should().Equal(expected, $"'{query}' ranks the same either way");
        }

        var singleStats = await single.ServiceProvider.GetRequiredService<IKeywordSearchService>().GetStatisticsAsync(Ct);
        var batchStats = await batch.ServiceProvider.GetRequiredService<IKeywordSearchService>().GetStatisticsAsync(Ct);
        batchStats.TotalDocuments.Should().Be(singleStats.TotalDocuments).And.BeGreaterThan(5, "d4 splits into several chunks");
    }

    // Equal scores are ordered by chunk id, which is random: compare the ranking with ties put in a fixed order.
    private static List<(string DocumentId, int ChunkIndex, double Score)> Ranked(IEnumerable<FluxIndex.Core.Domain.Models.VectorSearchResult> hits)
        => hits
            .Select(h => (h.DocumentChunk.DocumentId, h.DocumentChunk.ChunkIndex, Score: Math.Round((double)h.Score, 6)))
            .OrderByDescending(h => h.Score).ThenBy(h => h.DocumentId, StringComparer.Ordinal).ThenBy(h => h.ChunkIndex)
            .ToList();

    [Fact]
    public async Task BatchIndex_ReplacesIndexedDocuments_AndRunningItTwiceAddsNothing()
    {
        await using var context = NewContext();
        await context.Indexer.IndexDocumentAsync("오래된 내용: 복사기 고장", "d1", cancellationToken: Ct);

        await context.Indexer.IndexDocumentsBatchAsync(Corpus(), cancellationToken: Ct);
        await context.Indexer.IndexDocumentsBatchAsync(Corpus(), cancellationToken: Ct);

        (await context.Retriever.KeywordSearchAsync("복사기", cancellationToken: Ct)).Should().BeEmpty("d1's old version was replaced");
        var store = context.ServiceProvider.GetRequiredService<IVectorStore>();
        (await store.GetByDocumentIdAsync("d1", Ct)).Should().ContainSingle();
        (await context.Retriever.KeywordSearchAsync("프린터", cancellationToken: Ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task BatchIndex_AnIdGivenTwice_IsWrittenAsItsLastOccurrence()
    {
        await using var context = NewContext();

        await context.Indexer.IndexDocumentsBatchAsync(
            [("d1", "first version about scanners", null), ("d1", "second version about printers", null)],
            cancellationToken: Ct);

        (await context.Retriever.KeywordSearchAsync("scanners", cancellationToken: Ct)).Should().BeEmpty();
        (await context.Retriever.KeywordSearchAsync("printers", cancellationToken: Ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task BatchIndex_ADocumentThatCannotBePrepared_FailsAlone()
    {
        await using var context = NewContext();

        var result = await context.Indexer.IndexDocumentsBatchAsync(
            [("d1", "printers and scanners", null), ("d2", "   ", null), ("d3", "scanners only", null)],
            cancellationToken: Ct);

        result.SuccessfulDocuments.Should().Be(2);
        result.FailedDocuments.Should().Be(1);
        result.Results.Should().ContainSingle().Which.DocumentId.Should().Be("d2");
        (await context.Retriever.KeywordSearchAsync("scanners", cancellationToken: Ct)).Should().HaveCount(2);
    }

    [Fact]
    public async Task IndexBatchAsync_WritesTheDocuments_AndReplacesLikeIndexing()
    {
        await using var context = NewContext();
        await context.Indexer.IndexDocumentAsync("old text about fax machines", "d1", cancellationToken: Ct);

        var documents = Corpus().Take(3).Select(c =>
        {
            var document = Document.Create(c.Id);
            document.Content = c.Content;
            document.Chunks = [DocumentChunk.Create(c.Id, c.Content, 0, 1)];
            return document;
        }).ToList();

        var ids = (await context.Indexer.IndexBatchAsync(documents, parallelism: 3, cancellationToken: Ct)).ToList();

        ids.Should().Equal("d1", "d2", "d3");
        (await context.Retriever.KeywordSearchAsync("fax", cancellationToken: Ct)).Should().BeEmpty();
        (await context.Retriever.KeywordSearchAsync("노트북", cancellationToken: Ct)).Should().HaveCount(2);
    }
}
