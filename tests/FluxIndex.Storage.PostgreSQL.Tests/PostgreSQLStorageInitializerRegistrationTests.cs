using AwesomeAssertions;
using FluxIndex.SDK;
using FluxIndex.SDK.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Docker-free tests for the PostgreSQL auto-initialization wiring. These assert the opt-out gate
/// (<see cref="VectorStoreOptions.EnableAutoMigration"/> → <see cref="PostgreSQLOptions.AutoMigrate"/>)
/// without touching a live database, so they run in CI where the Integration-tagged schema-creation
/// test is filtered out. Since 0.38.0 the initializer is always registered and reads the option itself,
/// so the gate is observed on the option and on the initializer's behaviour, not on the registration.
/// </summary>
public class PostgreSQLStorageInitializerRegistrationTests
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

    [Fact]
    public void RegisterPostgreSQLServices_WithAutoMigrationEnabled_RegistersStorageInitializer()
    {
        var services = new ServiceCollection();

        FluxIndexContextBuilderExtensions.RegisterPostgreSQLServices(
            services, PostgresOptions(enableAutoMigration: true));

        services.Should().Contain(d => d.ServiceType == typeof(IStorageInitializer));
    }

    [Fact]
    public void RegisterPostgreSQLServices_WithAutoMigrationDisabled_TurnsTheStoreOptionOff()
    {
        var services = new ServiceCollection();
        FluxIndexContextBuilderExtensions.RegisterPostgreSQLServices(
            services, PostgresOptions(enableAutoMigration: false));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<PostgreSQLOptions>>().Value.AutoMigrate.Should().BeFalse(
            "the builder-level flag must reach the one switch the initializer reads");
    }

    [Fact]
    public void StorageInitializer_WithAutoMigrateOff_TouchesNothing_NotEvenAConnection()
    {
        // An unreachable host: any attempt to provision would fail on connect. With AutoMigrate off the
        // initializer must return before that — before 0.38.0 the option was never read at all.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSQLVectorStore(o =>
        {
            o.ConnectionString = "Host=192.0.2.1;Port=1;Database=flux;Username=u;Password=p;Timeout=1";
            o.AutoMigrate = false;
        });
        using var provider = services.BuildServiceProvider();

        var initializer = provider.GetServices<IStorageInitializer>().Should().ContainSingle().Subject;
        var act = () => initializer.InitializeSync(provider);

        act.Should().NotThrow();
    }

    [Fact]
    public void RegisterPostgreSQLServices_WithDefaultOptions_RegistersStorageInitializer()
    {
        // Default (no explicit flag) must auto-initialize — symmetric with SQLite, which always
        // initializes on Build(). This pins EnableAutoMigration's default to true.
        var services = new ServiceCollection();

        FluxIndexContextBuilderExtensions.RegisterPostgreSQLServices(services, PostgresOptions());

        services.Should().Contain(d => d.ServiceType == typeof(IStorageInitializer));
    }
}
