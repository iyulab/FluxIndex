using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Tests.Contract;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Integration test (requires Docker). Runs the quantized-path filter contract against
/// <see cref="PostgreSQLQuantizedVectorStore"/>: its quantized searches used to take no filter, so a scoped hybrid search
/// with <c>UseQuantizedSearch</c> returned chunks from outside the scope.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public class PostgreSQLQuantizedQuantizedFilterContractTests : QuantizedVectorStoreFilterContractSuite, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected override async Task<IQuantizedVectorStore> CreateStoreAsync()
    {
        var connectionString = _container.GetConnectionString();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
            await command.ExecuteNonQueryAsync();
        }

        var options = Options.Create(new PostgreSQLQuantizedOptions
        {
            ConnectionString = connectionString,
            EmbeddingDimensions = Dimension
        });

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        var dbOptions = new DbContextOptionsBuilder<FluxIndexQuantizedDbContext>()
            .UseNpgsql(dataSource, o => o.UseVector())
            .Options;
        var context = new FluxIndexQuantizedDbContext(dbOptions, options);
        await context.Database.EnsureCreatedAsync();

        return new PostgreSQLQuantizedVectorStore(
            new TestDbContextFactory<FluxIndexQuantizedDbContext>(() => new FluxIndexQuantizedDbContext(dbOptions, options)),
            Quantizer, NullLogger<PostgreSQLQuantizedVectorStore>.Instance, options);
    }
}
