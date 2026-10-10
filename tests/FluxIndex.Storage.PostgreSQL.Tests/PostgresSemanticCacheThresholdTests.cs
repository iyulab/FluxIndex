using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using FluxIndex.SDK.Configuration;
using FluxIndex.Storage.PostgreSQL.Cache;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// <see cref="PostgresCacheOptions.SimilarityThreshold"/> is the hit threshold of a lookup that passes none; a threshold
/// passed per call still wins. The stored query and the near one are 0.9 similar, between the configured 0.85 and the
/// default 0.95. Same contract as the SQLite cache's threshold tests, against a real server.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgresSemanticCacheThresholdTests : IAsyncLifetime
{
    private const int Dimensions = 4;
    private const string Stored = "what is postgres";
    private const string Near = "what's postgres";

    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();
    private ServiceProvider _provider = null!;
    private ISemanticCacheService _cache = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        float[] stored = [1f, 0f, 0f, 0f];
        float[] near = [0.9f, MathF.Sqrt(1f - 0.81f), 0f, 0f];
        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.ArgAt<string>(0) == Near ? near : stored));
        embeddings.GenerateQueryEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.ArgAt<string>(0) == Near ? near : stored));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(embeddings);
        services.AddPostgreSQLSemanticCache(o =>
        {
            o.ConnectionString = _container.GetConnectionString();
            o.EmbeddingDimensions = Dimensions;
            o.EnableAutoCleanup = false;
            o.SimilarityThreshold = 0.85f;
        });
        _provider = services.BuildServiceProvider();
        _provider.GetRequiredService<PostgresCacheSchemaInitializer>().InitializeSync(_provider);
        _cache = _provider.GetRequiredService<ISemanticCacheService>();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task ConfiguredThreshold_DecidesALookupThatPassesNone()
    {
        var ct = TestContext.Current.CancellationToken;
        await _cache.SetCachedResultAsync(
            Stored, [new CacheDocumentChunk { Id = "c1", DocumentId = "d1", Content = "a relational database" }],
            cancellationToken: ct);

        (await _cache.GetCachedResultAsync(Near, cancellationToken: ct)).Should().NotBeNull("0.9 clears the configured 0.85");

        (await _cache.GetCachedResultAsync(Near, similarityThreshold: 0.95f, cancellationToken: ct)).Should().BeNull(
            "a threshold passed with the call overrides the configured one");
    }
}

/// <summary>
/// Docker-free: the builder path copies the SDK's semantic cache threshold into the PostgreSQL cache options, and an
/// unset SDK threshold leaves the cache at its own default.
/// </summary>
public class PostgresSemanticCacheThresholdRegistrationTests
{
    [Fact]
    public void BuilderPath_CopiesTheSdkSemanticCacheThreshold()
    {
        using var configured = BuilderProvider(o => o.SimilarityThreshold = 0.97f);
        configured.GetRequiredService<IOptions<PostgresCacheOptions>>().Value.SimilarityThreshold.Should().Be(0.97f);

        using var unset = BuilderProvider(_ => { });
        unset.GetRequiredService<IOptions<PostgresCacheOptions>>().Value.SimilarityThreshold.Should().Be(0.95f);
    }

    private static ServiceProvider BuilderProvider(Action<SemanticCacheOptions> configure)
    {
        var options = new FluxIndexOptions();
        options.VectorStore.Provider = "PostgreSQL";
        options.VectorStore.ConnectionString = "Host=localhost;Database=flux;Username=u;Password=p";
        options.SemanticCache.Provider = "PostgreSQL";
        configure(options.SemanticCache);

        var services = new ServiceCollection();
        services.AddLogging();
        FluxIndexContextBuilderExtensions.RegisterPostgreSQLServices(services, options);
        return services.BuildServiceProvider();
    }
}
