using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Compiles the C# blocks of <c>docs/AI_PROVIDER_INTEGRATION.md</c> the way a reader assembles them: the guide defines its
/// own providers in some blocks and registers or calls them in others, so every block goes into one compilation.
/// <see cref="DocsSnippetRosterTests"/> only checks that a called name exists somewhere — the guide once called
/// <c>OpenAIClient.GetEmbeddingsAsync</c> from a retired SDK and passed, because an unrelated interface in a dependency
/// happened to declare a method of that name.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A block that declares types is one source file, as written.</item>
/// <item>A block of statements becomes the body of its own method; its <c>using</c> lines are hoisted, and the stand-ins below
/// are declared when the block uses the name without declaring it — values the surrounding text gives the reader.</item>
/// <item>A block that only restates one of the library's interfaces is a listing: compiled, it would declare a second type
/// of that name and check nothing, so each member it lists is looked up on the real interface instead.</item>
/// </list>
/// </remarks>
public class ProviderGuideSnippetCompileTests
{
    private const string GuidePath = "docs/AI_PROVIDER_INTEGRATION.md";

    // The guide's C# blocks, at least — a scan that finds fewer has lost the document, and passing then proves nothing.
    private const int MinimumBlocks = 19;

    // What a reader's project has without writing it: the SDK's implicit usings, and the web SDK's for a web host block.
    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    // The guide's «Your…Provider» names stand for the reader's own client, with the shape the guide calls it with; what the
    // block does with the result is still checked against the real base class.
    private const string Placeholders = """
        internal static class YourEmbeddingProvider
        {
            public static Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<float>());
        }
        internal static class YourLLMProvider
        {
            public static Task<string> GenerateAsync(string prompt, int maxTokens, float temperature, CancellationToken cancellationToken) => Task.FromResult("");
        }
        internal static class YourRerankerProvider
        {
            public static Task<IReadOnlyList<float>> ScoreAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<float>>([]);
        }
        """;

    // A block of statements continues the text around it: the reader already has these from the blocks before it.
    private const string StatementContextUsings = """
        using FluxIndex.Core.Application.Interfaces;
        using Microsoft.Extensions.DependencyInjection;
        """;

    private const string WebImplicitUsings = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Microsoft.Extensions.Logging;
        """;

    private static readonly (string Name, string Declaration)[] StandIns =
    [
        ("services", "Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();"),
        ("apiKey", "string apiKey = \"\";"),
        ("cohereApiKey", "string cohereApiKey = \"\";"),
        ("connectionString", "string connectionString = \"\";"),
        ("loggerFactory", "Microsoft.Extensions.Logging.ILoggerFactory loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;"),
    ];

    private static readonly Lazy<GuideCompilation> Guide = new(() => CompileGuide(ReadBlocks(File.ReadAllText(GuideFile()))));

    public static TheoryData<string> Blocks()
    {
        var data = new TheoryData<string>();
        foreach (var block in ReadBlocks(File.ReadAllText(GuideFile())))
            data.Add(block.Key);
        return data;
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void GuideBlock_Compiles(string key)
    {
        var guide = Guide.Value;
        var errors = guide.Errors.Where(e => e.Location.SourceTree?.FilePath == key).ToList();

        Assert.True(errors.Count == 0,
            $"{GuidePath} block {key} does not compile against the current API:\n" +
            string.Join("\n", errors.Select(e => e.ToString())) + "\n--- source ---\n" + guide.Sources[key]);
    }

    [Fact]
    public void TheGuide_IsFound_AndCompilesAsAWhole()
    {
        var guide = Guide.Value;

        Assert.True(guide.Sources.Count >= MinimumBlocks, $"expected the guide's C# blocks, found {guide.Sources.Count}");
        Assert.True(guide.Listings >= 3, $"expected the three interface listings, found {guide.Listings}");
        // Errors not tied to one block (a duplicate type across blocks, a missing reference) fail here.
        Assert.Empty(guide.Errors.Where(e => e.Location.SourceTree is null).Select(e => e.ToString()));
    }

    /// <summary>Positive control: one compilation of the guide rejects a block that calls the retired SDK.</summary>
    [Fact]
    public void Compile_RejectsTheRetiredAzureSdkCall()
    {
        var blocks = ReadBlocks(File.ReadAllText(GuideFile()));
        blocks.Add(new Block("control", "control", """
            using Azure.AI.OpenAI;
            var client = new OpenAIClient(apiKey);
            var response = await client.GetEmbeddingsAsync(new EmbeddingsOptions("m", new[] { "x" }));
            """));

        var guide = CompileGuide(blocks);

        Assert.Contains(guide.Errors, e => e.Location.SourceTree?.FilePath == "control");
    }

    /// <summary>Positive control: a listing that names a member the interface does not have is reported.</summary>
    [Fact]
    public void Listing_ReportsAMemberTheInterfaceDoesNotHave()
    {
        var blocks = new List<Block>
        {
            new("listing", "listing", """
                public interface IReranker
                {
                    Task<IEnumerable<RerankResult>> RerankAsync(string query, IEnumerable<RetrievalResult> candidates, int topN = 5, CancellationToken cancellationToken = default);
                    Task<float> ScoreEverythingAsync(string query);
                }
                """),
        };

        var guide = CompileGuide(blocks);

        Assert.Contains(guide.Errors, e => e.Location.SourceTree?.FilePath == "listing" && e.GetMessage().Contains("ScoreEverythingAsync", StringComparison.Ordinal));
    }

    private sealed record Block(string Key, string Heading, string Code);

    private sealed record GuideCompilation(IReadOnlyList<Diagnostic> Errors, IReadOnlyDictionary<string, string> Sources, int Listings);

    private static GuideCompilation CompileGuide(List<Block> blocks)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(ImplicitUsings, parse, path: "(implicit usings)"),
            CSharpSyntaxTree.ParseText(Placeholders, parse, path: "(placeholders)"),
        };
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var listingErrors = new List<Diagnostic>();
        var listings = 0;

        foreach (var block in blocks)
        {
            var root = CSharpSyntaxTree.ParseText(block.Code, parse).GetCompilationUnitRoot();
            if (ListedInterface(root) is { } listed)
            {
                listings++;
                sources[block.Key] = block.Code;
                var tree = CSharpSyntaxTree.ParseText(block.Code, parse, path: block.Key);
                listingErrors.AddRange(CheckListing(tree, listed));
                continue;
            }

            var source = root.Members.Any(m => m is GlobalStatementSyntax)
                ? StatementsAsMethod(block)
                : block.Code;
            sources[block.Key] = source;
            trees.Add(CSharpSyntaxTree.ParseText(source, parse, path: block.Key));
        }

        var compilation = CSharpCompilation.Create(
            "ProviderGuide", trees, References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Concat(listingErrors)
            .ToList();
        return new GuideCompilation(errors, sources, listings);
    }

    /// <summary>The library interface a block restates, when the block declares exactly one interface named like one.</summary>
    private static Type? ListedInterface(CompilationUnitSyntax root)
    {
        var members = root.Members.SelectMany(m => m is BaseNamespaceDeclarationSyntax ns ? ns.Members : [m]).ToList();
        if (members is not [InterfaceDeclarationSyntax declaration])
            return null;

        return LibraryInterfaces().FirstOrDefault(t => t.Name == declaration.Identifier.Text);
    }

    private static IEnumerable<Diagnostic> CheckListing(SyntaxTree tree, Type listed)
    {
        var declared = listed.GetMethods().Select(m => m.Name)
            .Concat(listed.GetProperties().Select(p => p.Name))
            .Concat(listed.GetInterfaces().SelectMany(i => i.GetMethods().Select(m => m.Name).Concat(i.GetProperties().Select(p => p.Name))))
            .ToHashSet(StringComparer.Ordinal);
        var descriptor = new DiagnosticDescriptor(
            "GUIDE001", "Listed member does not exist",
            "The listing of {0} names '{1}', which the interface does not declare", "Docs", DiagnosticSeverity.Error, true);

        foreach (var member in tree.GetCompilationUnitRoot().DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            var name = member switch
            {
                MethodDeclarationSyntax m => m.Identifier.Text,
                PropertyDeclarationSyntax p => p.Identifier.Text,
                _ => null,
            };
            if (name is not null && !declared.Contains(name))
                yield return Diagnostic.Create(descriptor, member.GetLocation(), listed.Name, name);
        }
    }

    private static IEnumerable<Type> LibraryInterfaces() =>
        new[] { typeof(IEmbeddingService), typeof(IReranker), typeof(Flux.Abstractions.ITextCompletionService) }
            .Select(t => t.Assembly)
            .Distinct()
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => t.IsInterface);

    private static string StatementsAsMethod(Block block)
    {
        var lines = block.Code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        // A using line may carry a trailing comment («using X;  // what it brings»).
        static bool IsUsingDirective(string l)
        {
            var t = Regex.Replace(l, @"\s*//.*$", "").Trim();
            return t.StartsWith("using ", StringComparison.Ordinal) && t.EndsWith(';') && !t.StartsWith("using var ", StringComparison.Ordinal);
        }

        var body = string.Join("\n", lines.Where(l => !IsUsingDirective(l)));
        var standIns = StandIns
            .Where(s => Regex.IsMatch(body, $@"\b{s.Name}\b")
                        && !Regex.IsMatch(body, $@"\b(var|[A-Z][\w<>?,.\s]*)\s+{s.Name}\s*[=;]"))
            .Select(s => s.Declaration);
        var web = body.Contains("WebApplication", StringComparison.Ordinal) ? WebImplicitUsings : "";
        var method = Regex.Replace(block.Key, @"\W", "_");

        return new StringBuilder()
            .AppendLine(string.Join("\n", lines.Where(IsUsingDirective)))
            .AppendLine(StatementContextUsings)
            .AppendLine(web)
            .AppendLine($"internal static class Block_{method}")
            .AppendLine("{")
            .AppendLine("    internal static async Task RunAsync(string[] args, CancellationToken cancellationToken)")
            .AppendLine("    {")
            .AppendLine(string.Join("\n", standIns))
            .AppendLine(body)
            .AppendLine("        await Task.CompletedTask;")
            .AppendLine("    }")
            .AppendLine("}")
            .ToString();
    }

    private static List<Block> ReadBlocks(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var blocks = new List<Block>();
        var heading = "(top)";
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('#'))
                heading = lines[i].TrimStart('#').Trim();
            if (lines[i].Trim() != "```csharp")
                continue;

            var start = i + 1;
            var code = new StringBuilder();
            for (i++; i < lines.Length && lines[i].Trim() != "```"; i++)
                code.AppendLine(lines[i]);
            blocks.Add(new Block($"line {start}: {heading}", heading, code.ToString()));
        }

        return blocks;
    }

    private static List<MetadataReference> References()
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
        {
            foreach (var path in trusted.Split(Path.PathSeparator).Where(p => p.Length > 0))
                paths.TryAdd(Path.GetFileName(path), path);
        }

        // Everything the test output carries: the FluxIndex packages, LMSupply, the OpenAI SDK and their dependencies.
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
        {
            try
            {
                AssemblyName.GetAssemblyName(path);
                paths.TryAdd(Path.GetFileName(path), path);
            }
            catch (BadImageFormatException)
            {
                // a native library next to the managed ones
            }
        }

        return paths.Values.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
    }

    private static string GuideFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FluxIndex.slnx")))
            dir = dir.Parent;
        return Path.Combine(
            dir?.FullName ?? throw new InvalidOperationException("FluxIndex.slnx not found above the test output directory"),
            GuidePath);
    }
}
