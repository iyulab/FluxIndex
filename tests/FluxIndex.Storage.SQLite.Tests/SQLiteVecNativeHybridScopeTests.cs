using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// <see cref="INativeHybridSearch.HybridSearchAsync"/> with a metadata filter: the scope has to reach
/// <em>both</em> legs before fusion. The interface had no filter parameter, so a consumer that needed a
/// document-id scope could not use native fusion at all and fell back to vector-only — for a consumer
/// whose every search is scoped, that silently removed the hybrid strategy (FluxFeed docket #214).
/// Runs the real sqlite-vec store (no in-memory fallback) so both the vec and FTS5 legs execute.
/// </summary>
[Collection("SQLite Tests")]
public class SQLiteVecNativeHybridScopeTests : IAsyncLifetime
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testDatabasePath;

    public SQLiteVecNativeHybridScopeTests()
    {
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"fluxindex_nativehybrid_{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSQLiteVecVectorStore(options =>
        {
            options.UseInMemory = false;
            options.VectorDimension = 8;
            options.UseSQLiteVec = true;
            options.UseFts5 = true;
            options.AutoMigrate = true;
            options.FallbackToInMemoryOnError = false;
            options.EmbeddingFingerprint = "nativehybridscope8";
            options.DatabasePath = _testDatabasePath;
        });
        _serviceProvider = services.BuildServiceProvider();
    }

    public async ValueTask InitializeAsync()
    {
        if (CITestHelper.ShouldSkipSqliteVec())
            return;

        foreach (var service in _serviceProvider.GetServices<IHostedService>())
            await service.StartAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_testDatabasePath}"));
        try { File.Delete(_testDatabasePath); } catch (IOException) { }
    }

    private static readonly float[] Query = [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];

    /// <summary>
    /// Two documents, both matching the text query and both close to the query vector, so that an
    /// unscoped fusion ranks a chunk of each document — and a scoped one must not.
    /// </summary>
    private async Task<IVectorStore> SeedAsync(CancellationToken ct)
    {
        var store = _serviceProvider.GetRequiredService<IVectorStore>();
        var chunks = new List<DocumentChunk>
        {
            Chunk("keep", 0, [0.99f, 0.1f, 0, 0, 0, 0, 0, 0], "quarterly budget forecast for the keep document"),
            Chunk("keep", 1, [0.95f, 0.2f, 0, 0, 0, 0, 0, 0], "budget appendix of the keep document"),
            Chunk("drop", 0, [1.0f, 0.0f, 0, 0, 0, 0, 0, 0], "quarterly budget forecast for the drop document"),
            Chunk("drop", 1, [0.98f, 0.05f, 0, 0, 0, 0, 0, 0], "budget appendix of the drop document"),
        };
        await store.StoreBatchAsync(chunks, ct);
        return store;
    }

    private static DocumentChunk Chunk(string documentId, int index, float[] embedding, string content) => new()
    {
        DocumentId = documentId,
        ChunkIndex = index,
        Content = content,
        Embedding = embedding,
        TokenCount = 8,
        Metadata = new Dictionary<string, object> { ["document_id"] = documentId },
    };

    private static Dictionary<string, object> ScopeTo(params string[] documentIds) =>
        new() { ["document_id"] = new HashSet<string>(documentIds) };

    [Fact]
    public async Task Unscoped_FusesChunksOfBothDocuments()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var ct = TestContext.Current.CancellationToken;
        var store = await SeedAsync(ct);

        var results = (await ((INativeHybridSearch)store).HybridSearchAsync(Query, "budget", topK: 10, cancellationToken: ct)).ToList();

        results.Select(r => r.Chunk.DocumentId).Distinct().Should().BeEquivalentTo(["keep", "drop"]);
    }

    [Fact]
    public async Task Scoped_ReturnsOnlyInScopeChunks_FromBothLegs()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var ct = TestContext.Current.CancellationToken;
        var store = await SeedAsync(ct);

        var results = (await ((INativeHybridSearch)store).HybridSearchAsync(
            Query, "budget", topK: 10, filters: ScopeTo("keep"), cancellationToken: ct)).ToList();

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(r => r.Chunk.DocumentId == "keep",
            "the scope has to be applied before fusion, not after the out-of-scope chunks were ranked");
        // Both legs contributed to the in-scope ranking: the "drop" chunks would have outranked "keep"
        // on the vector side (cosine 1.0 vs 0.99) and tied on the FTS side, so an in-scope result with
        // a sparse rank AND a vector rank proves neither leg was answered unscoped.
        results.Should().Contain(r => r.SparseRank > 0 && r.VectorRank > 0);
    }

    [Fact]
    public async Task Scoped_ToADocumentWithNoChunks_ReturnsEmpty_NotUnscopedResults()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var ct = TestContext.Current.CancellationToken;
        var store = await SeedAsync(ct);

        var results = await ((INativeHybridSearch)store).HybridSearchAsync(
            Query, "budget", topK: 10, filters: ScopeTo("absent"), cancellationToken: ct);

        results.Should().BeEmpty("an empty scope must not be answered with unscoped results");
    }
}
