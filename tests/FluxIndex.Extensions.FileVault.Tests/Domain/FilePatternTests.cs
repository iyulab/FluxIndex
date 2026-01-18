using FluentAssertions;
using FluxIndex.Extensions.FileVault.Domain.ValueObjects;
using Xunit;

namespace FluxIndex.Extensions.FileVault.Tests.Domain;

public class FilePatternTests
{
    [Theory]
    [InlineData("*.pdf", "document.pdf", true)]
    [InlineData("*.pdf", "document.PDF", true)]
    [InlineData("*.pdf", "document.docx", false)]
    [InlineData("*.txt", "readme.txt", true)]
    [InlineData("~$*", "~$temp.docx", true)]
    [InlineData("~$*", "normal.docx", false)]
    public void IsMatch_SimpleWildcard_MatchesCorrectly(string pattern, string fileName, bool expected)
    {
        // Arrange
        var filePattern = FilePattern.FromGlob(pattern);

        // Act
        var result = filePattern.IsMatch(fileName);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("doc?.txt", "doc1.txt", true)]
    [InlineData("doc?.txt", "docA.txt", true)]
    [InlineData("doc?.txt", "doc12.txt", false)]
    [InlineData("file?.pdf", "file.pdf", false)]
    public void IsMatch_SingleCharWildcard_MatchesCorrectly(string pattern, string fileName, bool expected)
    {
        // Arrange
        var filePattern = FilePattern.FromGlob(pattern);

        // Act
        var result = filePattern.IsMatch(fileName);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("**/*.pdf", "docs/report.pdf", true)]
    [InlineData("**/*.pdf", "a/b/c/deep.pdf", true)]
    [InlineData("src/**/*.cs", "src/services/file.cs", true)]
    [InlineData("src/**/*.cs", "test/file.cs", false)]
    public void IsMatch_DirectoryWildcard_MatchesCorrectly(string pattern, string filePath, bool expected)
    {
        // Arrange
        var filePattern = FilePattern.FromGlob(pattern);

        // Act
        var result = filePattern.IsMatch(filePath);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void IsMatch_EmptyPath_ReturnsFalse()
    {
        // Arrange
        var pattern = FilePattern.FromGlob("*.txt");

        // Act & Assert
        pattern.IsMatch("").Should().BeFalse();
        pattern.IsMatch(null!).Should().BeFalse();
    }

    [Fact]
    public void ToString_ReturnsOriginalPattern()
    {
        // Arrange
        var originalPattern = "*.pdf";
        var pattern = FilePattern.FromGlob(originalPattern);

        // Act & Assert
        pattern.ToString().Should().Be(originalPattern);
    }

    [Fact]
    public void Equals_SamePattern_ReturnsTrue()
    {
        // Arrange
        var pattern1 = FilePattern.FromGlob("*.pdf");
        var pattern2 = FilePattern.FromGlob("*.PDF");

        // Act & Assert
        pattern1.Equals(pattern2).Should().BeTrue();
    }
}
