using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Quantization;
using FluxIndex.Core.Application.Utilities;
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
/// Integration test (requires Docker). Deleting a chunk from <see cref="PostgreSQLQuantizedVectorStore"/> must remove its
/// quantized embedding too, whatever shape the chunk id has.
/// <para>
/// The vector row is keyed on the storage GUID derived from the chunk id, but the quantized row names its chunk by the id
/// the caller supplied. A delete that looks quantized rows up by the row key finds them only when the two spellings
/// coincide (a lower-case UUID id), so a non-UUID chunk id left its quantized row behind: still counted as quantized,
/// still occupying a candidate slot in every quantized search.
/// </para>
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgreSQLQuantizedDeleteTests : IAsyncLifetime
{
    private const int Dimension = 8;

    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();
    private DbContextOptions<FluxIndexQuantizedDbContext> _dbOptions = null!;
    private IOptions<PostgreSQLQuantizedOptions> _options = null!;
    private ScalarQuantizer _quantizer = null!;
    private PostgreSQLQuantizedVectorStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var connectionString = _container.GetConnectionString();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
            await command.ExecuteNonQueryAsync();
        }

        _options = Options.Create(new PostgreSQLQuantizedOptions
        {
            ConnectionString = connectionString,
            EmbeddingDimensions = Dimension
        });
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();
        _dbOptions = new DbContextOptionsBuilder<FluxIndexQuantizedDbContext>()
            .UseNpgsql(dataSource, o => o.UseVector())
            .Options;
        await using (var context = NewContext())
        {
            await context.Database.EnsureCreatedAsync();
        }

        _quantizer = new ScalarQuantizer(Options.Create(new QuantizationOptions { Dimension = Dimension }), NullLogger<ScalarQuantizer>.Instance);
        _store = new PostgreSQLQuantizedVectorStore(
            new TestDbContextFactory<FluxIndexQuantizedDbContext>(NewContext),
            _quantizer,
            NullLogger<PostgreSQLQuantizedVectorStore>.Instance,
            _options);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task DeleteByDocumentIdAsync_NonUuidChunkIds_RemovesTheQuantizedEmbeddings()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.StoreBatchAsync([Chunk("doc-a", "doc-a#0", 0), Chunk("doc-a", "doc-a#1", 1)], ct);
        (await _store.GetQuantizedStatsAsync(ct)).QuantizedChunkCount.Should().Be(2, "both chunks were auto-quantized on store");
        (await SearchQuantizedIdsAsync(ct)).Should().BeEquivalentTo(["doc-a#0", "doc-a#1"]);

        (await _store.DeleteByDocumentIdAsync("doc-a", ct)).Should().BeTrue();

        (await _store.GetQuantizedStatsAsync(ct)).QuantizedChunkCount.Should().Be(0);
        (await _store.HasQuantizedEmbeddingAsync("doc-a#0", ct)).Should().BeFalse();
        (await _store.HasQuantizedEmbeddingAsync("doc-a#1", ct)).Should().BeFalse();
        (await SearchQuantizedIdsAsync(ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_NonUuidChunkId_RemovesTheQuantizedEmbedding()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.StoreAsync(Chunk("doc-a", "doc-a#0", 0), ct);
        (await _store.HasQuantizedEmbeddingAsync("doc-a#0", ct)).Should().BeTrue();

        (await _store.DeleteAsync("doc-a#0", ct)).Should().BeTrue();

        (await _store.GetQuantizedStatsAsync(ct)).QuantizedChunkCount.Should().Be(0);
        (await SearchQuantizedIdsAsync(ct)).Should().BeEmpty();
    }

    /// <summary>
    /// A UUID chunk id in upper case is stored verbatim on the quantized row but lower-cased in the row key, so the two
    /// spellings differ even for a UUID.
    /// </summary>
    [Fact]
    public async Task DeleteByDocumentIdAsync_UpperCaseUuidChunkId_RemovesTheQuantizedEmbedding()
    {
        var ct = TestContext.Current.CancellationToken;
        var chunkId = Guid.NewGuid().ToString().ToUpperInvariant();
        await _store.StoreAsync(Chunk("doc-a", chunkId, 0), ct);

        (await _store.DeleteByDocumentIdAsync("doc-a", ct)).Should().BeTrue();

        (await _store.GetQuantizedStatsAsync(ct)).QuantizedChunkCount.Should().Be(0);
    }

    /// <summary>
    /// Earlier versions named a quantized row by the vector row key rather than the caller's chunk id. Such rows exist in
    /// databases written by those versions and must be removed along with their chunk.
    /// </summary>
    [Fact]
    public async Task Delete_QuantizedRowNamedByTheRowKey_IsRemovedToo()
    {
        var ct = TestContext.Current.CancellationToken;
        var chunk = Chunk("doc-a", "doc-a#0", 0);
        var other = Chunk("doc-b", "doc-b#0", 1);
        await _store.StoreBatchAsync([chunk, other], ct);
        await RenameQuantizedRowToRowKeyAsync("doc-a#0", ct);
        await RenameQuantizedRowToRowKeyAsync("doc-b#0", ct);
        (await _store.HasQuantizedEmbeddingAsync("doc-a#0", ct)).Should().BeTrue("a row named by the row key is still that chunk's embedding");

        (await _store.DeleteByDocumentIdAsync("doc-a", ct)).Should().BeTrue();
        (await _store.DeleteAsync("doc-b#0", ct)).Should().BeTrue();

        (await _store.GetQuantizedStatsAsync(ct)).QuantizedChunkCount.Should().Be(0);
    }

    /// <summary>A quantized search resolves a non-UUID chunk id back to its chunk.</summary>
    [Fact]
    public async Task SearchQuantizedAsync_NonUuidChunkId_ReturnsTheChunk()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.StoreAsync(Chunk("doc-a", "doc-a#0", 0), ct);

        (await SearchQuantizedIdsAsync(ct)).Should().Equal("doc-a#0");
    }

    /// <summary>
    /// A chunk re-stored by an earlier version can hold one quantized row under each spelling; a quantized search returns
    /// it once, and deleting it removes both.
    /// </summary>
    [Fact]
    public async Task ChunkWithAQuantizedRowUnderEachSpelling_IsReturnedOnce_AndDeletedCompletely()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.StoreAsync(Chunk("doc-a", "doc-a#0", 0), ct);
        await using (var context = NewContext())
        {
            var row = await context.QuantizedVectors.SingleAsync(q => q.ChunkId == "doc-a#0", ct);
            context.QuantizedVectors.Add(new PostgresQuantizedEmbeddingEntity
            {
                Id = Guid.NewGuid(),
                ChunkId = ChunkStorageId.ToStorageGuid("doc-a#0").ToString(),
                QuantizedData = row.QuantizedData,
                QuantizationType = row.QuantizationType,
                OriginalDimension = row.OriginalDimension,
                MetadataJson = row.MetadataJson,
                CreatedAt = row.CreatedAt
            });
            await context.SaveChangesAsync(ct);
        }
        (await _store.GetQuantizedStatsAsync(ct)).QuantizedChunkCount.Should().Be(2);

        (await SearchQuantizedIdsAsync(ct)).Should().Equal("doc-a#0");

        (await _store.DeleteAsync("doc-a#0", ct)).Should().BeTrue();
        (await _store.GetQuantizedStatsAsync(ct)).QuantizedChunkCount.Should().Be(0);
    }

    private async Task<List<string>> SearchQuantizedIdsAsync(CancellationToken ct)
    {
        var query = await _quantizer.QuantizeAsync(Embedding(0), ct);
        var results = await _store.SearchQuantizedAsync(query, topK: 10, minScore: 0f, ct);
        return results.Select(r => r.Chunk.Id!).ToList();
    }

    private async Task RenameQuantizedRowToRowKeyAsync(string chunkId, CancellationToken ct)
    {
        await using var context = NewContext();
        var row = await context.QuantizedVectors.AsTracking().SingleAsync(q => q.ChunkId == chunkId, ct);
        row.ChunkId = ChunkStorageId.ToStorageGuid(chunkId).ToString();
        await context.SaveChangesAsync(ct);
    }

    private FluxIndexQuantizedDbContext NewContext() => new(_dbOptions, _options);

    private static DocumentChunk Chunk(string documentId, string chunkId, int index) => new()
    {
        Id = chunkId,
        DocumentId = documentId,
        ChunkIndex = index,
        Content = $"content {chunkId}",
        Embedding = Embedding(index)
    };

    private static float[] Embedding(int index)
    {
        var v = new float[Dimension];
        v[0] = 1f;
        v[1 + (index % (Dimension - 1))] = 0.5f;
        return v;
    }
}
