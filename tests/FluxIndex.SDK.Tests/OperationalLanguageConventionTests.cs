using System.Reflection;
using System.Runtime.Loader;
using Iyu.Conventions.Testing;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Operational text — every <c>[LoggerMessage]</c> template and every exception message — is ASCII. Operators grep
/// it, paste it into issues and search it in log pipelines whose tokenizers split on Latin word boundaries; a dash or an
/// arrow outside ASCII is as opaque there as a Korean word. The rule is the umbrella's logging convention; the scan is
/// <c>Iyu.Conventions.Testing</c>'s, shared with the other repositories.
/// </summary>
/// <remarks>
/// It lives in this project because it is the one that references every FluxIndex library assembly, and it carries no
/// category trait, so it runs in CI. It replaces the per-assembly Hangul-only tests, which saw two of thirteen
/// assemblies and passed non-ASCII punctuation.
/// </remarks>
public class OperationalLanguageConventionTests
{
    private static readonly Lazy<OperationalLanguageReport> Result = new(() =>
        OperationalLanguage.Scan(LibraryAssemblies(), OperationalLanguage.NonAscii));

    [Fact]
    public void LogTemplatesAndExceptionMessages_AreAscii()
    {
        var findings = Result.Value.Findings;
        Assert.True(findings.Count == 0,
            "Non-ASCII operational text:\n" + string.Join("\n", findings.Select(f => $"  [{f.Kind}] {f.Location}: {f.Text}")));
    }

    // Positive control: the scan must see the operational text it exists to judge, or an empty finding list would pass
    // because the reader sees nothing.
    [Fact]
    public void Scan_SeesLogTemplatesAndExceptionMessages()
    {
        Assert.True(Result.Value.LogMessagesRead > 50, $"log templates seen: {Result.Value.LogMessagesRead}");
        Assert.True(Result.Value.ExceptionLiteralsRead > 50, $"exception messages seen: {Result.Value.ExceptionLiteralsRead}");
    }

    // Every FluxIndex library assembly copied next to the tests — not the tests themselves.
    private static List<Assembly> LibraryAssemblies()
        => [.. Directory.EnumerateFiles(AppContext.BaseDirectory, "FluxIndex.*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && !name.EndsWith(".Tests", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name => AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(name!)))];
}
