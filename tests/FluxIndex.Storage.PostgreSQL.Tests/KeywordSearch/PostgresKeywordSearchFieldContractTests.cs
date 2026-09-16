using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.Storage.PostgreSQL.KeywordSearch;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests.KeywordSearch;

/// <summary>
/// Runs the shared keyword-field contract suite against the PostgreSQL keyword index on a real
/// container — the backend the consumer that reported the defect (FluxIndex docket #28) runs. One
/// container serves every fact; each fact gets a fresh service over a cleared index, because the
/// field set is constructor state.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgresKeywordSearchFieldContractTests : KeywordSearchFieldContractSuite, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();
    private readonly List<PostgresKeywordSearchService> _services = [];

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
    }

    protected override async Task<IKeywordSearchService> CreateServiceAsync(KeywordFieldOptions? fields)
    {
        var service = new PostgresKeywordSearchService(
            _container.GetConnectionString(),
            NullLogger<PostgresKeywordSearchService>.Instance,
            analyzer: null,
            fields);
        _services.Add(service);
        await service.ClearIndexAsync();
        return service;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var service in _services)
            service.Dispose();
        await _container.DisposeAsync();
    }
}
