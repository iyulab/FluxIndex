using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Contract;

/// <summary>
/// Shared contract for <see cref="IKeywordSearchService.GetDocumentFrequenciesAsync"/>: how many indexed chunks hold
/// each term, looked up in one call. Before it existed a consumer that needed a term's rarity either inverted
/// <see cref="IKeywordSearchService.GetIDF"/> (a formula the index does not promise to keep) or ran one scoring search
/// per term. Derive a concrete class per implementation and implement <see cref="CreateServiceAsync"/>.
/// </summary>
public abstract class KeywordSearchDocumentFrequencyContractSuite
{
    /// <summary>Creates a fresh, empty keyword index.</summary>
    protected abstract Task<IKeywordSearchService> CreateServiceAsync();

    private static DocumentChunk Chunk(string id, string content, string documentId = "doc-1") => new()
    {
        Id = id,
        DocumentId = documentId,
        Content = content,
        TokenCount = 3
    };

    [Fact]
    public async Task CountsTheChunksHoldingEachTerm_AndZeroForAnUnknownTerm()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync();
        await service.IndexChunksAsync([
            Chunk("a", "receipt number zx4417 attached"),
            Chunk("b", "receipt for lunch"),
            Chunk("c", "another receipt for travel"),
        ], ct);

        var df = await service.GetDocumentFrequenciesAsync(["receipt", "zx4417", "absent"], ct);

        Assert.Equal(3, df["receipt"]);
        Assert.Equal(1, df["zx4417"]);
        Assert.Equal(0, df["absent"]);
        Assert.Equal(3, df.Count);
    }

    [Fact]
    public async Task AnswersUnderTheCallersSpelling_MatchingCaseInsensitively()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync();
        await service.IndexChunksAsync([Chunk("a", "receipt attached"), Chunk("b", "receipt again")], ct);

        var df = await service.GetDocumentFrequenciesAsync(["RECEIPT", "Receipt"], ct);

        Assert.Equal(2, df["RECEIPT"]);
        Assert.Equal(2, df["Receipt"]);
    }

    [Fact]
    public async Task ReindexingAChunk_DoesNotCountItTwice_AndDeletingItLowersTheCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync();
        await service.IndexChunksAsync([Chunk("a", "receipt attached"), Chunk("b", "receipt again")], ct);
        await service.IndexChunksAsync([Chunk("a", "receipt attached twice")], ct);

        Assert.Equal(2, (await service.GetDocumentFrequenciesAsync(["receipt"], ct))["receipt"]);

        await service.DeleteChunkAsync("b", ct);

        Assert.Equal(1, (await service.GetDocumentFrequenciesAsync(["receipt"], ct))["receipt"]);
    }

    [Fact]
    public async Task AChunkWithMoreDistinctTermsThanOneStatementHolds_KeepsEveryTermsCount()
    {
        // Document frequency is maintained in batches of term ids (a backend caps how many one statement names); a
        // chunk past that size must come out exactly as a small one does, in every batch, on index and on delete.
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync();
        var words = Enumerable.Range(0, 6_000).Select(i => $"w{i:D5}x").ToArray();
        await service.IndexChunksAsync([Chunk("big", string.Join(' ', words)), Chunk("small", "w00000x w05999x")], ct);

        var df = await service.GetDocumentFrequenciesAsync([words[0], words[2_500], words[5_999]], ct);
        Assert.Equal(2, df[words[0]]);
        Assert.Equal(1, df[words[2_500]]);
        Assert.Equal(2, df[words[5_999]]);

        await service.DeleteChunkAsync("big", ct);

        df = await service.GetDocumentFrequenciesAsync([words[0], words[2_500]], ct);
        Assert.Equal(1, df[words[0]]);
        Assert.Equal(0, df[words[2_500]]);
    }

    [Fact]
    public async Task NoTerms_ReturnsAnEmptyMap()
    {
        var service = await CreateServiceAsync();

        Assert.Empty(await service.GetDocumentFrequenciesAsync([], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ManyTerms_AreAnsweredInOneCall()
    {
        // More terms than one SQL statement's parameter list comfortably holds, so a batched lookup must stitch batches.
        var ct = TestContext.Current.CancellationToken;
        var service = await CreateServiceAsync();
        await service.IndexChunksAsync([Chunk("a", "term0 term1199")], ct);
        var terms = Enumerable.Range(0, 1200).Select(i => $"term{i}").ToList();

        var df = await service.GetDocumentFrequenciesAsync(terms, ct);

        Assert.Equal(1200, df.Count);
        Assert.Equal(1, df["term0"]);
        Assert.Equal(1, df["term1199"]);
        Assert.Equal(0, df["term600"]);
    }
}
