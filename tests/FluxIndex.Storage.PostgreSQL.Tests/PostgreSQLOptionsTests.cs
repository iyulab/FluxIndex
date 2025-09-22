using FluxIndex.Storage.PostgreSQL;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Tests for PostgreSQL options configuration
/// </summary>
public class PostgreSQLOptionsTests
{
    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        // Act
        var options = new PostgreSQLOptions();

        // Assert
        Assert.Equal(string.Empty, options.ConnectionString);
        Assert.Equal(1536, options.EmbeddingDimensions);
        Assert.True(options.AutoMigrate);
        Assert.Equal(30, options.CommandTimeout);
    }

    [Fact]
    public void SetConnectionString_ShouldUpdateProperty()
    {
        // Arrange
        var options = new PostgreSQLOptions();
        var connectionString = "Host=localhost;Database=fluxindex;Username=user;Password=pass;";

        // Act
        options.ConnectionString = connectionString;

        // Assert
        Assert.Equal(connectionString, options.ConnectionString);
    }

    [Theory]
    [InlineData(384)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(1536)]
    [InlineData(3072)]
    public void SetEmbeddingDimensions_ShouldUpdateProperty(int dimensions)
    {
        // Arrange
        var options = new PostgreSQLOptions();

        // Act
        options.EmbeddingDimensions = dimensions;

        // Assert
        Assert.Equal(dimensions, options.EmbeddingDimensions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetAutoMigrate_ShouldUpdateProperty(bool autoMigrate)
    {
        // Arrange
        var options = new PostgreSQLOptions();

        // Act
        options.AutoMigrate = autoMigrate;

        // Assert
        Assert.Equal(autoMigrate, options.AutoMigrate);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void SetCommandTimeout_ShouldUpdateProperty(int timeout)
    {
        // Arrange
        var options = new PostgreSQLOptions();

        // Act
        options.CommandTimeout = timeout;

        // Assert
        Assert.Equal(timeout, options.CommandTimeout);
    }

    [Fact]
    public void AllProperties_ShouldBeSettable()
    {
        // Arrange
        var options = new PostgreSQLOptions();
        var connectionString = "Host=test;Database=test;Username=test;Password=test;";

        // Act
        options.ConnectionString = connectionString;
        options.EmbeddingDimensions = 768;
        options.AutoMigrate = false;
        options.CommandTimeout = 45;

        // Assert
        Assert.Equal(connectionString, options.ConnectionString);
        Assert.Equal(768, options.EmbeddingDimensions);
        Assert.False(options.AutoMigrate);
        Assert.Equal(45, options.CommandTimeout);
    }
}