using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Quantization;
using FluxIndex.Core.Tests.Contract;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// Runs the shared IVectorStore filter-contract suite against SQLiteVectorStore.
/// </summary>
[Collection("SQLite Tests")]
public class SQLiteVectorStoreFilterContractTests : VectorStoreFilterContractSuite
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
/// Runs the shared IVectorStore filter-contract suite against SQLiteQuantizedVectorStore —
/// whose SearchAsync previously ignored filters entirely (filter-scope leak).
/// </summary>
[Collection("SQLite Tests")]
public class SQLiteQuantizedVectorStoreFilterContractTests : VectorStoreFilterContractSuite
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
