using System.Globalization;
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
/// Compiles the C# blocks of the guides the way a reader assembles them: a guide defines types in some blocks and uses them
/// in others, so every block of one document goes into one compilation. <see cref="DocsSnippetRosterTests"/> only checks that
/// a called name exists somewhere — the provider guide once called <c>OpenAIClient.GetEmbeddingsAsync</c> from a retired SDK
/// and passed, because an unrelated interface in a dependency happened to declare a method of that name.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A block that declares types is one source file, as written.</item>
/// <item>A block of statements becomes the body of its own method; its <c>using</c> lines are hoisted, and the document's
/// stand-ins are declared when the block uses the name without declaring it — values the surrounding text gives the
/// reader. A block that mixes both keeps its types at file level and its statements in the method.</item>
/// <item>A block of bare members (a test method without its class) is wrapped in a class.</item>
/// <item>A block that only restates library types is a listing: compiled, it would declare a second type of that name and
/// check nothing, so each member it lists is looked up on the real type instead — by name, then by signature.</item>
/// <item>A block that is deliberately not a program is named in its document's <see cref="Document.Fragments"/> with the
/// reason; that list may only shrink.</item>
/// </list>
/// </remarks>
public class DocsSnippetCompileTests
{
    private sealed record Document(
        string Path,
        // The document's C# blocks, at least — a scan that finds fewer has lost the document, and passing then proves nothing.
        int MinimumBlocks,
        int MinimumListings,
        // What a reader of the document has from its opening blocks; added to statement blocks, and to type blocks when
        // UsingsForTypeBlocks — the provider guide's type blocks carry their own usings and are checked as written.
        string Usings,
        bool UsingsForTypeBlocks,
        (string Name, string Declaration)[] StandIns,
        IReadOnlyDictionary<string, string> Fragments);

    // What a reader's project has without writing it: the SDK's implicit usings.
    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    // The guides' «Your…» names stand for the reader's own client or store, with the shape the guide calls it with; what
    // the block does with the result is still checked against the real types.
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
        internal static class YourVectorDatabase
        {
            public static Task UpsertAsync(FluxIndex.Core.Domain.Entities.DocumentChunk chunk, CancellationToken cancellationToken) => Task.CompletedTask;
            public static Task<IReadOnlyList<(FluxIndex.Core.Domain.Entities.DocumentChunk Chunk, float Score)>> QueryAsync(
                float[] vector, int topK, Dictionary<string, object>? filters, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<(FluxIndex.Core.Domain.Entities.DocumentChunk, float)>>([]);
            public static Task<FluxIndex.Core.Domain.Entities.DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken)
                => Task.FromResult<FluxIndex.Core.Domain.Entities.DocumentChunk?>(null);
            public static Task<bool> DeleteAsync(string id, CancellationToken cancellationToken) => Task.FromResult(false);
            public static Task<IEnumerable<FluxIndex.Core.Domain.Entities.DocumentChunk>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken)
                => Task.FromResult<IEnumerable<FluxIndex.Core.Domain.Entities.DocumentChunk>>([]);
            public static Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken) => Task.FromResult(false);
            public static Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(0);
            public static Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
        // A store the reader writes themselves (the reference shows one); here an empty one of that shape.
        internal sealed class YourVectorStore : FluxIndex.Core.Application.Services.Base.VectorStoreBase
        {
            public YourVectorStore(string endpoint) => _ = endpoint;
            protected override Task<string> StoreCoreAsync(FluxIndex.Core.Domain.Entities.DocumentChunk chunk, CancellationToken cancellationToken) => Task.FromResult(chunk.Id);
            protected override Task<FluxIndex.Core.Domain.Entities.DocumentChunk?> GetCoreAsync(string id, CancellationToken cancellationToken) => YourVectorDatabase.GetAsync(id, cancellationToken);
            protected override Task<IEnumerable<FluxIndex.Core.Application.Utilities.ScoredChunk>> SearchCoreAsync(float[] queryEmbedding, int topK, Dictionary<string, object>? filters, CancellationToken cancellationToken)
                => Task.FromResult<IEnumerable<FluxIndex.Core.Application.Utilities.ScoredChunk>>([]);
            protected override Task<bool> DeleteCoreAsync(string id, CancellationToken cancellationToken) => Task.FromResult(false);
            protected override Task<bool> UpdateCoreAsync(FluxIndex.Core.Domain.Entities.DocumentChunk chunk, CancellationToken cancellationToken) => Task.FromResult(false);
            protected override Task<IEnumerable<FluxIndex.Core.Domain.Entities.DocumentChunk>> GetByDocumentIdCoreAsync(string documentId, CancellationToken cancellationToken) => YourVectorDatabase.GetByDocumentIdAsync(documentId, cancellationToken);
            protected override Task<bool> DeleteByDocumentIdCoreAsync(string documentId, CancellationToken cancellationToken) => Task.FromResult(false);
            protected override Task<int> CountCoreAsync(CancellationToken cancellationToken) => Task.FromResult(0);
            protected override Task ClearCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
        """;

    private const string WebImplicitUsings = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Microsoft.Extensions.Logging;
        """;

    private static readonly (string Name, string Declaration)[] CommonStandIns =
    [
        ("services", "Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();"),
        ("apiKey", "string apiKey = \"\";"),
        ("cohereApiKey", "string cohereApiKey = \"\";"),
        ("connectionString", "string connectionString = \"\";"),
        ("loggerFactory", "Microsoft.Extensions.Logging.ILoggerFactory loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;"),
    ];

    // Values the guides' running text gives the reader: a resolved service, the query being discussed, a cancellation token.
    // Each is typed as the real API takes it, so a block that passes it to the wrong parameter still fails.
    private static (string Name, string Declaration) StandIn(string type, string name) => (name, $"{type} {name} = default!;");

    private static readonly (string Name, string Declaration)[] ServiceStandIns =
    [
        StandIn("System.IServiceProvider", "serviceProvider"),
        StandIn("System.Threading.CancellationToken", "ct"),
        StandIn("string", "query"),
        StandIn("FluxIndex.SDK.IFluxIndexContext", "context"),
        ("builder", "FluxIndex.SDK.FluxIndexContextBuilder builder = FluxIndex.SDK.FluxIndexContext.CreateBuilder();"),
        StandIn("FluxIndex.Core.Application.Interfaces.IVectorStore", "vectorStore"),
        StandIn("FluxIndex.Core.Application.Interfaces.IKeywordSearchService", "keywordSearch"),
        StandIn("FluxIndex.Core.Application.Interfaces.IEmbeddingService", "embeddingService"),
        StandIn("FluxIndex.Core.Application.Interfaces.IGraphRAGService", "graphRag"),
        StandIn("FluxIndex.Core.Application.Interfaces.IGraphStore", "graphStore"),
        StandIn("System.Collections.Generic.IEnumerable<FluxIndex.Core.Domain.Entities.DocumentChunk>", "chunks"),
        StandIn("string", "partition"),
    ];

    private static readonly Document[] Documents =
    [
        new("docs/AI_PROVIDER_INTEGRATION.md", 19, 3,
            """
            using FluxIndex.Core.Application.Interfaces;
            using Microsoft.Extensions.DependencyInjection;
            """,
            UsingsForTypeBlocks: false, CommonStandIns, new Dictionary<string, string>(StringComparer.Ordinal)),
        // The packages its Installation section adds, and the contracts every block speaks in; a block that needs an
        // extension namespace beyond those says so itself.
        new("docs/GUIDE.md", 26, 0,
            """
            using FluxIndex.SDK;
            using FluxIndex.Storage.SQLite;
            using FluxIndex.Storage.PostgreSQL;
            using FluxIndex.Storage.Qdrant;
            using FluxIndex.Storage.Neo4j;
            using FluxIndex.Cache.Redis;
            using FluxIndex.Core.Application.Interfaces;
            using FluxIndex.Core.Domain.Entities;
            using Microsoft.Extensions.DependencyInjection;
            """,
            UsingsForTypeBlocks: true,
            [
                .. CommonStandIns, .. ServiceStandIns,
                StandIn("string", "connStr"), StandIn("string", "conn"), StandIn("string", "uri"), StandIn("string", "user"),
                StandIn("string", "pass"), StandIn("string", "pgConn"), StandIn("string", "qdrantHost"), StandIn("string", "neo4jUri"),
                StandIn("string", "neo4jUser"), StandIn("string", "neo4jPassword"), StandIn("string", "keyword"), StandIn("string", "documentId"),
                StandIn("FluxIndex.Core.Application.Interfaces.IEmbeddingService", "myEmbedder"),
                StandIn("FluxIndex.Core.Application.Interfaces.IEmbeddingService", "myEmbeddingInstance"),
                StandIn("string[]", "files"), StandIn("System.IServiceProvider", "provider"),
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)),
        // What its opening block imports, plus the logging a reader who logs already has.
        new("docs/ADVANCED_RAG.md", 24, 0,
            """
            using FluxIndex.Core.Application.Interfaces;
            using FluxIndex.Core.Application.Services;
            using FluxIndex.Core.Application.Services.Reranking;
            using FluxIndex.Core.Domain.Entities;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Logging;
            """,
            UsingsForTypeBlocks: true,
            [
                .. CommonStandIns, .. ServiceStandIns,
                StandIn("FluxIndex.Core.Application.Interfaces.IAdvancedEntityExtractionService", "extractor"),
                StandIn("FluxIndex.Core.Application.Interfaces.ILeidenCommunityService", "communityService"),
                StandIn("FluxIndex.Core.Application.Interfaces.CommunityHierarchy", "hierarchy"),
                StandIn("FluxIndex.Core.Application.Interfaces.DynamicFusionConfiguration", "config"),
                StandIn("System.Collections.Generic.IReadOnlyList<FluxIndex.Core.Application.Interfaces.ListwiseRerankResult>", "reranked"),
                StandIn("System.Collections.Generic.IEnumerable<FluxIndex.Core.Application.Interfaces.RetrievalCandidate>", "searchResults"),
                StandIn("System.Collections.Generic.IEnumerable<FluxIndex.Core.Domain.Entities.DocumentChunk>", "documentChunks"),
                StandIn("System.Collections.Generic.IEnumerable<FluxIndex.Core.Domain.Entities.DocumentChunk>", "newChunks"),
                StandIn("System.Collections.Generic.IEnumerable<string>", "documentChunkIds"),
                StandIn("System.Collections.Generic.IEnumerable<string>", "removedChunkIds"),
                StandIn("System.Collections.Generic.IEnumerable<FluxIndex.Core.Domain.Entities.Document>", "documents"),
                StandIn("System.Collections.Generic.IEnumerable<FluxIndex.Core.Application.Interfaces.LeidenChunk>", "leidenChunks"),
                StandIn("string", "documentContent"),
                StandIn("Microsoft.Extensions.Logging.ILogger", "_logger"),
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)),
        // The contracts every block speaks in; the SDK's own HybridSearchOptions would collide with the core one the
        // hybrid-search blocks use, so a block that builds a context imports the SDK itself.
        new("docs/REFERENCE.md", 22, 3,
            """
            using FluxIndex.Core.Application.Interfaces;
            using FluxIndex.Core.Domain.Entities;
            using Microsoft.Extensions.DependencyInjection;
            """,
            UsingsForTypeBlocks: true,
            [
                .. CommonStandIns, .. ServiceStandIns,
                StandIn("FluxIndex.Core.Application.Interfaces.IHybridSearchService", "hybridSearch"),
                StandIn("FluxIndex.Core.Application.Interfaces.CommunityHierarchy", "hierarchy"),
                StandIn("string", "startId"), StandIn("string", "endId"), StandIn("string", "content"),
                StandIn("string", "oldDocId"), StandIn("string", "newDocId"), StandIn("string", "newPath"),
                StandIn("System.Collections.Generic.IReadOnlyDictionary<string, string>", "chunkIdMap"),
                StandIn("System.Collections.Generic.IReadOnlyDictionary<string, object?>", "sameUpdates"),
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)),
    ];

    private static readonly Dictionary<string, Lazy<DocumentCompilation>> Compiled = Documents.ToDictionary(
        d => d.Path,
        d => new Lazy<DocumentCompilation>(() => CompileDocument(d, ReadBlocks(File.ReadAllText(RepositoryFile(d.Path))))),
        StringComparer.Ordinal);

    public static TheoryData<string> Blocks()
    {
        var data = new TheoryData<string>();
        foreach (var document in Documents)
        {
            foreach (var block in ReadBlocks(File.ReadAllText(RepositoryFile(document.Path))))
                data.Add($"{document.Path} {block.Key}");
        }
        return data;
    }

    public static TheoryData<string> DocumentPaths() => new(Documents.Select(d => d.Path));

    [Theory]
    [MemberData(nameof(Blocks))]
    public void DocsBlock_Compiles(string key)
    {
        var document = Documents.Single(d => key.StartsWith(d.Path + " ", StringComparison.Ordinal));
        var blockKey = key[(document.Path.Length + 1)..];
        var compiled = Compiled[document.Path].Value;
        if (compiled.Fragments.TryGetValue(blockKey, out var reason))
            Assert.Skip($"not a program: {reason}");

        var errors = compiled.Errors.Where(e => e.Location.SourceTree?.FilePath == blockKey).ToList();

        Assert.True(errors.Count == 0,
            $"{document.Path} block {blockKey} does not compile against the current API:\n" +
            string.Join("\n", errors.Select(e => e.ToString())) + "\n--- source ---\n" + compiled.Sources[blockKey]);
    }

    [Theory]
    [MemberData(nameof(DocumentPaths))]
    public void Document_IsFound_AndCompilesAsAWhole(string path)
    {
        var document = Documents.Single(d => d.Path == path);
        var compiled = Compiled[path].Value;

        Assert.True(compiled.Sources.Count >= document.MinimumBlocks, $"expected {path}'s C# blocks, found {compiled.Sources.Count}");
        Assert.True(compiled.Listings >= document.MinimumListings, $"expected {path}'s type listings, found {compiled.Listings}");
        // A fragment entry must name a heading that still has a block — a stale entry would excuse whatever lands there next.
        Assert.All(document.Fragments.Keys, heading => Assert.Contains(compiled.Headings, h => h == heading));
        // Errors not tied to one block (a duplicate type across blocks, a missing reference, a stand-in or placeholder that
        // no longer matches the API) fail here.
        Assert.Empty(compiled.Errors
            .Where(e => e.Location.SourceTree is not { } tree || !compiled.Sources.ContainsKey(tree.FilePath))
            .Select(e => e.ToString()));
    }

    /// <summary>Positive control: one compilation of the provider guide rejects a block that calls the retired SDK.</summary>
    [Fact]
    public void Compile_RejectsTheRetiredAzureSdkCall()
    {
        var document = Documents[0];
        var blocks = ReadBlocks(File.ReadAllText(RepositoryFile(document.Path)));
        blocks.Add(new Block("control", "control", """
            using Azure.AI.OpenAI;
            var client = new OpenAIClient(apiKey);
            var response = await client.GetEmbeddingsAsync(new EmbeddingsOptions("m", new[] { "x" }));
            """));

        var compiled = CompileDocument(document, blocks);

        Assert.Contains(compiled.Errors, e => e.Location.SourceTree?.FilePath == "control");
    }

    /// <summary>Positive control: a listing that names a member the interface does not have is reported.</summary>
    [Fact]
    public void Listing_ReportsAMemberTheInterfaceDoesNotHave()
    {
        var compiled = CompileDocument(Documents[0], [new("listing", "listing", """
            public interface IReranker
            {
                Task<IEnumerable<RerankResult>> RerankAsync(string query, IEnumerable<RetrievalResult> candidates, int topN = 5, CancellationToken cancellationToken = default);
                Task<float> ScoreEverythingAsync(string query);
            }
            """)]);

        Assert.Contains(compiled.Errors, e => e.Location.SourceTree?.FilePath == "listing" && e.GetMessage(CultureInfo.InvariantCulture).Contains("ScoreEverythingAsync", StringComparison.Ordinal));
    }

    /// <summary>Positive control: a listing whose member exists under that name with other parameters is reported.</summary>
    [Fact]
    public void Listing_ReportsAMemberWhoseSignatureDiffers()
    {
        var compiled = CompileDocument(Documents[0], [new("listing", "listing", """
            public interface IReranker
            {
                Task<IEnumerable<RerankResult>> RerankAsync(string query, IEnumerable<RetrievalResult> candidates, int topN = 5, CancellationToken cancellationToken = default);
            }
            """)]);

        Assert.Contains(compiled.Errors, e => e.Id == "GUIDE002" && e.GetMessage(CultureInfo.InvariantCulture).Contains("RerankAsync", StringComparison.Ordinal));
    }

    /// <summary>Positive control: a listing of a class (an options type) that names a property it does not have is reported.</summary>
    [Fact]
    public void ClassListing_ReportsAPropertyTheClassDoesNotHave()
    {
        var compiled = CompileDocument(Documents[0], [new("listing", "listing", """
            public class GraphRAGQueryOptions
            {
                public bool IncludeRelationships { get; set; }
                public float NoSuchThreshold { get; set; }
            }
            """)]);

        Assert.Equal(1, compiled.Listings);
        Assert.Contains(compiled.Errors, e => e.Id == "GUIDE001" && e.GetMessage(CultureInfo.InvariantCulture).Contains("NoSuchThreshold", StringComparison.Ordinal));
    }

    private sealed record Block(string Key, string Heading, string Code);

    private sealed record DocumentCompilation(
        IReadOnlyList<Diagnostic> Errors,
        IReadOnlyDictionary<string, string> Sources,
        IReadOnlyDictionary<string, string> Fragments,
        IReadOnlyList<string> Headings,
        int Listings);

#pragma warning disable RS2008 // these label documentation findings in a test; no analyzer ships them
    private static readonly DiagnosticDescriptor MissingMember = new(
        "GUIDE001", "Listed member does not exist",
        "The listing of {0} names '{1}', which the type does not declare", "Docs", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor DifferentSignature = new(
        "GUIDE002", "Listed member has another signature",
        "The listing of {0} shows '{1}', but the type declares {2}", "Docs", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor AmbiguousListing = new(
        "GUIDE003", "Listed type is ambiguous",
        "The listing of {0} matches {1}; declare the namespace in the block", "Docs", DiagnosticSeverity.Error, true);
#pragma warning restore RS2008

    private static DocumentCompilation CompileDocument(Document document, List<Block> blocks)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(ImplicitUsings, parse, path: "(implicit usings)"),
            CSharpSyntaxTree.ParseText(Placeholders, parse, path: "(placeholders)"),
        };
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var fragments = new Dictionary<string, string>(StringComparer.Ordinal);
        var listingErrors = new List<Diagnostic>();
        var listings = 0;

        foreach (var block in blocks)
        {
            sources[block.Key] = block.Code;
            if (document.Fragments.TryGetValue(block.Heading, out var reason))
            {
                fragments[block.Key] = reason;
                continue;
            }

            var root = CSharpSyntaxTree.ParseText(block.Code, parse).GetCompilationUnitRoot();
            if (ListedTypes(root) is { } listed)
            {
                listings += listed.Count;
                var tree = CSharpSyntaxTree.ParseText(block.Code, parse, path: block.Key);
                listingErrors.AddRange(CheckListing(tree));
                continue;
            }

            var source = Assemble(document, block, root);
            sources[block.Key] = source;
            trees.Add(CSharpSyntaxTree.ParseText(source, parse, path: block.Key));
        }

        var compilation = CSharpCompilation.Create(
            "DocsSnippets", trees, References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Concat(listingErrors)
            .ToList();
        return new DocumentCompilation(errors, sources, fragments, blocks.Select(b => b.Heading).Distinct().ToList(), listings);
    }

    /// <summary>
    /// One block as a source file: its usings hoisted, its type declarations at file level, bare members in a class, and its
    /// statements in a method that declares the stand-ins it uses without declaring them.
    /// </summary>
    private static string Assemble(Document document, Block block, CompilationUnitSyntax root)
    {
        var statements = new StringBuilder();
        var types = new StringBuilder();
        var members = new StringBuilder();
        foreach (var member in root.Members)
        {
            switch (member)
            {
                case GlobalStatementSyntax { Statement: LocalFunctionStatementSyntax local } when local.Modifiers.Any(IsAccessModifier) || local.AttributeLists.Count > 0:
                    members.Append(member.ToFullString());
                    break;
                case GlobalStatementSyntax:
                    statements.Append(member.ToFullString());
                    break;
                case BaseTypeDeclarationSyntax or BaseNamespaceDeclarationSyntax or DelegateDeclarationSyntax:
                    types.Append(member.ToFullString());
                    break;
                default:
                    members.Append(member.ToFullString());
                    break;
            }
        }

        var hasStatements = statements.Length > 0;
        var usings = string.Concat(root.Usings.Select(u => u.ToFullString()))
                     + string.Concat(root.Externs.Select(e => e.ToFullString()));
        var code = statements.ToString();
        var name = Regex.Replace(block.Key, @"\W", "_");

        var source = new StringBuilder()
            .AppendLine(usings)
            .AppendLine(hasStatements || members.Length > 0 || document.UsingsForTypeBlocks ? document.Usings : "")
            .AppendLine(code.Contains("WebApplication", StringComparison.Ordinal) ? WebImplicitUsings : "")
            .AppendLine(types.ToString());
        if (members.Length > 0)
        {
            source.AppendLine(CultureInfo.InvariantCulture, $"public class Members_{name}")
                .AppendLine("{")
                .AppendLine(members.ToString())
                .AppendLine("}");
        }
        if (hasStatements)
        {
            var standIns = UndeclaredNames(code, document.StandIns.Select(s => s.Name));
            source.AppendLine(CultureInfo.InvariantCulture, $"internal static class Block_{name}")
                .AppendLine("{")
                .AppendLine("    internal static async Task RunAsync(string[] args, CancellationToken cancellationToken)")
                .AppendLine("    {")
                .AppendLine(string.Join("\n", document.StandIns.Where(s => standIns.Contains(s.Name)).Select(s => s.Declaration)))
                .AppendLine(code)
                .AppendLine("        await Task.CompletedTask;")
                .AppendLine("    }")
                .AppendLine("}");
        }

        return source.ToString();
    }

    private static bool IsAccessModifier(SyntaxToken token) =>
        token.IsKind(SyntaxKind.PublicKeyword) || token.IsKind(SyntaxKind.PrivateKeyword)
        || token.IsKind(SyntaxKind.InternalKeyword) || token.IsKind(SyntaxKind.ProtectedKeyword);

    /// <summary>The candidate names the statements read as a value without declaring them (a local, a parameter, a loop variable).</summary>
    private static HashSet<string> UndeclaredNames(string statements, IEnumerable<string> candidates)
    {
        var root = CSharpSyntaxTree.ParseText(statements, new CSharpParseOptions(LanguageVersion.Latest, kind: SourceCodeKind.Script)).GetRoot();
        var declared = root.DescendantNodes().Select(n => n switch
            {
                VariableDeclaratorSyntax v => v.Identifier.Text,
                ForEachStatementSyntax f => f.Identifier.Text,
                SingleVariableDesignationSyntax s => s.Identifier.Text,
                ParameterSyntax p => p.Identifier.Text,
                LocalFunctionStatementSyntax l => l.Identifier.Text,
                _ => null,
            })
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var used = root.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(i => i.Parent is not NameColonSyntax
                        && !(i.Parent is MemberAccessExpressionSyntax access && access.Name == i)
                        && !(i.Parent is AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax } assignment && assignment.Left == i))
            .Select(i => i.Identifier.Text)
            .ToHashSet(StringComparer.Ordinal);

        return candidates.Where(c => used.Contains(c) && !declared.Contains(c)).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The library types a block restates, when it declares nothing but types named like library types of the same kind.</summary>
    private static List<BaseTypeDeclarationSyntax>? ListedTypes(CompilationUnitSyntax root)
    {
        var members = root.Members.SelectMany(m => m is BaseNamespaceDeclarationSyntax ns ? ns.Members : [m]).ToList();
        if (members.Count == 0 || !members.All(m => m is BaseTypeDeclarationSyntax))
            return null;

        // A class that implements something is a sample, even under a library name; an interface keeps its default members.
        var declared = members.Cast<BaseTypeDeclarationSyntax>().ToList();
        if (declared.Any(d => d is not InterfaceDeclarationSyntax && d.DescendantNodes().Any(n => n is BlockSyntax or ArrowExpressionClauseSyntax)))
            return null;
        return declared.All(d => LibraryCandidates(d).Count > 0) ? declared : null;
    }

    private static List<Type> LibraryCandidates(BaseTypeDeclarationSyntax declaration)
    {
        var name = declaration.Identifier.Text;
        var ns = declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
        return LibraryTypes.Value
            .Where(t => t.Name == name && (ns is null || t.Namespace == ns))
            .Where(t => declaration switch
            {
                InterfaceDeclarationSyntax => t.IsInterface,
                EnumDeclarationSyntax => t.IsEnum,
                StructDeclarationSyntax => t.IsValueType && !t.IsEnum,
                RecordDeclarationSyntax r when r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => t.IsValueType && !t.IsEnum,
                _ => t.IsClass,
            })
            .ToList();
    }

    private static IEnumerable<Diagnostic> CheckListing(SyntaxTree tree)
    {
        var declarations = tree.GetCompilationUnitRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .Where(d => d.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax);
        foreach (var declaration in declarations)
        {
            var candidates = LibraryCandidates(declaration);
            if (candidates.Count > 1)
            {
                yield return Diagnostic.Create(AmbiguousListing, declaration.Identifier.GetLocation(), declaration.Identifier.Text,
                    string.Join(", ", candidates.Select(c => c.FullName)));
                continue;
            }

            var listed = candidates[0];
            foreach (var member in declaration.ChildNodes().OfType<MemberDeclarationSyntax>())
            {
                if (CheckMember(listed, member) is { } diagnostic)
                    yield return diagnostic;
            }
        }
    }

    private static Diagnostic? CheckMember(Type listed, MemberDeclarationSyntax member)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        var lookup = listed.IsInterface ? listed.GetInterfaces().Prepend(listed).ToArray() : [listed];

        switch (member)
        {
            case MethodDeclarationSyntax method:
            {
                var name = method.Identifier.Text;
                var real = lookup.SelectMany(t => t.GetMethods(Flags)).Where(m => m.Name == name).ToList();
                if (real.Count == 0)
                    return Diagnostic.Create(MissingMember, method.Identifier.GetLocation(), listed.Name, name);

                var shown = Signature(Format(method.ReturnType), name, method.ParameterList.Parameters.Select(p => p.Type is null ? "?" : Format(p.Type)));
                var declared = real.Select(m => Signature(Format(m.ReturnType), name, m.GetParameters().Select(p => Format(p.ParameterType)))).ToList();
                return declared.Contains(shown, StringComparer.Ordinal)
                    ? null
                    : Diagnostic.Create(DifferentSignature, method.Identifier.GetLocation(), listed.Name, shown, string.Join(" | ", declared));
            }
            case PropertyDeclarationSyntax property:
            {
                var name = property.Identifier.Text;
                var real = lookup.Select(t => t.GetProperty(name, Flags)).FirstOrDefault(p => p is not null);
                if (real is null)
                    return Diagnostic.Create(MissingMember, property.Identifier.GetLocation(), listed.Name, name);

                var shown = Format(property.Type);
                var declared = Format(real.PropertyType);
                return shown == declared
                    ? null
                    : Diagnostic.Create(DifferentSignature, property.Identifier.GetLocation(), listed.Name, $"{shown} {name}", $"{declared} {name}");
            }
            case EnumMemberDeclarationSyntax enumMember:
                return Enum.GetNames(listed).Contains(enumMember.Identifier.Text, StringComparer.Ordinal)
                    ? null
                    : Diagnostic.Create(MissingMember, enumMember.Identifier.GetLocation(), listed.Name, enumMember.Identifier.Text);
            case FieldDeclarationSyntax field:
            {
                var name = field.Declaration.Variables[0].Identifier.Text;
                return listed.GetField(name, Flags) is not null || listed.GetProperty(name, Flags) is not null
                    ? null
                    : Diagnostic.Create(MissingMember, field.Declaration.Variables[0].Identifier.GetLocation(), listed.Name, name);
            }
            default:
                return null;
        }
    }

    private static string Signature(string returnType, string name, IEnumerable<string> parameters) =>
        $"{returnType} {name}({string.Join(", ", parameters)})";

    /// <summary>A type as a listing writes it: simple names, C# keywords, nullability not distinguished.</summary>
    private static string Format(TypeSyntax type) => type switch
    {
        PredefinedTypeSyntax p => p.Keyword.Text,
        NullableTypeSyntax n => Format(n.ElementType),
        ArrayTypeSyntax a => Format(a.ElementType) + string.Concat(a.RankSpecifiers.Select(_ => "[]")),
        QualifiedNameSyntax q => Format(q.Right),
        AliasQualifiedNameSyntax a => Format(a.Name),
        GenericNameSyntax g => $"{g.Identifier.Text}<{string.Join(", ", g.TypeArgumentList.Arguments.Select(Format))}>",
        IdentifierNameSyntax i => i.Identifier.Text,
        TupleTypeSyntax t => $"({string.Join(", ", t.Elements.Select(e => Format(e.Type)))})",
        _ => type.ToString(),
    };

    private static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(string)] = "string", [typeof(int)] = "int", [typeof(long)] = "long", [typeof(float)] = "float",
        [typeof(double)] = "double", [typeof(bool)] = "bool", [typeof(object)] = "object", [typeof(void)] = "void",
        [typeof(byte)] = "byte", [typeof(char)] = "char", [typeof(decimal)] = "decimal", [typeof(short)] = "short",
        [typeof(uint)] = "uint", [typeof(ulong)] = "ulong",
    };

    private static string Format(Type type)
    {
        if (type.IsByRef)
            return Format(type.GetElementType()!);
        if (type.IsArray)
            return Format(type.GetElementType()!) + "[]";
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return Format(underlying);
        if (Keywords.TryGetValue(type, out var keyword))
            return keyword;
        if (!type.IsGenericType)
            return type.Name;

        var arguments = type.GetGenericArguments().Select(Format);
        if (type.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true)
            return $"({string.Join(", ", arguments)})";
        return $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", arguments)}>";
    }

    /// <summary>The public types of the FluxIndex packages and the shared contracts they implement.</summary>
    private static readonly Lazy<List<Type>> LibraryTypes = new(() =>
    {
        _ = typeof(IEmbeddingService);
        _ = typeof(Flux.Abstractions.ITextCompletionService);
        return Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
            .Where(p => Path.GetFileName(p) is var f
                        && (f.StartsWith("FluxIndex.", StringComparison.Ordinal) || f == "Flux.Abstractions.dll")
                        && !f.EndsWith(".Tests.dll", StringComparison.Ordinal))
            .Select(p => Assembly.Load(AssemblyName.GetAssemblyName(p)))
            .SelectMany(a =>
            {
                try { return a.GetExportedTypes(); }
                catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>().Where(t => t.IsPublic).ToArray(); }
            })
            .ToList();
    });

    private static List<Block> ReadBlocks(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var blocks = new List<Block>();
        var heading = "(top)";
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('#'))
                heading = lines[i].TrimStart('#').Trim();

            // A block inside a blockquote carries the quote marker on every line.
            var quoted = lines[i].TrimStart().StartsWith('>');
            if (!Regex.IsMatch(Unquote(lines[i], quoted).Trim(), @"^```(csharp|cs|c#)$", RegexOptions.IgnoreCase))
                continue;

            var start = i + 1;
            var code = new StringBuilder();
            for (i++; i < lines.Length && Unquote(lines[i], quoted).Trim() != "```"; i++)
                code.AppendLine(Unquote(lines[i], quoted));
            blocks.Add(new Block($"line {start}: {heading}", heading, code.ToString()));
        }

        return blocks;
    }

    private static string Unquote(string line, bool quoted)
    {
        if (!quoted)
            return line;
        var t = line.TrimStart();
        if (!t.StartsWith('>'))
            return line;
        t = t[1..];
        return t.StartsWith(' ') ? t[1..] : t;
    }

    private static List<MetadataReference> References()
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
        {
            foreach (var path in trusted.Split(Path.PathSeparator).Where(p => p.Length > 0))
                paths.TryAdd(Path.GetFileName(path), path);
        }

        // Everything the test output carries: the FluxIndex packages, LMSupply, the OpenAI SDK, xUnit and their dependencies.
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

    private static string RepositoryFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FluxIndex.slnx")))
            dir = dir.Parent;
        return Path.Combine(
            dir?.FullName ?? throw new InvalidOperationException("FluxIndex.slnx not found above the test output directory"),
            relative);
    }
}
