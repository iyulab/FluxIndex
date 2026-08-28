using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Providers.OpenAI.Extensions;
using FluxIndex.Providers.OpenAI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluxIndex.Providers.OpenAI.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOpenAICompatibleEmbedding_RegistersIEmbeddingService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenAICompatibleEmbedding(
            "http://localhost/v1", null, "text-embedding-3-small", 1536);

        using var provider = services.BuildServiceProvider();

        var embeddingService = provider.GetService<IEmbeddingService>();

        embeddingService.Should().NotBeNull();
        embeddingService.Should().BeOfType<OpenAICompatibleEmbeddingService>();
    }

    [Fact]
    public void AddOpenAICompatibleEmbedding_ConfiguresCorrectModel()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenAICompatibleEmbedding(
            "http://localhost/v1", "api-key", "qwen3-embedding-0.6b", 1024);

        using var provider = services.BuildServiceProvider();

        var embeddingService = provider.GetRequiredService<IEmbeddingService>();

        embeddingService.GetModelName().Should().Be("qwen3-embedding-0.6b");
        embeddingService.GetEmbeddingDimension().Should().Be(1024);
    }

    [Fact]
    public void AddOpenAICompatibleReranker_RegistersIReranker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenAICompatibleReranker(
            "http://localhost/v1", null, "qwen3-reranker-0.6b");

        using var provider = services.BuildServiceProvider();

        var reranker = provider.GetService<IReranker>();

        reranker.Should().NotBeNull();
        reranker.Should().BeOfType<OpenAICompatibleRerankerService>();
    }

    [Fact]
    public void AddOpenAICompatibleReranker_ConfiguresCorrectModel()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenAICompatibleReranker(
            "http://localhost/v1", "api-key", "qwen3-reranker-0.6b");

        using var provider = services.BuildServiceProvider();

        var reranker = provider.GetRequiredService<IReranker>();

        var info = reranker.GetModelInfo();
        info.Name.Should().Be("qwen3-reranker-0.6b");
        info.Type.Should().Be(RerankModel.Custom);
    }

    [Fact]
    public void AddBothServices_RegistersBoth()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenAICompatibleEmbedding(
            "http://localhost/v1", null, "embed-model", 768);
        services.AddOpenAICompatibleReranker(
            "http://localhost/v1", null, "rerank-model");

        using var provider = services.BuildServiceProvider();

        provider.GetService<IEmbeddingService>().Should().NotBeNull();
        provider.GetService<IReranker>().Should().NotBeNull();
    }

    [Fact]
    public void AddOpenAICompatibleEmbedding_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var result = services.AddOpenAICompatibleEmbedding(
            "http://localhost/v1", null, "model", 128);

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddOpenAICompatibleReranker_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var result = services.AddOpenAICompatibleReranker(
            "http://localhost/v1", null, "model");

        result.Should().BeSameAs(services);
    }
}
