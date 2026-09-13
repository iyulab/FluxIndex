using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Models;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// SDK options that were declared, documented and wired into the builder but never reached the
/// behaviour they describe (found by <c>OptionsReachabilityRosterTests</c> and by reading the
/// consumers it cannot see): <see cref="IndexerOptions.ChunkSize"/>/<see cref="IndexerOptions.ChunkOverlap"/>
/// on the string indexing overload, the per-call <see cref="IndexingOptions.CustomOptions"/> for AI
/// metadata extraction, and <see cref="SearchOptions.UseGraphRAG"/>. Each fact is the observable effect,
/// not that a getter is called.
/// </summary>
public class IndexerOptionsHonouredTests
{
    // The in-memory store hands back the very chunk objects the indexer stored, so fields the SQLite
    // schema does not persist (TotalChunks — see the issue draft on `totalChunks` citation metadata)
    // remain observable here.
    private static FluxIndexContextBuilder Builder() =>
        FluxIndexContext.CreateBuilder()
            .UseInMemoryEmbedding()
            .SuppressStartupMessages();

    private static string Sentences(int count) =>
        string.Join(" ", Enumerable.Range(1, count).Select(i => $"Sentence number {i} says something about the topic."));

    // ---- IndexerOptions.ChunkSize / ChunkOverlap on IndexDocumentAsync(string, ...) ----

    [Fact]
    public async Task StringOverload_SplitsByChunkSize_AndCarriesMetadataOnEveryChunk()
    {
        var context = Builder()
            .WithIndexerOptions(o => { o.ChunkSize = 200; o.ChunkOverlap = 20; })
            .Build();
        var content = Sentences(30); // ~1,500 characters
        var metadata = new Dictionary<string, object> { ["tenant"] = "acme" };

        await context.Indexer.IndexDocumentAsync(content, "doc-long", metadata, TestContext.Current.CancellationToken);

        var chunks = (await context.ServiceProvider.GetRequiredService<IVectorStore>()
            .GetByDocumentIdAsync("doc-long", TestContext.Current.CancellationToken))
            .OrderBy(c => c.ChunkIndex).ToList();

        chunks.Count.Should().BeGreaterThan(5,
            "the string overload must split by IndexerOptions.ChunkSize — before 0.38.0 it stored the whole text as one chunk");
        chunks.Select(c => c.ChunkIndex).Should().BeEquivalentTo(Enumerable.Range(0, chunks.Count));
        chunks.Should().OnlyContain(c => c.TotalChunks == chunks.Count);
        chunks.Should().OnlyContain(c => c.Content.Length <= 200);
        chunks.Should().OnlyContain(c => c.Metadata != null && Equals(c.Metadata["tenant"], "acme"),
            "filters read chunk metadata, so every chunk must carry it");
        string.Concat(chunks.Select(c => c.Content)).Should().Contain("Sentence number 30",
            "splitting must not drop the tail of the document");
    }

    [Fact]
    public async Task StringOverload_ChunkSizeLargerThanTheText_KeepsOneChunk()
    {
        var context = Builder()
            .WithChunking(chunkSize: 4000, chunkOverlap: 64)
            .Build();

        await context.Indexer.IndexDocumentAsync(Sentences(30), "doc-one", cancellationToken: TestContext.Current.CancellationToken);

        var chunks = await context.ServiceProvider.GetRequiredService<IVectorStore>()
            .GetByDocumentIdAsync("doc-one", TestContext.Current.CancellationToken);
        chunks.Should().ContainSingle("the same text under a larger ChunkSize must not be split — the option, not a constant, decides");
    }

    [Fact]
    public async Task StringOverload_ShortText_IsStoredVerbatim()
    {
        var context = Builder().Build();
        const string content = "First line.\n\nSecond paragraph with   irregular   spacing.";

        await context.Indexer.IndexDocumentAsync(content, "doc-short", cancellationToken: TestContext.Current.CancellationToken);

        var chunk = (await context.ServiceProvider.GetRequiredService<IVectorStore>()
            .GetByDocumentIdAsync("doc-short", TestContext.Current.CancellationToken)).Should().ContainSingle().Subject;
        chunk.Content.Should().Be(content, "text that fits in one chunk must round-trip byte for byte");
    }

    [Fact]
    public async Task StringOverload_OverlapNotSmallerThanChunkSize_Throws()
    {
        var context = Builder()
            .WithIndexerOptions(o => { o.ChunkSize = 100; o.ChunkOverlap = 100; })
            .Build();

        var act = () => context.Indexer.IndexDocumentAsync(Sentences(10), "doc-bad", cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*overlap*", "an overlap that can never advance the window must fail loudly, not hang or silently drop the overlap");
    }

    // ---- IndexingOptions.CustomOptions (per call) for AI metadata extraction ----

    private static (IFluxIndexContext Context, IMetadataExtractor Extractor) ContextWithExtractor(Action<IndexerOptions>? indexer = null)
    {
        var extractor = Substitute.For<IMetadataExtractor>();
        extractor.GenerateCacheKey(Arg.Any<string>(), Arg.Any<MetadataSchema>()).Returns("key");
        extractor.ExtractWithCacheAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MetadataSchema>(), Arg.Any<AIMetadataExtractionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExtractedMetadata()));

        var builder = Builder().ConfigureServices(s => s.AddSingleton(extractor));
        if (indexer != null)
            builder = builder.WithIndexerOptions(indexer);
        return (builder.Build(), extractor);
    }

    private static Document DocumentWithContent(string id)
    {
        var document = Document.Create(id);
        document.Content = "Quarterly revenue grew twelve percent on strong cloud demand.";
        document.AddChunk(DocumentChunk.Create(id, document.Content, 0, 1));
        return document;
    }

    [Fact]
    public async Task PerCallWithAIMetadataExtraction_ReachesTheExtractor()
    {
        var (context, extractor) = ContextWithExtractor();

        await context.Indexer.IndexDocumentAsync(DocumentWithContent("doc-ai"),
            new IndexingOptions().WithAIMetadataExtraction(MetadataSchema.General, MetadataExtractionStrategy.Smart, 0.5f),
            TestContext.Current.CancellationToken);

        await extractor.Received(1).ExtractWithCacheAsync(Arg.Any<string>(), Arg.Any<string>(), MetadataSchema.General,
            Arg.Is<AIMetadataExtractionOptions?>(o => o != null && o.MinConfidence == 0.5f), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutAnyOptIn_TheExtractorIsNotCalled()
    {
        var (context, extractor) = ContextWithExtractor();

        await context.Indexer.IndexDocumentAsync(DocumentWithContent("doc-plain"), new IndexingOptions(), TestContext.Current.CancellationToken);

        await extractor.DidNotReceiveWithAnyArgs().ExtractWithCacheAsync(default!, default!, default, default, default);
    }

    [Fact]
    public async Task BuilderLevelCustomOptions_StillReachTheExtractor_AndThePerCallKeyWins()
    {
        var (context, extractor) = ContextWithExtractor(o =>
            o.CustomOptions = new Dictionary<string, object> { ["EnableAIMetadataExtraction"] = true });

        await context.Indexer.IndexDocumentAsync(DocumentWithContent("doc-builder"), (IndexingOptions?)null, TestContext.Current.CancellationToken);
        await extractor.Received(1).ExtractWithCacheAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MetadataSchema>(), Arg.Any<AIMetadataExtractionOptions?>(), Arg.Any<CancellationToken>());

        extractor.ClearReceivedCalls();
        var optOut = new IndexingOptions();
        optOut.CustomOptions["EnableAIMetadataExtraction"] = false;
        await context.Indexer.IndexDocumentAsync(DocumentWithContent("doc-optout"), optOut, TestContext.Current.CancellationToken);

        await extractor.DidNotReceiveWithAnyArgs().ExtractWithCacheAsync(default!, default!, default, default, default);
    }

    // ---- SearchOptions.UseGraphRAG ----

    [Fact]
    public async Task UseGraphRAG_True_WithoutAService_Throws()
    {
        var context = Builder().Build();

        var act = () => context.Retriever.SearchAsync("anything", new SearchOptions { UseGraphRAG = true }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IGraphRAGService*",
            "the option documents an error when the service is missing; before 0.38.0 the request was dropped silently");
    }

    [Fact]
    public async Task UseGraphRAG_True_WithAService_ThrowsNotSupported_PointingAtTheGraphApi()
    {
        var graph = Substitute.For<IGraphRAGService>();
        var context = Builder().ConfigureServices(s => s.AddSingleton(graph)).Build();

        var act = () => context.Retriever.SearchAsync("anything", new SearchOptions { UseGraphRAG = true }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*QueryAsync*");
        await graph.DidNotReceiveWithAnyArgs().QueryAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task UseGraphRAG_Unset_WithAService_SearchesWithoutTheGraph()
    {
        var graph = Substitute.For<IGraphRAGService>();
        var context = Builder().ConfigureServices(s => s.AddSingleton(graph)).Build();
        await context.Indexer.IndexDocumentAsync("Graph databases store relationships as first-class data.", "doc-g", cancellationToken: TestContext.Current.CancellationToken);

        var response = await context.Retriever.SearchAsync("relationships", new SearchOptions { TopK = 5 }, TestContext.Current.CancellationToken);

        response.Should().NotBeNull();
        response.Metadata["graphRAGAvailable"].Should().Be(true);
        await graph.DidNotReceiveWithAnyArgs().QueryAsync(default!, default!, default, default);
    }
}
