using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Storage.SQLite;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// IVectorStoreManager 구현 테스트 (SQLiteVecVectorStore)
/// </summary>
[Collection("SQLite Tests")]
public class VectorStoreManagerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SQLiteVecDbContext _context;
    private readonly SQLiteVecVectorStore _store;
    private readonly SQLiteVecOptions _options;
    private readonly ISQLiteVecExtensionLoader _extensionLoader;

    public VectorStoreManagerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<SQLiteVecDbContext>()
            .UseSqlite(_connection)
            .Options;

        _options = new SQLiteVecOptions { UseSQLiteVec = true };
        var optionsWrapper = Options.Create(_options);

        _extensionLoader = new SQLiteVecExtensionLoader(
            NullLogger<SQLiteVecExtensionLoader>.Instance,
            optionsWrapper);

        _context = new SQLiteVecDbContext(
            dbOptions,
            optionsWrapper,
            _extensionLoader,
            NullLogger<SQLiteVecDbContext>.Instance);

        var fallbackStore = new Lazy<SQLiteVectorStore>(() =>
            throw new InvalidOperationException("Fallback should not be used"));

        _store = new SQLiteVecVectorStore(
            _context,
            NullLogger<SQLiteVecVectorStore>.Instance,
            optionsWrapper,
            _extensionLoader,
            fallbackStore);
    }

    // Helper to create a vec0 table for a given identity
    private async Task CreateCollectionForIdentity(EmbeddingIdentity identity)
    {
        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync();

        await _extensionLoader.LoadExtensionAsync(_connection, CancellationToken.None);

        var tableName = $"chunk_embeddings_{identity.Fingerprint}";
        await _extensionLoader.CreateVecTableAsync(
            _connection, tableName, identity.Dimension, "distance_metric=cosine", CancellationToken.None);
    }

    [Fact]
    public async Task ListCollectionsAsync_NoCollections_ReturnsEmpty()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Act
        var result = await ((IVectorStoreManager)_store).ListCollectionsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListCollectionsAsync_MultipleCollections_ReturnsAll()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var identityA = new EmbeddingIdentity { Provider = "OpenAI", Model = "text-embedding-3-small", Dimension = 1536 };
        var identityB = new EmbeddingIdentity { Provider = "LMSupply", Model = "all-MiniLM-L6-v2", Dimension = 384 };

        await CreateCollectionForIdentity(identityA);
        await CreateCollectionForIdentity(identityB);

        // Act
        var result = await ((IVectorStoreManager)_store).ListCollectionsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Should().HaveCount(2);

        var nameA = $"chunk_embeddings_{identityA.Fingerprint}";
        var nameB = $"chunk_embeddings_{identityB.Fingerprint}";

        result.Should().Contain(c => c.Name == nameA && c.Dimension == 1536);
        result.Should().Contain(c => c.Name == nameB && c.Dimension == 384);

        // StorageSizeBytes is always null for SQLite per-table
        result.Should().AllSatisfy(c => c.StorageSizeBytes.Should().BeNull());
    }

    [Fact]
    public async Task GetCollectionInfoAsync_ExistingCollection_ReturnsInfo()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var identity = new EmbeddingIdentity { Provider = "OpenAI", Model = "text-embedding-3-small", Dimension = 1536 };
        await CreateCollectionForIdentity(identity);
        var tableName = $"chunk_embeddings_{identity.Fingerprint}";

        // Act
        var result = await ((IVectorStoreManager)_store).GetCollectionInfoAsync(tableName, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be(tableName);
        result.Dimension.Should().Be(1536);
        result.EntryCount.Should().Be(0);
        result.StorageSizeBytes.Should().BeNull();
    }

    [Fact]
    public async Task GetCollectionInfoAsync_NonExistent_ReturnsNull()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange — use a valid name format but for a collection that doesn't exist
        var tableName = "chunk_embeddings_nonexist";

        // Act
        var result = await ((IVectorStoreManager)_store).GetCollectionInfoAsync(tableName, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteCollectionAsync_ExistingCollection_ReturnsTrueAndRemoves()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var identity = new EmbeddingIdentity { Provider = "LMSupply", Model = "all-MiniLM-L6-v2", Dimension = 384 };
        await CreateCollectionForIdentity(identity);
        var tableName = $"chunk_embeddings_{identity.Fingerprint}";

        // Verify it exists first
        var before = await ((IVectorStoreManager)_store).GetCollectionInfoAsync(tableName, TestContext.Current.CancellationToken);
        before.Should().NotBeNull();

        // Act
        var deleted = await ((IVectorStoreManager)_store).DeleteCollectionAsync(tableName, TestContext.Current.CancellationToken);

        // Assert
        deleted.Should().BeTrue();

        var after = await ((IVectorStoreManager)_store).GetCollectionInfoAsync(tableName, TestContext.Current.CancellationToken);
        after.Should().BeNull();
    }

    [Fact]
    public async Task DeleteCollectionAsync_NonExistent_ReturnsFalse()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange — valid name format but not present
        var tableName = "chunk_embeddings_nothere";

        // Act
        var result = await ((IVectorStoreManager)_store).DeleteCollectionAsync(tableName, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ListCollectionsAsync_LegacyDimensionTable_IncludedAsOrphan()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange: create a legacy dimension-only table directly (no fingerprint)
        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync(TestContext.Current.CancellationToken);

        await _extensionLoader.LoadExtensionAsync(_connection, CancellationToken.None);
        await _extensionLoader.CreateVecTableAsync(
            _connection, "chunk_embeddings_1536", 1536, "distance_metric=cosine", CancellationToken.None);

        // Act
        IVectorStoreManager manager = _store;
        var collections = await manager.ListCollectionsAsync(TestContext.Current.CancellationToken);

        // Assert: legacy table appears in listing
        collections.Should().HaveCount(1);
        collections[0].Name.Should().Be("chunk_embeddings_1536");
        collections[0].Dimension.Should().Be(1536);
    }

    /// <summary>
    /// Regression: DeleteVectorFromVecTableAsync used the
    /// ExecuteSqlRawAsync(string, params object[]) overload, so the trailing
    /// CancellationToken was passed as a SQL parameter and EF threw
    /// InvalidOperationException ("no store type mapping for CancellationToken").
    /// The catch block swallowed the error, so every unmemorize leaked a vec0 row.
    /// </summary>
    [Fact]
    public async Task DeleteVectorFromVecTableAsync_WithNonDefaultCancellationToken_DeletesRow()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var identity = new EmbeddingIdentity { Provider = "Test", Model = "regression-ct", Dimension = 4 };
        _options.EmbeddingFingerprint = identity.Fingerprint;
        _options.VectorDimension = identity.Dimension;
        await CreateCollectionForIdentity(identity);

        const string chunkId = "regression-delete-ct";
        var embedding = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
        await _context.StoreVectorInVecTableAsync(chunkId, embedding, CancellationToken.None);

        var tableName = $"chunk_embeddings_{identity.Fingerprint}";
        (await CountVecRowsAsync(tableName, chunkId)).Should().Be(1,
            "the seeded row must exist before delete");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _context.DeleteVectorFromVecTableAsync(chunkId, cts.Token);

        (await CountVecRowsAsync(tableName, chunkId)).Should().Be(0,
            "the vec0 row must be deleted even when a non-default CancellationToken is passed");
    }

    private async Task<int> CountVecRowsAsync(string tableName, string chunkId)
    {
        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM {tableName} WHERE chunk_id = $cid";
        cmd.Parameters.AddWithValue("$cid", chunkId);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task EnumerateVecTableNamesAsync_NoTables_ReturnsEmpty()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var names = await _context.EnumerateVecTableNamesAsync(TestContext.Current.CancellationToken);

        names.Should().BeEmpty();
    }

    [Fact]
    public async Task EnumerateVecTableNamesAsync_SingleFingerprint_ReturnsOneTable()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var identity = new EmbeddingIdentity { Provider = "Test", Model = "single", Dimension = 4 };
        await CreateCollectionForIdentity(identity);

        var names = await _context.EnumerateVecTableNamesAsync(TestContext.Current.CancellationToken);

        names.Should().ContainSingle().Which.Should().Be($"chunk_embeddings_{identity.Fingerprint}");
    }

    [Fact]
    public async Task EnumerateVecTableNamesAsync_TwoFingerprints_ReturnsBoth()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var current = new EmbeddingIdentity { Provider = "Test", Model = "current", Dimension = 4 };
        var legacy = new EmbeddingIdentity { Provider = "Test", Model = "legacy", Dimension = 4 };
        await CreateCollectionForIdentity(current);
        await CreateCollectionForIdentity(legacy);

        var names = await _context.EnumerateVecTableNamesAsync(TestContext.Current.CancellationToken);

        names.Should().BeEquivalentTo(new[]
        {
            $"chunk_embeddings_{current.Fingerprint}",
            $"chunk_embeddings_{legacy.Fingerprint}",
        });
    }

    [Fact]
    public async Task EnumerateVecTableNamesAsync_IgnoresShadowTables()
    {
        // sqlite-vec creates shadow tables (chunk_embeddings_<fp>_rowids, _chunks).
        // The enumerator must return only the vec0 virtual table itself, not its shadows.
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var identity = new EmbeddingIdentity { Provider = "Test", Model = "shadow-check", Dimension = 4 };
        await CreateCollectionForIdentity(identity);

        var names = await _context.EnumerateVecTableNamesAsync(TestContext.Current.CancellationToken);

        names.Should().ContainSingle()
            .Which.Should().NotContain("_rowids").And.NotContain("_chunks");
    }

    [Fact]
    public async Task DeleteVectorFromVecTableAsync_LegacyFingerprintTable_RemovesRow()
    {
        // Issue: upstream-fluxindex-legacy-fingerprint-vec0-orphan
        // Repro: chunk lives in legacy fingerprint vec0 table, current fingerprint
        // is set to a different value, delete must still remove the legacy row.
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var legacy = new EmbeddingIdentity { Provider = "Test", Model = "legacy", Dimension = 4 };
        var current = new EmbeddingIdentity { Provider = "Test", Model = "current", Dimension = 4 };
        await CreateCollectionForIdentity(legacy);
        await CreateCollectionForIdentity(current);

        // Configure the context to point at the *current* fingerprint (mimics a
        // post-migration deployment).
        _options.EmbeddingFingerprint = current.Fingerprint;
        _options.VectorDimension = current.Dimension;

        const string chunkId = "legacy-chunk-001";
        var embedding = new[] { 0.1f, 0.2f, 0.3f, 0.4f };

        // Seed the row in the legacy table only — temporarily switch fingerprint so
        // StoreVectorInVecTableAsync writes to the legacy table, then switch back.
        _options.EmbeddingFingerprint = legacy.Fingerprint;
        await _context.StoreVectorInVecTableAsync(chunkId, embedding, CancellationToken.None);
        _options.EmbeddingFingerprint = current.Fingerprint;

        var legacyTable = $"chunk_embeddings_{legacy.Fingerprint}";
        var currentTable = $"chunk_embeddings_{current.Fingerprint}";
        (await CountVecRowsAsync(legacyTable, chunkId)).Should().Be(1, "row was seeded in legacy table");
        (await CountVecRowsAsync(currentTable, chunkId)).Should().Be(0, "current table is empty");

        await _context.DeleteVectorFromVecTableAsync(chunkId, CancellationToken.None);

        (await CountVecRowsAsync(legacyTable, chunkId)).Should().Be(0,
            "delete must remove the row from the legacy fingerprint table even though the context points at the current fingerprint");
    }

    [Fact]
    public async Task DeleteVectorFromVecTableAsync_RowsInBothFingerprintTables_RemovesBoth()
    {
        // Pathological case: same chunk_id present in two fingerprint tables (e.g. mid-migration).
        // Delete must clean up both atomically.
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var a = new EmbeddingIdentity { Provider = "Test", Model = "a", Dimension = 4 };
        var b = new EmbeddingIdentity { Provider = "Test", Model = "b", Dimension = 4 };
        await CreateCollectionForIdentity(a);
        await CreateCollectionForIdentity(b);

        const string chunkId = "shared-chunk";
        var embedding = new[] { 0.5f, 0.5f, 0.5f, 0.5f };

        _options.EmbeddingFingerprint = a.Fingerprint;
        await _context.StoreVectorInVecTableAsync(chunkId, embedding, CancellationToken.None);
        _options.EmbeddingFingerprint = b.Fingerprint;
        await _context.StoreVectorInVecTableAsync(chunkId, embedding, CancellationToken.None);

        var tableA = $"chunk_embeddings_{a.Fingerprint}";
        var tableB = $"chunk_embeddings_{b.Fingerprint}";
        (await CountVecRowsAsync(tableA, chunkId)).Should().Be(1);
        (await CountVecRowsAsync(tableB, chunkId)).Should().Be(1);

        await _context.DeleteVectorFromVecTableAsync(chunkId, CancellationToken.None);

        (await CountVecRowsAsync(tableA, chunkId)).Should().Be(0);
        (await CountVecRowsAsync(tableB, chunkId)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteVectorFromVecTableAsync_InsideOuterTransaction_DoesNotThrowNestedTransactionError()
    {
        // Regression for the 0.13.7 multi-fingerprint refactor: SQLiteVecVectorStore.DeleteAsync
        // wraps the call to DeleteVectorFromVecTableAsync inside its own transaction. An earlier
        // 0.13.7 draft started an inner BeginTransactionAsync, which throws "connection is already
        // in a transaction" on SQLite. This test exercises that exact call shape.
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var identity = new EmbeddingIdentity { Provider = "Test", Model = "nested-tx", Dimension = 4 };
        _options.EmbeddingFingerprint = identity.Fingerprint;
        _options.VectorDimension = identity.Dimension;
        await CreateCollectionForIdentity(identity);

        const string chunkId = "nested-tx-chunk";
        await _context.StoreVectorInVecTableAsync(chunkId, new[] { 0.1f, 0.2f, 0.3f, 0.4f }, CancellationToken.None);

        await using var tx = await _context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await _context.DeleteVectorFromVecTableAsync(chunkId, CancellationToken.None);
        await tx.CommitAsync(TestContext.Current.CancellationToken);

        (await CountVecRowsAsync($"chunk_embeddings_{identity.Fingerprint}", chunkId)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteVectorFromVecTableAsync_OnlyCurrentFingerprintExists_BehavesUnchanged()
    {
        // Single-fingerprint deployment (the common case): no behavior or perf regression expected.
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var identity = new EmbeddingIdentity { Provider = "Test", Model = "solo", Dimension = 4 };
        _options.EmbeddingFingerprint = identity.Fingerprint;
        _options.VectorDimension = identity.Dimension;
        await CreateCollectionForIdentity(identity);

        const string chunkId = "solo-chunk";
        await _context.StoreVectorInVecTableAsync(chunkId, new[] { 0.1f, 0.2f, 0.3f, 0.4f }, CancellationToken.None);

        var table = $"chunk_embeddings_{identity.Fingerprint}";
        (await CountVecRowsAsync(table, chunkId)).Should().Be(1);

        await _context.DeleteVectorFromVecTableAsync(chunkId, CancellationToken.None);

        (await CountVecRowsAsync(table, chunkId)).Should().Be(0);
    }

    // ---------------------------------------------------------------------
    // Startup orphan sweep (Issue: upstream-fluxindex-legacy-fingerprint-vec0-orphan)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task SweepOrphanVectorsAsync_NoVecTables_RecordsMarkerAndReturnsZero()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // No vec table created — sweep should record marker and report zero work.
        var removed = await _context.SweepOrphanVectorsAsync(CancellationToken.None);

        removed.Should().Be(0);
        (await CountMigrationMarkerAsync("orphan-sweep-v1")).Should().Be(1);
    }

    [Fact]
    public async Task SweepOrphanVectorsAsync_OrphanedRows_AreRemoved()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        await _context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var identity = new EmbeddingIdentity { Provider = "Test", Model = "sweep-orphan", Dimension = 4 };
        _options.EmbeddingFingerprint = identity.Fingerprint;
        _options.VectorDimension = identity.Dimension;
        await CreateCollectionForIdentity(identity);

        // Seed two rows in vec0 directly. Neither has a matching vector_chunks row
        // — both should be classified as orphans and removed.
        await _context.StoreVectorInVecTableAsync("orphan-1", new[] { 0.1f, 0.2f, 0.3f, 0.4f }, CancellationToken.None);
        await _context.StoreVectorInVecTableAsync("orphan-2", new[] { 0.5f, 0.5f, 0.5f, 0.5f }, CancellationToken.None);

        var table = $"chunk_embeddings_{identity.Fingerprint}";
        (await CountVecRowsAsync(table, "orphan-1")).Should().Be(1);

        var removed = await _context.SweepOrphanVectorsAsync(CancellationToken.None);

        removed.Should().Be(2);
        (await CountVecRowsAsync(table, "orphan-1")).Should().Be(0);
        (await CountVecRowsAsync(table, "orphan-2")).Should().Be(0);
        (await CountMigrationMarkerAsync("orphan-sweep-v1")).Should().Be(1);
    }

    [Fact]
    public async Task SweepOrphanVectorsAsync_LiveRows_AreNotRemoved()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        await _context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var identity = new EmbeddingIdentity { Provider = "Test", Model = "sweep-live", Dimension = 4 };
        _options.EmbeddingFingerprint = identity.Fingerprint;
        _options.VectorDimension = identity.Dimension;
        await CreateCollectionForIdentity(identity);

        // Insert a vector_chunks row AND its vec0 row — the chunk_ids match, so this is NOT an orphan.
        const string liveId = "live-chunk";
        _context.VectorChunks.Add(new VectorChunkEntity
        {
            Id = liveId,
            DocumentId = "doc-1",
            ChunkIndex = 0,
            Content = "live",
            TokenCount = 1,
            Metadata = new Dictionary<string, object>(),
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await _context.StoreVectorInVecTableAsync(liveId, new[] { 0.1f, 0.2f, 0.3f, 0.4f }, CancellationToken.None);

        var removed = await _context.SweepOrphanVectorsAsync(CancellationToken.None);

        removed.Should().Be(0);
        (await CountVecRowsAsync($"chunk_embeddings_{identity.Fingerprint}", liveId)).Should().Be(1,
            "the live row must survive the sweep");
    }

    [Fact]
    public async Task SweepOrphanVectorsAsync_RunTwice_SecondRunIsSkipped()
    {
        // Idempotency: marker prevents re-execution.
        CITestHelper.SkipIfSqliteVecNotAvailable();

        await _context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var identity = new EmbeddingIdentity { Provider = "Test", Model = "sweep-idem", Dimension = 4 };
        _options.EmbeddingFingerprint = identity.Fingerprint;
        await CreateCollectionForIdentity(identity);

        var first = await _context.SweepOrphanVectorsAsync(CancellationToken.None);
        first.Should().BeGreaterThanOrEqualTo(0, "first run executes the sweep (possibly with no work to do)");

        // Seed an orphan AFTER the first sweep ran — the marker is set, so the second sweep skips it.
        await _context.StoreVectorInVecTableAsync("late-orphan", new[] { 0.1f, 0.2f, 0.3f, 0.4f }, CancellationToken.None);

        var second = await _context.SweepOrphanVectorsAsync(CancellationToken.None);

        second.Should().Be(-1, "second sweep must short-circuit on the marker");
        (await CountVecRowsAsync($"chunk_embeddings_{identity.Fingerprint}", "late-orphan")).Should().Be(1,
            "the late orphan must survive because the second sweep is skipped");
    }

    [Fact]
    public async Task SweepOrphanVectorsAsync_MultipleFingerprints_CleansAllTables()
    {
        // Filer scenario: post-migration deployment has orphans in both legacy and current vec0 tables.
        CITestHelper.SkipIfSqliteVecNotAvailable();

        await _context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var legacy = new EmbeddingIdentity { Provider = "Test", Model = "sweep-legacy", Dimension = 4 };
        var current = new EmbeddingIdentity { Provider = "Test", Model = "sweep-current", Dimension = 4 };
        await CreateCollectionForIdentity(legacy);
        await CreateCollectionForIdentity(current);

        _options.EmbeddingFingerprint = legacy.Fingerprint;
        await _context.StoreVectorInVecTableAsync("legacy-orphan-a", new[] { 0.1f, 0.2f, 0.3f, 0.4f }, CancellationToken.None);
        await _context.StoreVectorInVecTableAsync("legacy-orphan-b", new[] { 0.5f, 0.5f, 0.5f, 0.5f }, CancellationToken.None);
        _options.EmbeddingFingerprint = current.Fingerprint;
        await _context.StoreVectorInVecTableAsync("current-orphan", new[] { 0.2f, 0.3f, 0.4f, 0.5f }, CancellationToken.None);

        var removed = await _context.SweepOrphanVectorsAsync(CancellationToken.None);

        removed.Should().Be(3, "two legacy orphans + one current orphan");
        (await CountVecRowsAsync($"chunk_embeddings_{legacy.Fingerprint}", "legacy-orphan-a")).Should().Be(0);
        (await CountVecRowsAsync($"chunk_embeddings_{legacy.Fingerprint}", "legacy-orphan-b")).Should().Be(0);
        (await CountVecRowsAsync($"chunk_embeddings_{current.Fingerprint}", "current-orphan")).Should().Be(0);
    }

    [Fact]
    public async Task SweepOrphanVectorsAsync_VectorChunksTableMissing_TreatsAllAsOrphans()
    {
        // Pathological case: vec0 has rows but vector_chunks does not exist yet
        // (e.g. partial schema state mid-migration). Sweep must not throw.
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var identity = new EmbeddingIdentity { Provider = "Test", Model = "sweep-no-parent", Dimension = 4 };
        _options.EmbeddingFingerprint = identity.Fingerprint;
        await CreateCollectionForIdentity(identity);
        await _context.StoreVectorInVecTableAsync("no-parent-chunk", new[] { 0.1f, 0.2f, 0.3f, 0.4f }, CancellationToken.None);

        // Note: we deliberately did NOT call _context.Database.EnsureCreatedAsync() —
        // vector_chunks table does not exist.

        var removed = await _context.SweepOrphanVectorsAsync(CancellationToken.None);

        removed.Should().Be(1);
        (await CountVecRowsAsync($"chunk_embeddings_{identity.Fingerprint}", "no-parent-chunk")).Should().Be(0);
    }

    private async Task<int> CountMigrationMarkerAsync(string migrationId)
    {
        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM __fluxindex_migrations WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", migrationId);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    // Issue: ISSUE-158 / cross-fingerprint-orphan-vec-table — whole-table fingerprint divergence
    // with live vectors (distinct from the row-level orphan-sweep-v1).

    [Fact]
    public async Task DetectCrossFingerprintOrphanTables_DivergentTableWithVectors_IsReportedAndNotDeleted()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        await _context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var effective = new EmbeddingIdentity { Provider = "Test", Model = "effective", Dimension = 4 };
        var orphan = new EmbeddingIdentity { Provider = "Test", Model = "orphan", Dimension = 4 };
        await CreateCollectionForIdentity(effective);
        await CreateCollectionForIdentity(orphan);

        // Bind the store to the effective fingerprint.
        _options.EmbeddingFingerprint = effective.Fingerprint;
        _options.VectorDimension = effective.Dimension;

        // Seed live vectors into the orphan (non-effective) table by temporarily switching fingerprint.
        _options.EmbeddingFingerprint = orphan.Fingerprint;
        await _context.StoreVectorInVecTableAsync("orphan-vec-1", new[] { 0.1f, 0.2f, 0.3f, 0.4f }, CancellationToken.None);
        await _context.StoreVectorInVecTableAsync("orphan-vec-2", new[] { 0.5f, 0.5f, 0.5f, 0.5f }, CancellationToken.None);
        _options.EmbeddingFingerprint = effective.Fingerprint;

        var orphans = await _context.DetectCrossFingerprintOrphanTablesAsync(CancellationToken.None);

        orphans.Should().ContainSingle();
        orphans[0].TableName.Should().Be($"chunk_embeddings_{orphan.Fingerprint}");
        orphans[0].Fingerprint.Should().Be(orphan.Fingerprint);
        orphans[0].VectorCount.Should().Be(2);

        // Non-destructive: the orphan vectors must survive the scan (sole copy under a different identity).
        (await CountVecRowsAsync($"chunk_embeddings_{orphan.Fingerprint}", "orphan-vec-1")).Should().Be(1);
        (await CountVecRowsAsync($"chunk_embeddings_{orphan.Fingerprint}", "orphan-vec-2")).Should().Be(1);
    }

    [Fact]
    public async Task DetectCrossFingerprintOrphanTables_OnlyEffectiveTable_ReturnsEmpty()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        await _context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var effective = new EmbeddingIdentity { Provider = "Test", Model = "only-effective", Dimension = 4 };
        await CreateCollectionForIdentity(effective);
        _options.EmbeddingFingerprint = effective.Fingerprint;
        _options.VectorDimension = effective.Dimension;
        await _context.StoreVectorInVecTableAsync("eff-1", new[] { 0.1f, 0.2f, 0.3f, 0.4f }, CancellationToken.None);

        var orphans = await _context.DetectCrossFingerprintOrphanTablesAsync(CancellationToken.None);

        orphans.Should().BeEmpty("the effective table is never its own orphan");
    }

    [Fact]
    public async Task DetectCrossFingerprintOrphanTables_DivergentTableEmpty_NotReported()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        await _context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var effective = new EmbeddingIdentity { Provider = "Test", Model = "eff-2", Dimension = 4 };
        var emptyOrphan = new EmbeddingIdentity { Provider = "Test", Model = "empty-orphan", Dimension = 4 };
        await CreateCollectionForIdentity(effective);
        await CreateCollectionForIdentity(emptyOrphan);
        _options.EmbeddingFingerprint = effective.Fingerprint;
        _options.VectorDimension = effective.Dimension;

        // emptyOrphan table exists but holds no vectors — a divergent-but-empty table is not a data-loss risk.
        var orphans = await _context.DetectCrossFingerprintOrphanTablesAsync(CancellationToken.None);

        orphans.Should().BeEmpty("a divergent table with zero vectors carries no unreachable data");
    }

    [Fact]
    public async Task DetectCrossFingerprintOrphanTables_NoEffectiveFingerprintBound_ReturnsEmpty()
    {
        // No sqlite-vec needed: the guard short-circuits before touching the database.
        _options.EmbeddingFingerprint = null;

        var orphans = await _context.DetectCrossFingerprintOrphanTablesAsync(CancellationToken.None);

        orphans.Should().BeEmpty("detection cannot determine the effective table without a bound fingerprint");
    }

    [Fact]
    public async Task EnsureInitialized_FiresCrossFingerprintWarn_ViaInitPath_WhenFingerprintBound()
    {
        // Proves the scan fires from the store init path (where the fingerprint is guaranteed bound),
        // not only when DetectCrossFingerprintOrphanTablesAsync is called directly.
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Capturing logger on the DbContext — detection logs via the context's logger.
        var captured = new List<string>();
        var dbOptions = new DbContextOptionsBuilder<SQLiteVecDbContext>().UseSqlite(_connection).Options;
        var optionsWrapper = Options.Create(_options);
        using var ctx = new SQLiteVecDbContext(dbOptions, optionsWrapper, _extensionLoader, new CapturingLogger<SQLiteVecDbContext>(captured));
        var fallback = new Lazy<SQLiteVectorStore>(() => throw new InvalidOperationException("Fallback should not be used"));
        var store = new SQLiteVecVectorStore(ctx, NullLogger<SQLiteVecVectorStore>.Instance, optionsWrapper, _extensionLoader, fallback);

        await ctx.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var effective = new EmbeddingIdentity { Provider = "Test", Model = "init-effective", Dimension = 4 };
        var orphan = new EmbeddingIdentity { Provider = "Test", Model = "init-orphan", Dimension = 4 };
        await CreateCollectionForIdentity(effective);
        await CreateCollectionForIdentity(orphan);

        // Bind effective; seed a live vector into the orphan (non-effective) table.
        _options.EmbeddingFingerprint = effective.Fingerprint;
        _options.VectorDimension = effective.Dimension;
        _options.EmbeddingFingerprint = orphan.Fingerprint;
        await ctx.StoreVectorInVecTableAsync("init-orphan-vec", new[] { 0.1f, 0.2f, 0.3f, 0.4f }, CancellationToken.None);
        _options.EmbeddingFingerprint = effective.Fingerprint;

        // VerifyHealthAsync -> EnsureInitializedAsync -> cross-fingerprint scan.
        await store.VerifyHealthAsync(CancellationToken.None);

        captured.Should().Contain(m =>
            m.Contains("Cross-fingerprint orphan") && m.Contains(orphan.Fingerprint),
            "the init path must surface the orphan table WARN");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _sink;
        public CapturingLogger(List<string> sink) => _sink = sink;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _sink.Add(formatter(state, exception));
    }

    [Fact]
    public async Task DeleteCollectionAsync_InvalidName_ThrowsArgumentException()
    {
        // This test does NOT need sqlite-vec — it validates name guard logic only.

        // Attempt SQL injection / invalid names
        var invalidNames = new[]
        {
            "'; DROP TABLE vector_chunks; --",
            "chunk_embeddings_' OR '1'='1",
            "not_chunk_embeddings_prefix",
            "chunk_embeddings_",       // prefix only, no suffix
            "CHUNK_EMBEDDINGS_abc",    // wrong case
        };

        foreach (var name in invalidNames)
        {
            var act = async () => await ((IVectorStoreManager)_store).DeleteCollectionAsync(name);
            await act.Should().ThrowAsync<ArgumentException>(
                $"name '{name}' should be rejected as invalid");
        }
    }

    public void Dispose()
    {
        _store.Dispose();
        _context.Dispose();
        _connection.Dispose();
    }
}
