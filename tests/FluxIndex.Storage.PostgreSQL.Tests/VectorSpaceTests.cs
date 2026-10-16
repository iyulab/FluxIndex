using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// The PostgreSQL store keeps the vector-space revision of each vector in its row, so a fresh scope — the next request,
/// a restarted process — tells which vectors an in-place re-embed into a new revision has not reached, by the ids the
/// caller stored.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class VectorSpaceTests : IAsyncLifetime
{
    private const int Dimension = 1536;
    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();
    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSQLVectorStore(_container.GetConnectionString());
        _provider = services.BuildServiceProvider();
        foreach (var initializer in _provider.GetServices<IStorageInitializer>())
            initializer.InitializeSync(_provider);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    private IVectorStore Scope() => _provider.CreateScope().ServiceProvider.GetRequiredService<IVectorStore>();

    private static DocumentChunk Chunk(int i, string? revision)
    {
        var vector = new float[Dimension];
        vector[0] = 1f;
        vector[1] = 0.1f * i;
        return new DocumentChunk
        {
            Id = $"chunk-{i:00}", DocumentId = "doc", Content = $"text {i}", ChunkIndex = i, TotalChunks = 20, TokenCount = 1,
            CreatedAt = DateTime.UtcNow, Embedding = vector, VectorSpaceRevision = revision,
        };
    }

    [Fact]
    public async Task AfterARestartMidReembed_TheStoreCountsTheVectorsNotYetInTheNewSpace()
    {
        var ct = TestContext.Current.CancellationToken;
        await Scope().StoreBatchAsync(Enumerable.Range(0, 10).Select(i => Chunk(i, "A")), ct);
        var reembedding = Scope();
        await reembedding.StoreBatchAsync(Enumerable.Range(0, 3).Select(i => Chunk(i, "B")), ct);
        await reembedding.UpdateAsync(Chunk(3, "B"), ct);
        await reembedding.StoreAsync(Chunk(4, revision: null), ct);

        var fresh = Scope();
        (await fresh.CountVectorsOutsideSpaceAsync("B", ct)).Should().Be(6, "five still in A and one without a revision");
        var ids = await fresh.GetChunkIdsOutsideSpaceAsync("B", 10, ct);
        ids.Should().BeEquivalentTo(["chunk-04", "chunk-05", "chunk-06", "chunk-07", "chunk-08", "chunk-09"], "the caller's ids, not storage keys");
        (await fresh.GetAsync("chunk-02", ct))!.VectorSpaceRevision.Should().Be("B");
    }
}
