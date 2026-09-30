using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// A keyword-only context binds no embedding identity. The sqlite-vec store — the README default — then keeps chunks in
/// <c>vector_chunks</c> with no vec0 table; it used to demand a fingerprint on first use and could not store anything.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteVecKeywordOnlyTests : IAsyncLifetime
{
    private readonly List<(ServiceProvider Provider, string Path)> _stores = [];

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var (provider, path) in _stores)
        {
            await provider.DisposeAsync();
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }

    private async Task<IVectorStore> CreateUnboundStoreAsync()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var path = Path.Combine(Path.GetTempPath(), $"fluxindex_vec_keyword_only_{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSQLiteVecVectorStore(options =>
        {
            options.DatabasePath = path;
            options.UseInMemory = false;
            options.VectorDimension = 4;
            options.UseSQLiteVec = true;
            options.FallbackToInMemoryOnError = false;
            options.AutoMigrate = true;
        });
        var provider = services.BuildServiceProvider();
        _stores.Add((provider, path));
        foreach (var service in provider.GetServices<IHostedService>())
            await service.StartAsync(CancellationToken.None);
        return provider.GetRequiredService<IVectorStore>();
    }

    [Fact]
    public async Task WithoutABoundIdentity_ChunksWithoutVectors_AreStoredReadAndDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateUnboundStoreAsync();

        await store.StoreBatchAsync(
        [
            new DocumentChunk { Id = "k-1", DocumentId = "doc-1", ChunkIndex = 0, TotalChunks = 2, Content = "first" },
            new DocumentChunk { Id = "k-2", DocumentId = "doc-1", ChunkIndex = 1, TotalChunks = 2, Content = "second" },
        ], ct);

        var read = await store.GetAsync("k-1", ct);
        Assert.NotNull(read);
        Assert.Equal("first", read.Content);
        Assert.Null(read.Embedding);
        Assert.Equal(2, (await store.GetByDocumentIdAsync("doc-1", ct)).Count());

        await store.DeleteByDocumentIdAsync("doc-1", ct);
        Assert.Empty(await store.GetByDocumentIdAsync("doc-1", ct));
    }

    [Fact]
    public async Task WithoutABoundIdentity_VectorSearch_SaysWhatIsMissing_InsteadOfReturningNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CreateUnboundStoreAsync();

        var act = () => store.SearchAsync([1f, 0f, 0f, 0f], topK: 3, cancellationToken: ct);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("keyword-only", error.Message);
    }
}
