using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.Storage.PostgreSQL.KeywordSearch;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests.KeywordSearch;

/// <summary>
/// Runs the shared keyword-index chunk-identity contract suite against the PostgreSQL keyword index
/// on a real container — the same suite the SQLite and in-memory indexes pass, so the three
/// implementations of <see cref="IKeywordSearchService"/> are held to one meaning of "index this id"
/// and "which ids does this document have". One container serves every fact; each fact starts from
/// an empty index.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgresKeywordSearchChunkIdentityContractTests : KeywordSearchChunkIdentityContractSuite, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();
    private PostgresKeywordSearchService _service = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _service = new PostgresKeywordSearchService(_container.GetConnectionString(), NullLogger<PostgresKeywordSearchService>.Instance);
    }

    protected override async Task<IKeywordSearchService> CreateServiceAsync()
    {
        await _service.ClearIndexAsync();
        return _service;
    }

    public async ValueTask DisposeAsync()
    {
        _service.Dispose();
        await _container.DisposeAsync();
    }
}
