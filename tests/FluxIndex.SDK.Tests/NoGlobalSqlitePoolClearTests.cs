using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// No test calls <c>SqliteConnection.ClearAllPools()</c>: it is process-global, and from one fixture's cleanup it
/// disposes the native handle another fixture is opening in parallel — an <c>ObjectDisposedException</c> in an
/// unrelated test, a few times in a hundred runs. Fixtures release their own files' pools with
/// <c>SqliteTestPools.Release</c>.
/// </summary>
public partial class NoGlobalSqlitePoolClearTests
{
    [Fact]
    public void NoTestSource_ClearsEverySqlitePool()
    {
        var testsRoot = FindTestsRoot();

        var offenders = Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.EndsWith(nameof(NoGlobalSqlitePoolClearTests) + ".cs", StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) && GlobalClear().IsMatch(l.Text))
            .Select(l => $"{Path.GetRelativePath(testsRoot, l.File)}:{l.Line}")
            .ToList();

        offenders.Should().BeEmpty("SqliteConnection.ClearAllPools() yanks connections from fixtures running in parallel");
    }

    [Fact]
    public void TheScan_FindsACall_AndIgnoresAComment()
    {
        GlobalClear().IsMatch("        SqliteConnection.ClearAllPools();").Should().BeTrue();
        GlobalClear().IsMatch("Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();").Should().BeTrue();
        GlobalClear().IsMatch("/// <see cref=\"SqliteConnection.ClearAllPools\"/>").Should().BeFalse();
    }

    private static string FindTestsRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FluxIndex.slnx")))
                return Path.Combine(dir.FullName, "tests");
        }

        throw new InvalidOperationException("FluxIndex.slnx not found above the test binaries.");
    }

    [GeneratedRegex(@"\.ClearAllPools\s*\(")]
    private static partial Regex GlobalClear();
}
