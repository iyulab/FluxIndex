using FluentAssertions;
using FluxIndex.SDK;
using FluxIndex.SDK.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Teeth for the provisioning convention. The same defect class recurred three times — a component
/// migrated its schema from an <see cref="IHostedService"/> only, and the SDK builder never starts a
/// host, so <c>Build()</c> returned with that component's tables missing and its first operation failed
/// on a relation that did not exist (0.21.1 PostgreSQL vectors → 0.21.2/0.21.3 graph + cache →
/// 0.21.4 quantized).
/// <para>
/// The rule this pins: <b>a component that migrates via a hosted service must also register an
/// <see cref="IStorageInitializer"/></b>, because that is the only thing <c>Build()</c> runs. The
/// population is found by reflection so a newly added component cannot slip past — it will not be in
/// the table below and the test fails until someone declares its pairing (or its exemption).
/// </para>
/// </summary>
public class ProvisioningConventionTests
{
    /// <summary>
    /// Every migration hosted service in the storage assemblies, and how its schema reaches
    /// <c>Build()</c>. Exempt entries state why the builder cannot reach them.
    /// </summary>
    private static readonly (string HostedService, string? Initializer, string? ExemptBecause)[] KnownMigrations =
    [
        // SQLite — the builder enables all of these, so each needs an initializer.
        ("SQLiteMigrationService", "SQLiteStorageInitializer", null),
        ("SQLiteVecMigrationService", "SQLiteStorageInitializer", null),
        ("SQLiteCacheMigrationService", "SQLiteCacheSchemaInitializer", null),
        ("SQLiteGraphMigrationService", "SQLiteGraphSchemaInitializer", null),
        ("SQLiteEntityGraphMigrationService", "SQLiteEntityGraphSchemaInitializer", null),

        // PostgreSQL.
        ("PostgresCacheMigrationService", "PostgresCacheSchemaInitializer", null),
        ("PostgresGraphMigrationService", "PostgresGraphSchemaInitializer", null),
        ("PostgreSQLQuantizedMigrationService", "PostgreSQLQuantizedStorageInitializer", null),

        // Exemption: the SQLite quantized store has no initializer. Unlike its PostgreSQL counterpart
        // it is not reachable from any builder path (no UseSQLite option registers it), so only a
        // hosted application can construct it — and there the hosted service runs. Recorded as an
        // asymmetry rather than silently omitted: if the builder ever registers it, this exemption is
        // what should be revisited.
        ("SQLiteQuantizedMigrationService", null, "not reachable from any builder path"),
    ];

    private static Type[] StorageAssemblyTypes() =>
        [
            .. typeof(PostgreSQLStorageInitializer).Assembly.GetTypes(),
            .. typeof(FluxIndex.Storage.SQLite.SQLiteOptions).Assembly.GetTypes()
        ];

    /// <summary>
    /// Reflection finds the population; the table declares the pairings. A component added with only a
    /// hosted migration shows up here and fails until its pairing (or exemption) is declared.
    /// </summary>
    [Fact]
    public void EveryMigrationHostedService_IsDeclaredInTheConventionTable()
    {
        var discovered = StorageAssemblyTypes()
            .Where(t => typeof(IHostedService).IsAssignableFrom(t))
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => t.Name.Contains("Migration", StringComparison.Ordinal))
            .Select(t => t.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        discovered.Should().NotBeEmpty("otherwise this test proves nothing");
        discovered.Should().BeEquivalentTo(
            KnownMigrations.Select(m => m.HostedService),
            "a component that migrates from a hosted service must declare how Build() provisions it — "
            + "see this test's summary for the three times that was missed");
    }

    /// <summary>
    /// The declared initializer types must exist. A rename that leaves the table stale would otherwise
    /// keep the pairing "declared" while nothing enforces it.
    /// </summary>
    [Fact]
    public void EveryDeclaredInitializer_ExistsInTheStorageAssemblies()
    {
        var initializerNames = StorageAssemblyTypes()
            .Where(t => typeof(IStorageInitializer).IsAssignableFrom(t))
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (hostedService, initializer, exemptBecause) in KnownMigrations)
        {
            if (initializer is null)
            {
                exemptBecause.Should().NotBeNullOrWhiteSpace(
                    $"{hostedService} has no initializer, so the table must say why");
                continue;
            }

            initializerNames.Should().Contain(initializer,
                $"{hostedService} is paired with {initializer}, which must still exist");
        }
    }

    /// <summary>
    /// The builder path itself: every migration hosted service it registers must be accompanied by at
    /// least as many initializers, since initializers are the only provisioning <c>Build()</c> runs.
    /// The SQLite counterpart lives in that package's own test assembly — its registration entry point
    /// is internal to it, and exposing internals across backends for one assertion is not worth it.
    /// </summary>
    [Fact]
    public void PostgreSQLBuilderPath_RegistersAnInitializerForEveryMigrationHostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var options = new FluxIndexOptions();
        options.VectorStore.Provider = "PostgreSQL";
        options.VectorStore.ConnectionString = "Host=localhost;Database=flux;Username=u;Password=p";

        FluxIndexContextBuilderExtensions.RegisterPostgreSQLServices(services, options);

        var hostedMigrations = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType?.Name)
            .Count(n => n is not null && n.Contains("Migration", StringComparison.Ordinal));

        var initializers = services.Count(d => d.ServiceType == typeof(IStorageInitializer));

        initializers.Should().BeGreaterThanOrEqualTo(hostedMigrations,
            "Build() runs initializers only, so hosted-service-only migration leaves that "
            + "component's schema uncreated");
    }
}
