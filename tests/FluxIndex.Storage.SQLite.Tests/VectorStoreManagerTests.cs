using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Storage.SQLite;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

    [SkippableFact]
    public async Task ListCollectionsAsync_NoCollections_ReturnsEmpty()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Act
        var result = await ((IVectorStoreManager)_store).ListCollectionsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task ListCollectionsAsync_MultipleCollections_ReturnsAll()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var identityA = new EmbeddingIdentity { Provider = "OpenAI", Model = "text-embedding-3-small", Dimension = 1536 };
        var identityB = new EmbeddingIdentity { Provider = "LMSupply", Model = "all-MiniLM-L6-v2", Dimension = 384 };

        await CreateCollectionForIdentity(identityA);
        await CreateCollectionForIdentity(identityB);

        // Act
        var result = await ((IVectorStoreManager)_store).ListCollectionsAsync();

        // Assert
        result.Should().HaveCount(2);

        var nameA = $"chunk_embeddings_{identityA.Fingerprint}";
        var nameB = $"chunk_embeddings_{identityB.Fingerprint}";

        result.Should().Contain(c => c.Name == nameA && c.Dimension == 1536);
        result.Should().Contain(c => c.Name == nameB && c.Dimension == 384);

        // StorageSizeBytes is always null for SQLite per-table
        result.Should().AllSatisfy(c => c.StorageSizeBytes.Should().BeNull());
    }

    [SkippableFact]
    public async Task GetCollectionInfoAsync_ExistingCollection_ReturnsInfo()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var identity = new EmbeddingIdentity { Provider = "OpenAI", Model = "text-embedding-3-small", Dimension = 1536 };
        await CreateCollectionForIdentity(identity);
        var tableName = $"chunk_embeddings_{identity.Fingerprint}";

        // Act
        var result = await ((IVectorStoreManager)_store).GetCollectionInfoAsync(tableName);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be(tableName);
        result.Dimension.Should().Be(1536);
        result.EntryCount.Should().Be(0);
        result.StorageSizeBytes.Should().BeNull();
    }

    [SkippableFact]
    public async Task GetCollectionInfoAsync_NonExistent_ReturnsNull()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange — use a valid name format but for a collection that doesn't exist
        var tableName = "chunk_embeddings_nonexist";

        // Act
        var result = await ((IVectorStoreManager)_store).GetCollectionInfoAsync(tableName);

        // Assert
        result.Should().BeNull();
    }

    [SkippableFact]
    public async Task DeleteCollectionAsync_ExistingCollection_ReturnsTrueAndRemoves()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var identity = new EmbeddingIdentity { Provider = "LMSupply", Model = "all-MiniLM-L6-v2", Dimension = 384 };
        await CreateCollectionForIdentity(identity);
        var tableName = $"chunk_embeddings_{identity.Fingerprint}";

        // Verify it exists first
        var before = await ((IVectorStoreManager)_store).GetCollectionInfoAsync(tableName);
        before.Should().NotBeNull();

        // Act
        var deleted = await ((IVectorStoreManager)_store).DeleteCollectionAsync(tableName);

        // Assert
        deleted.Should().BeTrue();

        var after = await ((IVectorStoreManager)_store).GetCollectionInfoAsync(tableName);
        after.Should().BeNull();
    }

    [SkippableFact]
    public async Task DeleteCollectionAsync_NonExistent_ReturnsFalse()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange — valid name format but not present
        var tableName = "chunk_embeddings_nothere";

        // Act
        var result = await ((IVectorStoreManager)_store).DeleteCollectionAsync(tableName);

        // Assert
        result.Should().BeFalse();
    }

    [SkippableFact]
    public async Task ListCollectionsAsync_LegacyDimensionTable_IncludedAsOrphan()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange: create a legacy dimension-only table directly (no fingerprint)
        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync();

        await _extensionLoader.LoadExtensionAsync(_connection, CancellationToken.None);
        await _extensionLoader.CreateVecTableAsync(
            _connection, "chunk_embeddings_1536", 1536, "distance_metric=cosine", CancellationToken.None);

        // Act
        IVectorStoreManager manager = _store;
        var collections = await manager.ListCollectionsAsync();

        // Assert: legacy table appears in listing
        collections.Should().HaveCount(1);
        collections[0].Name.Should().Be("chunk_embeddings_1536");
        collections[0].Dimension.Should().Be(1536);
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
