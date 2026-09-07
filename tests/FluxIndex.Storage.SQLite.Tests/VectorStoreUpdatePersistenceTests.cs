using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// Guards the contract that <see cref="IVectorStore.UpdateAsync"/> actually writes.
///
/// <para>
/// These tests exist because a shipped release returned <c>true</c> from <c>UpdateAsync</c> while
/// persisting nothing: the DbContext is registered with <c>QueryTrackingBehavior.NoTracking</c>, so
/// the entity the method queried was never in the change tracker and <c>SaveChangesAsync</c> found
/// no changes to write. Every existing test passed throughout.
/// </para>
///
/// <para>
/// Two properties of this fixture are load-bearing and must not be "simplified" away:
/// </para>
/// <list type="number">
/// <item>
/// <b>The assertion reads through a second, independent service provider</b> — a different
/// DbContext over the same database file. Asserting through the same store instance cannot
/// distinguish a persisted row from an in-memory object that was mutated and dropped.
/// </item>
/// <item>
/// <b><c>UseSQLiteVec</c> is true and <c>FallbackToInMemoryOnError</c> is false.</b> The
/// pre-existing test class sets the opposite of both, which makes <c>SQLiteVecVectorStore</c>
/// delegate every call to its in-memory fallback — so those tests never execute the store they are
/// named after, and no assertion they could add would have caught this. There is no third option:
/// with <c>UseSQLiteVec</c> false the store either falls back or throws, so exercising the real EF
/// write path requires the native extension to be loaded.
/// </item>
/// </list>
/// </summary>
public class VectorStoreUpdatePersistenceTests : IAsyncLifetime
{
    private readonly string _databasePath;
    private readonly ServiceProvider _writer;

    public VectorStoreUpdatePersistenceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"fluxindex_update_{Guid.NewGuid():N}.db");
        _writer = BuildProvider(_databasePath);
    }

    public async ValueTask InitializeAsync()
    {
        if (CITestHelper.ShouldSkipSqliteVec())
            return;

        await StartHostedServicesAsync(_writer);
    }

    /// <summary>
    /// The relational schema is provisioned by a hosted service, which nothing starts outside a
    /// host — without this the store fails with "no such table: vector_chunks".
    /// </summary>
    private static async Task StartHostedServicesAsync(ServiceProvider provider)
    {
        foreach (var service in provider.GetServices<IHostedService>())
            await service.StartAsync(CancellationToken.None);
    }

    /// <summary>
    /// A second provider over the same database file — a genuinely independent DbContext, which is
    /// what makes the read-back assertions meaningful.
    /// </summary>
    private static async Task<ServiceProvider> BuildReaderAsync(string databasePath)
    {
        var provider = BuildProvider(databasePath);
        await StartHostedServicesAsync(provider);
        return provider;
    }

    private static ServiceProvider BuildProvider(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSQLiteVecVectorStore(options =>
        {
            options.DatabasePath = databasePath;
            options.UseInMemory = false;
            options.VectorDimension = 4;
            // Required when BindIdentity() is not used — the vec0 table name derives from it.
            options.EmbeddingFingerprint = "updatepersist4";
            // See the class remarks: both of these are deliberate.
            options.UseSQLiteVec = true;
            options.FallbackToInMemoryOnError = false;
            options.AutoMigrate = true;
        });
        return services.BuildServiceProvider();
    }

    private static DocumentChunk NewChunk() => new()
    {
        DocumentId = "doc-1",
        Content = "original content",
        ChunkIndex = 0,
        TokenCount = 3,
        Embedding = new[] { 0.1f, 0.2f, 0.3f, 0.4f },
        Metadata = new Dictionary<string, object> { ["origin"] = "seed" }
    };

    /// <summary>
    /// The shape the consumer report described: read a chunk, add one metadata key, write it back
    /// with no embedding. Before the fix this returned true and changed nothing.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_MetadataOnly_IsVisibleToAnIndependentContext()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var store = _writer.GetRequiredService<IVectorStore>();
        var chunk = NewChunk();
        var id = await store.StoreAsync(chunk, TestContext.Current.CancellationToken);

        var stored = await store.GetAsync(id, TestContext.Current.CancellationToken);
        stored.Should().NotBeNull();

        var metadata = new Dictionary<string, object>(stored!.Metadata!) { ["document_id"] = "doc-1" };
        stored.Metadata = metadata;
        stored.Embedding = null;

        var updated = await store.UpdateAsync(stored, TestContext.Current.CancellationToken);
        updated.Should().BeTrue();

        await using var reader = await BuildReaderAsync(_databasePath);
        var reread = await reader.GetRequiredService<IVectorStore>()
            .GetAsync(id, TestContext.Current.CancellationToken);

        reread.Should().NotBeNull();
        reread!.Metadata.Should().ContainKey("document_id");
        reread.Metadata!["document_id"].ToString().Should().Be("doc-1");
    }

    /// <summary>
    /// Content and token count travel by the same mechanism as metadata, so they fail the same way.
    /// Asserting them separately keeps a partial fix (for example marking only Metadata modified)
    /// from passing.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ContentAndTokenCount_AreVisibleToAnIndependentContext()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var store = _writer.GetRequiredService<IVectorStore>();
        var id = await store.StoreAsync(NewChunk(), TestContext.Current.CancellationToken);

        var stored = await store.GetAsync(id, TestContext.Current.CancellationToken);
        stored!.Content = "rewritten content";
        stored.TokenCount = 99;

        (await store.UpdateAsync(stored, TestContext.Current.CancellationToken)).Should().BeTrue();

        await using var reader = await BuildReaderAsync(_databasePath);
        var reread = await reader.GetRequiredService<IVectorStore>()
            .GetAsync(id, TestContext.Current.CancellationToken);

        reread.Should().NotBeNull();
        reread!.Content.Should().Be("rewritten content");
        reread.TokenCount.Should().Be(99);
    }

    /// <summary>
    /// A chunk that is not there cannot be updated, and the method must say so. This is the other
    /// half of making the return value meaningful — it has to be capable of being false.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_UnknownChunk_ReturnsFalse()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var store = _writer.GetRequiredService<IVectorStore>();
        var missing = NewChunk();
        missing.Id = $"absent-{Guid.NewGuid():N}";

        var updated = await store.UpdateAsync(missing, TestContext.Current.CancellationToken);

        updated.Should().BeFalse();
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        try
        {
            if (File.Exists(_databasePath))
                File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test run over.
        }
    }
}
