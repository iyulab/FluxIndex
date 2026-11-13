using System.Threading.Tasks;
using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK;
using FluxIndex.SDK.Services;
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
    [Fact]
    public async Task BuildProvisioning_WithEnableAutoMigrationOff_LeavesTheVectorSchemaAlone()
    {
        // The operator's opt-out. Since 0.38.0 the initializer is always registered and reads
        // PostgreSQLOptions.AutoMigrate (mapped from EnableAutoMigration) — the effect, not the
        // registration, is what the switch promises.
        var connectionString = _container.GetConnectionString();

        RunBuildProvisioning(connectionString, enableVectorAutoMigration: false);

        (await RegClassAsync(connectionString, "public.vectors")).Should().BeNull(
            "EnableAutoMigration = false must keep the vector store from provisioning");
        // The other components have their own switches and still provision.
        (await RegClassAsync(connectionString, "public.chunk_hierarchies")).Should().NotBeNull();
    }

    /// <summary>
    /// Issue: a builder-configured store on an embedding service of another size created vector(1536) tables and the first
    /// write failed with 22000. The size of every vector column the builder creates follows the bound service.
    /// </summary>
    [Theory]
    [InlineData(384)]
    [InlineData(1024)]
    public async Task BuildProvisioning_WithAnEmbeddingServiceOfAnotherSize_CreatesVectorColumnsOfThatSize(int dimensions)
    {
        var connectionString = _container.GetConnectionString();

        RunBuildProvisioning(
            connectionString,
            configure: s => s.AddSingleton<IEmbeddingService>(new InMemoryEmbeddingService(dimensions)));

        (await VectorSizeAsync(connectionString, "vectors")).Should().Be(dimensions);
        (await VectorSizeAsync(connectionString, "semantic_cache")).Should().Be(dimensions);
    }

    [Fact]
    public async Task BuildProvisioning_WithAnEmbeddingServiceOfAnotherSize_TheFirstWriteAndSearchSucceed()
    {
        var connectionString = _container.GetConnectionString();
        const int dimensions = 1024;

        using var provider = BuildProvider(
            connectionString,
            s => s.AddSingleton<IEmbeddingService>(new InMemoryEmbeddingService(dimensions)));
        foreach (var initializer in provider.GetServices<IStorageInitializer>())
            initializer.InitializeSync(provider);

        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IVectorStore>();
        var embedding = await provider.GetRequiredService<IEmbeddingService>().GenerateEmbeddingAsync("a chunk", TestContext.Current.CancellationToken);
        var chunk = new FluxIndex.Core.Domain.Entities.DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = "doc-a",
            ChunkIndex = 0,
            Content = "a chunk",
            Embedding = embedding,
        };

        var act = async () =>
        {
            await store.StoreAsync(chunk, TestContext.Current.CancellationToken);
            await store.SearchAsync(embedding, 3, 0.0f, cancellationToken: TestContext.Current.CancellationToken);
        };

        await act.Should().NotThrowAsync("the vector column is the size of the embedding service, not 1536");
    }

    private static void RunBuildProvisioning(
        string connectionString,
        bool enableVectorAutoMigration = true,
        Action<IServiceCollection>? configure = null)
    {
        using var provider = BuildProvider(connectionString, configure, enableVectorAutoMigration);

        foreach (var initializer in provider.GetServices<IStorageInitializer>())
        {
            initializer.InitializeSync(provider);
        }
    }

    private static async Task<int?> VectorSizeAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT atttypmod FROM pg_attribute WHERE attrelid = to_regclass(@t) AND attname = 'Embedding' AND NOT attisdropped",
            connection);
        command.Parameters.AddWithValue("t", table);
        return await command.ExecuteScalarAsync() as int?;
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        Action<IServiceCollection>? configure = null,
        bool enableVectorAutoMigration = true)
    {
        var options = new FluxIndexOptions();

        // Equivalent to builder.UsePostgreSQL(connectionString).
        options.VectorStore.Provider = "PostgreSQL";
        options.VectorStore.ConnectionString = connectionString;
        options.VectorStore.EnableAutoMigration = enableVectorAutoMigration;
        options.GraphStore.Provider = "PostgreSQL";
        options.GraphStore.UseVectorStoreConnection = true;
        options.SemanticCache.Provider = "PostgreSQL";
        options.SemanticCache.UseVectorStoreConnection = true;

        var services = new ServiceCollection();
        services.AddLogging();
        configure?.Invoke(services);
        FluxIndexContextBuilderExtensions.RegisterPostgreSQLServices(services, options);

        return services.BuildServiceProvider();
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
