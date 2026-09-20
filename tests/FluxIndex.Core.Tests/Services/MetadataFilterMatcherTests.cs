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
            matcher.Matches(row).Should().Be(
                VectorStoreBase.MatchesMetadataFilter(row, filters),
                "compiling a filter must not change which rows it accepts");
        }

        matcher.Matches(rows[0]).Should().BeTrue("the first row satisfies every entry — otherwise this fact is vacuous");
        rows.Skip(1).Should().OnlyContain(r => !matcher.Matches(r));
    }

    [Fact]
    public void Compile_NullAlternativeInACollection_StillMatchesANullMetadataValue()
    {
        var filters = new Dictionary<string, object> { ["owner"] = new object?[] { null, "ada" } };
        var unowned = new Dictionary<string, object> { ["owner"] = null! };

        var matcher = MetadataFilterMatcher.Compile(filters);

        matcher.Matches(unowned).Should().BeTrue("null is one of the alternatives the filter allows");
        matcher.Matches(unowned).Should().Be(VectorStoreBase.MatchesMetadataFilter(unowned, filters));
        matcher.Matches(Meta(("owner", "ada"))).Should().BeTrue();
        matcher.Matches(Meta(("owner", "grace"))).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compile_NoFilter_MatchesEveryRowButNotAbsentMetadata(bool nullDictionary)
    {
        var matcher = MetadataFilterMatcher.Compile(nullDictionary ? null : new Dictionary<string, object>());

        matcher.IsMatchAll.Should().BeTrue();
        matcher.Matches(Meta(("anything", "at all"))).Should().BeTrue();
        matcher.Matches(null).Should().BeFalse(
            "MatchesMetadataFilter answered false for absent metadata whatever the filter was");
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
