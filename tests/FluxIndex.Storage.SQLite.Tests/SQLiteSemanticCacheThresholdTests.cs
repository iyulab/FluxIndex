using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK.Configuration;
using FluxIndex.Storage.SQLite.Cache;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// <see cref="SQLiteCacheOptions.SimilarityThreshold"/> is the hit threshold of a lookup that passes none; a threshold
/// passed per call still wins. The stored query and the near one are 0.9 similar, between the default (0.85) and the
/// configured 0.95, so the two answers differ only by which threshold applied.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteSemanticCacheThresholdTests
{
    private const string Stored = "what is sqlite";
    private const string Near = "what's sqlite";

    private static ServiceProvider CacheProvider(float? similarityThreshold)
    {
        float[] stored = [1f, 0f, 0f, 0f];
        float[] near = [0.9f, MathF.Sqrt(1f - 0.81f), 0f, 0f];
        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.GenerateQueryEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.ArgAt<string>(0) == Near ? near : stored));
        embeddings.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.ArgAt<string>(0) == Near ? near : stored));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(embeddings);
        services.AddSQLiteSemanticCache(o =>
        {
            o.CacheDatabasePath = "semantic-cache-threshold.db";
            o.UseInMemory = true;
            o.EnableAutoCleanup = false;
            if (similarityThreshold is { } threshold)
                o.SimilarityThreshold = threshold;
        });
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<SQLiteCacheSchemaInitializer>().InitializeSync(provider);
        return provider;
    }

    [Fact]
    public async Task ConfiguredThreshold_DecidesALookupThatPassesNone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = CacheProvider(similarityThreshold: 0.95f);
        var cache = provider.GetRequiredService<ISemanticCache>();
        await cache.SetAsync(Stored, ["an embedded database"], cancellationToken: ct);

        (await cache.GetAsync(Near, cancellationToken: ct)).Should().BeNull("0.9 is below the configured 0.95");
        (await cache.HasSimilarQueryAsync(Near, cancellationToken: ct)).Should().BeFalse();
        (await cache.FindSimilarQueriesAsync(Near, cancellationToken: ct)).Should().BeEmpty();

        (await cache.GetAsync(Near, similarityThreshold: 0.85f, cancellationToken: ct)).Should().NotBeNull(
            "a threshold passed with the call overrides the configured one");
    }

    [Fact]
    public async Task UnsetThreshold_KeepsTheDefaultOf085()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = CacheProvider(similarityThreshold: null);
        provider.GetRequiredService<IOptions<SQLiteCacheOptions>>().Value.SimilarityThreshold.Should().Be(0.85f);
        var cache = provider.GetRequiredService<ISemanticCache>();
        await cache.SetAsync(Stored, ["an embedded database"], cancellationToken: ct);

        (await cache.GetAsync(Near, cancellationToken: ct)).Should().NotBeNull("0.9 clears the default 0.85");
    }

    [Fact]
    public void BuilderPath_CopiesTheSdkSemanticCacheThreshold()
    {
        using var configured = BuilderProvider(o => o.SimilarityThreshold = 0.97f);
        configured.GetRequiredService<IOptions<SQLiteCacheOptions>>().Value.SimilarityThreshold.Should().Be(0.97f);

        using var unset = BuilderProvider(_ => { });
        unset.GetRequiredService<IOptions<SQLiteCacheOptions>>().Value.SimilarityThreshold.Should().Be(0.85f,
            "an unset SDK threshold leaves the cache at its own default");
    }

    private static ServiceProvider BuilderProvider(Action<SemanticCacheOptions> configure)
    {
        var options = new FluxIndexOptions();
        options.VectorStore.Provider = "SQLite";
        options.VectorStore.ConnectionString = "Data Source=:memory:";
        options.SemanticCache.Provider = "SQLite";
        configure(options.SemanticCache);

        var services = new ServiceCollection();
        services.AddLogging();
        FluxIndexContextBuilderExtensions.RegisterSQLiteServices(services, options);
        return services.BuildServiceProvider();
    }
}
