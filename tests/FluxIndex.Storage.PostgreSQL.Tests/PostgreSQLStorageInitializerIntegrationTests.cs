using FluentAssertions;
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
