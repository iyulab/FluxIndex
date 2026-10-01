using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.Storage.PostgreSQL.KeywordSearch;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests.KeywordSearch;

/// <summary>Runs the shared document-frequency contract suite against the PostgreSQL keyword index on a real container.</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgresKeywordSearchDocumentFrequencyContractTests : KeywordSearchDocumentFrequencyContractSuite, IAsyncLifetime
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

    [Fact]
    public void The_configured_command_timeout_reaches_the_keyword_index()
    {
        // PostgreSQLOptions.CommandTimeout used to reach the vector store only; a long keyword rebuild could not raise it.
        var options = Microsoft.Extensions.Options.Options.Create(new PostgreSQLOptions
        {
            ConnectionString = _container.GetConnectionString(),
            CommandTimeout = 240,
        });
        using var service = new PostgresKeywordSearchService(options, NullLogger<PostgresKeywordSearchService>.Instance);

        var field = typeof(PostgresKeywordSearchService).GetField("_connectionString", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var effective = new Npgsql.NpgsqlConnectionStringBuilder((string)field.GetValue(service)!);

        Assert.Equal(240, effective.CommandTimeout);
    }

    [Fact]
    public async Task Document_frequency_for_a_large_write_is_maintained_in_bounded_statements()
    {
        // Production shape (a rebuild of one large entry on a 1.5 M-posting index): every term the write touched went
        // into one document-frequency UPDATE whose cost grows with the term count, and that statement outran the
        // command timeout. A small test index cannot make one statement slow, so the guard is on its size: the
        // 40,000 terms of this write must arrive in many statements, none naming all of them.
        var ct = TestContext.Current.CancellationToken;
        var statements = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if ((activity.GetTagItem("db.query.text") ?? activity.GetTagItem("db.statement")) is string text)
                {
                    statements.Add(text);
                }
            },
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        await _service.ClearIndexAsync(ct);
        var words = Enumerable.Range(0, 40_000).Select(i => $"t{i:D6}q").ToArray();

        await _service.IndexChunksAsync([new FluxIndex.Core.Domain.Entities.DocumentChunk
        {
            Id = "big", DocumentId = "doc", Content = string.Join(' ', words), TokenCount = words.Length,
        }], ct);

        var dfUpdates = statements.Count(t => t.Contains("SET document_frequency", StringComparison.Ordinal));
        Assert.True(dfUpdates >= 20, $"expected the 40,000 terms in batches of at most 2,000 (>= 20 statements), saw {dfUpdates}");
        var df = await _service.GetDocumentFrequenciesAsync([words[0], words[39_999]], ct);
        Assert.Equal(1, df[words[0]]);
        Assert.Equal(1, df[words[39_999]]);
    }

    public async ValueTask DisposeAsync()
    {
        _service.Dispose();
        await _container.DisposeAsync();
    }
}
