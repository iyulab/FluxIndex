using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Quantization;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.SDK;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Runs the shared reassignment contract suite against the PostgreSQL vector store on a real container. One container
/// serves every fact; each fact starts from an empty table.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgreSQLVectorStoreReassignContractTests : VectorStoreReassignContractSuite, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();
    private IVectorStore _store = null!;

    protected override int Dimensions => 1536;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSQLVectorStore(_container.GetConnectionString());
        var provider = services.BuildServiceProvider();
        foreach (var initializer in provider.GetServices<IStorageInitializer>())
        {
            initializer.InitializeSync(provider);
        }
        _store = provider.GetRequiredService<IVectorStore>();
    }

    protected override async Task<IVectorStore> CreateStoreAsync()
    {
        await _store.ClearAsync();
        return _store;
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>
/// Runs the shared reassignment contract suite against the PostgreSQL quantized vector store, whose quantized rows name
/// their chunk and must follow it to the new id.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgreSQLQuantizedVectorStoreReassignContractTests : VectorStoreReassignContractSuite, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();
    private PostgreSQLQuantizedVectorStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
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
            EmbeddingDimensions = Dimensions
        });
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();
        var dbOptions = new DbContextOptionsBuilder<FluxIndexQuantizedDbContext>()
            .UseNpgsql(dataSource, o => o.UseVector())
            .Options;
        await using (var context = new FluxIndexQuantizedDbContext(dbOptions, options))
        {
            await context.Database.EnsureCreatedAsync();
        }

        var quantizer = new ScalarQuantizer(Options.Create(new QuantizationOptions()), NullLogger<ScalarQuantizer>.Instance);
        _store = new PostgreSQLQuantizedVectorStore(
            new TestDbContextFactory<FluxIndexQuantizedDbContext>(() => new FluxIndexQuantizedDbContext(dbOptions, options)),
            quantizer,
            NullLogger<PostgreSQLQuantizedVectorStore>.Instance,
            options);
    }

    protected override async Task<IVectorStore> CreateStoreAsync()
    {
        await _store.ClearAsync();
        return _store;
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
