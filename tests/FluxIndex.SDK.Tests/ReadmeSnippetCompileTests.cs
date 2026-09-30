using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluxIndex.Storage.SQLite;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Compiles every <c>```csharp</c> block in README.md against the current assemblies. <see cref="DocsSnippetRosterTests"/>
/// checks that the names a snippet mentions exist somewhere; a compiler also checks the receiver, the arguments, the
/// members read and the namespaces — what a reader who copies the block actually runs into.
/// </summary>
/// <remarks>
/// A block is compiled as a top-level program: its <c>using</c> lines are hoisted, the common usings below are added, and
/// the stand-ins below are declared when the block uses the name without declaring it — values a reader already has from
/// the surrounding text (a built context, a connection string), not part of what the block shows.
/// </remarks>
public class ReadmeSnippetCompileTests
{
    // A block that is deliberately not a program (a signature sketch, pseudocode) is listed here by the heading it sits
    // under, with the reason. Shrink this, never grow it silently.
    private static readonly Dictionary<string, string> Fragments = new(StringComparer.Ordinal);

    private const string CommonUsings = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using FluxIndex.SDK;
        using FluxIndex.Core.Application.Interfaces;
        using FluxIndex.Core.Application.Services.Base;
        using FluxIndex.Core.Application.Services.KeywordSearch;
        using FluxIndex.Storage.SQLite;
        using FluxIndex.Storage.Qdrant;
        using FluxIndex.Storage.PostgreSQL;
        using FluxIndex.Providers.OpenAI.Extensions;
        using LMSupply.Embedder;
        using Microsoft.Extensions.DependencyInjection;
        """;

    private static readonly (string Name, string Declaration)[] StandIns =
    [
        ("context", "IFluxIndexContext context = null!;"),
        ("builder", "FluxIndexContextBuilder builder = FluxIndexContext.CreateBuilder();"),
        ("services", "IServiceCollection services = null!;"),
        ("vectorStore", "IVectorStore vectorStore = null!;"),
        ("keywordSearch", "IKeywordSearchService keywordSearch = null!;"),
        ("apiKey", "string apiKey = \"\";"),
        ("connectionString", "string connectionString = \"\";"),
        ("metadataConnectionString", "string metadataConnectionString = \"\";"),
    ];

    private static readonly string[] AssembliesToLoad =
    [
        "FluxIndex.SDK", "FluxIndex.Core", "FluxIndex.Storage.SQLite", "FluxIndex.Storage.Qdrant", "FluxIndex.Storage.PostgreSQL",
        "FluxIndex.Providers.OpenAI", "FluxIndex.Providers.LMSupply", "LMSupply.Embedder", "LMSupply.Core",
        "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection.Abstractions",
    ];

    public static TheoryData<string> Blocks()
    {
        var data = new TheoryData<string>();
        foreach (var block in ReadBlocks())
            data.Add(block.Key);
        return data;
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void ReadmeBlock_Compiles(string key)
    {
        var block = ReadBlocks().Single(b => b.Key == key);
        if (Fragments.ContainsKey(block.Heading))
            return;

        var errors = Compile(block.Code);

        Assert.True(errors.IsEmpty,
            $"README block {key} does not compile against the current API:\n" +
            string.Join("\n", errors.Select(e => e.ToString())) + "\n--- source ---\n" + Program(block.Code));
    }

    [Fact]
    public void EveryReadmeBlock_IsFoundAndFragmentsNameRealHeadings()
    {
        var blocks = ReadBlocks();
        Assert.True(blocks.Count >= 10, $"expected the README's C# blocks, found {blocks.Count}");
        Assert.All(Fragments.Keys, heading => Assert.Contains(blocks, b => b.Heading == heading));
    }

    /// <summary>Positive control: the compiler rejects a call the API does not have.</summary>
    [Fact]
    public void Compile_RejectsAMemberThatDoesNotExist()
    {
        var errors = Compile("""
            var results = await context.Retriever.KeywordSearchAsync("RAG library", maxResults: 5);
            Console.WriteLine(results.First().Chunk.Text);
            """);

        Assert.NotEmpty(errors);
    }

    /// <summary>The Quick Start, run: a consumer that copies it gets a hit, with no model registered.</summary>
    [Fact]
    public async Task QuickStartFlow_FindsTheDocument()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluxindex-readme-{Guid.NewGuid():N}.db");
        try
        {
            await using (var context = FluxIndexContext.CreateBuilder().UseSQLite(path).AddSQLiteStorage().SuppressStartupMessages().Build())
            {
                await context.Indexer.IndexDocumentAsync("FluxIndex is a RAG library for .NET", "doc-001", cancellationToken: TestContext.Current.CancellationToken);
                var results = await context.Retriever.KeywordSearchAsync("RAG library", maxResults: 5, cancellationToken: TestContext.Current.CancellationToken);
                Assert.Contains(results, r => r.DocumentChunk.DocumentId == "doc-001");
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }

    private sealed record Block(string Key, string Heading, string Code);

    private static List<Block> ReadBlocks()
    {
        var lines = File.ReadAllText(ReadmePath()).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
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

    private static string Program(string code)
    {
        var lines = code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        // Blocks inside a list item are indented; a using directive is recognized on its trimmed text.
        bool IsUsingDirective(string l)
        {
            var t = l.Trim();
            return t.StartsWith("using ", StringComparison.Ordinal) && t.EndsWith(';') && !t.StartsWith("using var ", StringComparison.Ordinal);
        }

        var body = string.Join("\n", lines.Where(l => !IsUsingDirective(l)));
        var standIns = StandIns
            .Where(s => Regex.IsMatch(body, $@"\b{s.Name}\b")
                        && !Regex.IsMatch(body, $@"\b(var|[A-Z][\w<>?,\s]*)\s+{s.Name}\s*[=;]"))
            .Select(s => s.Declaration);

        // A block that declares a type has it after the statements: C# top-level statements must come first.
        var typeStart = Regex.Match(body, @"^(public |internal )?(sealed )?(class|record|interface) ", RegexOptions.Multiline);
        var statements = typeStart.Success ? body[..typeStart.Index] : body;
        var types = typeStart.Success ? body[typeStart.Index..] : "";
        if (typeStart.Success)
        {
            // The statements that follow the type in the README ("Register and use") move before it.
            var afterType = TrailingStatements(types);
            types = types[..^afterType.Length];
            statements += "\n" + afterType;
        }

        return string.Join("\n", lines.Where(IsUsingDirective)) + "\n" + CommonUsings + "\n"
               + string.Join("\n", standIns) + "\n" + statements + "\n" + types;
    }

    /// <summary>The text after the last top-level closing brace of the declared types.</summary>
    private static string TrailingStatements(string types)
    {
        var depth = 0;
        var end = 0;
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] == '{') depth++;
            else if (types[i] == '}' && --depth == 0) end = i + 1;
        }
        return types[end..];
    }

    private static ImmutableArray<Diagnostic> Compile(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(Program(code), new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "ReadmeSnippet", [tree], References(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
        return compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
    }

    private static List<MetadataReference> References()
    {
        foreach (var name in AssembliesToLoad)
            Assembly.Load(name);
        _ = typeof(SQLiteOptions);

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
            paths.UnionWith(trusted.Split(Path.PathSeparator).Where(p => p.Length > 0));
        paths.UnionWith(AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Length > 0)
            .Select(a => a.Location));
        return paths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
    }

    private static string ReadmePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FluxIndex.slnx")))
            dir = dir.Parent;
        return Path.Combine(
            dir?.FullName ?? throw new InvalidOperationException("FluxIndex.slnx not found above the test output directory"),
            "README.md");
    }
}
