using System.Reflection;
using System.Text.RegularExpressions;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

// Operator log pipelines (grep / Loki / Elastic) and international support require
// English-only log messages. See Filer issue 2026-04-22 (fluxindex-sqlitevec-korean-logs.md):
// Korean tokens land as opaque tokens in Latin-tokenized indexes and require UTF-8-aware
// regex from operators.
public class LogLanguageConventionTests
{
    private static readonly Regex HangulRegex = new(@"[가-힣ᄀ-ᇿ㄰-㆏]");

    public static IEnumerable<object[]> AssemblyTypes()
    {
        var assembly = typeof(SQLiteVecOptions).Assembly;
        foreach (var type in assembly.GetTypes().Where(t => !t.IsCompilerGenerated()))
        {
            yield return new object[] { type };
        }
    }

    [Theory]
    [MemberData(nameof(AssemblyTypes))]
    public void LoggerMessageAttributes_HaveAsciiOnlyMessages(Type type)
    {
        var offenders = type
            .GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(m => (Method: m, Attr: m.GetCustomAttribute<LoggerMessageAttribute>()))
            .Where(x => x.Attr is not null && !string.IsNullOrEmpty(x.Attr.Message) && HangulRegex.IsMatch(x.Attr.Message))
            .Select(x => $"  {type.Name}.{x.Method.Name}: {x.Attr!.Message}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Found Korean text in [LoggerMessage] attributes — log messages must be English-only:\n"
            + string.Join("\n", offenders));
    }
}

internal static class TypeReflectionExtensions
{
    public static bool IsCompilerGenerated(this Type type)
        => type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null;
}
