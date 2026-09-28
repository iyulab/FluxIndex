using System.Reflection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// <c>FluxIndexContextBuilder.Build()</c> returns <see cref="IFluxIndexContext"/>, so a public member the concrete class
/// has and the interface does not is reachable only through a cast. Ten were: the adaptive search family, small-to-big,
/// query complexity and the semantic cache calls — the README advertised adaptive search, and a caller of <c>Build()</c>
/// could not call it.
/// </summary>
public class ContextInterfaceCoverageTests
{
    [Fact]
    public void EveryPublicInstanceMember_OfTheContext_IsOnTheInterfaceBuildReturns()
    {
        var type = typeof(FluxIndexContext);
        var mapped = new[] { typeof(IFluxIndexContext), typeof(IDisposable), typeof(IAsyncDisposable) }
            .SelectMany(i => type.GetInterfaceMap(i).TargetMethods)
            .ToHashSet();

        var missing = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !mapped.Contains(m))
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "public on FluxIndexContext but not on IFluxIndexContext (Build() returns the interface): " + string.Join(", ", missing));
    }

    [Fact]
    public void BuildReturnsTheInterface()
    {
        // The premise of the fact above: if Build() returned the concrete type, the interface gap would not matter.
        var build = typeof(FluxIndexContextBuilder).GetMethod(nameof(FluxIndexContextBuilder.Build), Type.EmptyTypes);

        Assert.Equal(typeof(IFluxIndexContext), build!.ReturnType);
    }
}
