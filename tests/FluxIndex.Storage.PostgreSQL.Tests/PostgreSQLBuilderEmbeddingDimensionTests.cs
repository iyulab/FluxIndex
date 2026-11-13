using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Constants;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.SDK;
using FluxIndex.SDK.Configuration;
using FluxIndex.SDK.Services;
using FluxIndex.Storage.PostgreSQL.Cache;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Docker-free pins for the vector size the builder path gives the PostgreSQL stores. Until 0.107.0
/// <c>UsePostgreSQL(...).AddPostgreSQLStorage()</c> left <see cref="PostgreSQLOptions.EmbeddingDimensions"/> and
/// <see cref="PostgresCacheOptions.EmbeddingDimensions"/> at 1536 whatever embedding service was bound, so a service of
/// another size created <c>vector(1536)</c> tables and failed its first write with
/// <c>22000: expected 1536 dimensions, not N</c> (the builder has no public knob for the size). The size now comes from
/// the bound embedding service, the same source the Qdrant builder path uses.
/// </summary>
public class PostgreSQLBuilderEmbeddingDimensionTests
{
    private static FluxIndexOptions FullStack()
    {
        var options = new FluxIndexOptions();
        // Equivalent to builder.UsePostgreSQL(connectionString): vector store, graph store and semantic cache.
        options.VectorStore.Provider = "PostgreSQL";
        options.VectorStore.ConnectionString = "Host=localhost;Database=flux;Username=u;Password=p";
        options.SemanticCache.Provider = "PostgreSQL";
        options.SemanticCache.UseVectorStoreConnection = true;
        return options;
    }

    private static ServiceProvider Register(
        FluxIndexOptions options,
        Action<IServiceCollection>? before = null,
        Action<IServiceCollection>? after = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        before?.Invoke(services);
        FluxIndexContextBuilderExtensions.RegisterPostgreSQLServices(services, options);
        after?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(384)]
    [InlineData(1024)]
    [InlineData(3072)]
    public void ABoundEmbeddingService_SetsTheVectorSizeOfTheVectorStoreAndTheSemanticCache(int dimensions)
    {
        using var provider = Register(
            FullStack(),
            before: s => s.AddSingleton<IEmbeddingService>(new InMemoryEmbeddingService(dimensions)));

        provider.GetRequiredService<IOptions<PostgreSQLOptions>>().Value.EmbeddingDimensions.Should().Be(dimensions);
        provider.GetRequiredService<IOptions<PostgresCacheOptions>>().Value.EmbeddingDimensions.Should().Be(dimensions);
    }

    [Fact]
    public void TheServiceIsFoundWhicheverOrderItWasRegisteredIn()
    {
        // The builder registers storage after ConfigureServices, but UseEmbeddingService can come before or after
        // UsePostgreSQL on the builder; the options read the service when first resolved, not at registration.
        using var provider = Register(
            FullStack(),
            after: s => s.AddSingleton<IEmbeddingService>(new InMemoryEmbeddingService(1024)));

        provider.GetRequiredService<IOptions<PostgreSQLOptions>>().Value.EmbeddingDimensions.Should().Be(1024);
    }

    [Fact]
    public void AServiceFactory_IsResolvedFromTheProvider()
    {
        using var provider = Register(
            FullStack(),
            before: s => s.AddSingleton<IEmbeddingService>(_ => new InMemoryEmbeddingService(768)));

        provider.GetRequiredService<IOptions<PostgreSQLOptions>>().Value.EmbeddingDimensions.Should().Be(768);
    }

    [Fact]
    public void WithoutAnEmbeddingService_TheDefaultSizeStays()
    {
        using var provider = Register(FullStack());

        provider.GetRequiredService<IOptions<PostgreSQLOptions>>().Value.EmbeddingDimensions
            .Should().Be(EmbeddingDefaults.DefaultVectorDimension);
        provider.GetRequiredService<IOptions<PostgresCacheOptions>>().Value.EmbeddingDimensions
            .Should().Be(EmbeddingDefaults.DefaultVectorDimension);
    }

    [Fact]
    public void TheKeywordOnlyPlaceholderService_ReportsNoSize_SoTheDefaultStays()
    {
        // Build() registers NoEmbeddingService when nothing else is bound; its dimension is 0, not a vector size.
        using var provider = Register(
            FullStack(),
            before: s => s.AddSingleton<IEmbeddingService>(NoEmbeddingService.Instance));

        provider.GetRequiredService<IOptions<PostgreSQLOptions>>().Value.EmbeddingDimensions
            .Should().Be(EmbeddingDefaults.DefaultVectorDimension);
    }

    [Fact]
    public void AServiceThatCannotSayItsSizeYet_LeavesTheConfiguredSize_InsteadOfFailingTheBuild()
    {
        // An embedder whose model is not loaded and whose size was never declared throws from GetEmbeddingDimension;
        // that is no worse than before this change, and the build must not die on options resolution.
        var unknown = Substitute.For<IEmbeddingService>();
        unknown.GetEmbeddingDimension().Returns(_ => throw new InvalidOperationException("not known before the model loads"));

        using var provider = Register(FullStack(), before: s => s.AddSingleton(unknown));

        provider.GetRequiredService<IOptions<PostgreSQLOptions>>().Value.EmbeddingDimensions
            .Should().Be(EmbeddingDefaults.DefaultVectorDimension);
    }

    [Fact]
    public void APostConfigureTheConsumerAlreadyWrote_StillWins()
    {
        // The workaround consumers ran against 0.106.x: PostConfigure<PostgreSQLOptions>(o => o.EmbeddingDimensions = n).
        using var provider = Register(
            FullStack(),
            before: s => s.AddSingleton<IEmbeddingService>(new InMemoryEmbeddingService(1024)),
            after: s => s.PostConfigure<PostgreSQLOptions>(o => o.EmbeddingDimensions = 512));

        provider.GetRequiredService<IOptions<PostgreSQLOptions>>().Value.EmbeddingDimensions.Should().Be(512);
    }

    [Fact]
    public void ABuilderPathWithoutAPostgreSQLSemanticCache_RegistersNoCacheOptionsOfItsOwn()
    {
        var options = FullStack();
        options.SemanticCache.Provider = "None";

        using var provider = Register(
            options,
            before: s => s.AddSingleton<IEmbeddingService>(new InMemoryEmbeddingService(1024)));

        provider.GetRequiredService<IOptions<PostgreSQLOptions>>().Value.EmbeddingDimensions.Should().Be(1024);
        provider.GetService<ISemanticCacheService>().Should().BeNull();
    }
}
