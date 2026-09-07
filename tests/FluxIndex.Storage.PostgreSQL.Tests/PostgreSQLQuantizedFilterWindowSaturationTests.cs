using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Quantization;
using FluxIndex.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Integration test (requires Docker). <see cref="PostgreSQLQuantizedVectorStore"/> fetches a
/// <c>topK * 3</c> candidate window from the database and only then applies the metadata filter —
/// the metadata is a jsonb column the query does not constrain. So a scope narrow relative to the
/// table loses matches that never enter the window, and the caller used to receive a short result
/// with nothing to distinguish it from "the table held nothing else".
/// <para>
/// The sibling <see cref="PostgreSQLVectorStore"/> does not share this: it constrains the query
/// itself, so its filter is a real pre-filter. Two stores in one package behaving differently is
/// exactly why this needed pinning down in a test rather than a comment.
/// </para>
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public class PostgreSQLQuantizedFilterWindowSaturationTests : IAsyncLifetime
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

    [Fact]
    public async Task SearchAsync_FilterStarvedByASaturatedWindow_Warns()
    {
        var logs = new CapturingLoggerProvider();
        var store = await CreateStoreAsync(logs);
        const int topK = 3;

        // Window is topK * 3 = 9. Store more than that many closer, filter-rejected chunks plus
        // one accepted chunk far enough away that it cannot reach the window.
        for (var i = 0; i < 12; i++)
        {
            await store.StoreAsync(Chunk($"noise-{i}", "other-tenant", Near(i * 0.0001f)));
        }
        await store.StoreAsync(Chunk("wanted", "wanted-tenant", Far()));

        var results = await store.SearchAsync(
            Near(0f), topK, minScore: 0f,
            filters: new Dictionary<string, object> { ["tenant"] = "wanted-tenant" },
            TestContext.Current.CancellationToken);

        results.Should().HaveCountLessThan(topK, "the accepted chunk sits outside the candidate window");
        logs.Warnings.Should().Contain(m => m.Contains("applied after the database candidate window"),
            "a short result caused by post-window filtering must be observable, not silent");
    }

    /// <summary>
    /// The counterpart: a filter satisfied from inside the window loses nothing, so the store must
    /// stay quiet. A warning that fires on healthy searches is noise, and noise is how a real one
    /// gets ignored.
    /// </summary>
    [Fact]
    public async Task SearchAsync_FilterSatisfiedWithinWindow_DoesNotWarn()
    {
        var logs = new CapturingLoggerProvider();
        var store = await CreateStoreAsync(logs);

        for (var i = 0; i < 3; i++)
        {
            await store.StoreAsync(Chunk($"wanted-{i}", "wanted-tenant", Near(i * 0.0001f)));
        }
        await store.StoreAsync(Chunk("noise", "other-tenant", Far()));

        var results = await store.SearchAsync(
            Near(0f), topK: 3, minScore: 0f,
            filters: new Dictionary<string, object> { ["tenant"] = "wanted-tenant" },
            TestContext.Current.CancellationToken);

        results.Should().HaveCount(3);
        logs.Warnings.Should().NotContain(m => m.Contains("applied after the database candidate window"));
    }

    private async Task<PostgreSQLQuantizedVectorStore> CreateStoreAsync(ILoggerProvider logs)
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

        using var factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Warning);
            b.AddProvider(logs);
        });

        return new PostgreSQLQuantizedVectorStore(
            context, quantizer, factory.CreateLogger<PostgreSQLQuantizedVectorStore>(), options);
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

    /// <summary>Captures warning-level messages so a test can assert on what an operator would see.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _warnings = [];
        private readonly Lock _gate = new();

        public IReadOnlyList<string> Warnings
        {
            get { lock (_gate) return [.. _warnings]; }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() { }

        private void Add(string message)
        {
            lock (_gate) _warnings.Add(message);
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning)
                    owner.Add(formatter(state, exception));
            }
        }
    }
}
