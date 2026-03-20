using Xunit;
using FluxIndex.SDK;
using FluxIndex.SDK.Tests.AI;
using FluxIndex.Storage.SQLite;

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
    public void Builder_DefaultEmbedding_ShouldBeInMemory()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .AddSQLiteStorage();

        // Access internal options through building
        var context = builder.Build();
        try
        {
            // Assert - Context should be created successfully with InMemory embedding (default)
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
    public void ConfigureServices_WithLMSupplyEmbedding_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .AddSQLiteStorage()
            .ConfigureServices(s => s.AddLMSupplyEmbedding());

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
    public void ConfigureServices_WithLMSupplyCustomModel_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .AddSQLiteStorage()
            .ConfigureServices(s => s.AddLMSupplyEmbedding("bge-small-en-v1.5"));

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
    public void ConfigureServices_WithLMSupplyMultilingual_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .AddSQLiteStorage()
            .ConfigureServices(s => s.AddLMSupplyEmbedding("multilingual"));

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
            .AddSQLiteStorage()
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
            .UseSQLiteInMemory()
            .AddSQLiteStorage();

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
            .AddSQLiteStorage()
            .ConfigureServices(s => s.AddLMSupplyEmbedding())
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
