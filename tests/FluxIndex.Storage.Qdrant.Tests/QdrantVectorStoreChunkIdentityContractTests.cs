using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Tests.Contract;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Qdrant;
using Xunit;

namespace FluxIndex.Storage.Qdrant.Tests;

/// <summary>
/// Runs the shared IVectorStore chunk-identity contract suite against Qdrant on a real container.
/// One container serves every fact; each fact gets its own collection.
/// </summary>
[Collection("Qdrant")]
[Trait("Category", "Integration")]
public sealed class QdrantVectorStoreChunkIdentityContractTests : VectorStoreChunkIdentityContractSuite, IAsyncLifetime
{
    private readonly QdrantContainer _container = new QdrantBuilder("qdrant/qdrant:latest").Build();
    private readonly List<QdrantVectorStore> _stores = [];

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    protected override Task<IVectorStore> CreateStoreAsync()
    {
        var store = new QdrantVectorStore(
            Options.Create(new QdrantOptions
            {
                Host = _container.Hostname,
                GrpcPort = _container.GetMappedPublicPort(6334),
                BaseCollectionName = $"contract_{Guid.NewGuid():N}",
                VectorSize = Dimensions,
                NamingStrategy = CollectionNamingStrategy.Fixed,
                CreateCollectionOnStartup = true
            }),
            NullLogger<QdrantVectorStore>.Instance);
        _stores.Add(store);
        return Task.FromResult<IVectorStore>(store);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var store in _stores)
        {
            if (store is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
            else if (store is IDisposable disposable) disposable.Dispose();
        }
        await _container.DisposeAsync();
    }
}
