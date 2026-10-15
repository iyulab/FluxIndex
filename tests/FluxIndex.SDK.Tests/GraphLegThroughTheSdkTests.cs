using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// The graph leg in a context the SDK builder built: SQLite storage registers the entity graph store and with it the
/// graph retriever, the indexer writes the graph, and a hybrid search that asks for the graph leg reads it back under its
/// scope — no registration of the retriever by hand, no index loaded per query.
/// </summary>
public sealed class GraphLegThroughTheSdkTests : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fluxindex-graphleg-{Guid.NewGuid():N}.db");
    private FluxIndexContext? _context;

    private async Task<FluxIndexContext> IndexedContextAsync()
    {
        _context = (FluxIndexContext)FluxIndexContext.CreateBuilder()
            .UseSQLite(_path)
            .AddSQLiteStorage()
            .UseInMemoryEmbedding()
            .ConfigureServices(s => s.AddFullGraphRAG())
            .SuppressStartupMessages()
            .Build();

        await _context.Indexer.IndexDocumentAsync("Acme Corp signed a supply agreement with Globex Industries last spring.", "d1", null, Ct);
        await _context.Indexer.IndexDocumentAsync("Initech Systems hired forty engineers for its new office.", "d2", null, Ct);
        return _context;
    }

    [Fact]
    public async Task A_hybrid_search_with_the_graph_leg_reads_the_graph_the_indexer_wrote()
    {
        var context = await IndexedContextAsync();
        using var scope = context.ServiceProvider.CreateScope();
        var hybrid = scope.ServiceProvider.GetRequiredService<IHybridSearchService>();

        // Lower-case on purpose: the rule-based extractor reads «Acme Corp» from the text, and the stored name is folded, so
        // a query typed without capitals still names it.
        var results = await hybrid.SearchAsync("what did acme corp agree to?", new HybridSearchOptions
        {
            GraphOptions = new GraphLegOptions { MaxHops = 0 },
        }, Ct);

        var fromGraph = results.Where(r => r.Source.HasFlag(SearchSource.Graph)).ToList();
        fromGraph.Should().NotBeEmpty("the indexer stored «Acme Corp» and the query names it");
        fromGraph.Should().OnlyContain(r => r.Chunk.DocumentId == "d1");
        fromGraph.Should().OnlyContain(r => r.GraphRank >= 1 && r.GraphRank < int.MaxValue);
        results.Should().OnlyContain(r => r.Fusion.GraphWeight == 0.3);
    }

    [Fact]
    public async Task The_graph_leg_honours_the_query_scope()
    {
        var context = await IndexedContextAsync();
        using var scope = context.ServiceProvider.CreateScope();
        var hybrid = scope.ServiceProvider.GetRequiredService<IHybridSearchService>();

        var results = await hybrid.SearchAsync("What did Acme Corp agree to?", new HybridSearchOptions
        {
            Filters = new Dictionary<string, object> { [FilterKeys.DocumentId] = "d2" },
            GraphOptions = new GraphLegOptions { MaxHops = 0 },
        }, Ct);

        results.Should().OnlyContain(r => r.Chunk.DocumentId == "d2");
        results.Should().NotContain(r => r.Source.HasFlag(SearchSource.Graph));
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
            await _context.DisposeAsync();
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileNameWithoutExtension(_path) + "*"))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }
}
