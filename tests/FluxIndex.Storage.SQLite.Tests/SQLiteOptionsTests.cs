using FluxIndex.Storage.SQLite;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// Tests for SQLite options configuration
/// </summary>
public class SQLiteOptionsTests
{
    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        // Act
        var options = new SQLiteOptions();

        // Assert
        Assert.Equal("fluxindex.db", options.DatabasePath);
        Assert.False(options.UseInMemory);
        Assert.False(options.AllowDuplicates);
        Assert.True(options.AutoMigrate);
        Assert.Equal(0.7, options.DefaultSearchThreshold);
        Assert.Equal(0.5, options.DefaultVectorWeight);
        Assert.Equal(100, options.BatchSize);
        Assert.Equal(30, options.CommandTimeout);
        Assert.True(options.EnableVectorCache);
        Assert.Equal(1000, options.VectorCacheSize);
    }

    [Fact]
    public void GetConnectionString_WithFilePath_ShouldReturnCorrectString()
    {
        // Arrange
        var options = new SQLiteOptions
        {
            DatabasePath = "test.db",
            UseInMemory = false
        };

        // Act
        var connectionString = options.GetConnectionString();

        // Assert
        Assert.Equal("Data Source=test.db", connectionString);
    }

    [Fact]
    public void GetConnectionString_WithInMemory_ShouldReturnMemoryString()
    {
        // Arrange
        var options = new SQLiteOptions
        {
            UseInMemory = true
        };

        // Act
        var connectionString = options.GetConnectionString();

        // Assert
        Assert.Equal("Data Source=:memory:", connectionString);
    }

    [Fact]
    public void SetDatabasePath_ShouldUpdateProperty()
    {
        // Arrange
        var options = new SQLiteOptions();
        var path = "custom_database.db";

        // Act
        options.DatabasePath = path;

        // Assert
        Assert.Equal(path, options.DatabasePath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetUseInMemory_ShouldUpdateProperty(bool useInMemory)
    {
        // Arrange
        var options = new SQLiteOptions();

        // Act
        options.UseInMemory = useInMemory;

        // Assert
        Assert.Equal(useInMemory, options.UseInMemory);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetAllowDuplicates_ShouldUpdateProperty(bool allowDuplicates)
    {
        // Arrange
        var options = new SQLiteOptions();

        // Act
        options.AllowDuplicates = allowDuplicates;

        // Assert
        Assert.Equal(allowDuplicates, options.AllowDuplicates);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetAutoMigrate_ShouldUpdateProperty(bool autoMigrate)
    {
        // Arrange
        var options = new SQLiteOptions();

        // Act
        options.AutoMigrate = autoMigrate;

        // Assert
        Assert.Equal(autoMigrate, options.AutoMigrate);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.7)]
    [InlineData(0.9)]
    public void SetDefaultSearchThreshold_ShouldUpdateProperty(double threshold)
    {
        // Arrange
        var options = new SQLiteOptions();

        // Act
        options.DefaultSearchThreshold = threshold;

        // Assert
        Assert.Equal(threshold, options.DefaultSearchThreshold);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.7)]
    [InlineData(1.0)]
    public void SetDefaultVectorWeight_ShouldUpdateProperty(double weight)
    {
        // Arrange
        var options = new SQLiteOptions();

        // Act
        options.DefaultVectorWeight = weight;

        // Assert
        Assert.Equal(weight, options.DefaultVectorWeight);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(500)]
    public void SetBatchSize_ShouldUpdateProperty(int batchSize)
    {
        // Arrange
        var options = new SQLiteOptions();

        // Act
        options.BatchSize = batchSize;

        // Assert
        Assert.Equal(batchSize, options.BatchSize);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void SetCommandTimeout_ShouldUpdateProperty(int timeout)
    {
        // Arrange
        var options = new SQLiteOptions();

        // Act
        options.CommandTimeout = timeout;

        // Assert
        Assert.Equal(timeout, options.CommandTimeout);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetEnableVectorCache_ShouldUpdateProperty(bool enableCache)
    {
        // Arrange
        var options = new SQLiteOptions();

        // Act
        options.EnableVectorCache = enableCache;

        // Assert
        Assert.Equal(enableCache, options.EnableVectorCache);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2000)]
    [InlineData(5000)]
    public void SetVectorCacheSize_ShouldUpdateProperty(int cacheSize)
    {
        // Arrange
        var options = new SQLiteOptions();

        // Act
        options.VectorCacheSize = cacheSize;

        // Assert
        Assert.Equal(cacheSize, options.VectorCacheSize);
    }

    [Fact]
    public void AllProperties_ShouldBeSettable()
    {
        // Arrange
        var options = new SQLiteOptions();

        // Act
        options.DatabasePath = "all_props.db";
        options.UseInMemory = false;
        options.AllowDuplicates = true;
        options.AutoMigrate = false;
        options.DefaultSearchThreshold = 0.8;
        options.DefaultVectorWeight = 0.6;
        options.BatchSize = 250;
        options.CommandTimeout = 45;
        options.EnableVectorCache = false;
        options.VectorCacheSize = 2000;

        // Assert
        Assert.Equal("all_props.db", options.DatabasePath);
        Assert.False(options.UseInMemory);
        Assert.True(options.AllowDuplicates);
        Assert.False(options.AutoMigrate);
        Assert.Equal(0.8, options.DefaultSearchThreshold);
        Assert.Equal(0.6, options.DefaultVectorWeight);
        Assert.Equal(250, options.BatchSize);
        Assert.Equal(45, options.CommandTimeout);
        Assert.False(options.EnableVectorCache);
        Assert.Equal(2000, options.VectorCacheSize);
    }

    [Theory]
    [InlineData("database1.db", false, "Data Source=database1.db")]
    [InlineData("database2.db", false, "Data Source=database2.db")]
    [InlineData("any_path.db", true, "Data Source=:memory:")]
    public void GetConnectionString_WithDifferentPaths_ShouldReturnCorrectString(string path, bool useInMemory, string expected)
    {
        // Arrange
        var options = new SQLiteOptions
        {
            DatabasePath = path,
            UseInMemory = useInMemory
        };

        // Act
        var connectionString = options.GetConnectionString();

        // Assert
        Assert.Equal(expected, connectionString);
    }
}