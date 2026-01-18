using FluentAssertions;
using FluxIndex.Extensions.FileVault.Services;
using Xunit;

namespace FluxIndex.Extensions.FileVault.Tests.Services;

public class ContentHasherTests
{
    private readonly ContentHasher _hasher = new();

    [Fact]
    public async Task ComputeHashAsync_FromFile_ReturnsValidHash()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "Hello, World!");

        try
        {
            // Act
            var hash = await _hasher.ComputeHashAsync(tempFile);

            // Assert
            hash.Value.Should().HaveLength(64);
            hash.IsEmpty.Should().BeFalse();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ComputeHashAsync_SameContent_ReturnsSameHash()
    {
        // Arrange
        var content = "Test content for hashing";
        var tempFile1 = Path.GetTempFileName();
        var tempFile2 = Path.GetTempFileName();

        await File.WriteAllTextAsync(tempFile1, content);
        await File.WriteAllTextAsync(tempFile2, content);

        try
        {
            // Act
            var hash1 = await _hasher.ComputeHashAsync(tempFile1);
            var hash2 = await _hasher.ComputeHashAsync(tempFile2);

            // Assert
            hash1.Should().Be(hash2);
        }
        finally
        {
            File.Delete(tempFile1);
            File.Delete(tempFile2);
        }
    }

    [Fact]
    public async Task ComputeHashAsync_DifferentContent_ReturnsDifferentHash()
    {
        // Arrange
        var tempFile1 = Path.GetTempFileName();
        var tempFile2 = Path.GetTempFileName();

        await File.WriteAllTextAsync(tempFile1, "Content A");
        await File.WriteAllTextAsync(tempFile2, "Content B");

        try
        {
            // Act
            var hash1 = await _hasher.ComputeHashAsync(tempFile1);
            var hash2 = await _hasher.ComputeHashAsync(tempFile2);

            // Assert
            hash1.Should().NotBe(hash2);
        }
        finally
        {
            File.Delete(tempFile1);
            File.Delete(tempFile2);
        }
    }

    [Fact]
    public async Task ComputeHashAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _hasher.ComputeHashAsync(nonExistentPath));
    }

    [Fact]
    public void ComputeHash_FromString_ReturnsValidHash()
    {
        // Act
        var hash = _hasher.ComputeHash("Hello, World!");

        // Assert
        hash.Value.Should().HaveLength(64);
        hash.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void ComputeHash_SameString_ReturnsSameHash()
    {
        // Arrange
        var content = "Test content";

        // Act
        var hash1 = _hasher.ComputeHash(content);
        var hash2 = _hasher.ComputeHash(content);

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHash_EmptyString_ReturnsKnownHash()
    {
        // Known SHA256 hash of empty string
        var expectedHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        // Act
        var hash = _hasher.ComputeHash("");

        // Assert
        hash.Value.Should().Be(expectedHash);
    }

    [Fact]
    public void ComputeHash_FromBytes_ReturnsValidHash()
    {
        // Arrange
        var bytes = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"

        // Act
        var hash = _hasher.ComputeHash(bytes);

        // Assert
        hash.Value.Should().HaveLength(64);
        hash.IsEmpty.Should().BeFalse();
    }
}
