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
/// Runs the shared IVectorStore chunk-identity contract suite against SQLiteVectorStore (the
/// in-process fallback store). The sqlite-vec store is covered by
/// <see cref="SQLiteVecChunkIdContractTests"/>, which needs the native extension.
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
