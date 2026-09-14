using FluxIndex.Storage.SQLite;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// Tests for SQLite options configuration
/// </summary>
[Collection("SQLite Tests")]
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
        Assert.True(options.AutoMigrate);
        Assert.Equal(30, options.CommandTimeout);
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
    public void GetConnectionString_WithInMemory_ShouldReturnSharedMemoryString()
    {
        // Arrange
        var options = new SQLiteOptions
        {
            UseInMemory = true,
            DatabasePath = "test.db"  // 기본값 사용 시 "fluxindex" 이름 사용됨
        };

        // Act
        var connectionString = options.GetConnectionString();

        // Assert - 공유 인메모리 데이터베이스 형식 확인
        Assert.Equal("Data Source=file:test?mode=memory&cache=shared", connectionString);
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

    [Fact]
    public void AllProperties_ShouldBeSettable()
    {
        // Arrange
        var options = new SQLiteOptions();

        // Act
        options.DatabasePath = "all_props.db";
        options.UseInMemory = false;
        options.AutoMigrate = false;
        options.CommandTimeout = 45;

        // Assert
        Assert.Equal("all_props.db", options.DatabasePath);
        Assert.False(options.UseInMemory);
        Assert.False(options.AutoMigrate);
        Assert.Equal(45, options.CommandTimeout);
    }

    [Theory]
    [InlineData("database1.db", false, "Data Source=database1.db")]
    [InlineData("database2.db", false, "Data Source=database2.db")]
    [InlineData("any_path.db", true, "Data Source=file:any_path?mode=memory&cache=shared")]
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