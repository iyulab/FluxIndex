using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Integration test (requires Docker) proving that the PostgreSQL storage initializer creates the
/// pgvector schema on a fresh database — the same code path Build() runs — without the consumer
/// calling EnsureCreated manually. This is the acceptance teeth for the auto-init fix; it cannot be
/// exercised without a live PostgreSQL, so it is Category=Integration and excluded from CI.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public class PostgreSQLStorageInitializerIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// Regression for the shared-database no-op: a consumer that points FluxIndex at a database that
    /// already holds its own application tables must still get the vector schema. EnsureCreated()
    /// short-circuits on ANY pre-existing relation, so this configuration used to leave the schema
    /// uncreated and die with 42P01 on the first index write, while Build() reported success.
    /// Reported by All.Manual (2026-07-21).
    /// </summary>
    [Fact]
    public async Task InitializeSync_OnDatabaseSharedWithApplicationTables_CreatesVectorSchema()
    {
        var connectionString = _container.GetConnectionString();

        // The consumer's own application table already lives here — this is the whole difference.
        await ExecuteAsync(connectionString, "CREATE TABLE any_app_table (id int)");
        (await RegClassAsync(connectionString, "public.vectors")).Should().BeNull();

        var services = new ServiceCollection();
        services.AddPostgreSQLVectorStore(connectionString);
        await using var provider = services.BuildServiceProvider();

        new PostgreSQLStorageInitializer().InitializeSync(provider);

        (await RegClassAsync(connectionString, "public.vectors")).Should().NotBeNull();
        (await RegClassAsync(connectionString, "public.any_app_table")).Should().NotBeNull();
    }

    /// <summary>
    /// Running the initializer twice must be a no-op, not a duplicate-relation failure.
    /// </summary>
    [Fact]
    public async Task InitializeSync_WhenSchemaAlreadyPresent_IsIdempotent()
    {
        var connectionString = _container.GetConnectionString();

        var services = new ServiceCollection();
        services.AddPostgreSQLVectorStore(connectionString);
        await using var provider = services.BuildServiceProvider();

        new PostgreSQLStorageInitializer().InitializeSync(provider);
        var act = () => new PostgreSQLStorageInitializer().InitializeSync(provider);

        act.Should().NotThrow();
        (await RegClassAsync(connectionString, "public.vectors")).Should().NotBeNull();
    }

    [Fact]
    public async Task InitializeSync_OnFreshDatabase_CreatesVectorSchema_WithoutManualEnsureCreated()
    {
        var connectionString = _container.GetConnectionString();

        // Fresh database — the vectors table does not exist yet.
        (await RegClassAsync(connectionString, "public.vectors")).Should().BeNull();

        var services = new ServiceCollection();
        services.AddPostgreSQLVectorStore(connectionString);
        await using var provider = services.BuildServiceProvider();

        // Exactly what FluxIndexContextBuilder.Build() does with a registered IStorageInitializer.
        new PostgreSQLStorageInitializer().InitializeSync(provider);

        // Schema now exists — the consumer never called EnsureCreated.
        (await RegClassAsync(connectionString, "public.vectors")).Should().NotBeNull();
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
