using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// Guards the chunk-identity contract (docs/REFERENCE.md, "Chunk identity") on the sqlite-vec store:
/// <c>DocumentChunk.Id</c> is a free string and the store keys on it — what you store under is what you
/// read back and delete by — and re-storing the same id is an update, not a duplicate row.
///
/// <para>
/// These tests exist because the store minted a <c>Guid</c> of its own in <c>StoreAsync</c> and
/// <c>StoreBatchAsync</c> and returned that, discarding the caller's id. Every read path then keyed on
/// the minted value, so a consumer that recorded the ids it wrote (to roll back a partial write, or to
/// tie graph provenance to chunks) could never find those rows again. The existing tests hid it by
/// assigning the returned id back onto the chunk before reading.
/// </para>
///
/// <para>
/// Assertions read through a second, independent service provider over the same database file, so a
/// persisted row cannot be confused with an object that only lived in the writer's memory.
/// </para>
/// </summary>
public class SQLiteVecChunkIdContractTests : IAsyncLifetime
{
    private readonly string _databasePath;
    private readonly ServiceProvider _writer;

    public SQLiteVecChunkIdContractTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"fluxindex_chunkid_{Guid.NewGuid():N}.db");
        _writer = BuildProvider(_databasePath);
    }

    public async ValueTask InitializeAsync()
    {
        if (CITestHelper.ShouldSkipSqliteVec())
            return;
        await StartHostedServicesAsync(_writer);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        try
        {
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public async Task StoreAsync_KeepsTheCallerId_AndEveryReadPathKeysOnIt()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var ct = TestContext.Current.CancellationToken;
        var store = _writer.GetRequiredService<IVectorStore>();

        var returned = await store.StoreAsync(Chunk("chunk-1", "the quick brown fox", Embedding(1)), ct);
        returned.Should().Be("chunk-1", "the store must not substitute an id of its own");

        await using var reader = await BuildReaderAsync(_databasePath);
        var readerStore = reader.GetRequiredService<IVectorStore>();

        var fetched = await readerStore.GetAsync("chunk-1", ct);
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be("chunk-1");
        fetched.Content.Should().Be("the quick brown fox");
        (await readerStore.ExistsAsync("chunk-1", ct)).Should().BeTrue();

        (await readerStore.DeleteAsync("chunk-1", ct)).Should().BeTrue("delete keys on the caller's id too");
        (await readerStore.GetAsync("chunk-1", ct)).Should().BeNull();
    }

    [Fact]
    public async Task StoreBatchAsync_KeepsEveryCallerId_InOrder()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var ct = TestContext.Current.CancellationToken;
        var store = _writer.GetRequiredService<IVectorStore>();

        var ids = (await store.StoreBatchAsync(
        [
            Chunk("batch-a", "alpha", Embedding(1), chunkIndex: 0),
            Chunk("batch-b", "bravo", Embedding(2), chunkIndex: 1),
        ], ct)).ToList();

        ids.Should().Equal("batch-a", "batch-b");

        await using var reader = await BuildReaderAsync(_databasePath);
        var readerStore = reader.GetRequiredService<IVectorStore>();
        (await readerStore.GetAsync("batch-a", ct))!.Content.Should().Be("alpha");
        (await readerStore.GetAsync("batch-b", ct))!.Content.Should().Be("bravo");
    }

    [Fact]
    public async Task StoreAsync_SameIdTwice_UpdatesTheRow_InEveryIndex()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var ct = TestContext.Current.CancellationToken;
        var store = _writer.GetRequiredService<IVectorStore>();

        await store.StoreAsync(Chunk("dup", "first wording", Embedding(1)), ct);
        await store.StoreAsync(Chunk("dup", "second wording", Embedding(2)), ct);

        await using var reader = await BuildReaderAsync(_databasePath);
        var readerStore = reader.GetRequiredService<IVectorStore>();

        // One row, carrying the second write.
        var rows = (await readerStore.GetByDocumentIdAsync("doc-1", ct)).ToList();
        rows.Should().ContainSingle().Which.Content.Should().Be("second wording");

        // The vector index follows the row: the second embedding finds it, and only once.
        var byVector = (await readerStore.SearchAsync(Embedding(2), topK: 10, cancellationToken: ct)).ToList();
        byVector.Should().ContainSingle(c => c.Id == "dup");

        // The full-text index follows too: the old wording is gone, the new one is found.
        var vecStore = (SQLiteVecVectorStore)readerStore;
        (await vecStore.TextSearchAsync("second", 10, ct)).Should().ContainSingle(c => c.Id == "dup");
        (await vecStore.TextSearchAsync("first", 10, ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task StoreBatchAsync_WithAnAlreadyStoredId_UpdatesInsteadOfDuplicating()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var ct = TestContext.Current.CancellationToken;
        var store = _writer.GetRequiredService<IVectorStore>();

        await store.StoreAsync(Chunk("keep", "original", Embedding(1), chunkIndex: 0), ct);
        await store.StoreBatchAsync(
        [
            Chunk("keep", "rewritten", Embedding(3), chunkIndex: 0),
            Chunk("new", "added", Embedding(2), chunkIndex: 1),
        ], ct);

        await using var reader = await BuildReaderAsync(_databasePath);
        var readerStore = reader.GetRequiredService<IVectorStore>();

        var rows = (await readerStore.GetByDocumentIdAsync("doc-1", ct)).ToList();
        rows.Select(r => r.Id).Should().BeEquivalentTo(["keep", "new"]);
        rows.Single(r => r.Id == "keep").Content.Should().Be("rewritten");

        var byVector = (await readerStore.SearchAsync(Embedding(3), topK: 10, cancellationToken: ct)).ToList();
        byVector.Should().ContainSingle(c => c.Id == "keep", "the batch path must replace the vector, not add a second one");
    }

    [Fact]
    public async Task StoreAsync_EmptyId_GetsOneFromTheStore()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var ct = TestContext.Current.CancellationToken;
        var store = _writer.GetRequiredService<IVectorStore>();

        var chunk = Chunk(string.Empty, "no id given", Embedding(1));
        var returned = await store.StoreAsync(chunk, ct);

        Assert.Equal(returned, chunk.Id); // the generated id lands on the instance too

        returned.Should().NotBeNullOrWhiteSpace();
        (await store.GetAsync(returned, ct)).Should().NotBeNull();
    }

    /// <summary>
    /// The in-process fallback store (<c>UseSQLiteVec = false</c>) is bound by the same contract — it
    /// minted its own id in exactly the same line.
    /// </summary>
    [Fact]
    public async Task FallbackStore_KeepsTheCallerId_AndReStoringUpdates()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"fluxindex_chunkid_fallback_{Guid.NewGuid():N}.db");
        try
        {
            await using var provider = BuildProvider(path, useSqliteVec: false);
            var store = provider.GetRequiredService<IVectorStore>();

            (await store.StoreAsync(Chunk("fb-1", "first", Embedding(1)), ct)).Should().Be("fb-1");
            (await store.StoreAsync(Chunk("fb-1", "second", Embedding(2)), ct)).Should().Be("fb-1");

            var rows = (await store.GetByDocumentIdAsync("doc-1", ct)).ToList();
            rows.Should().ContainSingle().Which.Content.Should().Be("second");
            (await store.GetAsync("fb-1", ct))!.Id.Should().Be("fb-1");
            (await store.DeleteAsync("fb-1", ct)).Should().BeTrue();
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort cleanup */ }
        }
    }

    private static async Task StartHostedServicesAsync(ServiceProvider provider)
    {
        foreach (var service in provider.GetServices<IHostedService>())
            await service.StartAsync(CancellationToken.None);
    }

    private static async Task<ServiceProvider> BuildReaderAsync(string databasePath)
    {
        var provider = BuildProvider(databasePath);
        await StartHostedServicesAsync(provider);
        return provider;
    }

    private static ServiceProvider BuildProvider(string databasePath, bool useSqliteVec = true)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSQLiteVecVectorStore(options =>
        {
            options.DatabasePath = databasePath;
            options.UseInMemory = false;
            options.VectorDimension = 4;
            options.EmbeddingFingerprint = "chunkid4";
            // Both deliberate: with UseSQLiteVec false and fallback on, every call goes to the
            // in-process SQLiteVectorStore — which is the point of the fallback test above.
            options.UseSQLiteVec = useSqliteVec;
            options.FallbackToInMemoryOnError = !useSqliteVec;
            options.AutoMigrate = true;
        });
        return services.BuildServiceProvider();
    }

    private static DocumentChunk Chunk(string id, string content, float[] embedding, int chunkIndex = 0) => new()
    {
        Id = id,
        DocumentId = "doc-1",
        Content = content,
        ChunkIndex = chunkIndex,
        TokenCount = 3,
        Embedding = embedding,
        Metadata = new Dictionary<string, object> { ["origin"] = "test" }
    };

    /// <summary>Four unit vectors far enough apart that the nearest neighbour is unambiguous.</summary>
    private static float[] Embedding(int axis) => axis switch
    {
        1 => [1f, 0f, 0f, 0f],
        2 => [0f, 1f, 0f, 0f],
        3 => [0f, 0f, 1f, 0f],
        _ => [0f, 0f, 0f, 1f],
    };
}
