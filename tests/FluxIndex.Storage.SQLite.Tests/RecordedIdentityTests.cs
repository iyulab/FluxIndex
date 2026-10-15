using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Exceptions;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// The plain SQLite vector store records the embedding identity of its vectors in the database, so a fresh store instance
/// — a restarted process, or the next request scope (the store is registered per scope) — bound to another model is
/// refused instead of writing a second model's vectors into the same table.
/// </summary>
[Collection("SQLite Tests")]
public sealed class RecordedIdentityTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"fluxindex-identity-{Guid.NewGuid():N}.db");
    private readonly List<ServiceProvider> _providers = [];

    private IVectorStore Store(EmbeddingIdentity? identity)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSQLiteVectorStore(new SQLiteOptions { DatabasePath = _db, AutoMigrate = false });
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        var store = provider.CreateScope().ServiceProvider.GetRequiredService<IVectorStore>();
        if (identity is not null)
            store.BindIdentity(identity);
        return store;
    }

    private static EmbeddingIdentity Model(string model) => new() { Provider = "test", Model = model, Dimension = 4 };

    private static DocumentChunk Chunk()
    {
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(), DocumentId = "doc", Content = "text", ChunkIndex = 0, TotalChunks = 1, TokenCount = 1,
            CreatedAt = DateTime.UtcNow,
        };
        chunk.SetEmbedding([1f, 0f, 0f, 0f]);
        return chunk;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFreshInstanceBoundToAnotherModel_IsRefused_OnWriteAndSearch()
    {
        await Store(Model("model-a")).StoreAsync(Chunk(), Ct);

        var switched = Store(Model("model-b"));

        (await switched.Awaiting(s => s.StoreAsync(Chunk(), Ct)).Should().ThrowAsync<EmbeddingModelMismatchException>())
            .Which.StoredIdentity.Model.Should().Be("model-a");
        await Store(Model("model-b")).Awaiting(s => s.SearchAsync([1f, 0f, 0f, 0f], 1, cancellationToken: Ct))
            .Should().ThrowAsync<EmbeddingModelMismatchException>();
    }

    [Fact]
    public async Task AFreshInstanceBoundToTheSameModel_Works()
    {
        await Store(Model("model-a")).StoreAsync(Chunk(), Ct);

        var again = Store(Model("model-a"));
        await again.StoreAsync(Chunk(), Ct);

        (await again.CountAsync(Ct)).Should().Be(2);
    }

    [Fact]
    public async Task AStoreWrittenWithoutARecord_AdoptsTheBoundIdentity_ThenGuardsIt()
    {
        await Store(identity: null).StoreAsync(Chunk(), Ct);

        await Store(Model("model-a")).StoreAsync(Chunk(), Ct);

        await Store(Model("model-b")).Awaiting(s => s.StoreAsync(Chunk(), Ct)).Should().ThrowAsync<EmbeddingModelMismatchException>();
    }

    [Fact]
    public async Task ClearingTheStore_ForgetsTheRecord()
    {
        var first = Store(Model("model-a"));
        await first.StoreAsync(Chunk(), Ct);
        await first.ClearAsync(Ct);

        var next = Store(Model("model-b"));
        await next.StoreAsync(Chunk(), Ct);

        (await next.CountAsync(Ct)).Should().Be(1);
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
            provider.Dispose();
        FluxIndex.Tests.Shared.SqliteTestPools.Release(_db);
        try { File.Delete(_db); } catch (IOException) { }
    }
}
