using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// The consumer's path: the same provider and model restarted at another <see cref="SQLiteVecOptions.VectorDimension"/>,
/// default options (<c>FallbackToInMemoryOnError = true</c>), through the registered store and its start-up initializer.
/// </summary>
[Collection("SQLite Tests")]
public sealed class VecTableWidthStoreTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"fluxindex_width_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task A_restart_at_a_new_width_over_an_empty_table_stores_and_reports_healthy()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var ct = TestContext.Current.CancellationToken;

        await using (var first = await StartAsync(8))
            (await first.GetRequiredService<SQLiteVecVectorStore>().VerifyHealthAsync(ct)).Should().BeTrue();

        await using var second = await StartAsync(16);
        var store = second.GetRequiredService<SQLiteVecVectorStore>();
        (await store.VerifyHealthAsync(ct)).Should().BeTrue();
        var chunk = Chunk("new-width", 16);
        await store.StoreAsync(chunk, ct);
        (await store.SearchAsync(chunk.Embedding!, 5, 0f, null, ct)).Should().ContainSingle(c => c.Id == chunk.Id);
    }

    [Fact]
    public async Task A_restart_at_a_new_width_over_stored_vectors_throws_the_typed_error_and_keeps_them()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var ct = TestContext.Current.CancellationToken;
        var kept = Chunk("kept", 8);

        await using (var first = await StartAsync(8))
            await first.GetRequiredService<IVectorStore>().StoreAsync(kept, ct);

        await using (var second = await StartAsync(16))
        {
            var store = second.GetRequiredService<SQLiteVecVectorStore>();
            var write = async () => await store.StoreAsync(Chunk("other", 16), ct);
            var thrown = (await write.Should().ThrowAsync<SQLiteVecDimensionMismatchException>()).Which;
            thrown.TableDimension.Should().Be(8);
            thrown.ConfiguredDimension.Should().Be(16);
            thrown.StoredVectorCount.Should().Be(1);
            (await store.VerifyHealthAsync(ct)).Should().BeFalse();
        }

        // Back at the width the table was made with, the stored vector is still there.
        await using var third = await StartAsync(8);
        (await third.GetRequiredService<IVectorStore>().SearchAsync(kept.Embedding!, 5, 0f, null, ct))
            .Should().ContainSingle(c => c.Id == kept.Id);
    }

    private async Task<ServiceProvider> StartAsync(int dimension)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSQLiteVecVectorStore(options =>
        {
            options.UseInMemory = false;
            options.UseSQLiteVec = true;
            options.AutoMigrate = true;
            options.VectorDimension = dimension;
            options.EmbeddingFingerprint = "abcd1234";
            options.DatabasePath = _databasePath;
        });
        var provider = services.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(TestContext.Current.CancellationToken);
        return provider;
    }

    private static DocumentChunk Chunk(string id, int dimension) => new()
    {
        DocumentId = "doc-" + id,
        ChunkIndex = 0,
        Content = "content " + id,
        Embedding = Enumerable.Range(0, dimension).Select(i => (float)(i + 1) / dimension).ToArray(),
        TokenCount = 3,
    };

    public void Dispose()
    {
        FluxIndex.Tests.Shared.SqliteTestPools.Release(_databasePath);
        foreach (var file in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }
}
