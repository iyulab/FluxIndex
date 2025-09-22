using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests.Extensions;

/// <summary>
/// Tests for SQLite service collection extensions
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSQLiteVectorStore_WithConfiguration_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SQLite:DatabasePath"] = "test.db",
                ["SQLite:UseInMemory"] = "false",
                ["SQLite:AutoMigrate"] = "true",
                ["SQLite:CommandTimeout"] = "60"
            })
            .Build();

        // Act
        services.AddSQLiteVectorStore(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var vectorStore = serviceProvider.GetService<IVectorStore>();
        var dbContext = serviceProvider.GetService<SQLiteDbContext>();
        var options = serviceProvider.GetService<SQLiteOptions>();

        Assert.NotNull(vectorStore);
        Assert.NotNull(dbContext);
        Assert.NotNull(options);
        Assert.IsType<SQLiteVectorStore>(vectorStore);
    }

    [Fact]
    public void AddSQLiteVectorStore_WithOptions_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddLogging();

        var options = new SQLiteOptions
        {
            DatabasePath = "custom.db",
            UseInMemory = false,
            AutoMigrate = true,
            DefaultSearchThreshold = 0.8,
            BatchSize = 200,
            CommandTimeout = 45
        };

        // Act
        services.AddSQLiteVectorStore(options);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var vectorStore = serviceProvider.GetService<IVectorStore>();
        var dbContext = serviceProvider.GetService<SQLiteDbContext>();
        var registeredOptions = serviceProvider.GetService<SQLiteOptions>();

        Assert.NotNull(vectorStore);
        Assert.NotNull(dbContext);
        Assert.NotNull(registeredOptions);

        Assert.IsType<SQLiteVectorStore>(vectorStore);
        Assert.Equal("custom.db", registeredOptions.DatabasePath);
        Assert.False(registeredOptions.UseInMemory);
        Assert.True(registeredOptions.AutoMigrate);
        Assert.Equal(0.8, registeredOptions.DefaultSearchThreshold);
        Assert.Equal(200, registeredOptions.BatchSize);
        Assert.Equal(45, registeredOptions.CommandTimeout);
    }

    [Fact]
    public void AddSQLiteVectorStore_WithDatabasePath_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddLogging();

        // Act
        services.AddSQLiteVectorStore("mytest.db");
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var vectorStore = serviceProvider.GetService<IVectorStore>();
        var options = serviceProvider.GetService<SQLiteOptions>();

        Assert.NotNull(vectorStore);
        Assert.NotNull(options);
        Assert.Equal("mytest.db", options.DatabasePath);
        Assert.True(options.AutoMigrate);
    }

    [Fact]
    public void AddSQLiteVectorStore_WithDefaultPath_ShouldUseDefault()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddLogging();

        // Act
        services.AddSQLiteVectorStore();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var options = serviceProvider.GetService<SQLiteOptions>();
        Assert.NotNull(options);
        Assert.Equal("fluxindex.db", options.DatabasePath);
    }

    [Fact]
    public void AddSQLiteInMemoryVectorStore_ShouldConfigureForMemory()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddLogging();

        // Act
        services.AddSQLiteInMemoryVectorStore();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var vectorStore = serviceProvider.GetService<IVectorStore>();
        var options = serviceProvider.GetService<SQLiteOptions>();

        Assert.NotNull(vectorStore);
        Assert.NotNull(options);
        Assert.True(options.UseInMemory);
        Assert.True(options.AutoMigrate);
        Assert.Equal("Data Source=:memory:", options.GetConnectionString());
    }

    [Fact]
    public void AddSQLiteVectorStore_WithActionConfiguration_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddLogging();

        // Act
        services.AddSQLiteVectorStore(options =>
        {
            options.DatabasePath = "action_test.db";
            options.UseInMemory = false;
            options.DefaultSearchThreshold = 0.9;
            options.BatchSize = 150;
            options.EnableVectorCache = false;
            options.VectorCacheSize = 500;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var vectorStore = serviceProvider.GetService<IVectorStore>();
        var optionsService = serviceProvider.GetService<IOptions<SQLiteOptions>>();

        Assert.NotNull(vectorStore);
        Assert.NotNull(optionsService);

        var options = optionsService.Value;
        Assert.Equal("action_test.db", options.DatabasePath);
        Assert.False(options.UseInMemory);
        Assert.Equal(0.9, options.DefaultSearchThreshold);
        Assert.Equal(150, options.BatchSize);
        Assert.False(options.EnableVectorCache);
        Assert.Equal(500, options.VectorCacheSize);
    }

    [Fact]
    public void AddSQLiteVectorStore_WithAutoMigrate_ShouldRegisterMigrationService()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddLogging();

        // Act
        services.AddSQLiteVectorStore(options =>
        {
            options.AutoMigrate = true;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var hostedServices = serviceProvider.GetServices<IHostedService>();
        Assert.Contains(hostedServices, service => service.GetType().Name.Contains("SQLiteMigrationService"));
    }

    [Fact]
    public void AddSQLiteVectorStore_ShouldRegisterScopedServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddLogging();

        // Act
        services.AddSQLiteInMemoryVectorStore();
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