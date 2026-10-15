using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Exceptions;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// The PostgreSQL vector store is registered per request scope, so only a record in the database can refuse another
/// model: a fresh scope bound to another model than the one the table's vectors were written with is refused.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class RecordedIdentityTests : IAsyncLifetime
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

    /// <summary>A store from a new request scope — what each request of a server gets.</summary>
    private IVectorStore Scope(string model)
    {
        var store = _provider.CreateScope().ServiceProvider.GetRequiredService<IVectorStore>();
        store.BindIdentity(new EmbeddingIdentity { Provider = "test", Model = model, Dimension = Dimension });
        return store;
    }

    private static DocumentChunk Chunk()
    {
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(), DocumentId = "doc", Content = "text", ChunkIndex = 0, TotalChunks = 1, TokenCount = 1,
            CreatedAt = DateTime.UtcNow,
        };
        var vector = new float[Dimension];
        vector[0] = 1f;
        chunk.SetEmbedding(vector);
        return chunk;
    }

    [Fact]
    public async Task AScopeBoundToAnotherModel_IsRefused_AndTheSameModelWorks_AndClearingForgetsTheRecord()
    {
        var ct = TestContext.Current.CancellationToken;
        await Scope("model-a").StoreAsync(Chunk(), ct);

        (await Scope("model-b").Awaiting(s => s.StoreAsync(Chunk(), ct)).Should().ThrowAsync<EmbeddingModelMismatchException>())
            .Which.StoredIdentity.Model.Should().Be("model-a");

        var same = Scope("model-a");
        await same.StoreAsync(Chunk(), ct);
        (await same.CountAsync(ct)).Should().Be(2);

        await same.ClearAsync(ct);
        var next = Scope("model-b");
        await next.StoreAsync(Chunk(), ct);
        (await next.CountAsync(ct)).Should().Be(1);
    }
}
