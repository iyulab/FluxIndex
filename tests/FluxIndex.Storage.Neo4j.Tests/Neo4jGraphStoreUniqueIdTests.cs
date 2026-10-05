using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neo4j.Driver;
using Testcontainers.Neo4j;
using Xunit;

namespace FluxIndex.Storage.Neo4j.Tests;

/// <summary>
/// Entity ids are derived, so concurrent builds merge the same new id; a single-node MERGE stays one node only when a
/// uniqueness constraint on the id gives it a lock. These facts check that the store provisions that constraint — on a
/// fresh database, over the plain id index earlier versions created, and not at all (keeping the index) while
/// duplicate ids already exist. Every fact starts from an empty database and a new store.
/// </summary>
[Collection("Neo4j")]
[Trait("Category", "Integration")]
public sealed class Neo4jGraphStoreUniqueIdTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Neo4jContainer _container = new Neo4jBuilder("neo4j:5-community").Build();
    private IDriver _driver = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _driver = GraphDatabase.Driver(_container.GetConnectionString(), AuthTokens.Basic("neo4j", "neo4j"));
    }

    private Neo4jGraphStore NewStore() => new(
        Options.Create(new Neo4jOptions
        {
            Uri = _container.GetConnectionString(),
            Username = "neo4j",
            Password = "neo4j",
            Database = "neo4j"
        }),
        NullLogger<Neo4jGraphStore>.Instance);

    private async Task<List<IRecord>> QueryAsync(string query, object? parameters = null)
    {
        var result = await _driver.ExecutableQuery(query).WithParameters(parameters ?? new { }).ExecuteAsync(Ct);
        return result.Result.ToList();
    }

    private async Task ResetAsync()
    {
        await QueryAsync("MATCH (n) DETACH DELETE n");
        foreach (var record in await QueryAsync("SHOW CONSTRAINTS YIELD name RETURN name"))
        {
            await QueryAsync($"DROP CONSTRAINT `{record["name"].As<string>()}` IF EXISTS");
        }
        foreach (var record in await QueryAsync("SHOW INDEXES YIELD name, type WHERE type = 'RANGE' RETURN name"))
        {
            await QueryAsync($"DROP INDEX `{record["name"].As<string>()}` IF EXISTS");
        }
    }

    private async Task<bool> EntityIdIsUniqueAsync() =>
        (await QueryAsync(
            "SHOW CONSTRAINTS YIELD labelsOrTypes, properties, type " +
            "WHERE labelsOrTypes = ['Entity'] AND properties = ['id'] AND type CONTAINS 'UNIQUENESS' RETURN count(*) AS n"))
        .Single()["n"].As<long>() == 1;

    private async Task<long> PlainEntityIdIndexesAsync() =>
        (await QueryAsync(
            "SHOW INDEXES YIELD labelsOrTypes, properties, owningConstraint, type " +
            "WHERE labelsOrTypes = ['Entity'] AND properties = ['id'] AND owningConstraint IS NULL AND type = 'RANGE' RETURN count(*) AS n"))
        .Single()["n"].As<long>();

    private static GraphEntity Entity(string id, string chunkId) => new()
    {
        Id = id,
        Name = "Cloudgate",
        NormalizedName = "cloudgate",
        Type = NamedEntityType.Product,
        SurfaceForms = ["Cloudgate"],
        Confidence = 0.9,
        MentionCount = 1,
        ChunkIds = [chunkId],
        DocumentIds = [$"doc-{chunkId}"]
    };

    [Fact]
    public async Task Concurrent_writes_of_one_new_id_leave_one_node_holding_every_chunk()
    {
        await ResetAsync();
        await using var store = NewStore();
        await store.GetEntityByIdAsync("warm-up", Ct); // provisions the schema

        var chunks = Enumerable.Range(0, 8).Select(i => $"c{i}").ToList();
        await Task.WhenAll(chunks.Select(chunk => Task.Run(() => store.StoreEntityAsync(Entity("entity-1", chunk), Ct), Ct)));

        var nodes = await QueryAsync("MATCH (e:Entity {id: 'entity-1'}) RETURN e.chunkIds AS chunkIds");
        var node = Assert.Single(nodes);
        Assert.Equal(chunks, node["chunkIds"].As<List<string>>().Order());
        Assert.True(await EntityIdIsUniqueAsync());
    }

    [Fact]
    public async Task The_plain_id_index_of_an_earlier_version_is_replaced_by_the_constraint()
    {
        await ResetAsync();
        await QueryAsync("CREATE INDEX FOR (e:Entity) ON (e.id)");
        await QueryAsync("CREATE (:Entity {id: 'stored', chunkIds: ['c0']})");
        Assert.Equal(1, await PlainEntityIdIndexesAsync());

        await using var store = NewStore();
        Assert.Null(await store.GetEntityByIdAsync("missing", Ct)); // provisions the schema

        Assert.True(await EntityIdIsUniqueAsync());
        Assert.Single(await QueryAsync("MATCH (e:Entity {id: 'stored'}) RETURN e"));
        Assert.Equal(0, await PlainEntityIdIndexesAsync());
    }

    [Fact]
    public async Task Duplicate_ids_already_stored_keep_the_plain_index_and_the_store_still_works()
    {
        await ResetAsync();
        await QueryAsync("CREATE (:Entity {id: 'twin', chunkIds: ['c0']}), (:Entity {id: 'twin', chunkIds: ['c1']})");

        await using var store = NewStore();
        await store.StoreEntityAsync(Entity("other", "c2"), Ct);

        Assert.False(await EntityIdIsUniqueAsync());
        Assert.Equal(1, await PlainEntityIdIndexesAsync());
        Assert.NotNull(await store.GetEntityByIdAsync("other", Ct));
    }

    public async ValueTask DisposeAsync()
    {
        if (_driver is not null) await _driver.DisposeAsync();
        await _container.DisposeAsync();
    }
}
