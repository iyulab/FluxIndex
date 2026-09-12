using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.Storage.SQLite.Graph;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// Runs the shared graph-store chunk-provenance contract suite against the SQLite entity graph store.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteEntityGraphStoreChunkProvenanceContractTests : GraphStoreChunkProvenanceContractSuite
{
    protected override async Task<IGraphStore> CreateStoreAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = Options.Create(new SQLiteEntityGraphOptions());
        var context = new SQLiteEntityGraphDbContext(
            new DbContextOptionsBuilder<SQLiteEntityGraphDbContext>().UseSqlite(connection).Options,
            options);
        await context.Database.EnsureCreatedAsync();
        return new SQLiteEntityGraphStore(context, options, NullLogger<SQLiteEntityGraphStore>.Instance);
    }
}
