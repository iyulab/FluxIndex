using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Quantization;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.Core.Tests.Contract;

/// <summary>
/// Shared filter-contract suite for the quantized search paths of <see cref="IQuantizedVectorStore"/>
/// (<see cref="IQuantizedVectorStore.SearchQuantizedAsync"/> and <see cref="IQuantizedVectorStore.SearchWithRerankAsync"/>),
/// which hybrid search takes when <c>UseQuantizedSearch</c> is on. Those paths used to take no filter at all, so a scoped
/// hybrid search returned chunks from outside its scope. Derive a concrete class per store and implement
/// <see cref="CreateStoreAsync"/>.
/// </summary>
public abstract class QuantizedVectorStoreFilterContractSuite
{
    /// <summary>Embedding dimension the seeded chunks use.</summary>
    protected const int Dimension = 8;

    /// <summary>The quantizer the store under test is built with; the query is quantized with the same one.</summary>
    protected IVectorQuantizer Quantizer { get; } = new ScalarQuantizer(
        Options.Create(new QuantizationOptions { Dimension = Dimension }),
        NullLogger<ScalarQuantizer>.Instance);

    /// <summary>Creates a fresh, empty store that quantizes with <see cref="Quantizer"/>.</summary>
    protected abstract Task<IQuantizedVectorStore> CreateStoreAsync();

    /// <summary>
    /// The recall case: closer chunks from another scope fill the candidate window several times over, and the one match
    /// sits past all of them. A store that filters before ranking finds it; one that ranks first and filters (or does not
    /// filter) does not.
    /// </summary>
    [Fact]
    public async Task SearchWithRerank_ScopedSearch_ReturnsTheScopesNearestChunk_PastCloserChunksOfOtherScopes()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeededStoreAsync(ct);

        var results = await store.SearchWithRerankAsync(
            Near(0f), await QuantizeAsync(Near(0f), ct), topK: 3, candidateMultiplier: 2, minScore: 0f,
            filters: new Dictionary<string, object> { ["tenant"] = "wanted-tenant" }, cancellationToken: ct);

        var hit = Assert.Single(results);
        Assert.Equal("wanted", hit.Chunk.DocumentId);
    }

    [Fact]
    public async Task SearchQuantized_ScopedSearch_ReturnsNothingFromOutsideTheScope()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeededStoreAsync(ct);

        var results = await store.SearchQuantizedAsync(
            await QuantizeAsync(Near(0f), ct), topK: 10, minScore: 0f,
            filters: new Dictionary<string, object> { ["tenant"] = "wanted-tenant" }, cancellationToken: ct);

        Assert.All(results, r => Assert.Equal("wanted", r.Chunk.DocumentId));
        Assert.Single(results);
    }

    [Fact]
    public async Task SearchQuantized_CollectionFilter_MatchesAnyElement()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeededStoreAsync(ct);

        var results = await store.SearchQuantizedAsync(
            await QuantizeAsync(Near(0f), ct), topK: 10, minScore: 0f,
            filters: new Dictionary<string, object> { ["tenant"] = new List<string> { "absent-tenant", "wanted-tenant" } },
            cancellationToken: ct);

        var hit = Assert.Single(results);
        Assert.Equal("wanted", hit.Chunk.DocumentId);
    }

    [Fact]
    public async Task SearchWithRerank_DocumentIdScope_ReturnsOnlyThoseDocuments()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeededStoreAsync(ct);

        var results = await store.SearchWithRerankAsync(
            Near(0f), await QuantizeAsync(Near(0f), ct), topK: 10, candidateMultiplier: 3, minScore: 0f,
            filters: new Dictionary<string, object> { [FilterKeys.DocumentId] = new[] { "wanted", "noise-11" } },
            cancellationToken: ct);

        Assert.Equal(["noise-11", "wanted"], results.Select(r => r.Chunk.DocumentId).Order().ToList());
    }

    [Fact]
    public async Task SearchWithRerank_NoFilter_StillRanksEveryScope()
    {
        // Control: without a filter the closer chunks win, so the scoped facts above are about the filter.
        var ct = TestContext.Current.CancellationToken;
        var store = await SeededStoreAsync(ct);

        var results = (await store.SearchWithRerankAsync(
            Near(0f), await QuantizeAsync(Near(0f), ct), topK: 3, candidateMultiplier: 2, minScore: 0f,
            filters: null, cancellationToken: ct)).ToList();

        Assert.Equal(3, results.Count);
        Assert.DoesNotContain(results, r => r.Chunk.DocumentId == "wanted");
    }

    private async Task<IQuantizedVectorStore> SeededStoreAsync(CancellationToken ct)
    {
        var store = await CreateStoreAsync();
        for (var i = 0; i < 12; i++)
            await store.StoreAsync(Chunk($"noise-{i}", "other-tenant", Near(i * 0.0001f)), ct);
        await store.StoreAsync(Chunk("wanted", "wanted-tenant", Far()), ct);
        return store;
    }

    private Task<QuantizedVector> QuantizeAsync(float[] embedding, CancellationToken ct) => Quantizer.QuantizeAsync(embedding, ct);

    private static DocumentChunk Chunk(string documentId, string tenant, float[] embedding) => new()
    {
        Id = Guid.NewGuid().ToString(),
        DocumentId = documentId,
        ChunkIndex = 0,
        Content = $"content for {documentId}",
        Embedding = embedding,
        Metadata = new Dictionary<string, object> { ["tenant"] = tenant }
    };

    private static float[] Near(float jitter)
    {
        var v = new float[Dimension];
        v[0] = 1f - jitter;
        v[1] = 0.05f;
        return v;
    }

    private static float[] Far()
    {
        var v = new float[Dimension];
        v[0] = 0.2f;
        v[Dimension - 1] = 1f;
        return v;
    }
}
