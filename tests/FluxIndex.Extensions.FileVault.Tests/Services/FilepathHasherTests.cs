using FluentAssertions;
using FluxIndex.Extensions.FileVault.Services;
using Xunit;

namespace FluxIndex.Extensions.FileVault.Tests.Services;

public class FilepathHasherTests
{
    [Fact]
    public void ComputeHash_ShouldReturn16CharHex()
    {
        var hash = FilepathHasher.ComputeHash("/some/path/file.txt");

        hash.Should().HaveLength(16);
        hash.Should().MatchRegex("^[0-9a-f]{16}$");
    }

    [Fact]
    public void ComputeHash_Null_ShouldThrow()
    {
        var act = () => FilepathHasher.ComputeHash(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ComputeHash_SamePath_ShouldReturnConsistentHash()
    {
        var hash1 = FilepathHasher.ComputeHash("C:/docs/report.pdf");
        var hash2 = FilepathHasher.ComputeHash("C:/docs/report.pdf");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHash_DifferentPaths_ShouldReturnDifferentHashes()
    {
        var hash1 = FilepathHasher.ComputeHash("C:/docs/file1.pdf");
        var hash2 = FilepathHasher.ComputeHash("C:/docs/file2.pdf");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHash_BackslashAndForwardSlash_ShouldProduceSameHash()
    {
        var hash1 = FilepathHasher.ComputeHash("C:\\docs\\report.pdf");
        var hash2 = FilepathHasher.ComputeHash("C:/docs/report.pdf");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHash_CaseInsensitive_ShouldProduceSameHash()
    {
        var hash1 = FilepathHasher.ComputeHash("C:/Docs/Report.PDF");
        var hash2 = FilepathHasher.ComputeHash("C:/docs/report.pdf");

        hash1.Should().Be(hash2);
    }

    // NormalizePath tests

    [Fact]
    public void NormalizePath_Null_ShouldThrow()
    {
        var act = () => FilepathHasher.NormalizePath(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NormalizePath_ShouldConvertBackslashToForwardSlash()
    {
        var result = FilepathHasher.NormalizePath("C:\\docs\\file.txt");

        result.Should().NotContain("\\");
        result.Should().Contain("/");
    }

    [Fact]
    public void NormalizePath_ShouldBeLowercase()
    {
        var result = FilepathHasher.NormalizePath("C:/Docs/Report.PDF");

        result.Should().Be(result.ToLowerInvariant());
    }

    [Fact]
    public void NormalizePath_ShouldRemoveTrailingSlash()
    {
        var result = FilepathHasher.NormalizePath("C:/docs/");

        result.Should().NotEndWith("/");
    }

    // IsValidHash tests

    [Fact]
    public void IsValidHash_ValidHash_ShouldReturnTrue()
    {
        var hash = FilepathHasher.ComputeHash("C:/test/file.txt");

        FilepathHasher.IsValidHash(hash).Should().BeTrue();
    }

    [Fact]
    public void IsValidHash_Null_ShouldReturnFalse()
    {
        FilepathHasher.IsValidHash(null).Should().BeFalse();
    }

    [Fact]
    public void IsValidHash_Empty_ShouldReturnFalse()
    {
        FilepathHasher.IsValidHash("").Should().BeFalse();
    }

    [Fact]
    public void IsValidHash_TooShort_ShouldReturnFalse()
    {
        FilepathHasher.IsValidHash("abcdef").Should().BeFalse();
    }

    [Fact]
    public void IsValidHash_TooLong_ShouldReturnFalse()
    {
        FilepathHasher.IsValidHash("abcdef0123456789x").Should().BeFalse();
    }

    [Fact]
    public void IsValidHash_UppercaseHex_ShouldReturnFalse()
    {
        FilepathHasher.IsValidHash("ABCDEF0123456789").Should().BeFalse();
    }

    [Fact]
    public void IsValidHash_NonHexChars_ShouldReturnFalse()
    {
        FilepathHasher.IsValidHash("ghijklmnopqrstuv").Should().BeFalse();
    }

    [Fact]
    public void IsValidHash_Valid16CharLowerHex_ShouldReturnTrue()
    {
        FilepathHasher.IsValidHash("0123456789abcdef").Should().BeTrue();
    }
}
