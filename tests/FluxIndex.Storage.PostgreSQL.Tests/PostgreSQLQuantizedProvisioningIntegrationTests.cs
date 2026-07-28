using System.Threading.Tasks;
using FluentAssertions;
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Integration test (requires Docker) for the quantized vector store, which registered no schema
/// provisioning at all — neither an initializer nor a migration — and so failed on its first write
/// even against an empty database. It is reachable only by direct registration, never from the SDK
/// builder, which is why no consumer had reported it.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public class PostgreSQLQuantizedProvisioningIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task QuantizedStore_Registration_ProvisionsBothRelations()
    {
        var connectionString = _container.GetConnectionString();

        RunProvisioning(connectionString);

        (await RegClassAsync(connectionString, "public.vectors")).Should().NotBeNull();
        (await RegClassAsync(connectionString, "public.quantized_vectors")).Should().NotBeNull();
    }

    [Fact]
    public async Task QuantizedStore_OnDatabaseSharedWithApplicationTables_ProvisionsBothRelations()
    {
        var connectionString = _container.GetConnectionString();
        await ExecuteAsync(connectionString, "CREATE TABLE any_app_table (id int)");

        RunProvisioning(connectionString);

        (await RegClassAsync(connectionString, "public.quantized_vectors")).Should().NotBeNull();
    }

    private static void RunProvisioning(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSQLQuantizedVectorStore(connectionString);

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
