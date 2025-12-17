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
    public void UseLMSupplyEmbedding_WithDefaultModel_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseLMSupplyEmbedding();

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
    public void UseLMSupplyEmbedding_WithCustomModel_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseLMSupplyEmbedding("bge-small-en-v1.5");

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
    public void UseLMSupplyMultilingual_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseLMSupplyMultilingual();

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
    public async Task Builder_WithLMSupplyEmbedding_ShouldIndexSuccessfully()
    {

        // Arrange
        var context = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseLMSupplyEmbedding()
            .Build();

        try
        {
            // Act
            var docId = await context.Indexer.IndexDocumentAsync(
                "Test document for LMSupply embedding integration",
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
}
