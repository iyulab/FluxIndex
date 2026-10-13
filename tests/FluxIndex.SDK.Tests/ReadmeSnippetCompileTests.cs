using System.Reflection;
using FluxIndex.Storage.SQLite;
using Iyu.Conventions.Testing.Snippets;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Compiles every C# block in README.md against the current assemblies: the receiver, the arguments, the members read and
/// the namespaces — what a reader who copies the block actually runs into. The guides under <c>docs/</c> are compiled by
/// <see cref="DocsSnippetCompileTests"/>.
/// </summary>
/// <remarks>
/// Each block is compiled as a top-level program by <see cref="SnippetCompiler"/>: its own usings, the usings below, and
/// the stand-ins below when the block uses the name without declaring it — values a reader already has from the
/// surrounding text (a built context, a connection string), not part of what the block shows. A block that declares a
/// type has its statements moved before the type, as a top-level program needs.
/// </remarks>
public class ReadmeSnippetCompileTests
{
    private static readonly IReadOnlyList<DocSnippet> Snippets = DocSnippets.Read(ReadmePath());

    // A block that is deliberately not a program (a signature sketch, pseudocode), by the heading it sits under, with the
    // reason. Shrink this, never grow it silently.
    private static readonly SnippetRoster Fragments = new(new Dictionary<string, string>(StringComparer.Ordinal));

    private static readonly SnippetCompiler Compiler = new(SnippetCompiler.ReferencesFor(
        [typeof(SQLiteOptions).Assembly, .. new[]
        {
            "FluxIndex.SDK", "FluxIndex.Core", "FluxIndex.Storage.SQLite", "FluxIndex.Storage.Qdrant", "FluxIndex.Storage.PostgreSQL",
            "FluxIndex.Providers.OpenAI", "FluxIndex.Providers.LMSupply", "LMSupply.Embedder", "LMSupply.Core",
            "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection.Abstractions",
        }.Select(Assembly.Load)]))
    {
        Usings =
        [
            "System", "System.Collections.Generic", "System.Linq", "System.Threading", "System.Threading.Tasks",
            "FluxIndex.SDK", "FluxIndex.Core.Application.Interfaces", "FluxIndex.Core.Application.Services.Base",
            "FluxIndex.Core.Application.Services.KeywordSearch", "FluxIndex.Storage.SQLite", "FluxIndex.Storage.Qdrant",
            "FluxIndex.Storage.PostgreSQL", "FluxIndex.Providers.OpenAI.Extensions", "LMSupply.Embedder",
            "Microsoft.Extensions.DependencyInjection",
        ],
        StandIns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["context"] = "IFluxIndexContext context = null!;",
            ["builder"] = "FluxIndexContextBuilder builder = FluxIndexContext.CreateBuilder();",
            ["services"] = "IServiceCollection services = null!;",
            ["vectorStore"] = "IVectorStore vectorStore = null!;",
            ["keywordSearch"] = "IKeywordSearchService keywordSearch = null!;",
            ["apiKey"] = "string apiKey = \"\";",
            ["connectionString"] = "string connectionString = \"\";",
            ["metadataConnectionString"] = "string metadataConnectionString = \"\";",
        },
    };

    public static TheoryData<string> Blocks() => new(Snippets.Select(s => s.Key));

    [Theory]
    [MemberData(nameof(Blocks))]
    public void ReadmeBlock_Compiles(string key)
    {
        var snippet = Snippets.Single(s => s.Key == key);
        if (!Fragments.IsFragment(snippet))
            Compiler.ShouldCompile(snippet);
    }

    [Fact]
    public void EveryReadmeBlock_IsFoundAndFragmentsNameRealHeadings() => Fragments.ShouldMatch([.. Snippets], atLeast: 10);

    /// <summary>Positive control: the compiler rejects a call the API does not have.</summary>
    [Fact]
    public void Compile_RejectsAMemberThatDoesNotExist()
    {
        var errors = Compiler.Compile("""
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
            FluxIndex.Tests.Shared.SqliteTestPools.Release(path);
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
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
