using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.SDK;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// With no embedding service the context is keyword-only: chunks are stored without vectors, keyword and hybrid search
/// find them through the keyword index, and a vector search fails with a message naming what is missing. Until 0.65.0
/// the builder silently registered random placeholder vectors instead, so a store without an embedder was filled with
/// vectors that mean nothing and a later real embedder needed a full re-index.
/// </summary>
public class KeywordOnlyIndexingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IFluxIndexContext KeywordOnlyContext()
        => FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .AddSQLiteStorage()
            .Build();

    private static Dictionary<string, object> Vault(string name) => new() { ["vault"] = name };

    [Fact]
    public async Task WithoutAnEmbedder_DocumentsAreFoundByKeyword_AndStoredWithoutVectors()
    {
        var context = KeywordOnlyContext();
        context.Indexer.IsKeywordOnly.Should().BeTrue();
        context.ServiceProvider.GetRequiredService<IEmbeddingService>().Should().BeSameAs(NoEmbeddingService.Instance);

        await context.Indexer.IndexDocumentAsync("Distributed systems require careful transaction boundaries", "doc-1", Vault("a"), Ct);

        var hits = (await context.Retriever.KeywordSearchAsync("transaction", cancellationToken: Ct)).ToList();
        hits.Should().ContainSingle(h => h.DocumentChunk.DocumentId == "doc-1");

        var stored = await context.ServiceProvider.GetRequiredService<IVectorStore>().GetByDocumentIdAsync("doc-1", Ct);
        stored.Should().NotBeEmpty().And.OnlyContain(c => c.Embedding == null, "no placeholder vector is written");
    }

    [Fact]
    public async Task WithAnEmbedder_TheSameIndexCall_StoresVectors()
    {
        // Positive control for the fact above: "no vector was written" must not pass because nothing ever writes one.
        var context = FluxIndexContext.CreateBuilder().UseSQLiteInMemory().UseInMemoryEmbedding().AddSQLiteStorage().Build();
        context.Indexer.IsKeywordOnly.Should().BeFalse();

        await context.Indexer.IndexDocumentAsync("Distributed systems require careful transaction boundaries", "doc-1", Vault("a"), Ct);

        var stored = await context.ServiceProvider.GetRequiredService<IVectorStore>().GetByDocumentIdAsync("doc-1", Ct);
        stored.Should().NotBeEmpty().And.OnlyContain(c => c.Embedding != null && c.Embedding.Length > 0);
    }

    [Fact]
    public async Task WithoutAnEmbedder_VectorSearch_SaysHowToAddOne()
    {
        var context = KeywordOnlyContext();
        await context.Indexer.IndexDocumentAsync("Distributed systems require careful transaction boundaries", "doc-1", Vault("a"), Ct);

        var act = () => context.Retriever.SearchAsync("transaction boundaries", cancellationToken: Ct);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*keyword-only*UseEmbeddingService*");
    }

    [Fact]
    public async Task WithoutAnEmbedder_HybridSearch_RunsTheKeywordLegAlone()
    {
        var context = KeywordOnlyContext();
        await context.Indexer.IndexDocumentAsync("Distributed systems require careful transaction boundaries", "doc-1", Vault("a"), Ct);

        var hits = (await context.Retriever.HybridSearchAsync("transaction", "transaction boundaries", cancellationToken: Ct)).ToList();

        hits.Should().Contain(h => h.DocumentChunk.DocumentId == "doc-1");
    }

    [Fact]
    public async Task KeywordSearch_WithAFilter_FindsAScopeThatLosesTheGlobalRanking()
    {
        // The filter used to be applied to the top MaxResults of the whole index, so a vault whose chunks rank below
        // other vaults' got nothing back for a query that matches it.
        var context = KeywordOnlyContext();
        for (var i = 0; i < 8; i++)
        {
            await context.Indexer.IndexDocumentAsync(
                "transaction transaction transaction ledger entries", $"b-{i}", Vault("b"), Ct);
        }
        await context.Indexer.IndexDocumentAsync(
            "A long note that mentions a transaction once among many other unrelated words about gardening and weather",
            "a-1", Vault("a"), Ct);

        var hits = (await context.Retriever.KeywordSearchAsync("transaction", maxResults: 3, filter: Vault("a"), cancellationToken: Ct)).ToList();

        hits.Should().ContainSingle(h => h.DocumentChunk.DocumentId == "a-1");
    }
}
