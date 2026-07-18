using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Integration tests for PostgreSQLVectorStore using Testcontainers (pgvector image).
/// These tests require Docker to be running.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public class PostgreSQLVectorStoreIntegrationTests : IAsyncLifetime
{
    private const int Dimensions = 4;

    private readonly PostgreSqlContainer _container;
    private NpgsqlDataSource _dataSource = null!;
    private FluxIndexDbContext _context = null!;
    private PostgreSQLVectorStore _store = null!;

    public PostgreSQLVectorStoreIntegrationTests()
    {
        _container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = Options.Create(new PostgreSQLOptions
        {
            ConnectionString = _container.GetConnectionString(),
            EmbeddingDimensions = Dimensions
        });

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(options.Value.ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        dataSourceBuilder.UseVector();
        _dataSource = dataSourceBuilder.Build();

        var contextOptions = new DbContextOptionsBuilder<FluxIndexDbContext>()
            .UseNpgsql(_dataSource, npgsql => npgsql.UseVector())
            .Options;

        _context = new FluxIndexDbContext(contextOptions, options);
        await _context.Database.EnsureCreatedAsync();

        _store = new PostgreSQLVectorStore(_context, NullLogger<PostgreSQLVectorStore>.Instance, options);
    }

    public async Task DisposeAsync()
    {
        if (_context is not null)
            await _context.DisposeAsync();
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static Core.Domain.Entities.DocumentChunk CreateChunk(
        string documentId, float[] embedding, string workspaceId, int chunkIndex = 0)
    {
        return new Core.Domain.Entities.DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = documentId,
            ChunkIndex = chunkIndex,
            Content = $"content of {documentId}#{chunkIndex}",
            Embedding = embedding,
            Metadata = new Dictionary<string, object> { ["workspace_id"] = workspaceId }
        };
    }

    [Fact]
    public async Task SearchAsync_MetadataFilter_IsPushedDownBeforeCandidateTrim()
    {
        // Arrange — 10 near-identical chunks from ANOTHER tenant dominate vector similarity.
        // With topK=1 the candidate window (topK*3=3) holds only other-tenant chunks unless the
        // filter is applied at SQL level, so this asserts real pushdown, not post-filtering.
        var query = new float[] { 1f, 0f, 0f, 0f };

        for (var i = 0; i < 10; i++)
            await _store.StoreAsync(CreateChunk($"other-{i}", [1f, 0.01f * i, 0f, 0f], "ws-other", i));

        await _store.StoreAsync(CreateChunk("target", [0f, 1f, 0f, 0f], "ws-target"));

        var filters = new Dictionary<string, object> { ["workspace_id"] = "ws-target" };

        // Act
        var results = (await _store.SearchAsync(query, topK: 1, minScore: -1f, filters: filters)).ToList();

        // Assert
        results.Should().ContainSingle();
        results[0].DocumentId.Should().Be("target");
    }

    [Fact]
    public async Task SearchAsync_MetadataFilter_NoMatch_ReturnsEmpty()
    {
        await _store.StoreAsync(CreateChunk("doc-1", [1f, 0f, 0f, 0f], "ws-1"));

        var results = await _store.SearchAsync(
            [1f, 0f, 0f, 0f], topK: 5, minScore: -1f,
            filters: new Dictionary<string, object> { ["workspace_id"] = "ws-absent" });

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_NoFilter_StillReturnsResults()
    {
        await _store.StoreAsync(CreateChunk("doc-1", [1f, 0f, 0f, 0f], "ws-1"));
        await _store.StoreAsync(CreateChunk("doc-2", [0f, 1f, 0f, 0f], "ws-2"));

        var results = (await _store.SearchAsync([1f, 0f, 0f, 0f], topK: 2, minScore: -1f)).ToList();

        results.Should().HaveCount(2);
        results[0].DocumentId.Should().Be("doc-1");
    }

    [Fact]
    public async Task DeleteByFilterAsync_RemovesOnlyMatchingTenant()
    {
        // Arrange — 2 tenants share the collection; deleting one must not touch the other.
        await _store.StoreAsync(CreateChunk("other-1", [1f, 0f, 0f, 0f], "ws-other", 0));
        await _store.StoreAsync(CreateChunk("other-2", [0f, 1f, 0f, 0f], "ws-other", 1));
        await _store.StoreAsync(CreateChunk("target", [0f, 0f, 1f, 0f], "ws-target"));

        // Act
        var deleted = await _store.DeleteByFilterAsync(
            new Dictionary<string, object> { ["workspace_id"] = "ws-other" });

        // Assert
        deleted.Should().Be(2);
        (await _store.CountAsync()).Should().Be(1);

        var remaining = (await _store.SearchAsync([0f, 0f, 1f, 0f], topK: 5, minScore: -1f)).ToList();
        remaining.Should().ContainSingle();
        remaining[0].DocumentId.Should().Be("target");
    }

    [Fact]
    public async Task DeleteByFilterAsync_NoMatch_ReturnsZero()
    {
        await _store.StoreAsync(CreateChunk("doc-1", [1f, 0f, 0f, 0f], "ws-1"));

        var deleted = await _store.DeleteByFilterAsync(
            new Dictionary<string, object> { ["workspace_id"] = "ws-absent" });

        deleted.Should().Be(0);
        (await _store.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeleteByFilterAsync_EmptyFilter_Throws()
    {
        var act = () => _store.DeleteByFilterAsync(new Dictionary<string, object>());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SearchAsync_BoolMetadataFilter_SurvivesJsonbRoundTripAndBackstop()
    {
        // Regression guard: jsonb containment matches typed booleans while the in-memory backstop
        // compares normalized JSON text — both must agree or pushdown matches get dropped.
        var chunk = CreateChunk("doc-flag", [1f, 0f, 0f, 0f], "ws-1");
        chunk.Metadata!["published"] = true;
        await _store.StoreAsync(chunk);

        var results = (await _store.SearchAsync(
            [1f, 0f, 0f, 0f], topK: 5, minScore: -1f,
            filters: new Dictionary<string, object> { ["published"] = true })).ToList();

        results.Should().ContainSingle();
        results[0].DocumentId.Should().Be("doc-flag");
    }
}
