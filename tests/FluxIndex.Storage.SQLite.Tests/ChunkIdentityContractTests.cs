using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Quantization;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// Runs the shared IVectorStore chunk-identity contract suite against SQLiteVectorStore (the
/// in-process fallback store). The sqlite-vec store runs the same suite below
/// (<see cref="SQLiteVecVectorStoreChunkIdentityContractTests"/>); <see cref="SQLiteVecChunkIdContractTests"/>
/// keeps what the suite cannot say about it — that a row read through an independent provider, and
/// the vector and full-text indexes, all follow the caller's id.
/// </summary>
[Collection("SQLite Tests")]
public class SQLiteVectorStoreChunkIdentityContractTests : VectorStoreChunkIdentityContractSuite
{
    protected override async Task<IVectorStore> CreateStoreAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = Options.Create(new SQLiteOptions());
        var dbOptions = new DbContextOptionsBuilder<SQLiteDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new SQLiteDbContext(dbOptions, options);
        await context.Database.EnsureCreatedAsync();
        return new SQLiteVectorStore(context, NullLogger<SQLiteVectorStore>.Instance, options);
    }
}

/// <summary>
/// Runs the shared IVectorStore chunk-identity contract suite against SQLiteQuantizedVectorStore.
/// </summary>
[Collection("SQLite Tests")]
public class SQLiteQuantizedVectorStoreChunkIdentityContractTests : VectorStoreChunkIdentityContractSuite
{
    protected override async Task<IVectorStore> CreateStoreAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = Options.Create(new SQLiteQuantizedOptions());
        var dbOptions = new DbContextOptionsBuilder<SQLiteQuantizedDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new SQLiteQuantizedDbContext(dbOptions, options);
        await context.Database.EnsureCreatedAsync();
        var quantizer = new ScalarQuantizer(
            Options.Create(new QuantizationOptions()),
            NullLogger<ScalarQuantizer>.Instance);
        return new SQLiteQuantizedVectorStore(
            context, quantizer, NullLogger<SQLiteQuantizedVectorStore>.Instance, options);
    }
}

/// <summary>
/// Runs the shared IVectorStore chunk-identity contract suite against the sqlite-vec store — the
/// README default, and until now the only vector store outside the suite (its own facts had to copy
/// each new contract fact by hand). Each store gets its own database file with the migration run,
/// the way the DI registration builds it; skipped where the native extension is absent.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteVecVectorStoreChunkIdentityContractTests : VectorStoreChunkIdentityContractSuite, IAsyncLifetime
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

    protected override async Task<IVectorStore> CreateStoreAsync()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var path = Path.Combine(Path.GetTempPath(), $"fluxindex_vec_contract_{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSQLiteVecVectorStore(options =>
        {
            options.DatabasePath = path;
            options.UseInMemory = false;
            options.VectorDimension = Dimensions;
            options.EmbeddingFingerprint = "contract4";
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
}
