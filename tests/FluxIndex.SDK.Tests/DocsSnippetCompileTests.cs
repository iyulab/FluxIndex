using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluxIndex.Core.Application.Interfaces;
using Iyu.Conventions.Testing.Snippets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Compiles the C# blocks of the guides the way a reader assembles them: a guide defines types in some blocks and uses them
/// in others, so every block of one document goes into one compilation (<see cref="SnippetCompiler.CompileDocument"/>).
/// The provider guide once called <c>OpenAIClient.GetEmbeddingsAsync</c> from a retired SDK; a name-only check passed it,
/// because an unrelated interface in a dependency happened to declare a method of that name.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The kit compiles each block as a file of its own: its types shared with the document, its statements in a method
/// of their own, with the document's stand-ins when a block uses the name without declaring it — values the surrounding
/// text gives the reader.</item>
/// <item>Before that, this test adds what a reader of these guides has that the kit cannot know: the ASP.NET usings for a
/// block that builds a <c>WebApplication</c>, and a class around bare members (a test method shown without its class).</item>
/// <item>A block that only restates library types is a listing: compiled, it would declare a second type of that name and
/// check nothing, so the kit looks each member it lists up on the real type instead — by name, then by signature
/// (<see cref="SnippetCompiler.CheckListings"/>).</item>
/// </list>
/// </remarks>
public class DocsSnippetCompileTests
{
    private sealed record Document(
        string Path,
        // The document's C# blocks, at least — a scan that finds fewer has lost the document, and passing then proves nothing.
        int MinimumBlocks,
        int MinimumListings,
        // What a reader of the document has from its opening blocks.
        string[] Usings,
        IReadOnlyDictionary<string, string> StandIns,
        // Blocks deliberately not a program, by heading, with the reason. Shrink it; never grow it silently.
        SnippetRoster? Fragments = null)
    {
        public SnippetRoster Roster => Fragments ?? NoFragments;
    }

    private static readonly SnippetRoster NoFragments = new(new Dictionary<string, string>(StringComparer.Ordinal));

    // What a reader's project has without writing it: the SDK's implicit usings.
    private static readonly string[] ImplicitUsings =
        ["System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Threading", "System.Threading.Tasks"];

    private const string WebUsings = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Microsoft.Extensions.Logging;

        """;

    // The guides' «Your…» names stand for the reader's own client or store, with the shape the guide calls it with; what
    // the block does with the result is still checked against the real types. Type stand-ins: added when a block names them.
    private static readonly Dictionary<string, string> Placeholders = new(StringComparer.Ordinal)
    {
        ["YourEmbeddingProvider"] = """
            internal static class YourEmbeddingProvider
            {
                public static Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<float>());
            }
            """,
        ["YourLLMProvider"] = """
            internal static class YourLLMProvider
            {
                public static Task<string> GenerateAsync(string prompt, int maxTokens, float temperature, CancellationToken cancellationToken) => Task.FromResult("");
            }
            """,
        ["YourRerankerProvider"] = """
            internal static class YourRerankerProvider
            {
                public static Task<IReadOnlyList<float>> ScoreAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken)
                    => Task.FromResult<IReadOnlyList<float>>([]);
            }
            """,
        ["YourVectorDatabase"] = """
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
            """,
        // A store the reader writes themselves (the reference shows one); here an empty one of that shape.
        ["YourVectorStore"] = """
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
            """,
    };

    // A block's statements run in a method without parameters; a guide's code may read the arguments and the token a
    // console program or an async method has.
    private static readonly (string Name, string Declaration)[] ProgramStandIns =
    [
        ("args", "string[] args = [];"),
        ("cancellationToken", "System.Threading.CancellationToken cancellationToken = default;"),
    ];

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

    private static Dictionary<string, string> StandIns(params (string Name, string Declaration)[][] groups) =>
        groups.SelectMany(g => g).Concat(ProgramStandIns)
            .ToDictionary(s => s.Name, s => s.Declaration, StringComparer.Ordinal);

    private static readonly Document[] Documents =
    [
        new("docs/AI_PROVIDER_INTEGRATION.md", 19, 3,
            ["FluxIndex.Core.Application.Interfaces", "Microsoft.Extensions.DependencyInjection"],
            StandIns(CommonStandIns)),
        // The packages its Installation section adds, and the contracts every block speaks in; a block that needs an
        // extension namespace beyond those says so itself.
        new("docs/GUIDE.md", 26, 0,
            [
                "FluxIndex.SDK", "FluxIndex.Storage.SQLite", "FluxIndex.Storage.PostgreSQL", "FluxIndex.Storage.Qdrant",
                "FluxIndex.Storage.Neo4j", "FluxIndex.Cache.Redis", "FluxIndex.Core.Application.Interfaces",
                "FluxIndex.Core.Domain.Entities", "Microsoft.Extensions.DependencyInjection",
            ],
            StandIns(CommonStandIns, ServiceStandIns,
            [
                StandIn("string", "connStr"), StandIn("string", "conn"), StandIn("string", "uri"), StandIn("string", "user"),
                StandIn("string", "pass"), StandIn("string", "pgConn"), StandIn("string", "qdrantHost"), StandIn("string", "neo4jUri"),
                StandIn("string", "neo4jUser"), StandIn("string", "neo4jPassword"), StandIn("string", "keyword"), StandIn("string", "documentId"),
                StandIn("FluxIndex.Core.Application.Interfaces.IEmbeddingService", "myEmbedder"),
                StandIn("FluxIndex.Core.Application.Interfaces.IEmbeddingService", "myEmbeddingInstance"),
                StandIn("string[]", "files"), StandIn("System.IServiceProvider", "provider"),
            ])),
        // What its opening block imports, plus the logging a reader who logs already has.
        new("docs/ADVANCED_RAG.md", 24, 0,
            [
                "FluxIndex.Core.Application.Interfaces", "FluxIndex.Core.Application.Services",
                "FluxIndex.Core.Application.Services.Reranking", "FluxIndex.Core.Domain.Entities",
                "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Logging",
            ],
            StandIns(CommonStandIns, ServiceStandIns,
            [
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
            ])),
        // The contracts every block speaks in; the SDK's own HybridSearchOptions would collide with the core one the
        // hybrid-search blocks use, so a block that builds a context imports the SDK itself.
        new("docs/REFERENCE.md", 22, 3,
            ["FluxIndex.Core.Application.Interfaces", "FluxIndex.Core.Domain.Entities", "Microsoft.Extensions.DependencyInjection"],
            StandIns(CommonStandIns, ServiceStandIns,
            [
                StandIn("FluxIndex.Core.Application.Interfaces.IHybridSearchService", "hybridSearch"),
                StandIn("FluxIndex.Core.Application.Interfaces.CommunityHierarchy", "hierarchy"),
                StandIn("string", "startId"), StandIn("string", "endId"), StandIn("string", "content"),
                StandIn("string", "oldDocId"), StandIn("string", "newDocId"), StandIn("string", "newPath"),
                StandIn("System.Collections.Generic.IReadOnlyDictionary<string, string>", "chunkIdMap"),
                StandIn("System.Collections.Generic.IReadOnlyDictionary<string, object?>", "sameUpdates"),
            ])),
        // The design document: two registration blocks and a sketch of the extension points.
        new("docs/FLUXINDEX_PHILOSOPHY.md", 3, 0,
            [
                "FluxIndex.Core.Application.Interfaces", "FluxIndex.Core.Application.Services",
                "FluxIndex.Providers.OpenAI.Extensions", "FluxIndex.Providers.LMSupply.Extensions",
                "FluxIndex.Integrations.FluxImprover", "Microsoft.Extensions.DependencyInjection",
            ],
            StandIns(CommonStandIns,
            [
                StandIn("string", "endpoint"),
                ("MyEmbeddingService", """
                    internal sealed class MyEmbeddingService : FluxIndex.Core.Application.Interfaces.IEmbeddingService
                    {
                        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<float>());
                        public Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<float[]>());
                        public int GetEmbeddingDimension() => 0;
                        public string GetModelName() => "";
                        public int GetMaxTokens() => 0;
                        public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(0);
                        public FluxIndex.Core.Domain.ValueObjects.EmbeddingIdentity GetIdentity() => default!;
                    }
                    """),
            ]),
            new SnippetRoster(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["5.2 Extension Points"] = "names the extension interfaces with empty class bodies — a map of the seams, not an implementation",
            })),
        // The documentation index: the two modes and a five-line start.
        new("docs/README.md", 2, 0,
            [
                "FluxIndex.SDK", "FluxIndex.Storage.SQLite", "FluxIndex.Storage.PostgreSQL", "FluxIndex.Storage.Qdrant",
                "FluxIndex.Storage.Neo4j", "Microsoft.Extensions.DependencyInjection",
            ],
            StandIns(CommonStandIns,
            [
                StandIn("string", "pgConn"), StandIn("string", "qdrantHost"), StandIn("string", "neo4jUri"),
                StandIn("string", "neo4jUser"), StandIn("string", "neo4jPassword"),
            ])),
    ];

    private static readonly Lazy<IReadOnlyList<MetadataReference>> SharedReferences = new(References);

    private static readonly Dictionary<string, Lazy<GuideCompilation>> Compiled = Documents.ToDictionary(
        d => d.Path,
        d => new Lazy<GuideCompilation>(() => CompileGuide(d, Read(d))),
        StringComparer.Ordinal);

    public static TheoryData<string> Blocks() => new(Documents.SelectMany(Read).Select(s => s.Key));

    public static TheoryData<string> DocumentPaths() => new(Documents.Select(d => d.Path));

    [Theory]
    [MemberData(nameof(Blocks))]
    public void DocsBlock_Compiles(string key)
    {
        var document = Documents.Single(d => key.StartsWith(d.Path + " ", StringComparison.Ordinal));
        var compiled = Compiled[document.Path].Value;
        var snippet = compiled.Snippets.Single(s => s.Key == key);
        if (document.Roster.IsFragment(snippet))
            Assert.Skip($"not a program: listed in {document.Path}'s fragment roster");

        var errors = compiled.ErrorsOf(snippet);

        Assert.True(errors.Count == 0,
            $"{key} does not compile against the current API:\n" +
            string.Join("\n", errors.Select(e => e.ToString())) + "\n--- source ---\n" + compiled.SourceOf(snippet));
    }

    [Theory]
    [MemberData(nameof(DocumentPaths))]
    public void Document_IsFound_AndCompilesAsAWhole(string path)
    {
        var document = Documents.Single(d => d.Path == path);
        var compiled = Compiled[path].Value;

        // A floor, and every fragment heading still has a block (a stale entry would excuse whatever lands there next).
        document.Roster.ShouldMatch(compiled.Snippets, document.MinimumBlocks);
        Assert.True(compiled.Listings >= document.MinimumListings, $"expected {path}'s type listings, found {compiled.Listings}");
        // Errors not tied to one block (a type stand-in or placeholder that no longer matches the API) fail here.
        compiled.Shared.ShouldHaveNoSharedErrors();
    }

    /// <summary>Positive control: one compilation of the provider guide rejects a block that calls the retired SDK.</summary>
    [Fact]
    public void Compile_RejectsTheRetiredAzureSdkCall()
    {
        var document = Documents[0];
        var control = new DocSnippet("control", 1, "control", """
            using Azure.AI.OpenAI;
            var client = new OpenAIClient(apiKey);
            var response = await client.GetEmbeddingsAsync(new EmbeddingsOptions("m", new[] { "x" }));
            """);

        var compiled = CompileGuide(document, [.. Read(document), control]);

        Assert.NotEmpty(compiled.ErrorsOf(control));
    }

    /// <summary>Positive control: a listing that names a member the interface does not have is reported.</summary>
    [Fact]
    public void Listing_ReportsAMemberTheInterfaceDoesNotHave()
    {
        var listing = Listing("""
            public interface IReranker
            {
                Task<IEnumerable<RerankResult>> RerankAsync(string query, IEnumerable<RetrievalResult> candidates, int topN = 5, CancellationToken cancellationToken = default);
                Task<float> ScoreEverythingAsync(string query);
            }
            """);

        var compiled = CompileGuide(Documents[0], [listing]);

        Assert.Contains(compiled.ErrorsOf(listing), e => e.GetMessage(CultureInfo.InvariantCulture).Contains("ScoreEverythingAsync", StringComparison.Ordinal));
    }

    /// <summary>Positive control: a listing whose member exists under that name with other parameters is reported.</summary>
    [Fact]
    public void Listing_ReportsAMemberWhoseSignatureDiffers()
    {
        var listing = Listing("""
            public interface IReranker
            {
                Task<IEnumerable<RerankResult>> RerankAsync(string query, IEnumerable<RetrievalResult> candidates, int topN = 5, CancellationToken cancellationToken = default);
            }
            """);

        var compiled = CompileGuide(Documents[0], [listing]);

        Assert.Contains(compiled.ErrorsOf(listing), e => e.Id == "LISTING002" && e.GetMessage(CultureInfo.InvariantCulture).Contains("RerankAsync", StringComparison.Ordinal));
    }

    /// <summary>Positive control: a listing of a class (an options type) that names a property it does not have is reported.</summary>
    [Fact]
    public void ClassListing_ReportsAPropertyTheClassDoesNotHave()
    {
        var listing = Listing("""
            public class GraphRAGQueryOptions
            {
                public bool IncludeRelationships { get; set; }
                public float NoSuchThreshold { get; set; }
            }
            """);

        var compiled = CompileGuide(Documents[0], [listing]);

        Assert.Equal(1, compiled.Listings);
        Assert.Contains(compiled.ErrorsOf(listing), e => e.Id == "LISTING001" && e.GetMessage(CultureInfo.InvariantCulture).Contains("NoSuchThreshold", StringComparison.Ordinal));
    }

    /// <summary>Positive control: a test method shown without its class compiles once wrapped, and its errors still count.</summary>
    [Fact]
    public void BareMembers_AreWrappedAndChecked()
    {
        var good = new DocSnippet("members", 1, "members", """
            public async Task Search_Works()
            {
                await Task.CompletedTask;
            }
            """);
        var bad = good with { File = "members-bad", Code = good.Code.Replace("Task.CompletedTask", "Task.NoSuchMember", StringComparison.Ordinal) };

        var compiled = CompileGuide(Documents[0], [good, bad]);

        Assert.Empty(compiled.ErrorsOf(good));
        Assert.NotEmpty(compiled.ErrorsOf(bad));
    }

    private static DocSnippet Listing(string code) => new("listing", 1, "listing", code);

    /// <summary>One guide's compilation: the kit's, for every block but the fragments.</summary>
    private sealed class GuideCompilation(IReadOnlyList<DocSnippet> snippets, DocumentCompilation shared, IReadOnlySet<string> compiledKeys)
    {
        public IReadOnlyList<DocSnippet> Snippets { get; } = snippets;

        public DocumentCompilation Shared { get; } = shared;

        public int Listings => Shared.ListedTypes;

        public IReadOnlyList<Diagnostic> ErrorsOf(DocSnippet snippet) => compiledKeys.Contains(snippet.Key) ? Shared.ErrorsOf(snippet) : [];

        public string SourceOf(DocSnippet snippet) => compiledKeys.Contains(snippet.Key) ? Shared.SourceOf(snippet) : snippet.Code;
    }

    private static List<DocSnippet> Read(Document document) => [.. DocSnippets.Read(RepositoryFile(document.Path), document.Path)];

    private static GuideCompilation CompileGuide(Document document, IReadOnlyList<DocSnippet> snippets)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var compiler = new SnippetCompiler(SharedReferences.Value)
        {
            Usings = [.. ImplicitUsings, .. document.Usings],
            StandIns = new Dictionary<string, string>(document.StandIns.Concat(Placeholders), StringComparer.Ordinal),
            CheckListings = true,
        };

        var compiled = snippets
            .Where(s => !document.Roster.IsFragment(s))
            .Select((s, i) => s with { Code = Prepare(CSharpSyntaxTree.ParseText(s.Code, parse).GetCompilationUnitRoot(), i) })
            .ToList();
        return new GuideCompilation(snippets, compiler.CompileDocument(compiled), compiled.Select(s => s.Key).ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// The block as a reader of this guide has it: the ASP.NET usings for a web host, and bare members inside a class.
    /// </summary>
    private static string Prepare(CompilationUnitSyntax root, int index)
    {
        var members = new StringBuilder();
        var rest = new StringBuilder();
        foreach (var member in root.Members)
        {
            switch (member)
            {
                case GlobalStatementSyntax { Statement: LocalFunctionStatementSyntax local } when local.Modifiers.Any(IsAccessModifier) || local.AttributeLists.Count > 0:
                    members.Append(member.ToFullString());
                    break;
                case GlobalStatementSyntax or BaseTypeDeclarationSyntax or BaseNamespaceDeclarationSyntax or DelegateDeclarationSyntax:
                    rest.Append(member.ToFullString());
                    break;
                default:
                    members.Append(member.ToFullString());
                    break;
            }
        }

        var source = new StringBuilder();
        source.Append(string.Concat(root.Externs.Select(e => e.ToFullString())));
        source.Append(string.Concat(root.Usings.Select(u => u.ToFullString())));
        if (rest.ToString().Contains("WebApplication", StringComparison.Ordinal))
            source.Append(WebUsings);

        source.Append(rest);
        if (members.Length > 0)
        {
            source.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"public class DocumentMembers{index}")
                .AppendLine("{").Append(members).AppendLine().AppendLine("}");
        }

        return source.ToString();
    }

    private static bool IsAccessModifier(SyntaxToken token) =>
        token.IsKind(SyntaxKind.PublicKeyword) || token.IsKind(SyntaxKind.PrivateKeyword)
        || token.IsKind(SyntaxKind.InternalKeyword) || token.IsKind(SyntaxKind.ProtectedKeyword);

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
