using FluxIndex.Core.Application.Interfaces;
using Xunit;

namespace FluxIndex.Core.Tests;

/// <summary>
/// <see cref="FilterScope.Of"/> names the scope a filter selects: the same scope gets the same name however the filter is
/// written, and different scopes never share one — the property a per-scope cache key rests on.
/// </summary>
public class FilterScopeTests
{
    private static string Of(Dictionary<string, object>? filters) => FilterScope.Of(filters);

    [Fact]
    public void NoFilter_IsUnscoped()
    {
        Assert.Equal(FilterScope.Unscoped, Of(null));
        Assert.Equal(FilterScope.Unscoped, Of([]));
        Assert.NotEqual(FilterScope.Unscoped, Of(new() { ["tag"] = "a" }));
    }

    [Fact]
    public void SameScope_WrittenDifferently_HasOneName()
    {
        var reference = Of(new() { ["desk"] = "d1", ["tag"] = new[] { "a", "b" } });

        Assert.Equal(reference, Of(new() { ["tag"] = new List<string> { "b", "a" }, ["desk"] = "d1" }));   // key and element order
        Assert.Equal(reference, Of(new() { ["desk"] = new[] { "d1" }, ["tag"] = new HashSet<string> { "a", "b", "a" } }));   // scalar vs one element, duplicates
    }

    [Fact]
    public void DifferentScopes_HaveDifferentNames()
    {
        var names = new[]
        {
            Of(new() { ["tag"] = new[] { "a", "b" } }),
            Of(new() { ["tag"] = new[] { "a", "c" } }),
            Of(new() { ["tag"] = new[] { "a" } }),
            Of(new() { ["label"] = new[] { "a", "b" } }),
            Of(new() { ["tag"] = new[] { "a", "b" }, ["desk"] = "d1" }),
            Of(new() { [FilterKeys.DocumentId] = new[] { "a", "b" } }),
        };

        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Fact]
    public void SeparatorsInsideValues_DoNotCollideWithSeparateValues()
    {
        Assert.NotEqual(Of(new() { ["tag"] = "a|b" }), Of(new() { ["tag"] = new[] { "a", "b" } }));
        Assert.NotEqual(Of(new() { ["k"] = "v;x=y" }), Of(new() { ["k"] = "v", ["x"] = "y" }));
    }

    [Fact]
    public void AValueOutsideTheFilterContract_Throws()
    {
        Assert.Throws<ArgumentException>(() => Of(new() { ["tag"] = Array.Empty<string>() }));
        Assert.Throws<ArgumentException>(() => Of(new() { ["tag"] = new object() }));
    }
}
