using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK;
using FluxIndex.SDK.Configuration;
using FluxIndex.Storage.PostgreSQL.KeywordSearch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests.KeywordSearch;

/// <summary>
/// Docker-free wiring tests for the PostgreSQL keyword backend. The defect these guard is silent:
/// if the persistent backend is not registered, hybrid search still returns results — from the vector
/// leg alone — so nothing fails and the keyword leg is simply empty after every restart.
/// </summary>
public class PostgresKeywordSearchRegistrationTests
{
    private static FluxIndexOptions PostgresOptions(bool? enableAutoMigration = null)
    {
        var options = new FluxIndexOptions();
        options.VectorStore.Provider = "PostgreSQL";
        options.VectorStore.ConnectionString = "Host=localhost;Database=flux;Username=u;Password=p";
        if (enableAutoMigration.HasValue)
        {
            options.VectorStore.EnableAutoMigration = enableAutoMigration.Value;
        }
        return options;
    }

    private static ServiceProvider BuildProvider(FluxIndexOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        FluxIndexContextBuilderExtensions.RegisterPostgreSQLServices(services, options);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void RegisterPostgreSQLServices_ResolvesTheKeywordServiceToThePersistentBackend()
    {
        using var provider = BuildProvider(PostgresOptions());

        var service = provider.GetRequiredService<IKeywordSearchService>();

        service.Should().BeOfType<PostgresKeywordSearchService>(
            "an in-memory keyword index would degrade hybrid search to vector-only after a restart");
    }

    /// <summary>
    /// The indexer and the retriever must see one instance — two would each hold their own lazily
    /// created schema state and, worse, invite two different notions of what is indexed.
    /// </summary>
    [Fact]
    public void RegisterPostgreSQLServices_RegistersTheKeywordServiceAsASingleton()
    {
        using var provider = BuildProvider(PostgresOptions());

        var first = provider.GetRequiredService<IKeywordSearchService>();
        var second = provider.GetRequiredService<IKeywordSearchService>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void RegisterPostgreSQLServices_RegistersTheKeywordSchemaInitializer()
    {
        using var provider = BuildProvider(PostgresOptions());

        provider.GetServices<IStorageInitializer>()
            .Should().Contain(i => i.GetType().Name == "PostgresKeywordSearchInitializer",
                "Build() provisions storage only through IStorageInitializer");
    }

    /// <summary>
    /// A caller that manages schema externally opts out with the same flag the vector store honors.
    /// </summary>
    [Fact]
    public void RegisterPostgreSQLServices_WithAutoMigrationDisabled_StillRegistersTheServiceButNotTheInitializer()
    {
        using var provider = BuildProvider(PostgresOptions(enableAutoMigration: false));

        provider.GetRequiredService<IKeywordSearchService>()
            .Should().BeOfType<PostgresKeywordSearchService>("keyword search itself is not opt-out");
        provider.GetServices<IStorageInitializer>()
            .Should().NotContain(i => i.GetType().Name == "PostgresKeywordSearchInitializer");
    }

    [Fact]
    public void RegisterPostgreSQLServices_WithANonPostgresVectorProvider_RegistersNoKeywordBackend()
    {
        var options = PostgresOptions();
        options.VectorStore.Provider = "SQLite";

        var services = new ServiceCollection();
        services.AddLogging();
        FluxIndexContextBuilderExtensions.RegisterPostgreSQLServices(services, options);

        services.Should().NotContain(d => d.ServiceType == typeof(IKeywordSearchService));
    }
}
