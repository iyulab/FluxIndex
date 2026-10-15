using AwesomeAssertions;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Exceptions;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Testcontainers.Qdrant;
using Xunit;

namespace FluxIndex.Storage.Qdrant.Tests;

/// <summary>
/// A collection whose name does not carry the model (<see cref="CollectionNamingStrategy.Fixed"/>) records the embedding
/// identity of its points in the collection metadata, so a model switched under it is refused by a fresh instance — a
/// restarted process — and not only by the instance that bound the first identity.
/// </summary>
[Collection("Qdrant")]
[Trait("Category", "Integration")]
public class RecordedIdentityTests : IAsyncLifetime
{
    private const int Dimension = 8;
    private readonly QdrantContainer _container = new QdrantBuilder("qdrant/qdrant:latest").Build();
    private QdrantClient? _client;

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
            _client = new QdrantClient(_container.Hostname, _container.GetMappedPublicPort(6334));
        }
        catch (Exception)
        {
            // Docker not available - tests are skipped.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private bool IsDockerAvailable => _client is not null;

    private QdrantVectorStore Store(string collection, EmbeddingIdentity? identity)
    {
        var store = new QdrantVectorStore(
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
        if (identity is not null)
            store.BindIdentity(identity);
        return store;
    }

    private static EmbeddingIdentity Model(string model) => new() { Provider = "test", Model = model, Dimension = Dimension };

    private static DocumentChunk Chunk()
    {
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = Guid.NewGuid().ToString(),
            Content = "text",
            ChunkIndex = 0,
            TotalChunks = 1,
            TokenCount = 1,
            CreatedAt = DateTime.UtcNow,
        };
        var vector = new float[Dimension];
        vector[0] = 1f;
        chunk.SetEmbedding(vector);
        return chunk;
    }

    [Fact]
    public async Task AFreshInstanceBoundToAnotherModel_IsRefused()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");
        var ct = TestContext.Current.CancellationToken;
        var collection = $"identity_{Guid.NewGuid():N}";
        await using (var first = Store(collection, Model("model-a")))
            await first.StoreAsync(Chunk(), ct);

        await using var restarted = Store(collection, Model("model-b"));
        var act = () => restarted.StoreAsync(Chunk(), ct);

        (await act.Should().ThrowAsync<EmbeddingModelMismatchException>())
            .Which.StoredIdentity.Model.Should().Be("model-a");
    }

    [Fact]
    public async Task AFreshInstanceBoundToTheSameModel_Works()
    {
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");
        var ct = TestContext.Current.CancellationToken;
        var collection = $"identity_{Guid.NewGuid():N}";
        await using (var first = Store(collection, Model("model-a")))
            await first.StoreAsync(Chunk(), ct);

        await using var restarted = Store(collection, Model("model-a"));
        await restarted.StoreAsync(Chunk(), ct);

        (await restarted.CountAsync(ct)).Should().Be(2);
    }

    [Fact]
    public async Task ACollectionWrittenWithoutARecord_AdoptsTheBoundIdentity_ThenGuardsIt()
    {
        // A collection from before identities were recorded: nothing says which model wrote it, so it is adopted.
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");
        var ct = TestContext.Current.CancellationToken;
        var collection = $"identity_{Guid.NewGuid():N}";
        await using (var unbound = Store(collection, identity: null))
            await unbound.StoreAsync(Chunk(), ct);

        await using (var adopting = Store(collection, Model("model-a")))
            await adopting.StoreAsync(Chunk(), ct);

        await using var switched = Store(collection, Model("model-b"));
        await switched.Awaiting(s => s.StoreAsync(Chunk(), ct)).Should().ThrowAsync<EmbeddingModelMismatchException>();
    }

    [Fact]
    public async Task RecordingTheIdentity_LeavesTheCollectionsQuantizationAlone()
    {
        // The client documents a null quantization argument to UpdateCollection as «disabled»; recording the identity on
        // a collection a consumer configured must not turn its quantization off.
        Assert.SkipUnless(IsDockerAvailable, "Docker is not available");
        var ct = TestContext.Current.CancellationToken;
        var collection = $"identity_{Guid.NewGuid():N}";
        await _client!.CreateCollectionAsync(
            collection,
            new VectorParams { Size = Dimension, Distance = Distance.Cosine },
            quantizationConfig: new QuantizationConfig { Scalar = new ScalarQuantization { Type = QuantizationType.Int8, AlwaysRam = true } },
            cancellationToken: ct);

        await using (var store = Store(collection, Model("model-a")))
            await store.StoreAsync(Chunk(), ct);

        var config = (await _client.GetCollectionInfoAsync(collection, ct)).Config;
        config.QuantizationConfig.Should().NotBeNull();
        config.QuantizationConfig.Scalar.Should().NotBeNull();
        config.Metadata.Should().ContainKey(QdrantVectorStore.IdentityMetadataKey);
    }
}
