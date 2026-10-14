using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.Storage.PostgreSQL.KeywordSearch;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests.KeywordSearch;

/// <summary>
/// Runs the shared index-definition contract suite against the PostgreSQL keyword index on a real container. One
/// container serves every fact; <see cref="CreateServiceAsync"/> clears the index, <see cref="ReopenAsync"/> does not.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgresKeywordSearchDefinitionChangeContractTests : KeywordSearchDefinitionChangeContractSuite, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();
    private readonly List<PostgresKeywordSearchService> _services = [];

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
    }

    private PostgresKeywordSearchService Open(ITextAnalyzer? analyzer, KeywordFieldOptions? fields)
    {
        var service = new PostgresKeywordSearchService(
            _container.GetConnectionString(),
            NullLogger<PostgresKeywordSearchService>.Instance,
            analyzer,
            fields);
        _services.Add(service);
        return service;
    }

    protected override async Task<IKeywordSearchService> CreateServiceAsync(ITextAnalyzer? analyzer, KeywordFieldOptions? fields = null)
    {
        var service = Open(analyzer, fields);
        await service.ClearIndexAsync();
        return service;
    }

    protected override Task<IKeywordSearchService> ReopenAsync(ITextAnalyzer analyzer, KeywordFieldOptions? fields = null)
        => Task.FromResult<IKeywordSearchService>(Open(analyzer, fields));

    protected override async Task ForgetRecordedAnalyzerAsync()
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM bm25_statistics WHERE key LIKE 'analyzer:%'";
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var service in _services)
            service.Dispose();
        await _container.DisposeAsync();
    }
}
