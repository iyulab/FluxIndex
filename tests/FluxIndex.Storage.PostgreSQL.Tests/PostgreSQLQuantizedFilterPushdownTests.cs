using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Quantization;
using FluxIndex.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Integration test (requires Docker). <see cref="PostgreSQLQuantizedVectorStore"/> must apply a
/// metadata filter as part of the query, not over rows already selected by similarity.
/// <para>
/// It used to fetch a <c>topK * 3</c> window ordered by distance and filter that in memory, so a
/// scope narrow relative to the table lost matches that never entered the window — the caller
/// received fewer rows than existed, with nothing to distinguish that from "the table held nothing
/// else". The sibling <see cref="PostgreSQLVectorStore"/> never had the problem because it
/// constrains the query itself; these tests hold this store to the same behaviour.
/// </para>
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public class PostgreSQLQuantizedFilterPushdownTests : IAsyncLifetime
{
    private const int Dimension = 8;

    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync()
    {
        _container.DisposeAsync();
        GC.SuppressFinalize(this);
        return default;
    }

    /// <summary>
    /// The recall case: enough closer, filter-rejected rows to fill the old window several times
    /// over, and the one match sitting past all of them. A store that filters in the query finds
    /// it; one that filters a distance-ordered window does not.
    /// </summary>
    [Fact]
    public async Task SearchAsync_MatchOutsideTheDistanceWindow_IsStillReturned()
    {
        var store = await CreateStoreAsync();

        await SeedNoiseAsync(store, 12);
        await store.StoreAsync(Chunk("wanted", "wanted-tenant", Far()));

        var results = await store.SearchAsync(
            Near(0f), topK: 3, minScore: 0f,
            filters: new Dictionary<string, object> { ["tenant"] = "wanted-tenant" },
            TestContext.Current.CancellationToken);

        results.Should().ContainSingle()
            .Which.DocumentId.Should().Be("wanted");
    }

    /// <summary>
    /// A collection-valued filter matches ANY of its elements (the IVectorStore filter contract),
    /// and must do so through the same pushed-down predicate rather than an in-memory pass.
    /// </summary>
    [Fact]
    public async Task SearchAsync_CollectionValuedFilterOutsideTheWindow_MatchesAnyElement()
    {
        var store = await CreateStoreAsync();

        await SeedNoiseAsync(store, 12);
        await store.StoreAsync(Chunk("wanted", "wanted-tenant", Far()));

        var results = await store.SearchAsync(
            Near(0f), topK: 3, minScore: 0f,
            filters: new Dictionary<string, object>
            {
                ["tenant"] = new HashSet<string> { "absent-tenant", "wanted-tenant" }
            },
            TestContext.Current.CancellationToken);

        results.Should().ContainSingle()
            .Which.DocumentId.Should().Be("wanted");
    }

    /// <summary>
    /// The filter must not widen what is returned: rows outside the scope stay out even when they
    /// are the closest matches in the table.
    /// </summary>
    [Fact]
    public async Task SearchAsync_ScopedSearch_ReturnsNothingFromOutsideTheScope()
    {
        var store = await CreateStoreAsync();

        await SeedNoiseAsync(store, 5);

        var results = await store.SearchAsync(
            Near(0f), topK: 10, minScore: 0f,
            filters: new Dictionary<string, object> { ["tenant"] = "wanted-tenant" },
            TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }

    private static async Task SeedNoiseAsync(PostgreSQLQuantizedVectorStore store, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await store.StoreAsync(Chunk($"noise-{i}", "other-tenant", Near(i * 0.0001f)));
        }
    }

    private async Task<PostgreSQLQuantizedVectorStore> CreateStoreAsync()
    {
        var connectionString = _container.GetConnectionString();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
            await command.ExecuteNonQueryAsync();
        }

        var options = Options.Create(new PostgreSQLQuantizedOptions
        {
            ConnectionString = connectionString,
            EmbeddingDimensions = Dimension
        });

        // Mirrors the production registration: dynamic JSON for the Dictionary-typed metadata
        // column, and UseVector at the data-source level as well as the EF level.
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        var dbOptions = new DbContextOptionsBuilder<FluxIndexQuantizedDbContext>()
            .UseNpgsql(dataSource, o => o.UseVector())
            .Options;
        var context = new FluxIndexQuantizedDbContext(dbOptions, options);
        await context.Database.EnsureCreatedAsync();

        var quantizer = new ScalarQuantizer(
            Options.Create(new QuantizationOptions()),
            NullLogger<ScalarQuantizer>.Instance);

        return new PostgreSQLQuantizedVectorStore(
            context, quantizer, NullLogger<PostgreSQLQuantizedVectorStore>.Instance, options);
    }

    private static DocumentChunk Chunk(string documentId, string tenant, float[] embedding) => new()
    {
        DocumentId = documentId,
        ChunkIndex = 0,
        Content = $"content for {documentId}",
        Embedding = embedding,
        Metadata = new Dictionary<string, object> { ["tenant"] = tenant }
    };

    private static float[] Near(float jitter)
    {
        var v = new float[Dimension];
        v[0] = 1f - jitter;
        return v;
    }

    private static float[] Far()
    {
        var v = new float[Dimension];
        v[0] = 0.2f;
        v[Dimension - 1] = 1f;
        return v;
    }
}
