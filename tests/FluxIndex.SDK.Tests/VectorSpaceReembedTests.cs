using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.SDK;
using FluxIndex.SDK.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// The embedding library starts computing different vectors for the same model id (a new vector-space revision): the
/// indexer records on each vector the revision it was computed in, the store counts the vectors outside the current one,
/// and the re-embed moves only those — batch by batch, so a call cut short continues where it stopped.
/// </summary>
public sealed class VectorSpaceReembedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (IFluxIndexContext Context, RevisionEmbedder Embedder) NewContext()
    {
        var embedder = new RevisionEmbedder { Revision = "A" };
        var context = FluxIndexContext.CreateBuilder()
            .UseEmbeddingService(embedder)
            .SuppressStartupMessages()
            .Build();
        return (context, embedder);
    }

    private static async Task IndexAsync(IFluxIndexContext context, int documents)
    {
        for (var i = 0; i < documents; i++)
            await context.Indexer.IndexDocumentAsync($"document number {i} about printers and paper", $"d{i:00}", cancellationToken: Ct);
    }

    [Fact]
    public async Task IndexedVectors_CarryTheRevisionTheyWereComputedIn()
    {
        var (context, _) = NewContext();
        await IndexAsync(context, 3);

        var store = context.ServiceProvider.GetRequiredService<IVectorStore>();
        (await store.GetByDocumentIdAsync("d00", Ct)).Should().OnlyContain(c => c.VectorSpaceRevision == "A");
        (await context.Indexer.CountVectorsOutsideCurrentSpaceAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task ANewRevision_ReembedsOnlyTheVectorsOutsideIt_AndACutShortCallContinues()
    {
        var (context, embedder) = NewContext();
        await IndexAsync(context, 5);
        var store = context.ServiceProvider.GetRequiredService<IVectorStore>();
        var total = await store.CountAsync(Ct);

        embedder.Revision = "B";
        (await context.Indexer.CountVectorsOutsideCurrentSpaceAsync(Ct)).Should().Be(total);

        // The first call is stopped after its first batch has been written.
        embedder.Calls = 0;
        embedder.CancelAfterBatches = 2;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        embedder.Cancel = cts;
        var cut = async () => await context.Indexer.ReembedOutsideCurrentSpaceAsync(batchSize: 2, cts.Token);
        await cut.Should().ThrowAsync<OperationCanceledException>();
        var left = await context.Indexer.CountVectorsOutsideCurrentSpaceAsync(Ct);
        left.Should().BeLessThan(total).And.BeGreaterThan(0);

        embedder.Cancel = null;
        embedder.Calls = 0;
        var moved = await context.Indexer.ReembedOutsideCurrentSpaceAsync(batchSize: 2, Ct);

        moved.Should().Be(left!.Value, "only the vectors the first call did not reach are embedded again");
        (await context.Indexer.CountVectorsOutsideCurrentSpaceAsync(Ct)).Should().Be(0);
        (await context.Indexer.ReembedOutsideCurrentSpaceAsync(cancellationToken: Ct)).Should().Be(0);
        (await store.CountAsync(Ct)).Should().Be(total, "re-embedding rewrites chunks under their own ids");
    }

    [Fact]
    public async Task AnEmbedderWithoutARevision_HasNoSpaceToCompare()
    {
        var (context, embedder) = NewContext();
        embedder.Revision = null;

        var count = async () => await context.Indexer.CountVectorsOutsideCurrentSpaceAsync(Ct);
        await count.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no vector-space revision*");
    }

    [Fact]
    public async Task InMemoryStore_KeepsRevisionsAndChunkIdentityAcrossSaveAndLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluxindex-inmem-{Guid.NewGuid():N}.json");
        try
        {
            var store = new InMemoryVectorStore();
            await store.StoreBatchAsync(
            [
                new DocumentChunk { Id = "c0", DocumentId = "doc", Content = "first", ChunkIndex = 0, TotalChunks = 3, Embedding = [1f, 0f], VectorSpaceRevision = "A",
                    Metadata = new() { ["vault"] = "a" } },
                new DocumentChunk { Id = "c1", DocumentId = "doc", Content = "second", ChunkIndex = 1, TotalChunks = 3, Embedding = [0f, 1f], VectorSpaceRevision = "B" },
                new DocumentChunk { Id = "c2", DocumentId = "doc", Content = "third", ChunkIndex = 2, TotalChunks = 3 },
            ], Ct);
            await store.SaveToFileAsync(path, Ct);

            var loaded = new InMemoryVectorStore();
            await loaded.LoadFromFileAsync(path, Ct);

            var c1 = await loaded.GetAsync("c1", Ct);
            c1!.Id.Should().Be("c1");
            c1.TotalChunks.Should().Be(3);
            c1.VectorSpaceRevision.Should().Be("B");
            (await loaded.GetAsync("c0", Ct))!.Metadata!["vault"].Should().Be("a");
            (await loaded.CountVectorsOutsideSpaceAsync("B", Ct)).Should().Be(1, "c0 is in A; c2 has no vector");
            (await loaded.GetChunkIdsOutsideSpaceAsync("B", 10, Ct)).Should().Equal("c0");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class RevisionEmbedder : IEmbeddingService
    {
        public string? Revision { get; set; }
        public int CancelAfterBatches { get; set; }
        public CancellationTokenSource? Cancel { get; set; }
        public int Calls { get; set; }

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(Embed(text));

        public Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Cancel is not null && Calls >= CancelAfterBatches)
                Cancel.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IEnumerable<float[]>>(texts.Select(Embed).ToList());
        }

        private float[] Embed(string text) => [text.Length, Revision == "B" ? 2f : 1f, 0f];

        public int GetEmbeddingDimension() => 3;
        public string GetModelName() => "revision-test-embedder";
        public EmbeddingIdentity GetIdentity() =>
            new() { Provider = "test", Model = GetModelName(), Dimension = 3, VectorSpaceRevision = Revision };
    }
}
