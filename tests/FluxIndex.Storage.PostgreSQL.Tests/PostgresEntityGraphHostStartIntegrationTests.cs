using AwesomeAssertions;
using FluxIndex.Storage.PostgreSQL.EntityGraph;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// A direct <c>AddPostgresEntityGraph</c> registration provisions its schema at host start when
/// <see cref="EntityGraphOptions.AutoMigrate"/> is on, and leaves the database alone when it is off. Before 0.72.0 it
/// provisioned nothing either way: the application had to call <c>EnsureEntityGraphSchemaAsync</c>. Requires Docker.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public class PostgresEntityGraphHostStartIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private async Task StartHostAsync(bool autoMigrate)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgresEntityGraph(_container.GetConnectionString(), o =>
        {
            o.EmbeddingDimension = 4;
            o.AutoMigrate = autoMigrate;
        });
        await using var provider = services.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(TestContext.Current.CancellationToken);
        }
    }

    private async Task<long> HnswIndexCountAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        await using var cmd = dataSource.CreateCommand("SELECT count(*) FROM pg_indexes WHERE indexdef LIKE '%USING hnsw%'");
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task HostStart_ProvisionsTheSchema_OnlyWhenAutoMigrateIsOn()
    {
        await StartHostAsync(autoMigrate: false);
        (await HnswIndexCountAsync()).Should().Be(0, "AutoMigrate off must leave the database untouched");

        await StartHostAsync(autoMigrate: true);
        (await HnswIndexCountAsync()).Should().Be(2, "host start provisions the entity and community tables with their vector indexes");
    }
}
