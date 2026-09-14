using System.Reflection;
using System.Runtime.Loader;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Every public implementation of a FluxIndex service contract must be reachable from the library —
/// registered in DI, constructed, or handed on by some other library type. A public implementation
/// nothing in the library references is a second, unregistered copy of a capability: it compiles, it
/// reads options, it even passes its own tests, and none of it runs. In 0.38.0 a second
/// <c>SelfRAGService</c> (843 lines, a different namespace) had sat unregistered next to the registered
/// one; a fix landed on the dead copy first, and the options roster counted the dead copy's reads as the
/// option being honoured.
/// </summary>
/// <remarks>
/// <para>
/// This scans the IL of every FluxIndex library assembly for the three ways a type is referenced —
/// <c>newobj</c> of one of its constructors, a generic-method call carrying it as a type argument
/// (<c>AddScoped&lt;I, T&gt;()</c>), and <c>ldtoken</c> (<c>typeof(T)</c>) — plus metadata references
/// (base type, field, property, parameter and generic-argument types), all from outside the type itself.
/// A public, non-abstract class that implements a FluxIndex interface and has none of these is pinned
/// below, so the next dead copy lands here as a deliberate roster change rather than in a consumer's
/// decompiler.
/// </para>
/// <para>
/// The limit is the mirror of the options roster's: being referenced is necessary, not sufficient — a
/// type can be registered and never resolved. And a public type a consumer may construct directly is
/// still a valid public surface; an entry here is a triage item (register it, document it as
/// consumer-constructed, or remove it), not a verdict.
/// </para>
/// </remarks>
public class UnreferencedImplementationRosterTests
{
    // Decided: public entry points a consumer constructs itself, documented as such (README / XML doc).
    private static readonly string[] ConsumerConstructed =
    [
        "FluxIndex.Core.Application.Services.KeywordSearch.CjkBigramTextAnalyzer",   // README: AddSingleton<ITextAnalyzer>(CjkBigramTextAnalyzer.Instance)
    ];

    // Undecided: each entry needs a decision — wire it, document it as consumer-constructed, or remove it.
    // Empty since 0.40.0: the first run's twelve (whole capability clusters with no registration and no caller)
    // were removed rather than wired. A new entry here is a deliberate decision, not a way to make the test pass.
    private static readonly string[] KnownUnreferenced = [];

    private static readonly Lazy<Scan> Result = new(Run);

    [Fact]
    public void EveryPublicImplementation_IsReferencedByTheLibrary_ExceptTheKnownRoster()
    {
        var unreferenced = Result.Value.Unreferenced.Order(StringComparer.Ordinal).ToList();
        var expected = KnownUnreferenced.Concat(ConsumerConstructed).Order(StringComparer.Ordinal).ToList();

        Assert.True(expected.SequenceEqual(unreferenced),
            "a public implementation nothing in the library references is a capability that never runs. " +
            "Register it, document it as consumer-constructed, or remove it — and change this roster as a " +
            "deliberate decision.\nfound:\n  " + string.Join("\n  ", unreferenced) +
            "\nexpected:\n  " + string.Join("\n  ", expected));
    }

    // Positive controls: the scan must see the reference shapes it is built for, or an empty roster above
    // would pass because the detector sees nothing.
    [Fact]
    public void Scan_SeesKnownReferences()
    {
        var scan = Result.Value;
        Assert.True(scan.Implementations.Count > 30, $"the scan must find the library's implementations (found {scan.Implementations.Count})");
        // Registered through a generic DI call in the SDK builder.
        Assert.Contains("FluxIndex.Core.Application.Services.SelfRAGService", scan.Referenced);
        // Constructed with `new` inside the library.
        Assert.Contains("FluxIndex.Core.Services.SimpleChunkingService", scan.Referenced);
    }

    private sealed record Scan(IReadOnlyList<Type> Implementations, IReadOnlySet<string> Referenced, IReadOnlyList<string> Unreferenced);

    private static Scan Run()
    {
        var assemblies = LibraryAssemblies();
        var assemblySet = assemblies.ToHashSet();

        // Candidates: public, concrete classes that implement an interface FluxIndex itself declares.
        var implementations = assemblies
            .SelectMany(SafeTypes)
            .Where(t => t is { IsPublic: true, IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(t => t.GetInterfaces().Any(i => assemblySet.Contains(i.Assembly)))
            .Where(t => !typeof(Exception).IsAssignableFrom(t) && !typeof(Attribute).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
        var candidates = implementations.ToHashSet();

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        void Note(Type? t, Type from)
        {
            t = Unwrap(t);
            if (t is null || !candidates.Contains(t) || IsWithin(from, t)) return;
            referenced.Add(t.FullName!);
        }

        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                 BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var assembly in assemblies)
        {
            foreach (var type in SafeTypes(assembly))
            {
                // Metadata references: base type, generic arguments of the base, fields, properties, parameters.
                for (var b = type.BaseType; b is not null; b = b.BaseType) { Note(b, type); foreach (var a in GenericArgs(b)) Note(a, type); }
                foreach (var i in type.GetInterfaces()) foreach (var a in GenericArgs(i)) Note(a, type);
                foreach (var f in type.GetFields(all)) { Note(f.FieldType, type); foreach (var a in GenericArgs(f.FieldType)) Note(a, type); }
                foreach (var p in type.GetProperties(all)) { Note(p.PropertyType, type); foreach (var a in GenericArgs(p.PropertyType)) Note(a, type); }

                IEnumerable<MethodBase> bodies = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
                foreach (var method in bodies)
                {
                    foreach (var p in method.GetParameters()) { Note(p.ParameterType, type); foreach (var a in GenericArgs(p.ParameterType)) Note(a, type); }
                    if (method is MethodInfo mi) { Note(mi.ReturnType, type); foreach (var a in GenericArgs(mi.ReturnType)) Note(a, type); }

                    foreach (var reference in IlReferences(method, type.Module))
                    {
                        switch (reference)
                        {
                            case MethodBase target:
                                Note(target.DeclaringType, type);
                                if (target.IsGenericMethod) foreach (var a in target.GetGenericArguments()) Note(a, type);
                                if (target.DeclaringType is { IsGenericType: true } dt) foreach (var a in dt.GetGenericArguments()) Note(a, type);
                                break;
                            case Type t:
                                Note(t, type); foreach (var a in GenericArgs(t)) Note(a, type);
                                break;
                        }
                    }
                }
            }
        }

        var unreferenced = implementations
            .Where(t => !referenced.Contains(t.FullName!))
            .Select(t => t.FullName!)
            .ToList();
        return new Scan(implementations, referenced, unreferenced);
    }

    private static Type? Unwrap(Type? t)
    {
        while (t is not null && (t.IsByRef || t.IsArray || t.IsPointer)) t = t.GetElementType();
        if (t is { IsGenericType: true, IsGenericTypeDefinition: false }) t = t.GetGenericTypeDefinition();
        return t;
    }

    private static IEnumerable<Type> GenericArgs(Type t)
    {
        if (!t.IsGenericType) yield break;
        foreach (var a in t.GetGenericArguments())
        {
            yield return a;
            foreach (var inner in GenericArgs(a)) yield return inner;
        }
    }

    // Every member a body references: call/callvirt/newobj/ldftn (method tokens), ldtoken (type or member
    // tokens). Tokens: MethodDef 0x06, MemberRef 0x0A, MethodSpec 0x2B, TypeDef 0x02, TypeRef 0x01,
    // TypeSpec 0x1B. A byte that merely looks like an opcode inside another operand yields a token that
    // resolves to something else, or to nothing; callers match exact members only.
    private static IEnumerable<object> IlReferences(MethodBase method, Module module)
    {
        byte[]? il;
        try { il = method.GetMethodBody()?.GetILAsByteArray(); }
        catch (Exception) { yield break; }
        if (il is null) yield break;
        for (var i = 0; i + 4 < il.Length; i++)
        {
            var op = il[i];
            if (op is not (0x28 or 0x6F or 0x73 or 0xD0) && !(op == 0xFE && i + 5 < il.Length && il[i + 1] == 0x06)) continue;
            var at = op == 0xFE ? i + 2 : i + 1;
            if (at + 4 > il.Length) continue;
            var token = BitConverter.ToInt32(il, at);
            var table = token >> 24;
            object? resolved = null;
            try
            {
                if (table is 0x06 or 0x0A or 0x2B) resolved = module.ResolveMethod(token);
                else if (op == 0xD0 && table is 0x01 or 0x02 or 0x1B) resolved = module.ResolveType(token);
            }
            catch (Exception) { continue; }
            if (resolved is not null) yield return resolved;
        }
    }

    private static bool IsWithin(Type type, Type container)
    {
        for (var t = type; t is not null; t = t.DeclaringType)
        {
            if (t == container) return true;
        }
        return false;
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private static List<Assembly> LibraryAssemblies()
        => [.. Directory.EnumerateFiles(AppContext.BaseDirectory, "FluxIndex.*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && !name.EndsWith(".Tests", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name => AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(name!)))];
}
