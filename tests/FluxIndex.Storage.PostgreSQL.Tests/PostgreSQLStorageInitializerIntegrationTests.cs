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
        PostgreSqlTestContainer.Create();

    public ValueTask InitializeAsync() => new ValueTask(_container.StartAsync());

    public async ValueTask DisposeAsync()
    {
        // Awaited: an unawaited DisposeAsync returns before the container is torn down, and the
        // ValueTask this method returns would claim a teardown that never ran -- leaking a
        // container per test class.
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

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

    /// <summary>
    /// A database created before 0.52.0 has <c>vectors."DocumentId"</c> as <c>varchar(50)</c>, and a longer document
    /// id failed inside the store with <c>22001</c> while indexing looked like it had run. Startup widens it in place.
    /// </summary>
    [Fact]
    public async Task InitializeSync_OnADatabaseWithTheOldBoundedDocumentId_WidensIt_AndALongIdStores()
    {
        var connectionString = _container.GetConnectionString();
        var services = new ServiceCollection();
        services.AddLogging();
        // Default dimensions: the class shares one container, and a sibling fact may already have created `vectors`
        // at the default size (startup does not recreate an existing table).
        services.AddPostgreSQLVectorStore(connectionString);
        await using var provider = services.BuildServiceProvider();
        new PostgreSQLStorageInitializer().InitializeSync(provider);

        // Reproduce the pre-0.52.0 column.
        await ExecuteAsync(connectionString, "ALTER TABLE vectors ALTER COLUMN \"DocumentId\" TYPE varchar(50)");
        (await MaxLengthAsync(connectionString, "vectors", "DocumentId")).Should().Be(50);

        new PostgreSQLStorageInitializer().InitializeSync(provider);

        (await MaxLengthAsync(connectionString, "vectors", "DocumentId")).Should().BeNull("startup widens a column the model no longer bounds");

        var longId = new string('d', 200);
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<FluxIndex.Core.Application.Interfaces.IVectorStore>();
        await store.StoreAsync(new Core.Domain.Entities.DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = longId,
            Content = "content",
            Embedding = UnitVector(1536),
        }, TestContext.Current.CancellationToken);

        (await store.GetByDocumentIdAsync(longId, TestContext.Current.CancellationToken)).Should().ContainSingle();
    }

    /// <summary>
    /// A store whose table was created for one embedding size, started with another, fails at startup naming both —
    /// it used to start cleanly and fail every write and search with <c>22000: expected N dimensions, not M</c>.
    /// </summary>
    [Fact]
    public async Task InitializeSync_WithAnotherEmbeddingSizeThanTheTable_FailsNamingBoth()
    {
        var connectionString = _container.GetConnectionString();

        var created = new ServiceCollection();
        created.AddLogging();
        created.AddPostgreSQLVectorStore(connectionString);
        await using (var provider = created.BuildServiceProvider())
            new PostgreSQLStorageInitializer().InitializeSync(provider);

        var changed = new ServiceCollection();
        changed.AddLogging();
        changed.AddPostgreSQLVectorStore(connectionString, embeddingDimensions: 384);
        await using var changedProvider = changed.BuildServiceProvider();

        var start = () => new PostgreSQLStorageInitializer().InitializeSync(changedProvider);

        start.Should().Throw<InvalidOperationException>().WithMessage("*1536*384*");
    }

    private static float[] UnitVector(int dimensions)
    {
        var vector = new float[dimensions];
        vector[0] = 1f;
        return vector;
    }

    private static async Task<int?> MaxLengthAsync(string connectionString, string table, string column)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT character_maximum_length FROM information_schema.columns WHERE table_name = @t AND column_name = @c", connection);
        command.Parameters.AddWithValue("t", table);
        command.Parameters.AddWithValue("c", column);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
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
