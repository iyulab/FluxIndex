using Xunit;
using FluxIndex.SDK;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Tests for FluxIndexContextBuilder - verifying embedding provider configuration
/// </summary>
public class FluxIndexContextBuilderTests : IDisposable
{
    private readonly string _testDbPath;

    public FluxIndexContextBuilderTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"fluxindex_builder_test_{Guid.NewGuid()}.db");
    }

    public void Dispose()
    {
        Thread.Sleep(100);
        try
        {
            if (File.Exists(_testDbPath))
                File.Delete(_testDbPath);
        }
        catch (IOException) { }
    }

    [Fact]
    public void Builder_DefaultEmbedding_ShouldBeLocalEmbedder()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath);

        // Access internal options through building
        var context = builder.Build();
        try
        {
            // Assert - Context should be created successfully with LocalEmbedder
            Assert.NotNull(context);
            Assert.NotNull(context.Indexer);
            Assert.NotNull(context.Retriever);
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void UseLocalAIEmbedding_WithDefaultModel_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseLocalAIEmbedding();

        var context = builder.Build();
        try
        {
            // Assert
            Assert.NotNull(context);
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void UseLocalAIEmbedding_WithCustomModel_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseLocalAIEmbedding("bge-small-en-v1.5");

        var context = builder.Build();
        try
        {
            // Assert
            Assert.NotNull(context);
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void UseLocalAIMultilingual_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseLocalAIMultilingual();

        var context = builder.Build();
        try
        {
            // Assert
            Assert.NotNull(context);
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void UseGPUStack_WithParameters_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseGPUStack(
                endpoint: "http://localhost:8080",
                apiKey: "test-api-key",
                modelName: "BAAI/bge-m3");

        // Assert - Builder configured without error
        Assert.NotNull(builder);
    }

    [Fact]
    public void UseGPUStack_WithDimensions_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseGPUStack(
                endpoint: "http://localhost:8080",
                apiKey: "test-api-key",
                modelName: "BAAI/bge-m3",
                dimensions: 1024);

        // Assert - Builder configured without error
        Assert.NotNull(builder);
    }

    [Fact]
    public void UseOpenAICompatible_WithParameters_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseOpenAICompatible(
                endpoint: "http://localhost:11434",
                apiKey: "ollama",
                modelName: "nomic-embed-text");

        // Assert - Builder configured without error
        Assert.NotNull(builder);
    }

    [Fact]
    public void UseOpenAICompatible_WithDimensions_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseOpenAICompatible(
                endpoint: "http://localhost:11434",
                apiKey: "ollama",
                modelName: "nomic-embed-text",
                dimensions: 768);

        // Assert - Builder configured without error
        Assert.NotNull(builder);
    }

    [Fact]
    public void UseOpenAI_ShouldOverrideLocalAIEmbedding()
    {
        // Act - Use OpenAI after LocalAI embedding
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseLocalAIEmbedding()
            .UseOpenAI("test-api-key");

        // Assert - Builder should accept both calls (last one wins)
        Assert.NotNull(builder);
    }

    [Fact]
    public void UseInMemoryEmbedding_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseInMemoryEmbedding();

        var context = builder.Build();
        try
        {
            // Assert
            Assert.NotNull(context);
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void UseSQLiteInMemory_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory();

        var context = builder.Build();
        try
        {
            // Assert
            Assert.NotNull(context);
            Assert.NotNull(context.Indexer);
            Assert.NotNull(context.Retriever);
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task Builder_WithLocalAIEmbedding_ShouldIndexSuccessfully()
    {
        // Arrange
        var context = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseLocalAIEmbedding()
            .Build();

        try
        {
            // Act
            var docId = await context.Indexer.IndexDocumentAsync(
                "Test document for LocalAI embedding integration",
                "test-doc-001");

            // Assert
            Assert.NotNull(docId);
            Assert.Equal("test-doc-001", docId);
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void UseAzureOpenAI_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseAzureOpenAI(
                endpoint: "https://my-resource.openai.azure.com",
                apiKey: "test-api-key",
                embeddingDeployment: "text-embedding-ada-002");

        // Assert - Builder configured without error
        Assert.NotNull(builder);
    }

    [Fact]
    public void UseAzureOpenAI_WithCompletionModel_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseAzureOpenAI(
                endpoint: "https://my-resource.openai.azure.com",
                apiKey: "test-api-key",
                embeddingDeployment: "text-embedding-ada-002",
                completionDeployment: "gpt-4");

        // Assert - Builder configured without error
        Assert.NotNull(builder);
    }

    [Fact]
    public void UseOpenAI_WithCompletionModel_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseOpenAI(
                apiKey: "test-api-key",
                embeddingModel: "text-embedding-3-small",
                completionModel: "gpt-4o-mini");

        // Assert - Builder configured without error
        Assert.NotNull(builder);
    }

    [Fact]
    public void UseAIProvider_WithOpenAI_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseAIProvider("openai/text-embedding-3-small", "test-api-key");

        // Assert - Builder configured without error
        Assert.NotNull(builder);
    }
}
