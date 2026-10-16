using AwesomeAssertions;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Qdrant;
using Xunit;

namespace FluxIndex.Storage.Qdrant.Tests;

/// <summary>
/// The Qdrant store keeps the vector-space revision of each point in its payload, so a fresh instance — a restarted
/// process — tells which vectors an in-place re-embed into a new revision has not reached.
/// </summary>
[Collection("Qdrant")]
[Trait("Category", "Integration")]
public class VectorSpaceTests : IAsyncLifetime
{
    private const int Dimension = 8;
    private readonly QdrantContainer _container = new QdrantBuilder("qdrant/qdrant:latest").Build();
    private bool _started;

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
            _started = true;
        }
        catch (Exception)
        {
            // Docker not available - tests are skipped.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private QdrantVectorStore Store(string collection) => new(
        Options.Create(new QdrantOptions
        {
            Host = _container.Hostname,
            GrpcPort = _container.GetMappedPublicPort(6334),
            BaseCollectionName = collection,
            NamingStrategy = CollectionNamingStrategy.Fixed,
            VectorSize = Dimension,
            CreateCollectionOnStartup = true,
        }),
        NullLogger<QdrantVectorStore>.Instance);

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
        Assert.SkipUnless(_started, "Docker is not available");
        var ct = TestContext.Current.CancellationToken;
        var collection = $"space_{Guid.NewGuid():N}";
        await using (var first = Store(collection))
        {
            await first.StoreBatchAsync(Enumerable.Range(0, 10).Select(i => Chunk(i, "A")), ct);
            await first.StoreBatchAsync(Enumerable.Range(0, 3).Select(i => Chunk(i, "B")), ct);
            await first.StoreAsync(Chunk(3, revision: null), ct);
        }

        await using var restarted = Store(collection);
        (await restarted.CountVectorsOutsideSpaceAsync("B", ct)).Should().Be(7, "six still in A and one without a revision");
        var ids = await restarted.GetChunkIdsOutsideSpaceAsync("B", 20, ct);
        ids.Should().BeEquivalentTo(["chunk-03", "chunk-04", "chunk-05", "chunk-06", "chunk-07", "chunk-08", "chunk-09"]);
        (await restarted.GetAsync("chunk-01", ct))!.VectorSpaceRevision.Should().Be("B");
    }
}
