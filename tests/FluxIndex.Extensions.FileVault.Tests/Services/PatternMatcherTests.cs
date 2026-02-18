using FluentAssertions;
using FluxIndex.Extensions.FileVault.Services;
using Xunit;

namespace FluxIndex.Extensions.FileVault.Tests.Services;

public class PatternMatcherTests
{
    [Fact]
    public void IsMatch_NullPath_ShouldReturnFalse()
    {
        var matcher = new PatternMatcher(["*.pdf"]);

        matcher.IsMatch(null!).Should().BeFalse();
    }

    [Fact]
    public void IsMatch_EmptyPath_ShouldReturnFalse()
    {
        var matcher = new PatternMatcher(["*.pdf"]);

        matcher.IsMatch("").Should().BeFalse();
    }

    [Fact]
    public void IsMatch_NoPatterns_ShouldIncludeAll()
    {
        var matcher = new PatternMatcher();

        matcher.IsMatch("anything.xyz").Should().BeTrue();
    }

    [Theory]
    [InlineData("document.pdf", true)]
    [InlineData("report.docx", true)]
    [InlineData("image.png", false)]
    [InlineData("code.cs", false)]
    public void IsMatch_IncludePatterns_ShouldFilterCorrectly(string file, bool expected)
    {
        var matcher = new PatternMatcher(["*.pdf", "*.docx"]);

        matcher.IsMatch(file).Should().Be(expected);
    }

    [Fact]
    public void IsMatch_ExcludePatterns_ShouldTakePrecedence()
    {
        var matcher = new PatternMatcher(
            includePatterns: ["*.txt"],
            excludePatterns: ["~$*"]);

        matcher.IsMatch("normal.txt").Should().BeTrue();
        matcher.IsMatch("~$temp.txt").Should().BeFalse();
    }

    [Fact]
    public void IsMatch_ExcludeOnly_ShouldIncludeAllExceptExcluded()
    {
        var matcher = new PatternMatcher(
            excludePatterns: ["*.tmp", "*.bak"]);

        matcher.IsMatch("document.pdf").Should().BeTrue();
        matcher.IsMatch("cache.tmp").Should().BeFalse();
        matcher.IsMatch("backup.bak").Should().BeFalse();
    }

    [Fact]
    public void Filter_ShouldReturnOnlyMatching()
    {
        var matcher = new PatternMatcher(["*.pdf", "*.txt"]);
        var files = new[] { "a.pdf", "b.txt", "c.png", "d.pdf" };

        var result = matcher.Filter(files).ToList();

        result.Should().HaveCount(3);
        result.Should().Contain("a.pdf");
        result.Should().Contain("b.txt");
        result.Should().Contain("d.pdf");
    }

    [Fact]
    public void Filter_EmptyInput_ShouldReturnEmpty()
    {
        var matcher = new PatternMatcher(["*.pdf"]);

        matcher.Filter([]).Should().BeEmpty();
    }

    [Fact]
    public void ShouldInclude_Static_NullPath_ShouldReturnFalse()
    {
        PatternMatcher.ShouldInclude(null!, ["*.pdf"], null).Should().BeFalse();
    }

    [Fact]
    public void ShouldInclude_Static_MatchingInclude_ShouldReturnTrue()
    {
        PatternMatcher.ShouldInclude("doc.pdf", ["*.pdf"], null).Should().BeTrue();
    }

    [Fact]
    public void ShouldInclude_Static_ExcludeTakesPrecedence()
    {
        PatternMatcher.ShouldInclude("~$temp.pdf", ["*.pdf"], ["~$*"]).Should().BeFalse();
    }

    [Fact]
    public void ShouldInclude_Static_NoPatterns_ShouldIncludeAll()
    {
        PatternMatcher.ShouldInclude("anything.xyz", null, null).Should().BeTrue();
    }

    [Fact]
    public void CreateDefault_ShouldIncludeDocumentTypes()
    {
        var matcher = PatternMatcher.CreateDefault();

        matcher.IsMatch("report.pdf").Should().BeTrue();
        matcher.IsMatch("doc.docx").Should().BeTrue();
        matcher.IsMatch("data.csv").Should().BeTrue();
        matcher.IsMatch("config.json").Should().BeTrue();
        matcher.IsMatch("readme.md").Should().BeTrue();
    }

    [Fact]
    public void CreateDefault_ShouldExcludeTempFiles()
    {
        var matcher = PatternMatcher.CreateDefault();

        matcher.IsMatch("~$temp.docx").Should().BeFalse();
        matcher.IsMatch("file.tmp").Should().BeFalse();
        matcher.IsMatch("file.bak").Should().BeFalse();
    }

    [Fact]
    public void CreateDefault_ShouldExcludeDotFiles()
    {
        var matcher = PatternMatcher.CreateDefault();

        matcher.IsMatch(".DS_Store").Should().BeFalse();
        matcher.IsMatch(".gitignore").Should().BeFalse();
    }

    [Fact]
    public void CreateDefault_ShouldExcludeUnknownExtensions()
    {
        var matcher = PatternMatcher.CreateDefault();

        matcher.IsMatch("binary.exe").Should().BeFalse();
        matcher.IsMatch("image.png").Should().BeFalse();
    }
}
