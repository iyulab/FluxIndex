using FluxIndex.Storage.SQLite.Graph;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// <c>AutoMigrate = false</c> is the operator's "I manage this schema" switch. The SDK builder copied it
/// from <c>FluxIndexOptions.GraphStore.AutoMigrate</c> into both SQLite graph option objects — and neither
/// initializer ever read it, so the documented opt-out (docs/GUIDE.md) provisioned the schema anyway.
/// </summary>
public sealed class GraphAutoMigrateTests : IDisposable
{
    private readonly string _entityDb = Path.Combine(Path.GetTempPath(), $"fi-eg-{Guid.NewGuid():N}.db");
    private readonly string _graphDb = Path.Combine(Path.GetTempPath(), $"fi-g-{Guid.NewGuid():N}.db");

    private static int TableCount(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    [Fact]
    public void EntityGraph_AutoMigrateOff_ProvisionsNothing_AndOnProvisionsEverything()
    {
        var off = new ServiceCollection();
        off.AddLogging();
        off.AddSQLiteEntityGraphStore(o => { o.DatabasePath = _entityDb; o.AutoMigrate = false; });
        using (var provider = off.BuildServiceProvider())
        {
            provider.GetRequiredService<SQLiteEntityGraphSchemaInitializer>().InitializeSync(provider);
        }
        Assert.True(!File.Exists(_entityDb) || TableCount(_entityDb) == 0,
            "with AutoMigrate off the initializer must not create the schema");

        var on = new ServiceCollection();
        on.AddLogging();
        on.AddSQLiteEntityGraphStore(o => { o.DatabasePath = _entityDb; o.AutoMigrate = true; });
        using (var provider = on.BuildServiceProvider())
        {
            provider.GetRequiredService<SQLiteEntityGraphSchemaInitializer>().InitializeSync(provider);
        }
        Assert.True(TableCount(_entityDb) > 0);
    }

    [Fact]
    public void GraphStore_AutoMigrateOff_ProvisionsNothing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSQLiteGraphStore(o => { o.GraphDatabasePath = _graphDb; o.AutoMigrate = false; });
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<SQLiteGraphSchemaInitializer>().InitializeSync(provider);

        Assert.True(!File.Exists(_graphDb) || TableCount(_graphDb) == 0);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _entityDb, _graphDb })
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
