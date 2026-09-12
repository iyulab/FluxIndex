using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.Storage.PostgreSQL.EntityGraph;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Runs the shared graph-store chunk-provenance contract suite against the PostgreSQL entity graph
/// store on a real pgvector container. One container and one store serve every fact; the suite keys
/// its rows under fresh ids.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgresEntityGraphStoreChunkProvenanceContractTests : GraphStoreChunkProvenanceContractSuite, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();
    private NpgsqlDataSource _dataSource = null!;
    private EntityGraphDbContext _context = null!;
    private PostgresEntityGraphStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var graphOptions = Options.Create(new EntityGraphOptions
        {
            ConnectionString = _container.GetConnectionString(),
            EmbeddingDimension = 4
        });
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(graphOptions.Value.ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        dataSourceBuilder.UseVector();
        _dataSource = dataSourceBuilder.Build();
        _context = new EntityGraphDbContext(
            new DbContextOptionsBuilder<EntityGraphDbContext>().UseNpgsql(_dataSource, npgsql => npgsql.UseVector()).Options,
            graphOptions);
        await using (var cmd = _dataSource.CreateCommand("CREATE EXTENSION IF NOT EXISTS vector"))
        {
            await cmd.ExecuteNonQueryAsync();
        }
        await _context.Database.EnsureCreatedAsync();
        _store = new PostgresEntityGraphStore(_context, graphOptions, NullLogger<PostgresEntityGraphStore>.Instance);
    }

    protected override Task<IGraphStore> CreateStoreAsync() => Task.FromResult<IGraphStore>(_store);

    public async ValueTask DisposeAsync()
    {
        if (_context is not null) await _context.DisposeAsync();
        if (_dataSource is not null) await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}
