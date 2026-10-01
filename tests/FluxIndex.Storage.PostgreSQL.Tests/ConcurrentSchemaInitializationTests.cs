using AwesomeAssertions;
using FluxIndex.Storage.PostgreSQL.KeywordSearch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Several processes or workers that start against the same database at the same moment all create the schema at
/// once. Every statement is "if not exists", but concurrent DDL on the same catalogue still deadlocks (40P01) or
/// collides on a relation another session created a moment earlier (42P07, 23505 on pg_type) — and schema creation
/// was not retried, so one of the starters failed to start. Each fact starts on a database nobody has initialized.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class ConcurrentSchemaInitializationTests : IAsyncLifetime
{
    private const int Starters = 4;
    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Keyword_indexes_initialized_at_the_same_time_on_a_fresh_database_all_start()
    {
        var ct = TestContext.Current.CancellationToken;
        for (var round = 0; round < 10; round++)
        {
            var connectionString = await FreshDatabaseAsync($"kw_{round}", ct);
            var services = Enumerable.Range(0, Starters)
                .Select(_ => new PostgresKeywordSearchService(connectionString, NullLogger<PostgresKeywordSearchService>.Instance))
                .ToList();
            try
            {
                using var start = new SemaphoreSlim(0, Starters);
                var starts = services.Select(s => Task.Run(async () =>
                {
                    await start.WaitAsync(ct);
                    await s.EnsureSchemaAsync(ct);
                }, ct)).ToList();
                start.Release(Starters);

                var act = () => Task.WhenAll(starts);
                await act.Should().NotThrowAsync($"round {round}");
            }
            finally
            {
                services.ForEach(s => s.Dispose());
            }
        }
    }

    [Fact]
    public async Task Vector_stores_initialized_at_the_same_time_on_a_database_that_does_not_exist_yet_all_start()
    {
        var ct = TestContext.Current.CancellationToken;
        for (var round = 0; round < 5; round++)
        {
            // The database itself is absent: every starter tries to create it, then the extension, then the tables.
            var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
            {
                Database = $"vs_{round}",
            }.ConnectionString;
            var providers = Enumerable.Range(0, Starters).Select(_ =>
            {
                var services = new ServiceCollection();
                services.AddPostgreSQLVectorStore(connectionString);
                return services.BuildServiceProvider();
            }).ToList();
            try
            {
                using var start = new SemaphoreSlim(0, Starters);
                var starts = providers.Select(p => Task.Run(async () =>
                {
                    await start.WaitAsync(ct);
                    new PostgreSQLStorageInitializer().InitializeSync(p);
                }, ct)).ToList();
                start.Release(Starters);

                var act = () => Task.WhenAll(starts);
                await act.Should().NotThrowAsync($"round {round}");
                (await RegClassAsync(connectionString, "public.vectors", ct)).Should().NotBeNull();
            }
            finally
            {
                foreach (var provider in providers)
                {
                    await provider.DisposeAsync();
                }
            }
        }
    }

    private async Task<string> FreshDatabaseAsync(string name, CancellationToken ct)
    {
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync(ct);
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }

    private static async Task<string?> RegClassAsync(string connectionString, string relation, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT to_regclass(@rel)::text", connection);
        command.Parameters.AddWithValue("rel", relation);
        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (string?)result;
    }
}
