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
/// <item>Before that, this test adds what a reader of these guides has that the kit cannot know: the document's usings (for
/// the provider guide, only to blocks with statements — its type blocks carry their own usings and are checked as
/// written), the ASP.NET usings for a block that builds a <c>WebApplication</c>, and a class around bare members (a test
/// method shown without its class).</item>
/// <item>A block that only restates library types is a listing: compiled, it would declare a second type of that name and
/// check nothing, so each member it lists is looked up on the real type instead — by name, then by signature. That check
/// is this repository's; the kit compiles a restating block on its own and checks nothing more.</item>
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
        string[] Usings,
        bool UsingsForTypeBlocks,
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
            UsingsForTypeBlocks: false, StandIns(CommonStandIns)),
        // The packages its Installation section adds, and the contracts every block speaks in; a block that needs an
        // extension namespace beyond those says so itself.
        new("docs/GUIDE.md", 26, 0,
            [
                "FluxIndex.SDK", "FluxIndex.Storage.SQLite", "FluxIndex.Storage.PostgreSQL", "FluxIndex.Storage.Qdrant",
                "FluxIndex.Storage.Neo4j", "FluxIndex.Cache.Redis", "FluxIndex.Core.Application.Interfaces",
                "FluxIndex.Core.Domain.Entities", "Microsoft.Extensions.DependencyInjection",
            ],
            UsingsForTypeBlocks: true,
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
            UsingsForTypeBlocks: true,
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
            UsingsForTypeBlocks: true,
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
            UsingsForTypeBlocks: true,
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
            UsingsForTypeBlocks: true,
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

        Assert.Contains(compiled.ErrorsOf(listing), e => e.Id == "GUIDE002" && e.GetMessage(CultureInfo.InvariantCulture).Contains("RerankAsync", StringComparison.Ordinal));
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
        Assert.Contains(compiled.ErrorsOf(listing), e => e.Id == "GUIDE001" && e.GetMessage(CultureInfo.InvariantCulture).Contains("NoSuchThreshold", StringComparison.Ordinal));
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

    /// <summary>One guide's compilation: the kit's for every block but the listings, and this test's listing check.</summary>
    private sealed class GuideCompilation(
        IReadOnlyList<DocSnippet> snippets, DocumentCompilation shared, IReadOnlySet<string> compiledKeys,
        IReadOnlyList<Diagnostic> listingErrors, int listings)
    {
        public IReadOnlyList<DocSnippet> Snippets { get; } = snippets;

        public DocumentCompilation Shared { get; } = shared;

        public int Listings { get; } = listings;

        public IReadOnlyList<Diagnostic> ErrorsOf(DocSnippet snippet) =>
            compiledKeys.Contains(snippet.Key)
                ? Shared.ErrorsOf(snippet)
                : [.. listingErrors.Where(e => e.Location.SourceTree?.FilePath == snippet.Key)];

        public string SourceOf(DocSnippet snippet) => compiledKeys.Contains(snippet.Key) ? Shared.SourceOf(snippet) : snippet.Code;
    }

    private static List<DocSnippet> Read(Document document) => [.. DocSnippets.Read(RepositoryFile(document.Path), document.Path)];

    private static GuideCompilation CompileGuide(Document document, IReadOnlyList<DocSnippet> snippets)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var compiler = new SnippetCompiler(SharedReferences.Value)
        {
            Usings = document.UsingsForTypeBlocks ? [.. ImplicitUsings, .. document.Usings] : ImplicitUsings,
            StandIns = new Dictionary<string, string>(document.StandIns.Concat(Placeholders), StringComparer.Ordinal),
        };

        var listingErrors = new List<Diagnostic>();
        var listings = 0;
        var compiled = new List<DocSnippet>();
        foreach (var snippet in snippets)
        {
            if (document.Roster.IsFragment(snippet))
                continue;

            var root = CSharpSyntaxTree.ParseText(snippet.Code, parse).GetCompilationUnitRoot();
            if (ListedTypes(root) is { } listed)
            {
                listings += listed.Count;
                listingErrors.AddRange(CheckListing(CSharpSyntaxTree.ParseText(snippet.Code, parse, path: snippet.Key)));
                continue;
            }

            compiled.Add(snippet with { Code = Prepare(document, root, compiled.Count) });
        }

        return new GuideCompilation(
            snippets, compiler.CompileDocument(compiled), compiled.Select(s => s.Key).ToHashSet(StringComparer.Ordinal),
            listingErrors, listings);
    }

    /// <summary>
    /// The block as a reader of this guide has it: the document's usings when the kit does not add them (the provider guide's
    /// statement blocks), the ASP.NET usings for a web host, and bare members inside a class.
    /// </summary>
    private static string Prepare(Document document, CompilationUnitSyntax root, int index)
    {
        var members = new StringBuilder();
        var rest = new StringBuilder();
        var hasStatements = false;
        foreach (var member in root.Members)
        {
            switch (member)
            {
                case GlobalStatementSyntax { Statement: LocalFunctionStatementSyntax local } when local.Modifiers.Any(IsAccessModifier) || local.AttributeLists.Count > 0:
                    members.Append(member.ToFullString());
                    break;
                case GlobalStatementSyntax:
                    hasStatements = true;
                    rest.Append(member.ToFullString());
                    break;
                case BaseTypeDeclarationSyntax or BaseNamespaceDeclarationSyntax or DelegateDeclarationSyntax:
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
        if (!document.UsingsForTypeBlocks && (hasStatements || members.Length > 0))
            source.Append(string.Concat(document.Usings.Select(u => $"using {u};\n")));
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
