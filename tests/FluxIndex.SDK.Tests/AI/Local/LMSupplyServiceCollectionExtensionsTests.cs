using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK.AI.Local;
using FluxIndex.SDK.AI.Local.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.SDK.Tests.AI.Local;

/// <summary>
/// Tests for LMSupply service registration and DI configuration
/// </summary>
public class LMSupplyServiceCollectionExtensionsTests
{
    #region Text Completion Service Tests

    [Fact]
    public void AddLMSupplyTextCompletion_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLMSupplyTextCompletion();

        // Assert
        var provider = services.BuildServiceProvider();
        var textCompletionService = provider.GetService<ITextCompletionService>();
        Assert.NotNull(textCompletionService);
        Assert.IsType<LMSupplyTextCompletionService>(textCompletionService);
    }

    [Fact]
    public void AddLMSupplyTextCompletion_WithModelId_RegistersWithCorrectModel()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLMSupplyTextCompletion("fast");

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<IOptions<LMSupplyTextCompletionOptions>>();
        Assert.NotNull(options);
        Assert.Equal("fast", options.Value.ModelId);
    }

    [Fact]
    public void AddLMSupplyTextCompletion_WithConfigureOptions_AppliesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLMSupplyTextCompletion(options =>
        {
            options.ModelId = "quality";
            options.MaxContextLength = 8192;
            options.TopP = 0.95f;
            options.TopK = 100;
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<IOptions<LMSupplyTextCompletionOptions>>();
        Assert.NotNull(options);
        Assert.Equal("quality", options.Value.ModelId);
        Assert.Equal(8192, options.Value.MaxContextLength);
        Assert.Equal(0.95f, options.Value.TopP);
        Assert.Equal(100, options.Value.TopK);
    }

    #endregion

    #region Embedding Service Tests

    [Fact]
    public void AddLMSupplyEmbedding_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLMSupplyEmbedding();

        // Assert
        var provider = services.BuildServiceProvider();
        var embeddingService = provider.GetService<IEmbeddingService>();
        Assert.NotNull(embeddingService);
        Assert.IsType<LMSupplyEmbeddingService>(embeddingService);
    }

    [Fact]
    public void AddLMSupplyEmbedding_WithModelId_RegistersWithCorrectModel()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLMSupplyEmbedding("multilingual");

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<IOptions<LMSupplyEmbeddingOptions>>();
        Assert.NotNull(options);
        Assert.Equal("multilingual", options.Value.ModelId);
    }

    [Fact]
    public void AddLMSupplyEmbedding_DefaultOptions_HasCorrectDefaults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLMSupplyEmbedding();

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<IOptions<LMSupplyEmbeddingOptions>>()?.Value;
        Assert.NotNull(options);
        Assert.Equal("default", options.ModelId);
        Assert.Equal(LMSupplyExecutionProvider.Auto, options.ExecutionProvider);
        Assert.Equal(512, options.MaxSequenceLength);
        Assert.True(options.NormalizeEmbeddings);
        Assert.False(options.WarmupOnStartup);
    }

    #endregion

    #region Reranker Service Tests

    [Fact]
    public void AddLMSupplyReranker_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLMSupplyReranker();

        // Assert
        var provider = services.BuildServiceProvider();
        var reranker = provider.GetService<IReranker>();
        Assert.NotNull(reranker);
        Assert.IsType<LMSupplyRerankerAdapter>(reranker);
    }

    [Fact]
    public void AddResilientLMSupplyReranker_RegistersResilientService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddResilientLMSupplyReranker();

        // Assert
        var provider = services.BuildServiceProvider();
        var reranker = provider.GetService<IReranker>();
        Assert.NotNull(reranker);
        Assert.IsType<ResilientLMSupplyReranker>(reranker);
    }

    #endregion

    #region Combined Services Tests

    [Fact]
    public void AddLMSupply_RegistersEmbeddingAndReranker()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLMSupply();

        // Assert
        var provider = services.BuildServiceProvider();

        var embeddingService = provider.GetService<IEmbeddingService>();
        Assert.NotNull(embeddingService);
        Assert.IsType<LMSupplyEmbeddingService>(embeddingService);

        var reranker = provider.GetService<IReranker>();
        Assert.NotNull(reranker);
        Assert.IsType<ResilientLMSupplyReranker>(reranker);
    }

    #endregion
}
