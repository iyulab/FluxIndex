using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// <c>FluxIndex.Storage.SQLite</c> carries a <c>buildTransitive</c> props and targets pair that replaces the platform check
/// of the <c>sqlite-vec</c> package. Its own check refuses any executable whose <c>PlatformTarget</c> is not x64 or x86, so a
/// self-contained <c>osx-arm64</c> or <c>linux-arm64</c> publish failed at build time although the package ships natives for
/// both; and it passed <c>win-x86</c>, for which none is shipped. The replacement names the runtime identifiers that really
/// have no native library. These facts keep the three parts together: the files are packed, the package's own check is off,
/// and the list of refused identifiers matches the natives the package actually delivers.
/// </summary>
public sealed partial class SqliteVecPlatformCheckConventionTests
{
    private static string ProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "FluxIndex.Storage.SQLite");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("src/FluxIndex.Storage.SQLite not found above the test output.");
    }

    private static XDocument Load(string relativePath) => XDocument.Load(Path.Combine(ProjectDirectory(), relativePath));

    [Fact]
    public void BothFilesArePackedUnderBuildTransitive()
    {
        var project = Load("FluxIndex.Storage.SQLite.csproj");

        var packed = project.Descendants("None")
            .Where(n => string.Equals((string?)n.Attribute("Pack"), "true", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                n => ((string)n.Attribute("Include")!).Replace('\\', '/'),
                n => ((string?)n.Attribute("PackagePath") ?? "").Replace('\\', '/'));

        packed.Should().ContainKey("buildTransitive/FluxIndex.Storage.SQLite.props");
        packed.Should().ContainKey("buildTransitive/FluxIndex.Storage.SQLite.targets");
        packed.Values.Should().OnlyContain(p => p.StartsWith("buildTransitive/", StringComparison.Ordinal),
            "a package's build files take effect for the consumer only from buildTransitive/ (or build/), and the file name must be the package id");
        File.Exists(Path.Combine(ProjectDirectory(), "buildTransitive", "FluxIndex.Storage.SQLite.props")).Should().BeTrue();
        File.Exists(Path.Combine(ProjectDirectory(), "buildTransitive", "FluxIndex.Storage.SQLite.targets")).Should().BeTrue();
    }

    [Fact]
    public void ThePropsTurnTheSqliteVecCheckOff_UnlessTheConsumerSetsIt()
    {
        var props = Load("buildTransitive/FluxIndex.Storage.SQLite.props");

        var setting = props.Descendants("EnableUnsupportedPlatformTargetCheck").Should().ContainSingle().Subject;

        setting.Value.Should().Be("false");
        ((string?)setting.Attribute("Condition")).Should().Contain("== ''",
            "a project that sets the property itself keeps its value");
    }

    [Fact]
    public void TheRefusedIdentifiers_AreExactlyTheOnesWithoutAShippedNative()
    {
        var targets = Load("buildTransitive/FluxIndex.Storage.SQLite.targets");
        var error = targets.Descendants("Error").Should().ContainSingle().Subject;

        var refused = RidInCondition().Matches((string)error.Attribute("Condition")!).Select(m => m.Groups[1].Value).ToHashSet();
        var named = NamedInMessage().Match((string)error.Attribute("Text")!);
        named.Success.Should().BeTrue("the message lists what the package ships");
        var advertised = named.Groups[1].Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w != "and").ToHashSet();

        var shipped = Directory.EnumerateDirectories(Path.Combine(AppContext.BaseDirectory, "runtimes"))
            .Where(d => Directory.Exists(Path.Combine(d, "native")) && Directory.EnumerateFiles(Path.Combine(d, "native"), "vec0.*").Any())
            .Select(d => Path.GetFileName(d))
            .ToHashSet();

        shipped.Should().NotBeEmpty("the scan must find the sqlite-vec natives in the test output, or this test says nothing");
        advertised.Should().BeEquivalentTo(shipped, "the message must name the natives the package really ships");
        refused.Should().NotBeEmpty();
        refused.Intersect(shipped).Should().BeEmpty("an identifier whose native is shipped must not be refused");
    }

    [GeneratedRegex(@"RuntimeIdentifier\)' == '([^']+)'")]
    private static partial Regex RidInCondition();

    [GeneratedRegex(@"ships native libraries for ([^;]+?) only;")]
    private static partial Regex NamedInMessage();
}
