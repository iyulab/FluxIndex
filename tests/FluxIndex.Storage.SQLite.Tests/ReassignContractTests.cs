using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Quantization;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.Storage.SQLite.KeywordSearch;
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
/// Runs the shared reassignment contract suite against SQLiteVectorStore.
/// </summary>
[Collection("SQLite Tests")]
public class SQLiteVectorStoreReassignContractTests : VectorStoreReassignContractSuite
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
        return new SQLiteVectorStore(new DelegateDbContextFactory<SQLiteDbContext>(() => new SQLiteDbContext(dbOptions, options)), NullLogger<SQLiteVectorStore>.Instance, options);
    }
}

/// <summary>
/// Runs the shared reassignment contract suite against SQLiteQuantizedVectorStore.
/// </summary>
[Collection("SQLite Tests")]
public class SQLiteQuantizedVectorStoreReassignContractTests : VectorStoreReassignContractSuite
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
            new DelegateDbContextFactory<SQLiteQuantizedDbContext>(() => new SQLiteQuantizedDbContext(dbOptions, options)), quantizer, NullLogger<SQLiteQuantizedVectorStore>.Instance, options);
    }
}

/// <summary>
/// Runs the shared reassignment contract suite against the sqlite-vec store, where a move spans three tables: the
/// chunk row, its vec0 vector and its full-text row. Skipped where the native extension is absent.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteVecVectorStoreReassignContractTests : VectorStoreReassignContractSuite, IAsyncLifetime
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

        var path = Path.Combine(Path.GetTempPath(), $"fluxindex_vec_reassign_{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSQLiteVecVectorStore(options =>
        {
            options.DatabasePath = path;
            options.UseInMemory = false;
            options.VectorDimension = Dimensions;
            options.EmbeddingFingerprint = "reassign4";
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

/// <summary>
/// Runs the shared keyword-index reassignment contract suite against the SQLite (relational BM25) keyword index.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteKeywordSearchReassignContractTests : KeywordSearchReassignContractSuite, IDisposable
{
    private readonly List<string> _paths = [];

    protected override bool ScoresFileNameField => true;

    protected override Task<IKeywordSearchService> CreateServiceAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluxindex-kw-reassign-{Guid.NewGuid():N}.db");
        _paths.Add(path);
        return Task.FromResult<IKeywordSearchService>(
            new SQLiteKeywordSearchService($"Data Source={path}", NullLogger<SQLiteKeywordSearchService>.Instance));
    }

    public void Dispose()
    {
        FluxIndex.Tests.Shared.SqliteTestPools.Release(_paths);
        foreach (var path in _paths)
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
