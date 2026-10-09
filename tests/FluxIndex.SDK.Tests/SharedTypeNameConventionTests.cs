using System.Reflection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// No two public top-level types in the FluxIndex libraries share a simple name. A consumer importing both namespaces
/// cannot name either (CS0104), and a type moved between namespaces silently binds to its twin — the namespace merge of
/// <c>FluxIndex.Core.Services</c> made <c>SmallToBigRetriever</c> pick late chunking's <c>ChunkBoundary</c> over the
/// hierarchy's. Static classes (extension-method holders) are exempt: nothing names them.
/// </summary>
/// <remarks>
/// The roster is the set shared today, each with why it stays or what fixing it means. Shrink it; a new entry is a
/// roster change on purpose.
/// </remarks>
public class SharedTypeNameConventionTests
{
    private static readonly Dictionary<string, string> KnownSharedNames = new(StringComparer.Ordinal)
    {
        // Per-provider EF entities: each lives in its own storage package, never imported together.
        ["CacheStatsEntity"] = "storage entity (PostgreSQL, SQLite)",
        ["ChunkHierarchyEntity"] = "storage entity (PostgreSQL, SQLite)",
        ["ChunkRelationshipEntity"] = "storage entity (PostgreSQL, SQLite)",
        ["QuantizedVectorEntity"] = "storage entity (PostgreSQL, SQLite)",
        ["SemanticCacheEntity"] = "storage entity (PostgreSQL, SQLite)",
        ["VectorEntity"] = "storage entity (PostgreSQL, SQLite)",

        // Nothing left to fix (run 130 emptied the list): a new name shared by two public types is a defect to fix, not a
        // roster entry — unless it is one of the deliberate shares above.
    };

    private static Dictionary<string, List<string>> SharedNames()
        => OperationalLanguageConventionTests.LibraryAssemblies()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => !type.IsNested && !(type.IsAbstract && type.IsSealed))
            .GroupBy(type => type.IsGenericType ? type.Name[..type.Name.IndexOf('`')] : type.Name, StringComparer.Ordinal)
            .Where(group => group.Select(type => type.Namespace).Distinct().Count() > 1)
            .ToDictionary(group => group.Key, group => group.Select(type => type.FullName!).Order().ToList(), StringComparer.Ordinal);

    [Fact]
    public void NoNewPublicTypeSharesAName()
    {
        var unlisted = SharedNames().Where(pair => !KnownSharedNames.ContainsKey(pair.Key)).ToList();

        Assert.True(unlisted.Count == 0,
            "Public types sharing a simple name (rename one, or list it with a reason):\n"
            + string.Join("\n", unlisted.Select(pair => $"  {pair.Key}: {string.Join(", ", pair.Value)}")));
    }

    [Fact]
    public void RosterHoldsOnlyNamesStillShared()
    {
        var shared = SharedNames();
        var stale = KnownSharedNames.Keys.Where(name => !shared.ContainsKey(name)).Order().ToList();

        Assert.True(stale.Count == 0, "No longer shared — remove from the roster: " + string.Join(", ", stale));
    }

    // Positive control: the scan must see the exported surface, or an empty result would pass for the wrong reason.
    [Fact]
    public void Scan_SeesTheExportedSurface()
    {
        var exported = OperationalLanguageConventionTests.LibraryAssemblies().Sum(assembly => assembly.GetExportedTypes().Length);

        Assert.True(exported > 300, $"exported types seen: {exported}");
        Assert.Contains("VectorEntity", SharedNames().Keys); // a deliberate share (one storage entity per backend)
    }
}
