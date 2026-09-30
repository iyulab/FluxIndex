using System.Text.Json;
using System.Text.RegularExpressions;
using FluxIndex.Core.Application.Utilities;
using Xunit;

namespace FluxIndex.Core.Tests.Utilities;

/// <summary>
/// Stored metadata reads back as plain values whichever store returned it. Stores with a JSON column used to hand
/// <see cref="JsonElement"/> values to consumers (and to the library's own restore code, which accepted only strings),
/// so the same chunk read <c>value is string</c> differently per store and per hybrid leg.
/// </summary>
public sealed class MetadataValuesTests
{
    [Fact]
    public void Deserialize_GivesPlainValues()
    {
        var values = MetadataValues.Deserialize("""
            {"file_name":"report.pdf","page":12,"score":0.5,"draft":true,"missing":null,
             "tags":["a",2],"source":{"path":"/docs/report.pdf","size":1024}}
            """);

        Assert.Equal("report.pdf", Assert.IsType<string>(values["file_name"]));
        Assert.Equal(12L, Assert.IsType<long>(values["page"]));
        Assert.Equal(0.5, Assert.IsType<double>(values["score"]));
        Assert.True(Assert.IsType<bool>(values["draft"]));
        Assert.False(values.ContainsKey("missing"), "a null member is left out, as a key that was never written");
        Assert.Equal(new object?[] { "a", 2L }, Assert.IsType<List<object?>>(values["tags"]));
        var source = Assert.IsType<Dictionary<string, object>>(values["source"]);
        Assert.Equal(("/docs/report.pdf", 1024L), ((string)source["path"], (long)source["size"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    public void Deserialize_NothingIsEmpty(string? json) => Assert.Empty(MetadataValues.Deserialize(json));

    [Fact]
    public void Deserialize_NotAnObject_Throws() => Assert.Throws<JsonException>(() => MetadataValues.Deserialize("[1]"));

    [Fact]
    public void ToPlain_ConvertsJsonElementsInPlace_AndLeavesOtherValues()
    {
        var fromJson = JsonSerializer.Deserialize<Dictionary<string, object>>("""{"file_name":"a.md","page":3,"gone":null}""")!;
        fromJson["native"] = 7;

        var result = MetadataValues.ToPlain(fromJson);

        Assert.Same(fromJson, result);
        Assert.Equal("a.md", Assert.IsType<string>(result["file_name"]));
        Assert.Equal(3L, Assert.IsType<long>(result["page"]));
        Assert.Equal(7, Assert.IsType<int>(result["native"]));
        Assert.False(result.ContainsKey("gone"));
    }

    /// <summary>
    /// The teeth: deserializing a JSON object into <c>Dictionary&lt;string, object&gt;</c> is what produces
    /// <see cref="JsonElement"/> values, so no source file does it — every read goes through <see cref="MetadataValues"/>.
    /// </summary>
    [Fact]
    public void NoSourceFile_DeserializesIntoAnObjectDictionary()
    {
        var pattern = new Regex(@"Deserialize\s*<\s*Dictionary\s*<\s*string\s*,\s*object\??\s*>\s*>", RegexOptions.Compiled);
        var src = Path.Combine(RepositoryRoot(), "src");

        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, line, i)))
            .Where(x => pattern.IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(src, x.f)}:{x.i + 1}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Deserialize JSON metadata with MetadataValues.Deserialize, not into Dictionary<string, object>: " +
            string.Join(", ", offenders));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FluxIndex.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("FluxIndex.slnx not found above the test assembly");
    }
}
