using System.Data.Common;
using System.Reflection;
using AwesomeAssertions;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests.Extensions;

/// <summary>
/// The per-connection settings of <see cref="SQLiteOptions"/> reach the connections the stores use at run time — not only
/// the one connection the startup migration opened. Values are chosen away from SQLite's defaults so a connection that
/// never received them reads differently.
/// </summary>
public sealed class ConnectionPragmaTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fluxindex-pragmas-{Guid.NewGuid():N}");

    public ConnectionPragmaTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // Best-effort: pooled connections may still hold the files. Not ClearAllPools() — it is process-global and would
        // yank connections from tests running in parallel (NoGlobalSqlitePoolClearTests).
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void Configure(SQLiteOptions o)
    {
        o.DatabasePath = Path.Combine(_dir, "store.db");
        o.BusyTimeout = 1234;
        o.CacheSize = -4321;
        o.MmapSize = 0;
        o.Synchronous = SynchronousMode.Full;
        o.TempStore = TempStoreMode.File;
        o.WalAutocheckpoint = 777;
    }

    private static async Task<long> ReadAsync(DbConnection connection, string pragma)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma}";
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task AssertConfiguredAsync(DbConnection connection)
    {
        (await ReadAsync(connection, "busy_timeout")).Should().Be(1234);
        (await ReadAsync(connection, "cache_size")).Should().Be(-4321);
        (await ReadAsync(connection, "synchronous")).Should().Be(2, "FULL");
        (await ReadAsync(connection, "temp_store")).Should().Be(1, "FILE");
        (await ReadAsync(connection, "wal_autocheckpoint")).Should().Be(777);
    }

    [Fact]
    public async Task A_vector_store_context_opened_at_run_time_carries_the_configured_pragmas()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSQLiteVectorStore(Configure);
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<SQLiteDbContext>>();

        // Two contexts, each its own open: every connection gets the settings, not the first one only.
        for (var i = 0; i < 2; i++)
        {
            await using var context = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            await AssertConfiguredAsync(context.Database.GetDbConnection());
        }
    }

    [Fact]
    public async Task A_sqlite_vec_store_context_carries_the_configured_pragmas()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSQLiteVecVectorStore(o => Configure(o));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<SQLiteVecDbContext>>();

        await using var context = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await AssertConfiguredAsync(context.Database.GetDbConnection());
    }

    [Fact]
    public async Task The_keyword_index_sharing_a_store_carries_that_stores_pragmas()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSQLiteVectorStore(Configure);
        services.AddSQLiteKeywordSearch();
        await using var provider = services.BuildServiceProvider();
        var keyword = provider.GetRequiredService<SQLiteKeywordSearchService>();

        await using var connection = await OpenAsync(keyword);
        await AssertConfiguredAsync(connection);
    }

    [Fact]
    public async Task A_keyword_index_on_a_bare_connection_string_keeps_its_own_default()
    {
        var keyword = new SQLiteKeywordSearchService($"Data Source={Path.Combine(_dir, "bare.db")}", NullLogger<SQLiteKeywordSearchService>.Instance);

        await using var connection = await OpenAsync(keyword);
        (await ReadAsync(connection, "busy_timeout")).Should().NotBe(1234, "no store options were given");
    }

    private static async Task<DbConnection> OpenAsync(SQLiteKeywordSearchService service)
    {
        var open = typeof(SQLiteKeywordSearchService).BaseType!
            .GetMethod("OpenConnectionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return await (Task<DbConnection>)open.Invoke(service, [TestContext.Current.CancellationToken])!;
    }
}
