using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Providers.OpenAI.Extensions;
using FluxIndex.Providers.OpenAI.Services;
using FluxIndex.SDK.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// <c>Build()</c> registers its defaults after <c>ConfigureServices()</c> ran, so a default added with <c>Add*</c> is the
/// later registration and replaces what the caller registered — without a word. A real embedding endpoint configured
/// through <c>ConfigureServices(s =&gt; s.AddOpenAICompatibleEmbedding(...))</c> resolved as the in-memory random
/// embedder, and every write to a store sized for the real model failed on the dimension. The defaults yield now; an
/// explicit builder selection (<c>UseInMemoryEmbedding()</c>) still wins.
/// </summary>
public class BuilderDefaultsYieldToRegistrationsTests
{
    private static T Resolve<T>(FluxIndexContextBuilder builder) where T : notnull
    {
        var context = builder.SuppressStartupMessages().Build();
        try
        {
            using var scope = context.ServiceProvider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<T>();
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Embedding_registered_through_ConfigureServices_is_the_one_resolved()
    {
        var builder = FluxIndexContext.CreateBuilder()
            .ConfigureServices(s => s.AddOpenAICompatibleEmbedding("http://localhost:1/v1", null, "test-model", 1024));

        Assert.IsType<OpenAICompatibleEmbeddingService>(Resolve<IEmbeddingService>(builder));
    }

    [Fact]
    public void Without_a_registration_the_default_is_the_in_memory_embedder()
    {
        // Control for the fact above: the default still applies when nothing else is registered.
        Assert.IsType<InMemoryEmbeddingService>(Resolve<IEmbeddingService>(FluxIndexContext.CreateBuilder()));
    }

    [Fact]
    public void An_explicit_UseInMemoryEmbedding_wins_over_an_earlier_registration()
    {
        var builder = FluxIndexContext.CreateBuilder()
            .ConfigureServices(s => s.AddOpenAICompatibleEmbedding("http://localhost:1/v1", null, "test-model", 1024))
            .UseInMemoryEmbedding();

        Assert.IsType<InMemoryEmbeddingService>(Resolve<IEmbeddingService>(builder));
    }

    [Fact]
    public void Chunking_service_registered_through_ConfigureServices_is_the_one_resolved()
    {
        var chunking = Substitute.For<IChunkingService>();
        var builder = FluxIndexContext.CreateBuilder().ConfigureServices(s => s.AddSingleton(chunking));

        Assert.Same(chunking, Resolve<IChunkingService>(builder));
    }

    [Fact]
    public void Document_repository_registered_through_ConfigureServices_is_the_one_resolved()
    {
        var repository = Substitute.For<IDocumentRepository>();
        var builder = FluxIndexContext.CreateBuilder().ConfigureServices(s => s.AddSingleton(repository));

        Assert.Same(repository, Resolve<IDocumentRepository>(builder));
    }

    [Fact]
    public void Hybrid_search_service_registered_through_ConfigureServices_is_the_one_resolved()
    {
        // e.g. AddQdrantWithHybridSearch registers QdrantHybridSearchService this way.
        var hybrid = Substitute.For<IHybridSearchService>();
        var builder = FluxIndexContext.CreateBuilder().ConfigureServices(s => s.AddSingleton(hybrid));

        Assert.Same(hybrid, Resolve<IHybridSearchService>(builder));
    }
}
