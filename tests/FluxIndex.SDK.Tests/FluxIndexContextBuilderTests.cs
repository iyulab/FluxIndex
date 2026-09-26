using Xunit;
using FluxIndex.SDK;
using FluxIndex.Providers.LMSupply.Extensions;
using FluxIndex.Providers.LMSupply.Services;
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

    // Loads a real LMSupply model (download on a cold cache). Before 0.52.1 the builder replaced this registration
    // with the in-memory embedder, so the test passed without ever loading LMSupply — and the alias it named was gone.
    // The provider's registration is lazy, so the test embeds once: resolving alone would not prove the alias loads.
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConfigureServices_WithLMSupplyEmbedding_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .AddSQLiteStorage()
            .ConfigureServices(s => s.AddLMSupplyEmbedding());

        var context = builder.Build();
        try
        {
            // Assert: the registered LMSupply embedder is the one resolved, and its model loads
            Assert.NotNull(context);
            var embedder = Assert.IsType<LMSupplyEmbeddingService>(
                context.ServiceProvider.GetService(typeof(FluxIndex.Core.Application.Interfaces.IEmbeddingService)));
            var vector = await embedder.GenerateEmbeddingAsync("alias check", TestContext.Current.CancellationToken);
            Assert.NotEmpty(vector);
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    // Loads a real LMSupply model (download on a cold cache). Before 0.52.1 the builder replaced this registration
    // with the in-memory embedder, so the test passed without ever loading LMSupply — and the alias it named was gone.
    // The provider's registration is lazy, so the test embeds once: resolving alone would not prove the alias loads.
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConfigureServices_WithLMSupplyCustomModel_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .AddSQLiteStorage()
            .ConfigureServices(s => s.AddLMSupplyEmbedding("fast"));

        var context = builder.Build();
        try
        {
            // Assert: the registered LMSupply embedder is the one resolved, and its model loads
            Assert.NotNull(context);
            var embedder = Assert.IsType<LMSupplyEmbeddingService>(
                context.ServiceProvider.GetService(typeof(FluxIndex.Core.Application.Interfaces.IEmbeddingService)));
            var vector = await embedder.GenerateEmbeddingAsync("alias check", TestContext.Current.CancellationToken);
            Assert.NotEmpty(vector);
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    // Loads a real LMSupply model (download on a cold cache). Before 0.52.1 the builder replaced this registration
    // with the in-memory embedder, so the test passed without ever loading LMSupply — and the alias it named was gone.
    // The provider's registration is lazy, so the test embeds once: resolving alone would not prove the alias loads.
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConfigureServices_WithLMSupplyMultilingual_ShouldConfigure()
    {
        // Act
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .AddSQLiteStorage()
            .ConfigureServices(s => s.AddLMSupplyEmbedding("multilingual-e5-small"));

        var context = builder.Build();
        try
        {
            // Assert: the registered LMSupply embedder is the one resolved, and its model loads
            Assert.NotNull(context);
            var embedder = Assert.IsType<LMSupplyEmbeddingService>(
                context.ServiceProvider.GetService(typeof(FluxIndex.Core.Application.Interfaces.IEmbeddingService)));
            var vector = await embedder.GenerateEmbeddingAsync("alias check", TestContext.Current.CancellationToken);
            Assert.NotEmpty(vector);
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
    [Trait("Category", "Integration")]
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
            var docId = await context.Indexer.IndexDocumentAsync("Test document for LMSupply embedding integration", "test-doc-001", cancellationToken: TestContext.Current.CancellationToken);

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
