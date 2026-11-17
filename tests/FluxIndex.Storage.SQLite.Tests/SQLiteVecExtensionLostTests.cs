using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// A store that already used sqlite-vec must not turn into an in-memory store when a later connection cannot load the
/// extension (the native file was removed by a temporary-file cleanup, quarantined, replaced by an update). With
/// <c>FallbackToInMemoryOnError = true</c> it used to: a search then read an empty memory store and a write was lost at
/// exit while the caller was told it succeeded.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteVecExtensionLostTests : IDisposable
{
    private const int Dimension = 8;
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"fluxindex_extlost_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task OperationsAfterTheLoadedExtensionIsLost_Throw_AndNeverLandInMemory()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var loader = new LosableLoader();
        await using var provider = BuildProvider(loader, fallbackToInMemory: true);
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(TestContext.Current.CancellationToken);
        var store = provider.GetRequiredService<IVectorStore>();

        // The store works on sqlite-vec first.
        var chunk = Chunk("on-disk");
        await store.StoreAsync(chunk, TestContext.Current.CancellationToken);
        (await store.SearchAsync(chunk.Embedding!, 5, 0f, null, TestContext.Current.CancellationToken))
            .Should().ContainSingle(c => c.Id == chunk.Id);

        // The native file disappears: a later connection cannot load the extension (the real loader answers `false`
        // under the fallback option - that is what this stand-in does).
        loader.Lose();

        var search = async () => await store.SearchAsync(chunk.Embedding!, 5, 0f, null, TestContext.Current.CancellationToken);
        (await search.Should().ThrowAsync<SQLiteVecExtensionLostException>())
            .Which.ExtensionPath.Should().Be(loader.GetExtensionPath());

        var write = async () => await store.StoreAsync(Chunk("lost"), TestContext.Current.CancellationToken);
        await write.Should().ThrowAsync<SQLiteVecExtensionLostException>();

        // The extension comes back (the file was restored): the same store reads what is on disk again - it never
        // switched, so nothing was split.
        loader.Restore();
        (await store.SearchAsync(chunk.Embedding!, 5, 0f, null, TestContext.Current.CancellationToken))
            .Should().ContainSingle(c => c.Id == chunk.Id);
    }

    [Fact]
    public async Task WithoutTheFallbackOption_ALostExtensionIsTheSameTypedError()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var loader = new LosableLoader { ThrowWhenLost = true };
        await using var provider = BuildProvider(loader, fallbackToInMemory: false);
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(TestContext.Current.CancellationToken);
        var store = provider.GetRequiredService<IVectorStore>();
        var chunk = Chunk("on-disk");
        await store.StoreAsync(chunk, TestContext.Current.CancellationToken);

        loader.Lose();

        var search = async () => await store.SearchAsync(chunk.Embedding!, 5, 0f, null, TestContext.Current.CancellationToken);
        (await search.Should().ThrowAsync<SQLiteVecExtensionLostException>())
            .Which.InnerException.Should().BeOfType<FileNotFoundException>();
    }

    [Fact]
    public async Task AFailedSearch_Throws_AndTheStoreKeepsReadingTheDisk()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        await using var provider = BuildProvider(new LosableLoader(), fallbackToInMemory: true);
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(TestContext.Current.CancellationToken);
        var store = provider.GetRequiredService<IVectorStore>();
        var chunk = Chunk("on-disk");
        await store.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // One failed query is that query's failure (here sqlite-vec refuses a query vector of the wrong size; a locked
        // database or an I/O error ends the same way). It used to switch the whole store to the empty in-memory
        // fallback for the rest of the process and answer this call - and every later one - from it.
        var search = async () => await store.SearchAsync([1f, 2f, 3f], 5, 0f, null, TestContext.Current.CancellationToken);
        await search.Should().ThrowAsync<Microsoft.Data.Sqlite.SqliteException>();

        (await store.SearchAsync(chunk.Embedding!, 5, 0f, null, TestContext.Current.CancellationToken))
            .Should().ContainSingle(c => c.Id == chunk.Id);
    }

    [Fact]
    public async Task AFailedVectorInsertWhoseRecoveryFails_Throws_AndNeverLandsInMemory()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var faults = new VecFaults();
        var loader = new LosableLoader { RecreateFails = true };
        await using var provider = BuildProvider(loader, fallbackToInMemory: true, faults);
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(TestContext.Current.CancellationToken);
        var store = provider.GetRequiredService<IVectorStore>();
        var chunk = Chunk("on-disk");
        await store.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // The insert fails with a sqlite-vec error and the vec0 table cannot be rebuilt. The batch used to be handed
        // to the in-memory fallback and reported as stored; it is a failure of the call.
        faults.FailVectorInserts = true;
        var write = async () => await store.StoreBatchAsync([Chunk("lost")], TestContext.Current.CancellationToken);
        await write.Should().ThrowAsync<Microsoft.Data.Sqlite.SqliteException>();

        faults.FailVectorInserts = false;
        (await store.SearchAsync(chunk.Embedding!, 5, 0f, null, TestContext.Current.CancellationToken))
            .Should().ContainSingle(c => c.Id == chunk.Id);
    }

    [Fact]
    public async Task AFailedHealthCheckRecovery_ReportsFalse_AndTheStoreKeepsReadingTheDisk()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var faults = new VecFaults();
        var loader = new LosableLoader { RecreateFails = true };
        await using var provider = BuildProvider(loader, fallbackToInMemory: true, faults);
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(TestContext.Current.CancellationToken);
        var store = provider.GetRequiredService<SQLiteVecVectorStore>();
        var chunk = Chunk("on-disk");
        await store.StoreAsync(chunk, TestContext.Current.CancellationToken);

        // Health said "true" after a failed recovery because the store had switched to the fallback ("nothing to
        // verify") - the switch is what hid the failure.
        faults.FailVectorInserts = true;
        (await store.VerifyHealthAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        (await store.VerifyHealthAsync(TestContext.Current.CancellationToken)).Should().BeFalse();

        faults.FailVectorInserts = false;
        (await store.SearchAsync(chunk.Embedding!, 5, 0f, null, TestContext.Current.CancellationToken))
            .Should().ContainSingle(c => c.Id == chunk.Id);
    }

    [Fact]
    public async Task TheRememberedExtensionPath_IsPerCustomExtensionPath()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<SQLiteVecExtensionLoader>.Instance;
        using var first = new SqliteConnection("Data Source=:memory:");
        var defaultLoader = new SQLiteVecExtensionLoader(logger,
            Microsoft.Extensions.Options.Options.Create(new SQLiteVecOptions { UseSQLiteVec = true }));
        (await defaultLoader.LoadExtensionAsync(first, TestContext.Current.CancellationToken)).Should().BeTrue();

        // A second store of the same process names its own file. The loader used to answer from the path the first
        // one remembered, so the option was ignored: here the named file is not a library, and loading it must fail.
        var notALibrary = Path.Combine(Path.GetTempPath(), $"fluxindex_notvec_{Guid.NewGuid():N}" + Path.GetExtension(defaultLoader.GetExtensionPath()));
        await File.WriteAllTextAsync(notALibrary, "not a native library", TestContext.Current.CancellationToken);
        try
        {
            using var second = new SqliteConnection("Data Source=:memory:");
            var customLoader = new SQLiteVecExtensionLoader(logger,
                Microsoft.Extensions.Options.Options.Create(new SQLiteVecOptions
                {
                    UseSQLiteVec = true,
                    FallbackToInMemoryOnError = false,
                    CustomExtensionPath = notALibrary,
                }));
            var load = async () => await customLoader.LoadExtensionAsync(second, TestContext.Current.CancellationToken);
            await load.Should().ThrowAsync<Exception>();
        }
        finally
        {
            try { File.Delete(notALibrary); } catch (IOException) { /* best effort */ }
        }
    }

    private ServiceProvider BuildProvider(LosableLoader loader, bool fallbackToInMemory, VecFaults? faults = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (faults is not null)
            services.ConfigureDbContext<SQLiteVecDbContext>(builder => builder.AddInterceptors(faults));
        services.AddSQLiteVecVectorStore(options =>
        {
            options.UseInMemory = false;
            options.UseSQLiteVec = true;
            options.AutoMigrate = true;
            options.VectorDimension = Dimension;
            options.EmbeddingFingerprint = "extlost" + Dimension;
            options.FallbackToInMemoryOnError = fallbackToInMemory;
            options.DatabasePath = _databasePath;
        });
        services.AddSingleton<ISQLiteVecExtensionLoader>(loader);
        return services.BuildServiceProvider();
    }

    private static DocumentChunk Chunk(string id) => new()
    {
        DocumentId = "doc-" + id,
        ChunkIndex = 0,
        Content = "content " + id,
        Embedding = Enumerable.Range(0, Dimension).Select(i => (float)(i + 1) / Dimension).ToArray(),
        TokenCount = 3,
    };

    public void Dispose()
    {
        foreach (var file in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }

    /// <summary>Makes vector inserts fail the way a broken vec0 table does.</summary>
    private sealed class VecFaults : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public volatile bool FailVectorInserts;

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (FailVectorInserts
                && command.CommandText.Contains("INSERT INTO chunk_embeddings", StringComparison.Ordinal))
                throw new SqliteException("sqlite-vec error: injected", 1);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>The real loader until <see cref="Lose"/>; then the failure a vanished file gives.</summary>
    private sealed class LosableLoader : ISQLiteVecExtensionLoader
    {
        private readonly ISQLiteVecExtensionLoader _real = new SQLiteVecExtensionLoader(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SQLiteVecExtensionLoader>.Instance,
            Microsoft.Extensions.Options.Options.Create(new SQLiteVecOptions { UseSQLiteVec = true, FallbackToInMemoryOnError = true }));

        private volatile bool _lost;

        /// <summary>true: throw the load failure (the loader of a store without the fallback option); false: answer `false`.</summary>
        public bool ThrowWhenLost { get; init; }

        public void Lose() => _lost = true;

        public void Restore() => _lost = false;

        public Task<bool> LoadExtensionAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
        {
            if (!_lost)
                return _real.LoadExtensionAsync(connection, cancellationToken);
            if (ThrowWhenLost)
                throw new FileNotFoundException("The sqlite-vec extension file was not found: " + GetExtensionPath());
            return Task.FromResult(false);
        }

        public Task<bool> IsExtensionLoadedAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
            => _real.IsExtensionLoadedAsync(connection, cancellationToken);

        public string GetExtensionPath() => _real.GetExtensionPath();

        public bool ExtensionFileExists() => _real.ExtensionFileExists();

        public Task<bool> CreateVecTableAsync(SqliteConnection connection, string tableName, int vectorDimension, string options = "distance_metric=cosine", CancellationToken cancellationToken = default)
            => _real.CreateVecTableAsync(connection, tableName, vectorDimension, options, cancellationToken);

        /// <summary>The vec0 table cannot be rebuilt (the recovery of a failed insert or health check fails).</summary>
        public bool RecreateFails { get; init; }

        public Task<bool> RecreateVecTableAsync(SqliteConnection connection, string tableName, int vectorDimension, string options = "distance_metric=cosine", CancellationToken cancellationToken = default)
            => RecreateFails
                ? Task.FromResult(false)
                : _real.RecreateVecTableAsync(connection, tableName, vectorDimension, options, cancellationToken);

        public Task<string?> GetExtensionVersionAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
            => _real.GetExtensionVersionAsync(connection, cancellationToken);
    }
}
