using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// «Documents like this one»: the whole document is the query (not its first chunk), one result per document with its
/// real score, scoped by a filter, and available keyword-only. Chunks indexed keyword-only get their vectors later
/// through the backfill.
/// </summary>
public class FindSimilarAndBackfillTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, object> Vault(string name) => new() { ["vault"] = name };

    private static IFluxIndexContext Context(bool withEmbedder)
    {
        var builder = FluxIndexContext.CreateBuilder().UseSQLiteInMemory().AddSQLiteStorage();
        if (withEmbedder)
            builder = builder.UseInMemoryEmbedding();
        return builder.Build();
    }

    private const string Contract = "The supplier shall deliver the goods within thirty days. Payment is due on delivery of the goods.";
    private const string ContractLike = "Goods are delivered by the supplier; payment of the invoice is due within thirty days of delivery.";
    private const string Garden = "Tomatoes need full sun and regular watering through the summer months.";

    [Fact]
    public async Task KeywordOnly_FindsDocumentsSharingTheSourcesTerms_OnePerDocument_WithoutTheSource()
    {
        var context = Context(withEmbedder: false);
        await context.Indexer.IndexDocumentAsync(Contract, "contract", Vault("a"), Ct);
        await context.Indexer.IndexDocumentAsync(ContractLike, "contract-like", Vault("a"), Ct);
        await context.Indexer.IndexDocumentAsync(Garden, "garden", Vault("a"), Ct);

        var similar = (await context.Retriever.FindSimilarAsync("contract", cancellationToken: Ct)).ToList();

        similar.Should().NotBeEmpty();
        similar[0].DocumentChunk.DocumentId.Should().Be("contract-like");
        similar.Should().NotContain(r => r.DocumentChunk.DocumentId == "contract", "the source is never its own neighbour");
        similar.Select(r => r.DocumentChunk.DocumentId).Should().OnlyHaveUniqueItems("one result per document");
        similar.Select(r => r.Rank).Should().Equal(Enumerable.Range(1, similar.Count));
    }

    [Fact]
    public async Task Filter_ScopesTheCandidates()
    {
        var context = Context(withEmbedder: false);
        await context.Indexer.IndexDocumentAsync(Contract, "contract", Vault("a"), Ct);
        await context.Indexer.IndexDocumentAsync(ContractLike, "contract-like", Vault("b"), Ct);
        await context.Indexer.IndexDocumentAsync(Contract + " Signed copy.", "contract-copy", Vault("a"), Ct);

        var similar = (await context.Retriever.FindSimilarAsync("contract", filter: Vault("b"), cancellationToken: Ct)).ToList();

        similar.Should().ContainSingle().Which.DocumentChunk.DocumentId.Should().Be("contract-like");
    }

    [Fact]
    public async Task WithAnEmbedder_ReportsTheRealSimilarity_NotAConstant()
    {
        var context = Context(withEmbedder: true);
        await context.Indexer.IndexDocumentAsync(Contract, "contract", Vault("a"), Ct);
        await context.Indexer.IndexDocumentAsync(Contract, "same-text", Vault("a"), Ct);
        await context.Indexer.IndexDocumentAsync(Garden, "garden", Vault("a"), Ct);

        var similar = (await context.Retriever.FindSimilarAsync("contract", minScore: -1f, cancellationToken: Ct)).ToList();

        // The deterministic test embedder gives identical text identical vectors: the copy is the nearest neighbour at 1.
        similar[0].DocumentChunk.DocumentId.Should().Be("same-text");
        similar[0].Score.Should().BeApproximately(1.0, 1e-3);
        similar.Should().Contain(r => r.DocumentChunk.DocumentId == "garden" && r.Score < 0.99,
            "a different document's score is its own similarity, not the old constant 1.0");
    }

    [Fact]
    public async Task UnknownDocument_HasNoSimilarDocuments()
    {
        var context = Context(withEmbedder: false);
        (await context.Retriever.FindSimilarAsync("nobody", cancellationToken: Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Backfill_FillsTheVectorsOfChunksIndexedKeywordOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluxindex_backfill_{Guid.NewGuid():N}.db");
        try
        {
            var keywordOnly = FluxIndexContext.CreateBuilder().UseSQLite(path).AddSQLiteStorage().Build();
            await keywordOnly.Indexer.IndexDocumentAsync(Contract, "contract", Vault("a"), Ct);
            (keywordOnly as IDisposable)?.Dispose();

            var withEmbedder = FluxIndexContext.CreateBuilder().UseSQLite(path).AddSQLiteStorage().UseInMemoryEmbedding().Build();
            var store = withEmbedder.ServiceProvider.GetRequiredService<IVectorStore>();
            (await store.GetByDocumentIdAsync("contract", Ct)).Should().OnlyContain(c => c.Embedding == null);

            var filled = await withEmbedder.Indexer.BackfillEmbeddingsAsync("contract", Ct);

            filled.Should().BeGreaterThan(0);
            (await store.GetByDocumentIdAsync("contract", Ct)).Should().OnlyContain(c => c.Embedding != null && c.Embedding.Length > 0);
            (await withEmbedder.Indexer.BackfillEmbeddingsAsync("contract", Ct)).Should().Be(0, "a second run has nothing left to fill");
            (await withEmbedder.Retriever.SearchAsync("supplier goods", minScore: -1f, cancellationToken: Ct))
                .Should().Contain(r => r.DocumentChunk.DocumentId == "contract", "the backfilled vectors are searchable");
            (withEmbedder as IDisposable)?.Dispose();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Backfill_WithoutAnEmbedder_Throws()
    {
        var context = Context(withEmbedder: false);
        var act = () => context.Indexer.BackfillEmbeddingsAsync("contract", Ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*keyword-only*");
    }

    [Fact]
    public async Task Delete_ReportsTheChunksItRemoved_EvenWhenTheProcessLocalRecordIsGone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluxindex_delete_{Guid.NewGuid():N}.db");
        try
        {
            var writer = FluxIndexContext.CreateBuilder().UseSQLite(path).AddSQLiteStorage().Build();
            await writer.Indexer.IndexDocumentAsync(Contract, "contract", Vault("a"), Ct);
            (writer as IDisposable)?.Dispose();

            // A new context (a restarted process) has no in-memory record of the document; the store still has it.
            var restarted = FluxIndexContext.CreateBuilder().UseSQLite(path).AddSQLiteStorage().Build();
            (await restarted.Indexer.DeleteByDocumentIdAsync("contract", Ct)).Should().BeTrue();
            (await restarted.Indexer.DeleteByDocumentIdAsync("contract", Ct)).Should().BeFalse("nothing is left to remove");
            (restarted as IDisposable)?.Dispose();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }
}
