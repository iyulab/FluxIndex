using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK.AI.Local;
using FluxIndex.SDK.AI.Local.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.SDK.Tests.AI.Local;

/// <summary>
/// Tests for LocalAI service registration and DI configuration
/// </summary>
public class LocalAIServiceCollectionExtensionsTests
{
    #region Text Completion Service Tests

    [Fact]
    public void AddLocalAITextCompletion_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalAITextCompletion();

        // Assert
        var provider = services.BuildServiceProvider();
        var textCompletionService = provider.GetService<ITextCompletionService>();
        Assert.NotNull(textCompletionService);
        Assert.IsType<LocalAITextCompletionService>(textCompletionService);
    }

    [Fact]
    public void AddLocalAITextCompletion_WithModelId_RegistersWithCorrectModel()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalAITextCompletion("fast");

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<IOptions<LocalAITextCompletionOptions>>();
        Assert.NotNull(options);
        Assert.Equal("fast", options.Value.ModelId);
    }

    [Fact]
    public void AddLocalAITextCompletion_WithConfigureOptions_AppliesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalAITextCompletion(options =>
        {
            options.ModelId = "quality";
            options.MaxContextLength = 8192;
            options.TopP = 0.95f;
            options.TopK = 100;
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<IOptions<LocalAITextCompletionOptions>>();
        Assert.NotNull(options);
        Assert.Equal("quality", options.Value.ModelId);
        Assert.Equal(8192, options.Value.MaxContextLength);
        Assert.Equal(0.95f, options.Value.TopP);
        Assert.Equal(100, options.Value.TopK);
    }

    #endregion

    #region Embedding Service Tests

    [Fact]
    public void AddLocalAIEmbedding_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalAIEmbedding();

        // Assert
        var provider = services.BuildServiceProvider();
        var embeddingService = provider.GetService<IEmbeddingService>();
        Assert.NotNull(embeddingService);
        Assert.IsType<LocalAIEmbeddingService>(embeddingService);
    }

    [Fact]
    public void AddLocalAIEmbedding_WithModelId_RegistersWithCorrectModel()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalAIEmbedding("multilingual");

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<IOptions<LocalAIEmbeddingOptions>>();
        Assert.NotNull(options);
        Assert.Equal("multilingual", options.Value.ModelId);
    }

    [Fact]
    public void AddLocalAIEmbedding_DefaultOptions_HasCorrectDefaults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalAIEmbedding();

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<IOptions<LocalAIEmbeddingOptions>>()?.Value;
        Assert.NotNull(options);
        Assert.Equal("default", options.ModelId);
        Assert.Equal(LocalAIExecutionProvider.Auto, options.ExecutionProvider);
        Assert.Equal(512, options.MaxSequenceLength);
        Assert.True(options.NormalizeEmbeddings);
        Assert.False(options.WarmupOnStartup);
    }

    #endregion

    #region Reranker Service Tests

    [Fact]
    public void AddLocalAIReranker_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalAIReranker();

        // Assert
        var provider = services.BuildServiceProvider();
        var reranker = provider.GetService<IReranker>();
        Assert.NotNull(reranker);
        Assert.IsType<LocalAIRerankerAdapter>(reranker);
    }

    [Fact]
    public void AddResilientLocalAIReranker_RegistersResilientService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddResilientLocalAIReranker();

        // Assert
        var provider = services.BuildServiceProvider();
        var reranker = provider.GetService<IReranker>();
        Assert.NotNull(reranker);
        Assert.IsType<ResilientLocalAIReranker>(reranker);
    }

    #endregion

    #region Combined Services Tests

    [Fact]
    public void AddLocalAI_RegistersEmbeddingAndReranker()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalAI();

        // Assert
        var provider = services.BuildServiceProvider();

        var embeddingService = provider.GetService<IEmbeddingService>();
        Assert.NotNull(embeddingService);
        Assert.IsType<LocalAIEmbeddingService>(embeddingService);

        var reranker = provider.GetService<IReranker>();
        Assert.NotNull(reranker);
        Assert.IsType<ResilientLocalAIReranker>(reranker);
    }

    #endregion
}
