using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Storage.SQLite.Graph;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// A database created by an earlier version has every table the graph store owns but not every
/// column the current model declares. Start-up provisioning must add the columns it can add
/// without guessing (nullable, or with a default) rather than refusing the whole schema or, worse,
/// reporting success and failing on the first write with "no such column".
/// </summary>
public sealed class SQLiteSchemaProvisionerColumnAddTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    private SQLiteEntityGraphDbContext NewContext() => new(
        new DbContextOptionsBuilder<SQLiteEntityGraphDbContext>().UseSqlite(_connection).Options,
        Options.Create(new SQLiteEntityGraphOptions()));

    private static (string Table, string Column) ChunkIdsColumn(DbContext context)
    {
        var entityType = context.Model.FindEntityType(typeof(SQLiteEntityCommunityEntity))!;
        var table = entityType.GetTableName()!;
        var column = entityType.FindProperty(nameof(SQLiteEntityCommunityEntity.ChunkIds))!
            .GetColumnName(StoreObjectIdentifier.Table(table))!;
        return (table, column);
    }

    private static HashSet<string> ColumnsOf(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\")";
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) columns.Add(reader.GetString(1));
        return columns;
    }

    [Fact]
    public async Task ProvisioningAnOlderDatabase_AddsTheMissingNullableColumn_AndTheStoreWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        _connection.Open();

        // A database as an earlier version left it: every owned table, minus the column that was
        // added later. Derived from the current model plus one DROP so no DDL is restated here.
        string table, column;
        await using (var older = NewContext())
        {
            older.Database.EnsureCreated();
            (table, column) = ChunkIdsColumn(older);
            await older.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{table}\" DROP COLUMN \"{column}\"", ct);
        }
        Assert.DoesNotContain(column, ColumnsOf(_connection, table));

        await using var context = NewContext();
        SQLiteSchemaProvisioner.Provision(context);

        Assert.Contains(column, ColumnsOf(_connection, table));

        var store = new SQLiteEntityGraphStore(context, Options.Create(new SQLiteEntityGraphOptions()), NullLogger<SQLiteEntityGraphStore>.Instance);
        await store.StoreCommunityAsync(new GraphCommunity { Id = "c1", Name = "c1", ChunkIds = ["k1", "k2"] }, ct);

        var found = await store.GetCommunitiesByChunkIdsAsync(["k2"], ct: ct);
        Assert.Equal("c1", Assert.Single(found).Id);
        Assert.Equal(["k1", "k2"], Assert.Single(found).ChunkIds.Order());
    }

    [Fact]
    public async Task ProvisioningADatabaseFromBeforePartitions_AddsThePartitionColumns_AndItsRowsReadAsTheDefaultPartition()
    {
        var ct = TestContext.Current.CancellationToken;
        _connection.Open();

        // Rows written, then the partition columns taken away: the shape a database written before partitions has.
        await using (var older = NewContext())
        {
            older.Database.EnsureCreated();
            var olderStore = new SQLiteEntityGraphStore(older, Options.Create(new SQLiteEntityGraphOptions()), NullLogger<SQLiteEntityGraphStore>.Instance);
            await olderStore.StoreEntitiesBatchAsync([new GraphEntity { Id = "e1", Name = "Acme", NormalizedName = "acme", ChunkIds = ["k1"] }], ct);
            await olderStore.StoreCommunityAsync(new GraphCommunity { Id = "c1", Name = "c1", EntityIds = ["e1"], ChunkIds = ["k1"] }, ct);
            foreach (var clrType in new[] { typeof(SQLiteEntityGraphEntity), typeof(SQLiteEntityCommunityEntity) })
            {
                var (table, column) = PartitionColumn(older, clrType);
                await older.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{table}\" DROP COLUMN \"{column}\"", ct);
                Assert.DoesNotContain(column, ColumnsOf(_connection, table));
            }
        }

        await using var context = NewContext();
        SQLiteSchemaProvisioner.Provision(context);

        foreach (var clrType in new[] { typeof(SQLiteEntityGraphEntity), typeof(SQLiteEntityCommunityEntity) })
        {
            var (table, column) = PartitionColumn(context, clrType);
            Assert.Contains(column, ColumnsOf(_connection, table));
        }

        var store = new SQLiteEntityGraphStore(context, Options.Create(new SQLiteEntityGraphOptions()), NullLogger<SQLiteEntityGraphStore>.Instance);
        var entity = Assert.Single(await store.GetEntitiesByChunkIdsAsync(["k1"], ct: ct));
        Assert.Equal(GraphPartition.Default, entity.Partition);
        Assert.Equal("c1", Assert.Single(await store.GetCommunitiesByChunkIdsAsync(["k1"], ct: ct)).Id);
        Assert.Empty(await store.GetEntitiesByChunkIdsAsync(["k1"], "tenant-1", ct));
    }

    private static (string Table, string Column) PartitionColumn(DbContext context, Type clrType)
    {
        var entityType = context.Model.FindEntityType(clrType)!;
        var table = entityType.GetTableName()!;
        return (table, entityType.FindProperty("Partition")!.GetColumnName(StoreObjectIdentifier.Table(table))!);
    }

    [Fact]
    public void ProvisioningAnUpToDateDatabase_ChangesNothing()
    {
        _connection.Open();
        using var context = NewContext();
        context.Database.EnsureCreated();
        var (table, _) = ChunkIdsColumn(context);
        var before = ColumnsOf(_connection, table);

        SQLiteSchemaProvisioner.Provision(context);

        Assert.Equal(before, ColumnsOf(_connection, table));
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
