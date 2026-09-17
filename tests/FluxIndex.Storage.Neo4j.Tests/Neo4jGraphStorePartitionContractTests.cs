using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Tests.Contract;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Neo4j;
using Xunit;

namespace FluxIndex.Storage.Neo4j.Tests;

/// <summary>
/// Runs the shared graph-store partition contract suite against Neo4j on a real container.
/// One container and one store serve every fact; the suite works in freshly named partitions.
/// </summary>
[Collection("Neo4j")]
[Trait("Category", "Integration")]
public sealed class Neo4jGraphStorePartitionContractTests : GraphStorePartitionContractSuite, IAsyncLifetime
{
    private readonly Neo4jContainer _container = new Neo4jBuilder("neo4j:5-community").Build();
    private Neo4jGraphStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _store = new Neo4jGraphStore(
            Options.Create(new Neo4jOptions
            {
                Uri = _container.GetConnectionString(),
                Username = "neo4j",
                Password = "neo4j",
                Database = "neo4j"
            }),
            NullLogger<Neo4jGraphStore>.Instance);
    }

    protected override Task<IGraphStore> CreateStoreAsync() => Task.FromResult<IGraphStore>(_store);

    public async ValueTask DisposeAsync()
    {
        if (_store is not null) await _store.DisposeAsync();
        await _container.DisposeAsync();
    }
}
