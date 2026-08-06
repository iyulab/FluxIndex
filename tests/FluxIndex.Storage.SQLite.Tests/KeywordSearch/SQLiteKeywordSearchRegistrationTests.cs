using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK;
using FluxIndex.SDK.Configuration;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests.KeywordSearch;

/// <summary>
/// Wiring tests for the SQLite keyword backend's own options. The defect these guard is silent: a
/// keyword leg that is not registered still returns results — from the vector leg alone — so
/// nothing fails and the sparse half is simply absent.
/// </summary>
public class SQLiteKeywordSearchRegistrationTests
{
    private static FluxIndexOptions SqliteOptions()
    {
        var options = new FluxIndexOptions();
        options.VectorStore.Provider = "SQLite";
        options.VectorStore.ConnectionString = "Data Source=:memory:";
        return options;
    }

    private static ServiceProvider BuildProvider(FluxIndexOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        FluxIndexContextBuilderExtensions.RegisterSQLiteServices(services, options);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void RegisterSQLiteServices_WithDefaults_StillFollowsTheVectorStore()
    {
        using var provider = BuildProvider(SqliteOptions());

        provider.GetRequiredService<IKeywordSearchService>()
            .Should().BeOfType<SQLiteKeywordSearchService>(
                "an unset keyword provider must keep the behavior the vector-gated registration had");
    }

    /// <summary>
    /// The split case the vector gate could not express: vectors elsewhere, keyword index here.
    /// </summary>
    [Fact]
    public void RegisterSQLiteServices_WithItsOwnProvider_RegistersTheKeywordLegBesideANonSqliteVectorStore()
    {
        var options = SqliteOptions();
        options.VectorStore.Provider = "Qdrant";
        options.KeywordSearch.Provider = "SQLite";
        options.KeywordSearch.UseVectorStoreConnection = false;
        options.KeywordSearch.ConnectionString = "Data Source=:memory:";

        using var provider = BuildProvider(options);

        provider.GetRequiredService<IKeywordSearchService>()
            .Should().BeOfType<SQLiteKeywordSearchService>();
    }

    [Fact]
    public void RegisterSQLiteServices_WithANonSqliteKeywordProvider_RegistersNoKeywordBackend()
    {
        var options = SqliteOptions();
        options.KeywordSearch.Provider = "PostgreSQL";

        var services = new ServiceCollection();
        services.AddLogging();
        FluxIndexContextBuilderExtensions.RegisterSQLiteServices(services, options);

        services.Should().NotContain(d => d.ServiceType == typeof(IKeywordSearchService),
            "this package must not claim a leg another provider was asked for");
    }

    /// <summary>
    /// This backend always provisioned, and VectorStoreOptions.EnableAutoMigration documents itself
    /// as honored by PostgreSQL — so unset here means "keep provisioning", not "inherit that flag".
    /// </summary>
    [Fact]
    public void RegisterSQLiteServices_WithMigrationUnset_StillRegistersTheKeywordInitializer()
    {
        var options = SqliteOptions();
        options.VectorStore.EnableAutoMigration = false;

        using var provider = BuildProvider(options);

        provider.GetServices<IStorageInitializer>()
            .Should().Contain(i => i.GetType().Name == "SQLiteKeywordSearchInitializer");
    }

    [Fact]
    public void RegisterSQLiteServices_WithMigrationDisabledExplicitly_RegistersNoKeywordInitializer()
    {
        var options = SqliteOptions();
        options.KeywordSearch.EnableAutoMigration = false;

        using var provider = BuildProvider(options);

        provider.GetRequiredService<IKeywordSearchService>()
            .Should().BeOfType<SQLiteKeywordSearchService>("keyword search itself is not opt-out");
        provider.GetServices<IStorageInitializer>()
            .Should().NotContain(i => i.GetType().Name == "SQLiteKeywordSearchInitializer");
    }
}
