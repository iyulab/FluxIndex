using FluentAssertions;
using FluxIndex.SDK;
using FluxIndex.SDK.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Docker-free tests for the PostgreSQL auto-initialization wiring. These assert the opt-out gate
/// (<see cref="VectorStoreOptions.EnableAutoMigration"/>) without touching a live database, so they
/// run in CI where the Integration-tagged schema-creation test is filtered out.
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
    public void RegisterPostgreSQLServices_WithAutoMigrationDisabled_DoesNotRegisterStorageInitializer()
    {
        var services = new ServiceCollection();

        FluxIndexContextBuilderExtensions.RegisterPostgreSQLServices(
            services, PostgresOptions(enableAutoMigration: false));

        services.Should().NotContain(d => d.ServiceType == typeof(IStorageInitializer));
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
