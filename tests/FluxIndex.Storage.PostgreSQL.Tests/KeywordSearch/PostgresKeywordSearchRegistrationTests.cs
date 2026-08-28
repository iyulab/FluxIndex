using AwesomeAssertions;
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

    /// <summary>
    /// The configuration the library itself recommends — vectors in Qdrant, metadata in PostgreSQL —
    /// could not express a keyword leg at all while the registration was gated on the vector
    /// provider. Consumers responded by copying the registration out of the library.
    ///
    /// <para>
    /// This is the exact option set the README documents under "Placing the keyword leg". All three
    /// are required together: with <c>Provider</c> left unset the leg follows the vector store, and
    /// a vector store with no keyword backend means the leg silently falls back to the in-memory
    /// index — the degradation this option exists to remove. Keep the two in step.
    /// </para>
    /// </summary>
    [Fact]
    public void RegisterPostgreSQLServices_WithItsOwnProvider_RegistersTheKeywordLegBesideANonPostgresVectorStore()
    {
        var options = PostgresOptions();
        options.VectorStore.Provider = "Qdrant";
        options.KeywordSearch.Provider = "PostgreSQL";
        options.KeywordSearch.UseVectorStoreConnection = false;
        options.KeywordSearch.ConnectionString = "Host=localhost;Database=meta;Username=u;Password=p";

        using var provider = BuildProvider(options);

        provider.GetRequiredService<IKeywordSearchService>()
            .Should().BeOfType<PostgresKeywordSearchService>();
    }

    /// <summary>
    /// Asking for a connection of its own and then not supplying one is a configuration error, and
    /// the failure mode it would otherwise take is the worst kind: indexing quietly into whatever
    /// database the vector store happens to use.
    /// </summary>
    [Fact]
    public void RegisterPostgreSQLServices_WithItsOwnConnectionRequestedButMissing_FailsLoudly()
    {
        var options = PostgresOptions();
        options.KeywordSearch.UseVectorStoreConnection = false;

        var act = () => BuildProvider(options);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Unset must keep the previous rule — the keyword leg followed the vector store's migration
    /// flag — so a caller who disabled provisioning does not silently get DDL back.
    /// </summary>
    [Fact]
    public void RegisterPostgreSQLServices_WithMigrationUnset_FollowsTheVectorStoreFlag()
    {
        using var disabled = BuildProvider(PostgresOptions(enableAutoMigration: false));
        using var enabled = BuildProvider(PostgresOptions(enableAutoMigration: true));

        disabled.GetServices<IStorageInitializer>()
            .Should().NotContain(i => i.GetType().Name == "PostgresKeywordSearchInitializer");
        enabled.GetServices<IStorageInitializer>()
            .Should().Contain(i => i.GetType().Name == "PostgresKeywordSearchInitializer");
    }

    [Fact]
    public void RegisterPostgreSQLServices_WithMigrationSetExplicitly_OverridesTheVectorStoreFlag()
    {
        var options = PostgresOptions(enableAutoMigration: false);
        options.KeywordSearch.EnableAutoMigration = true;

        using var provider = BuildProvider(options);

        provider.GetServices<IStorageInitializer>()
            .Should().Contain(i => i.GetType().Name == "PostgresKeywordSearchInitializer");
    }

    /// <summary>
    /// The direct-DI entry point exists because IKeywordSearchService is resolved from consumers'
    /// own root containers, not only from inside FluxIndexContext.
    /// </summary>
    [Fact]
    public void AddPostgreSQLKeywordSearch_RegistersTheBackendAsASharedSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSQLKeywordSearch("Host=localhost;Database=flux;Username=u;Password=p");

        using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<IKeywordSearchService>();
        service.Should().BeOfType<PostgresKeywordSearchService>();
        service.Should().BeSameAs(provider.GetRequiredService<IKeywordSearchService>());
        provider.GetServices<IStorageInitializer>()
            .Should().Contain(i => i.GetType().Name == "PostgresKeywordSearchInitializer");
    }

    [Fact]
    public void AddPostgreSQLKeywordSearch_WithoutAutoMigration_RegistersNoInitializer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSQLKeywordSearch(
            "Host=localhost;Database=flux;Username=u;Password=p", autoMigrate: false);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IKeywordSearchService>().Should().NotBeNull();
        provider.GetServices<IStorageInitializer>()
            .Should().NotContain(i => i.GetType().Name == "PostgresKeywordSearchInitializer");
    }
}
