using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Quantization;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Integration tests (require Docker) for the PostgreSQL vector stores' chunk-id contract.
/// </summary>
/// <remarks>
/// This path had no integration coverage at all — the existing PostgreSQL integration tests cover
/// provisioning and schema, never a store/read round trip — which is why two defects lived here
/// unnoticed: <c>StoreAsync</c> discarded the caller's <c>DocumentChunk.Id</c> and generated its
/// own, while every read path ran the caller's id through <c>Guid.Parse</c> and threw on anything
/// that was not a UUID.
/// </remarks>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public class PostgreSQLVectorStoreChunkIdIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    // Deliberately left as a bare container: pgvector is NOT pre-installed here. The store's own
    // initializer has to install it and still write successfully afterwards, which is the guard for
    // the data-source type-cache ordering defect (installing the extension through the very data
    // source the store then writes with left it holding a catalogue from before `vector` existed).
    public ValueTask InitializeAsync() => new ValueTask(_container.StartAsync());

    public ValueTask DisposeAsync()
    {
        _container.DisposeAsync();
        GC.SuppressFinalize(this);
        return default;
    }

    private static DocumentChunk Chunk(string id) => new()
    {
        Id = id,
        DocumentId = "doc-1",
        ChunkIndex = 0,
        Content = "the quick brown fox",
        Embedding = Enumerable.Repeat(0.1f, 1536).ToArray(),
        TokenCount = 4
    };

    private IVectorStore CreateStore()
    {
        // Direct registration, deliberately: this one call must be enough to make the store
        // usable. It used to register no schema provisioning at all, so the first write failed
        // with `relation "vectors" does not exist` unless the caller went through the SDK builder.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSQLVectorStore(_container.GetConnectionString());

        var provider = services.BuildServiceProvider();

        foreach (var initializer in provider.GetServices<IStorageInitializer>())
        {
            initializer.InitializeSync(provider);
        }

        return provider.GetRequiredService<IVectorStore>();
    }

    [Fact]
    public async Task Store_NonGuidChunkId_RoundTripsUnderThatSameId()
    {
        // The whole contract in one test: a consumer that uses its own id scheme stores, reads back
        // and deletes by the id it chose, and gets that id back on the chunk it reads.
        var ct = TestContext.Current.CancellationToken;
        var store = CreateStore();

        var returnedId = await store.StoreAsync(Chunk("chunk-1"), ct);
        returnedId.Should().Be("chunk-1", "the store must not substitute an id of its own");

        var fetched = await store.GetAsync("chunk-1", ct);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be("chunk-1");
        fetched.Content.Should().Be("the quick brown fox");

        (await store.DeleteAsync("chunk-1", ct)).Should().BeTrue();
        (await store.GetAsync("chunk-1", ct)).Should().BeNull();
    }

    [Fact]
    public async Task Store_SameNonGuidChunkIdTwice_IsAnUpdateNotADuplicate()
    {
        // The derivation has to be deterministic for this to hold; a random or per-process mapping
        // would silently accumulate duplicate rows for one logical chunk.
        var ct = TestContext.Current.CancellationToken;
        var store = CreateStore();

        await store.StoreAsync(Chunk("chunk-dup"), ct);
        var again = await store.GetAsync("chunk-dup", ct);

        again.Should().NotBeNull();
        (await store.ExistsAsync("chunk-dup", ct)).Should().BeTrue();
    }

    [Fact]
    public async Task Store_GuidChunkId_IsKeyedOnThatGuidVerbatim()
    {
        // Backward compatibility: rows written before the derivation existed were keyed on the id
        // itself, so a UUID id must keep resolving to exactly that row.
        var ct = TestContext.Current.CancellationToken;
        var store = CreateStore();
        var id = Guid.NewGuid().ToString();

        (await store.StoreAsync(Chunk(id), ct)).Should().Be(id);

        var fetched = await store.GetAsync(id, ct);
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(id);
    }

    [Fact]
    public async Task Store_ChunkWithoutId_StillGetsAGeneratedOne()
    {
        // Callers that never set an id keep the old behaviour rather than failing.
        var ct = TestContext.Current.CancellationToken;
        var store = CreateStore();

        var returnedId = await store.StoreAsync(Chunk(string.Empty), ct);

        returnedId.Should().NotBeNullOrWhiteSpace();
        (await store.GetAsync(returnedId, ct)).Should().NotBeNull();
    }

    private IVectorStore CreateQuantizedStore()
    {
        // The quantized store is reachable only by direct registration, and it does register its
        // own initializer -- so unlike the plain store above, this path provisions itself.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSQLQuantizedVectorStore(_container.GetConnectionString());
        // AddPostgreSQLQuantizedVectorStore does not bring a quantizer of its own, so the store
        // cannot be activated without one being registered alongside it. Remove this line once the
        // registration supplies a default — choosing which one is a consumer-visible decision that
        // the options type does not currently express, so it is still open.
        services.AddSingleton<IVectorQuantizer, ScalarQuantizer>();

        var provider = services.BuildServiceProvider();

        foreach (var initializer in provider.GetServices<IStorageInitializer>())
        {
            initializer.InitializeSync(provider);
        }

        return provider.GetRequiredService<IVectorStore>();
    }

    [Fact]
    public async Task QuantizedStore_NonGuidChunkId_RoundTripsUnderThatSameId()
    {
        // Same contract, second implementation. The two stores key on the same derivation, so a
        // consumer moving between them keeps addressing its chunks by the ids it chose.
        var ct = TestContext.Current.CancellationToken;
        // Written and read through separate stores, the way a consumer's write and read paths sit
        // in different scopes -- and the only way to prove the id survives the database rather than
        // an EF change tracker.
        var writer = CreateQuantizedStore();
        var reader = CreateQuantizedStore();

        var returnedId = await writer.StoreAsync(Chunk("q-chunk-1"), ct);
        returnedId.Should().Be("q-chunk-1");

        var fetched = await reader.GetAsync("q-chunk-1", ct);
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be("q-chunk-1");

        (await reader.ExistsAsync("q-chunk-1", ct)).Should().BeTrue();
        (await reader.DeleteAsync("q-chunk-1", ct)).Should().BeTrue();
        (await reader.GetAsync("q-chunk-1", ct)).Should().BeNull();
    }

    [Fact]
    public async Task QuantizedStore_ChunkWithoutId_StillGetsAGeneratedOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = CreateQuantizedStore();

        var returnedId = await store.StoreAsync(Chunk(string.Empty), ct);

        returnedId.Should().NotBeNullOrWhiteSpace();
        (await store.GetAsync(returnedId, ct)).Should().NotBeNull();
    }
}
