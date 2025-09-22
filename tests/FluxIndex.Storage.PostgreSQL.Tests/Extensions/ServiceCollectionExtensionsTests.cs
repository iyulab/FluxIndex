using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Storage.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests.Extensions;

/// <summary>
/// Tests for PostgreSQL service collection extensions
/// </summary>
public class ServiceCollectionExtensionsTests
{
    private const string TestConnectionString = "Host=localhost;Database=test_db;Username=test;Password=test;";

    [Fact]
    public void AddPostgreSQLVectorStore_WithConfiguration_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddLogging();

        // Act
        services.AddPostgreSQLVectorStore(options =>
        {
            options.ConnectionString = TestConnectionString;
            options.EmbeddingDimensions = 384;
            options.AutoMigrate = false;
            options.CommandTimeout = 60;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var vectorStore = serviceProvider.GetService<IVectorStore>();
        var dbContext = serviceProvider.GetService<FluxIndexDbContext>();
        var options = serviceProvider.GetService<IOptions<PostgreSQLOptions>>();

        Assert.NotNull(vectorStore);
        Assert.NotNull(dbContext);
        Assert.NotNull(options);

        Assert.IsType<PostgreSQLVectorStore>(vectorStore);
        Assert.Equal(TestConnectionString, options.Value.ConnectionString);
        Assert.Equal(384, options.Value.EmbeddingDimensions);
        Assert.False(options.Value.AutoMigrate);
        Assert.Equal(60, options.Value.CommandTimeout);
    }

    [Fact]
    public void AddPostgreSQLVectorStore_WithConnectionString_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddLogging();

        // Act
        services.AddPostgreSQLVectorStore(TestConnectionString, 512);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var vectorStore = serviceProvider.GetService<IVectorStore>();
        var options = serviceProvider.GetService<IOptions<PostgreSQLOptions>>();

        Assert.NotNull(vectorStore);
        Assert.NotNull(options);

        Assert.IsType<PostgreSQLVectorStore>(vectorStore);
        Assert.Equal(TestConnectionString, options.Value.ConnectionString);
        Assert.Equal(512, options.Value.EmbeddingDimensions);
    }

    [Fact]
    public void AddPostgreSQLVectorStore_WithDefaultDimensions_ShouldUse1536()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddLogging();

        // Act
        services.AddPostgreSQLVectorStore(TestConnectionString);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var options = serviceProvider.GetService<IOptions<PostgreSQLOptions>>();
        Assert.NotNull(options);
        Assert.Equal(1536, options.Value.EmbeddingDimensions);
    }

    [Fact]
    public void AddPostgreSQLVectorStore_ShouldConfigureDbContextCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddLogging();

        // Act
        services.AddPostgreSQLVectorStore(options =>
        {
            options.ConnectionString = TestConnectionString;
            options.CommandTimeout = 45;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var dbContext = serviceProvider.GetService<FluxIndexDbContext>();
        Assert.NotNull(dbContext);

        // Verify that the DbContext is configured with the correct connection string
        var connectionString = dbContext.Database.GetConnectionString();
        Assert.Equal(TestConnectionString, connectionString);
    }

    [Fact]
    public void AddPostgreSQLVectorStore_ShouldRegisterScopedServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddLogging();

        // Act
        services.AddPostgreSQLVectorStore(TestConnectionString);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var vectorStore1 = serviceProvider.GetService<IVectorStore>();
        var vectorStore2 = serviceProvider.GetService<IVectorStore>();

        // In the same scope, should get the same instance
        Assert.Same(vectorStore1, vectorStore2);

        // In different scopes, should get different instances
        using var scope = serviceProvider.CreateScope();
        var vectorStore3 = scope.ServiceProvider.GetService<IVectorStore>();
        Assert.NotSame(vectorStore1, vectorStore3);
    }
}