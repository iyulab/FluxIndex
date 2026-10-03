using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.SDK;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Indexing a document under an id that is already indexed replaces it: the previous version's chunks stop matching,
/// the stored record is the new one, and other documents are untouched. Until 0.66.0 the new chunks were stored beside
/// the old ones (chunk ids are not derived from the document), so a document existed in two versions at once and the
/// old text kept matching searches — across restarts, in a file-backed index.
/// </summary>
public sealed class ReindexReplacesDocumentTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fluxindex-reindex-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        FluxIndex.Tests.Shared.SqliteTestPools.Release(_dbPath);
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    private IFluxIndexContext FileContext()
        => FluxIndexContext.CreateBuilder().UseSQLite(_dbPath).AddSQLiteStorage().SuppressStartupMessages().Build();

    public static TheoryData<string> Setups => new() { "sqlite-file-keyword-only", "sqlite-memory-embedder", "in-memory-embedder" };

    private IFluxIndexContext Context(string setup) => setup switch
    {
        "sqlite-file-keyword-only" => FileContext(),
        "sqlite-memory-embedder" => FluxIndexContext.CreateBuilder().UseSQLiteInMemory().UseInMemoryEmbedding().AddSQLiteStorage().SuppressStartupMessages().Build(),
        // The in-memory store does not override ReplaceDocumentsAsync: this runs the interface's default implementation.
        "in-memory-embedder" => FluxIndexContext.CreateBuilder().UseInMemoryEmbedding().SuppressStartupMessages().Build(),
        _ => throw new ArgumentOutOfRangeException(nameof(setup)),
    };

    [Theory]
    [MemberData(nameof(Setups))]
    public async Task IndexingTheSameIdAgain_TheOldTextNoLongerMatches(string setup)
    {
        await using var context = (FluxIndexContext)Context(setup);

        await context.Indexer.IndexDocumentAsync("Laptop battery drains quickly", "d1", cancellationToken: Ct);
        await context.Indexer.IndexDocumentAsync("Laptop screen flickers after the update", "d1", cancellationToken: Ct);

        (await context.Retriever.KeywordSearchAsync("battery", cancellationToken: Ct)).Should().BeEmpty(
            "the first version was replaced, not kept beside the second");
        (await context.Retriever.KeywordSearchAsync("flickers", cancellationToken: Ct))
            .Should().ContainSingle().Which.DocumentChunk.DocumentId.Should().Be("d1");

        var stored = (await context.ServiceProvider.GetRequiredService<IVectorStore>().GetByDocumentIdAsync("d1", Ct)).ToList();
        stored.Should().ContainSingle().Which.Content.Should().Contain("flickers");
    }

    [Theory]
    [MemberData(nameof(Setups))]
    public async Task IndexingTheSameIdAgain_LeavesOtherDocumentsAlone(string setup)
    {
        await using var context = (FluxIndexContext)Context(setup);

        await context.Indexer.IndexDocumentAsync("Laptop battery drains quickly", "d1", cancellationToken: Ct);
        await context.Indexer.IndexDocumentAsync("Phone battery lasts two days", "d2", cancellationToken: Ct);
        await context.Indexer.IndexDocumentAsync("Laptop screen flickers after the update", "d1", cancellationToken: Ct);

        (await context.Retriever.KeywordSearchAsync("battery", cancellationToken: Ct))
            .Should().ContainSingle().Which.DocumentChunk.DocumentId.Should().Be("d2");
    }

    [Theory]
    [MemberData(nameof(Setups))]
    public async Task AShorterNewVersion_DropsTheOldTailChunks(string setup)
    {
        await using var context = (FluxIndexContext)Context(setup);
        var longText = string.Join(" ", Enumerable.Range(0, 400).Select(i => $"word{i}"));

        await context.Indexer.IndexDocumentAsync(longText, "d1", cancellationToken: Ct);
        var store = context.ServiceProvider.GetRequiredService<IVectorStore>();
        (await store.GetByDocumentIdAsync("d1", Ct)).Should().HaveCountGreaterThan(1, "the long version splits into several chunks");

        await context.Indexer.IndexDocumentAsync("short replacement", "d1", cancellationToken: Ct);

        (await store.GetByDocumentIdAsync("d1", Ct)).Should().ContainSingle();
        (await context.Retriever.KeywordSearchAsync("word399", cancellationToken: Ct)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Setups))]
    public async Task GetDocumentAsync_AfterReindex_ReturnsTheNewVersionOnce(string setup)
    {
        await using var context = (FluxIndexContext)Context(setup);

        await context.Indexer.IndexDocumentAsync("Laptop battery drains quickly", "d1", cancellationToken: Ct);
        await context.Indexer.IndexDocumentAsync("Laptop screen flickers after the update", "d1", cancellationToken: Ct);

        var first = await context.Retriever.GetDocumentAsync("d1", Ct);
        var second = await context.Retriever.GetDocumentAsync("d1", Ct);

        first!.Content.Should().Contain("flickers");
        first.Chunks.Should().ContainSingle();
        second!.Chunks.Should().ContainSingle("reading a document does not grow it");
    }

    [Fact]
    public async Task IndexingTheSameIdAgain_TheReplacementSurvivesARestart()
    {
        await using (var context = FileContext())
        {
            await context.Indexer.IndexDocumentAsync("Laptop battery drains quickly", "d1", cancellationToken: Ct);
            await context.Indexer.IndexDocumentAsync("Laptop screen flickers after the update", "d1", cancellationToken: Ct);
        }

        await using var reopened = FileContext();
        (await reopened.Retriever.KeywordSearchAsync("battery", cancellationToken: Ct)).Should().BeEmpty();
        (await reopened.Retriever.KeywordSearchAsync("flickers", cancellationToken: Ct)).Should().ContainSingle();
        (await reopened.ServiceProvider.GetRequiredService<IKeywordSearchService>().GetStatisticsAsync(Ct))
            .TotalDocuments.Should().Be(1, "the replaced chunk no longer counts in the corpus statistics");
    }

    [Fact]
    public async Task UpdateDocumentAsync_ReplacesLikeIndexing()
    {
        await using var context = (FluxIndexContext)Context("sqlite-file-keyword-only");
        await context.Indexer.IndexDocumentAsync("Laptop battery drains quickly", "d1", cancellationToken: Ct);

        var updated = Document.Create("d1");
        updated.Content = "Laptop screen flickers after the update";
        updated.AddChunk(DocumentChunk.Create("d1", updated.Content, 0, 1));
        await context.Indexer.UpdateDocumentAsync("d1", updated, Ct);

        (await context.Retriever.KeywordSearchAsync("battery", cancellationToken: Ct)).Should().BeEmpty();
        (await context.Retriever.KeywordSearchAsync("flickers", cancellationToken: Ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task ADocumentWhoseChunksBelongToAnotherId_DoesNotReplaceThatId()
    {
        // FileFlux's streaming path indexes an ultra-large file as several "partial" documents with ids of their own,
        // each carrying chunks of the parent document. Replacing is keyed by the indexed document's id, so one partial
        // must not remove the chunks another partial stored under the parent.
        await using var context = (FluxIndexContext)Context("sqlite-file-keyword-only");

        foreach (var (partialId, text) in new[] { ("p_partial_1", "first part about batteries"), ("p_partial_2", "second part about screens") })
        {
            var partial = Document.Create(partialId);
            partial.Content = text;
            partial.Chunks = [DocumentChunk.Create("p", text, 0, 1)];
            await context.Indexer.IndexDocumentAsync(partial, Ct);
        }

        (await context.ServiceProvider.GetRequiredService<IVectorStore>().GetByDocumentIdAsync("p", Ct)).Should().HaveCount(2);
        (await context.Retriever.KeywordSearchAsync("batteries", cancellationToken: Ct)).Should().ContainSingle();
    }
}
