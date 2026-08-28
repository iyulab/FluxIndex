using System.Threading.Tasks;
using AwesomeAssertions;
using FluxIndex.SDK;
using FluxIndex.SDK.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Integration test (requires Docker) covering what the SDK builder actually provisions.
/// <c>UsePostgreSQL(conn)</c> turns on the vector store, the graph store and the semantic cache on one
/// connection, and <c>Build()</c> provisions storage exclusively by running the registered
/// <see cref="IStorageInitializer"/> instances against its own service provider — it never starts a
/// host, so any component that migrates from an <c>IHostedService</c> is never provisioned on this
/// path. This test pins that every component the builder enables gets its schema.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public class PostgreSQLBuilderPathProvisioningIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task BuildProvisioning_WithFullPostgreSQLStack_CreatesEveryEnabledComponentSchema()
    {
        var connectionString = _container.GetConnectionString();

        RunBuildProvisioning(connectionString);

        // Vector store.
        (await RegClassAsync(connectionString, "public.vectors")).Should().NotBeNull();

        // Graph store — enabled by UsePostgreSQL on the same connection.
        (await RegClassAsync(connectionString, "public.chunk_hierarchies")).Should().NotBeNull();
        (await RegClassAsync(connectionString, "public.chunk_relationships")).Should().NotBeNull();

        // Semantic cache — likewise enabled by UsePostgreSQL.
        (await RegClassAsync(connectionString, "public.semantic_cache")).Should().NotBeNull();
        (await RegClassAsync(connectionString, "public.cache_stats")).Should().NotBeNull();
    }

    [Fact]
    public async Task BuildProvisioning_RunTwice_IsIdempotent()
    {
        var connectionString = _container.GetConnectionString();

        RunBuildProvisioning(connectionString);
        var act = () => RunBuildProvisioning(connectionString);

        act.Should().NotThrow();
        (await RegClassAsync(connectionString, "public.chunk_hierarchies")).Should().NotBeNull();
    }

    [Fact]
    public async Task BuildProvisioning_OnDatabaseSharedWithApplicationTables_CreatesEveryComponentSchema()
    {
        var connectionString = _container.GetConnectionString();
        await ExecuteAsync(connectionString, "CREATE TABLE any_app_table (id int)");

        RunBuildProvisioning(connectionString);

        (await RegClassAsync(connectionString, "public.vectors")).Should().NotBeNull();
        (await RegClassAsync(connectionString, "public.chunk_hierarchies")).Should().NotBeNull();
        (await RegClassAsync(connectionString, "public.semantic_cache")).Should().NotBeNull();
    }

    /// <summary>
    /// Exactly what FluxIndexContextBuilder.Build() does for storage: register the PostgreSQL
    /// services the options select, build the provider, run every IStorageInitializer.
    /// </summary>
    private static void RunBuildProvisioning(string connectionString)
    {
        var options = new FluxIndexOptions();

        // Equivalent to builder.UsePostgreSQL(connectionString).
        options.VectorStore.Provider = "PostgreSQL";
        options.VectorStore.ConnectionString = connectionString;
        options.GraphStore.Provider = "PostgreSQL";
        options.GraphStore.UseVectorStoreConnection = true;
        options.SemanticCache.Provider = "PostgreSQL";
        options.SemanticCache.UseVectorStoreConnection = true;

        var services = new ServiceCollection();
        services.AddLogging();
        FluxIndexContextBuilderExtensions.RegisterPostgreSQLServices(services, options);

        using var provider = services.BuildServiceProvider();

        foreach (var initializer in provider.GetServices<IStorageInitializer>())
        {
            initializer.InitializeSync(provider);
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> RegClassAsync(string connectionString, string relation)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT to_regclass(@rel)::text", connection);
        command.Parameters.AddWithValue("rel", relation);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : (string?)result;
    }
}
