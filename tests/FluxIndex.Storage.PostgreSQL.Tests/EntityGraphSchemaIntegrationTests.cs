using AwesomeAssertions;
using FluxIndex.Storage.PostgreSQL.EntityGraph;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Schema-level integration tests for EntityGraphDbContext against real pgvector.
/// Guards the ivfflat -> HNSW conversion (0.20.1): ivfflat trains centroids at CREATE INDEX
/// time, so an index created by EnsureCreated on an empty table silently lost recall for
/// data inserted afterwards. Requires Docker.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public class EntityGraphSchemaIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;
    private NpgsqlDataSource _dataSource = null!;
    private EntityGraphDbContext _context = null!;

    public EntityGraphSchemaIntegrationTests()
    {
        _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
    }

    public async Task InitializeAsync()
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

        var contextOptions = new DbContextOptionsBuilder<EntityGraphDbContext>()
            .UseNpgsql(_dataSource, npgsql => npgsql.UseVector())
            .Options;

        _context = new EntityGraphDbContext(contextOptions, graphOptions);

        // Ensure the vector extension exists before EnsureCreated builds the vector indexes.
        await using (var cmd = _dataSource.CreateCommand("CREATE EXTENSION IF NOT EXISTS vector"))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_context is not null)
            await _context.DisposeAsync();
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task EnsureCreated_EmbeddingVectorIndexes_UseHnswNotIvfflat()
    {
        var indexDefs = new List<string>();
        await using var cmd = _dataSource.CreateCommand(
            "SELECT indexdef FROM pg_indexes WHERE indexdef ILIKE '%embedding%'");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            indexDefs.Add(reader.GetString(0));

        indexDefs.Should().NotBeEmpty("EnsureCreated must create the entity/community vector indexes");
        indexDefs.Should().OnlyContain(d => d.Contains("USING hnsw"),
            "ivfflat trained on an empty table silently loses recall for later inserts");
        indexDefs.Should().NotContain(d => d.Contains("ivfflat"));
    }

    [Fact]
    public async Task EnsureCreated_CreatesBothEntityAndCommunityVectorIndexes()
    {
        await using var cmd = _dataSource.CreateCommand(
            "SELECT count(*) FROM pg_indexes WHERE indexdef LIKE '%USING hnsw%'");
        var count = (long)(await cmd.ExecuteScalarAsync())!;

        count.Should().Be(2, "entity embeddings and community embeddings each get an HNSW index");
    }
}
