using AwesomeAssertions;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// One context built once and shared by concurrent callers — the hosting shape a DI singleton gives
/// (HTTP requests plus a background indexer). Overlapping delete / index / search calls must all
/// succeed; before 0.63.0 the context held one EF Core <c>DbContext</c> for its lifetime and an
/// overlapping call failed with "A second operation was started on this context instance".
/// </summary>
public class ConcurrentContextUseTests : IDisposable
{
    private const int Callers = 16;

    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"fluxindex_concurrent_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task OverlappingDeleteIndexSearch_OnOneContext_AllSucceed()
    {
        var context = FluxIndexContext.CreateBuilder()
            .UseSQLite(_dbPath)
            .UseInMemoryEmbedding()
            .AddSQLiteStorage()
            .Build();

        try
        {
            var ct = TestContext.Current.CancellationToken;
            using var start = new ManualResetEventSlim(false);

            var callers = Enumerable.Range(0, Callers).Select(i => Task.Run(async () =>
            {
                start.Wait(ct);
                var id = $"doc-{i}";
                await context.DeleteDocumentAsync(id, ct);
                await context.Indexer.IndexDocumentAsync($"Concurrent caller {i} writes about shared storage", id, cancellationToken: ct);
                await context.SearchAsync("shared storage", cancellationToken: ct);
            }, ct)).ToArray();

            start.Set();
            await Task.WhenAll(callers);

            (await context.GetDocumentCountAsync(ct)).Should().Be(Callers,
                "every caller's document must have been written despite the overlap");
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// The sqlite-vec store shared by overlapping callers — one instance, the way a scope (or a context
    /// built once) hands it to every request. Each caller writes, searches and deletes through it; every
    /// write must land, and every delete must reach the vec0 table as well as the metadata row.
    /// </summary>
    [Fact]
    public async Task OverlappingStoreSearchDelete_OnOneSqliteVecStore_AllSucceed()
    {
        var ct = TestContext.Current.CancellationToken;
        if (!await SqliteVecAvailableAsync(ct))
        {
            Assert.Skip("The sqlite-vec native extension is not available on this machine.");
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSQLiteVecVectorStore(o =>
        {
            o.DatabasePath = _dbPath;
            o.UseSQLiteVec = true;
            o.VectorDimension = 4;
            o.FallbackToInMemoryOnError = false; // fail loud: a fallback would hide the native path
        });
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<SQLiteVecVectorStore>();
        store.BindIdentity(new EmbeddingIdentity { Provider = "test", Model = "concurrent", Dimension = 4 });

        using var start = new ManualResetEventSlim(false);
        var callers = Enumerable.Range(0, Callers).Select(i => Task.Run(async () =>
        {
            start.Wait(ct);
            var embedding = new[] { 1f, i + 1f, 0.5f, 0.25f };
            await store.StoreAsync(Chunk($"keep-{i}", embedding), ct);
            await store.StoreBatchAsync([Chunk($"drop-{i}", embedding)], ct);
            await store.SearchAsync(embedding, topK: 5, cancellationToken: ct);
            (await store.DeleteAsync($"drop-{i}", ct)).Should().BeTrue();
            (await store.GetAsync($"keep-{i}", ct)).Should().NotBeNull();
        }, ct)).ToArray();

        start.Set();
        await Task.WhenAll(callers);

        (await store.CountAsync(ct)).Should().Be(Callers, "each caller keeps one chunk and deletes the other");
        var collection = await store.GetCollectionInfoAsync(store.ResolvedStoreName!, ct);
        collection!.EntryCount.Should().Be(Callers, "a delete must remove the vector as well as the metadata row");
    }

    private static DocumentChunk Chunk(string id, float[] embedding) => new()
    {
        Id = id,
        DocumentId = id,
        ChunkIndex = 0,
        Content = $"concurrent chunk {id}",
        Embedding = embedding,
        TokenCount = 3,
    };

    private static async Task<bool> SqliteVecAvailableAsync(CancellationToken ct)
    {
        var options = Options.Create(new SQLiteVecOptions { FallbackToInMemoryOnError = true });
        var loader = new SQLiteVecExtensionLoader(NullLogger<SQLiteVecExtensionLoader>.Instance, options);
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        return await loader.LoadExtensionAsync(connection, ct);
    }

    public void Dispose()
    {
        FluxIndex.Tests.Shared.SqliteTestPools.Release(_dbPath);
        foreach (var path in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(path); } catch (IOException) { }
        }
        GC.SuppressFinalize(this);
    }
}
