using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// Regression guard for the fingerprint-drift lost-write window described in
/// Filer ISSUE-fluxindex-20260717 (SCOPE-REVERSAL section).
///
/// Scenario: a store instance is initialized against effective fingerprint A (creating
/// chunk_embeddings_{A} and latching _initialized=true), then the effective fingerprint
/// shifts to B on the SAME instance — the state a later BindIdentity on the shared
/// singleton SQLiteVecOptions leaves behind. A subsequent write must land in
/// chunk_embeddings_{B}. If EnsureInitializedAsync short-circuits on _initialized without
/// re-checking the current effective table name, the write targets a table that was
/// never created ("no such table: chunk_embeddings_{B}").
/// </summary>
[Collection("SQLite Tests")]
public class SQLiteVecBindIdentityDriftTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _dbPath = $"drift_{Guid.NewGuid():N}.db";
    private ServiceProvider? _sp;

    public SQLiteVecBindIdentityDriftTests(ITestOutputHelper output) => _output = output;

    // Same dimension (isolate the table-name issue from any dimension-mismatch error),
    // distinct Provider/Model => distinct SHA256 fingerprint => distinct chunk_embeddings_{fp}.
    private static EmbeddingIdentity IdentityA() =>
        new() { Provider = "test-a", Model = "model-a", Dimension = 4 };

    private static EmbeddingIdentity IdentityB() =>
        new() { Provider = "test-b", Model = "model-b", Dimension = 4 };

    private static DocumentChunk Chunk(string documentId) => new()
    {
        DocumentId = documentId,
        ChunkIndex = 0,
        Content = $"drift probe {documentId}",
        Embedding = new[] { 0.1f, 0.2f, 0.3f, 0.4f },
        TokenCount = 4,
        Metadata = new Dictionary<string, object> { ["probe"] = true },
    };

    [SkippableFact]
    public async Task WriteAfterFingerprintDrift_LandsInCurrentFingerprintTable()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSQLiteVecVectorStore(o =>
        {
            o.UseInMemory = true; // keep-alive single connection — schema persists across ops in-scope
            o.UseSQLiteVec = true;
            o.VectorDimension = 4;
            o.DatabasePath = _dbPath;
            o.FallbackToInMemoryOnError = false; // fail loud — do not mask the drift
        });
        _sp = services.BuildServiceProvider();

        using var scope = _sp.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<SQLiteVecVectorStore>();

        // 0) Bind A, then run the boot init (context.InitializeAsync) the hosted
        //    SQLiteVecMigrationService performs in production: EnsureCreatedAsync creates the
        //    EF relational tables (vector_chunks) BEFORE the vec0 table, then the vec0 table for A.
        store.BindIdentity(IdentityA());
        var ctx = scope.ServiceProvider.GetRequiredService<SQLiteVecDbContext>();
        await ctx.InitializeAsync();

        // 1) Real write under fingerprint A so the store latches _initialized=true against table A.
        var idA = await store.StoreAsync(Chunk("doc-a"));
        (await store.GetAsync(idA)).Should().NotBeNull("baseline write under fingerprint A must round-trip");

        // 2) Drift the effective fingerprint to B on the same shared options instance.
        //    A second BindIdentity on the same store throws by design (EmbeddingModelMismatch),
        //    so model the shared-singleton aliasing by mutating options directly — this is the
        //    exact state a different scope's BindIdentity(B) would leave behind.
        var options = scope.ServiceProvider.GetRequiredService<IOptions<SQLiteVecOptions>>().Value;
        options.EmbeddingFingerprint = IdentityB().Fingerprint;
        options.VectorDimension = IdentityB().Dimension;

        // 3) Write under fingerprint B. Must land in chunk_embeddings_{B}, round-tripping cleanly.
        //    A throw ("no such table: chunk_embeddings_{B}") or a silent loss both fail this.
        var act = async () =>
        {
            var idB = await store.StoreAsync(Chunk("doc-b"));
            var back = await store.GetAsync(idB);
            back.Should().NotBeNull("write under the current effective fingerprint B must round-trip");
        };

        await act.Should().NotThrowAsync(
            "EnsureInitializedAsync must ensure the table for the current effective fingerprint before writing");
    }

    public void Dispose()
    {
        _sp?.Dispose();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch
        {
            /* best effort test cleanup */
        }
    }
}
