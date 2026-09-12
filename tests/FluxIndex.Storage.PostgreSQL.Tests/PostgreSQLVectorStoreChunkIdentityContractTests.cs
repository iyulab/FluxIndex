using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Runs the shared IVectorStore chunk-identity contract suite against the PostgreSQL vector store on
/// a real container. One container serves every fact; each fact starts from an empty "doc-1", the
/// only document the suite writes.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgreSQLVectorStoreChunkIdentityContractTests : VectorStoreChunkIdentityContractSuite, IAsyncLifetime
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
        await _store.DeleteByDocumentIdAsync("doc-1");
        return _store;
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
