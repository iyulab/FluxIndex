using FluentAssertions;
using FluxIndex.Extensions.FileVault.Domain.ValueObjects;
using Xunit;

namespace FluxIndex.Extensions.FileVault.Tests.Domain;

public class ContentHashTests
{
    [Fact]
    public void FromHex_ValidHexString_CreatesHash()
    {
        // Arrange
        var hexValue = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"; // SHA256 of empty string

        // Act
        var hash = ContentHash.FromHex(hexValue);

        // Assert
        hash.Value.Should().Be(hexValue.ToLowerInvariant());
    }

    [Fact]
    public void FromHex_InvalidLength_ThrowsArgumentException()
    {
        // Arrange
        var shortHex = "e3b0c44298fc1c149afbf4c899";

        // Act & Assert
        var act = () => ContentHash.FromHex(shortHex);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*64 hex characters*");
    }

    [Fact]
    public void FromBytes_Valid32Bytes_CreatesHash()
    {
        // Arrange
        var bytes = new byte[32];
        bytes[0] = 0xAB;
        bytes[31] = 0xCD;

        // Act
        var hash = ContentHash.FromBytes(bytes);

        // Assert
        hash.Value.Should().StartWith("ab");
        hash.Value.Should().EndWith("cd");
        hash.Value.Length.Should().Be(64);
    }

    [Fact]
    public void FromBytes_InvalidLength_ThrowsArgumentException()
    {
        // Arrange
        var bytes = new byte[16];

        // Act & Assert
        var act = () => ContentHash.FromBytes(bytes);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*32 bytes*");
    }

    [Fact]
    public void Empty_ReturnsAllZeroHash()
    {
        // Act
        var empty = ContentHash.Empty;

        // Assert
        empty.Value.Should().Be(new string('0', 64));
        empty.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void IsEmpty_NonEmptyHash_ReturnsFalse()
    {
        // Arrange
        var hash = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

        // Act & Assert
        hash.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Equals_SameValue_ReturnsTrue()
    {
        // Arrange
        var hash1 = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        var hash2 = ContentHash.FromHex("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");

        // Act & Assert
        hash1.Equals(hash2).Should().BeTrue();
    }
}
