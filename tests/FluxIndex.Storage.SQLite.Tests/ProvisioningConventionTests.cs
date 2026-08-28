using AwesomeAssertions;
using FluxIndex.SDK;
using FluxIndex.SDK.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// SQLite half of the provisioning convention. <c>Build()</c> never starts a host, so it provisions
/// exclusively through <see cref="IStorageInitializer"/>: a component that migrates from a hosted
/// service and registers no initializer has its schema created on neither path, which is the defect
/// that recurred three times (0.21.1 → 0.21.3 → 0.21.4).
/// <para>
/// The cross-assembly half — which migration services exist at all, and what each is paired with —
/// lives in <c>FluxIndex.Storage.PostgreSQL.Tests.ProvisioningConventionTests</c>, the one test project
/// that references both storage packages.
/// </para>
/// </summary>
public class ProvisioningConventionTests
{
    [Fact]
    public void SQLiteBuilderPath_RegistersAnInitializerForEveryMigrationHostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var options = new FluxIndexOptions();
        options.VectorStore.Provider = "SQLite";
        options.VectorStore.ConnectionString = "Data Source=:memory:";
        options.GraphStore.Provider = "SQLite";
        options.SemanticCache.Provider = "SQLite";

        FluxIndexContextBuilderExtensions.RegisterSQLiteServices(services, options);

        var hostedMigrations = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType?.Name)
            .Count(n => n is not null && n.Contains("Migration", StringComparison.Ordinal));

        hostedMigrations.Should().BeGreaterThan(0,
            "otherwise this test would pass without observing anything");

        var initializers = services.Count(d => d.ServiceType == typeof(IStorageInitializer));

        initializers.Should().BeGreaterThanOrEqualTo(hostedMigrations,
            "every component the builder enables must provision on the Build() path too");
    }

    /// <summary>
    /// The keyword index is provisioned on the builder path as well — it was the newest component to
    /// join this convention, and it is the one a consumer notices last (an empty keyword index does not
    /// fail, it just makes hybrid search quietly vector-only).
    /// </summary>
    [Fact]
    public void SQLiteBuilderPath_ProvisionsTheKeywordIndex()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var options = new FluxIndexOptions();
        options.VectorStore.Provider = "SQLite";
        options.VectorStore.ConnectionString = "Data Source=:memory:";

        FluxIndexContextBuilderExtensions.RegisterSQLiteServices(services, options);

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IStorageInitializer>()
            .Should().Contain(i => i.GetType().Name == "SQLiteKeywordSearchInitializer");
    }
}
