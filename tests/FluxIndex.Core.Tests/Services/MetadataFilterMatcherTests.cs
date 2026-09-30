using FluxIndex.Core.Application.Interfaces;
using AwesomeAssertions;
using FluxIndex.Core.Application.Services.Base;
using System.Text.Json;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// The compiled matcher exists so an in-memory post-filter loop expands a filter once instead of
/// once per row. It is only allowed to be faster — the answers must stay the answers
/// <see cref="VectorStoreBase.MatchesMetadataFilter"/> gave, which is what these fix.
/// </summary>
public class MetadataFilterMatcherTests
{
    private static Dictionary<string, object> Meta(params (string Key, object Value)[] pairs)
        => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void Compile_AgreesWithTheOneShotOverload_OnEveryDocumentedValueShape()
    {
        var filters = new Dictionary<string, object>
        {
            ["tenant"] = new[] { "a", "b", "c" },
            ["active"] = true,
            ["rank"] = 3,
            ["kind"] = JsonSerializer.Deserialize<JsonElement>("[\"note\",\"memo\"]"),
        };

        var rows = new[]
        {
            Meta(("tenant", "b"), ("active", true), ("rank", 3), ("kind", "memo")),        // matches
            Meta(("tenant", "z"), ("active", true), ("rank", 3), ("kind", "memo")),        // wrong tenant
            Meta(("tenant", "b"), ("active", false), ("rank", 3), ("kind", "memo")),       // wrong bool
            Meta(("tenant", "b"), ("active", true), ("rank", 4), ("kind", "memo")),        // wrong number
            Meta(("tenant", "b"), ("active", true), ("rank", 3), ("kind", "letter")),      // outside the JSON array
            Meta(("tenant", "b"), ("active", true), ("rank", 3)),                          // missing key
        };

        var matcher = MetadataFilterMatcher.Compile(filters);

        foreach (var row in rows)
        {
            matcher.Matches(null, row).Should().Be(
                VectorStoreBase.MatchesMetadataFilter(null, row, filters),
                "compiling a filter must not change which rows it accepts");
        }

        matcher.Matches(null, rows[0]).Should().BeTrue("the first row satisfies every entry — otherwise this fact is vacuous");
        rows.Skip(1).Should().OnlyContain(r => !matcher.Matches(null, r));
    }

    [Fact]
    public void Compile_NullAlternativeInACollection_StillMatchesANullMetadataValue()
    {
        var filters = new Dictionary<string, object> { ["owner"] = new object?[] { null, "ada" } };
        var unowned = new Dictionary<string, object> { ["owner"] = null! };

        var matcher = MetadataFilterMatcher.Compile(filters);

        matcher.Matches(null, unowned).Should().BeTrue("null is one of the alternatives the filter allows");
        matcher.Matches(null, unowned).Should().Be(VectorStoreBase.MatchesMetadataFilter(null, unowned, filters));
        matcher.Matches(null, Meta(("owner", "ada"))).Should().BeTrue();
        matcher.Matches(null, Meta(("owner", "grace"))).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compile_NoFilter_MatchesEveryRowButNotAbsentMetadata(bool nullDictionary)
    {
        var matcher = MetadataFilterMatcher.Compile(nullDictionary ? null : new Dictionary<string, object>());

        matcher.IsMatchAll.Should().BeTrue();
        matcher.Matches(null, Meta(("anything", "at all"))).Should().BeTrue();
        matcher.Matches(null, null).Should().BeTrue(
            "a filter with no entries constrains nothing — a chunk without metadata is not outside it");
    }

    [Fact]
    public void Matches_DocumentIdEntry_ComparesTheChunkDocumentIdNotAMetadataCopy()
    {
        var filters = new Dictionary<string, object> { [FilterKeys.DocumentId] = new[] { "doc-a", "doc-b" } };
        var matcher = MetadataFilterMatcher.Compile(filters);

        matcher.Matches("doc-b", null).Should().BeTrue("a chunk with no metadata at all is still in its document's scope");
        matcher.Matches("doc-a", Meta(("file_name", "a.txt"))).Should().BeTrue("the metadata need not repeat the document id");
        matcher.Matches("doc-z", Meta((FilterKeys.DocumentId, "doc-a"))).Should().BeFalse(
            "a metadata entry of the same name is not consulted — the chunk's own document id decides");
        matcher.Matches(null, Meta((FilterKeys.DocumentId, "doc-a"))).Should().BeFalse();
    }

    [Fact]
    public void Matches_DocumentIdEntryCombinedWithMetadataEntry_RequiresBoth()
    {
        var filters = new Dictionary<string, object>
        {
            [FilterKeys.DocumentId] = "doc-a",
            ["tenant"] = "t1",
        };
        var matcher = MetadataFilterMatcher.Compile(filters);

        matcher.Matches("doc-a", Meta(("tenant", "t1"))).Should().BeTrue();
        matcher.Matches("doc-a", Meta(("tenant", "t2"))).Should().BeFalse();
        matcher.Matches("doc-b", Meta(("tenant", "t1"))).Should().BeFalse();
    }

    [Fact]
    public void Compile_MalformedFilterValue_ThrowsBeforeAnyRowIsSeen()
    {
        // The one-shot overload expanded the filter while walking rows, so a store holding no rows
        // never reached the throw and a malformed filter passed as an empty result.
        var nested = new Dictionary<string, object> { ["tag"] = new object[] { new[] { "nested" } } };
        var empty = new Dictionary<string, object> { ["tag"] = Array.Empty<string>() };

        FluentActions.Invoking(() => MetadataFilterMatcher.Compile(nested)).Should().Throw<Exception>();
        FluentActions.Invoking(() => MetadataFilterMatcher.Compile(empty)).Should().Throw<Exception>();
    }
}
