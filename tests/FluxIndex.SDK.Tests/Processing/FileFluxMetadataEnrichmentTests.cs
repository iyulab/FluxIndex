using AwesomeAssertions;
using FileFlux;
using FileFlux.Core;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Integrations.FileFlux;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using FileFluxChunk = FileFlux.Core.DocumentChunk;
using MetadataSchema = FluxIndex.Core.Application.Models.MetadataSchema;

namespace FluxIndex.SDK.Tests.Processing;

/// <summary>
/// The FileFlux integration's <c>EnableMetadataEnrichment</c> used to be written into FileFlux chunking properties no stage
/// reads, so it did nothing. It now asks the indexer for AI metadata extraction with the configured schema — on every path
/// that indexes (whole document, streamed document, and the batches streaming indexes as it goes) — and fails before a file
/// is processed when no metadata extractor is registered.
/// </summary>
public class FileFluxMetadataEnrichmentTests
{
    private static readonly FileFluxChunk[] Chunks =
    [
        new() { Content = "Quarterly revenue grew twelve percent.", Location = new SourceLocation { StartChar = 0, EndChar = 38 } },
        new() { Content = "Cloud demand drove the growth.", Location = new SourceLocation { StartChar = 39, EndChar = 69 } },
    ];

    private static IDocumentProcessorFactory Factory()
    {
        var processor = Substitute.For<IDocumentProcessor>();
        processor.Result.Returns(new ProcessingResult { Chunks = Chunks });
        processor.ProcessStreamAsync(Arg.Any<FileFlux.Core.ProcessingOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Chunks.ToAsyncEnumerable());
        var factory = Substitute.For<IDocumentProcessorFactory>();
        factory.Create(Arg.Any<string>()).Returns(processor);
        return factory;
    }

    private static (FileFluxIntegration Integration, IMetadataExtractor? Extractor, IDocumentProcessorFactory Factory) Create(
        bool withExtractor, Action<FileFluxOptions> configure)
    {
        IMetadataExtractor? extractor = null;
        var builder = FluxIndexContext.CreateBuilder().UseInMemoryEmbedding().SuppressStartupMessages();
        if (withExtractor)
        {
            extractor = Substitute.For<IMetadataExtractor>();
            extractor.GenerateCacheKey(Arg.Any<string>(), Arg.Any<MetadataSchema>()).Returns("key");
            extractor.ExtractWithCacheAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MetadataSchema>(),
                    Arg.Any<AIMetadataExtractionOptions?>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ExtractedMetadata()));
            var registered = extractor;
            builder = builder.ConfigureServices(s => s.AddSingleton(registered));
        }

        var options = new FileFluxOptions();
        configure(options);
        var factory = Factory();
        var integration = new FileFluxIntegration(
            factory,
            Substitute.For<ILanguageProfileProvider>(),
            builder.Build().Indexer,
            NullLogger<FileFluxIntegration>.Instance,
            Options.Create(options));
        return (integration, extractor, factory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enrichment_ExtractsAIMetadata_WithTheConfiguredSchema(bool streaming)
    {
        var (integration, extractor, _) = Create(withExtractor: true, o =>
        {
            o.UseStreamingApi = streaming;
            o.EnableMetadataEnrichment = true;
            o.DefaultMetadataSchema = MetadataSchema.TechnicalDoc;
        });

        await integration.ProcessAndIndexAsync("report.pdf", cancellationToken: TestContext.Current.CancellationToken);

        await extractor!.Received(1).ExtractWithCacheAsync(Arg.Any<string>(), Arg.Any<string>(), MetadataSchema.TechnicalDoc,
            Arg.Any<AIMetadataExtractionOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enrichment_ReachesTheBatchesStreamingIndexesAsItGoes()
    {
        var (integration, extractor, _) = Create(withExtractor: true, o =>
        {
            o.UseStreamingApi = true;
            o.EnableImmediateIndexing = true;
            o.ImmediateIndexingBatchSize = 1;
            o.EnableMetadataEnrichment = true;
        });

        await integration.ProcessAndIndexAsync("report.pdf", cancellationToken: TestContext.Current.CancellationToken);

        await extractor!.Received(2).ExtractWithCacheAsync(Arg.Any<string>(), Arg.Any<string>(), MetadataSchema.General,
            Arg.Any<AIMetadataExtractionOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutEnrichment_NoExtraction()
    {
        var (integration, extractor, _) = Create(withExtractor: true, o => o.UseStreamingApi = false);

        await integration.ProcessAndIndexAsync("report.pdf", cancellationToken: TestContext.Current.CancellationToken);

        await extractor!.DidNotReceiveWithAnyArgs().ExtractWithCacheAsync(default!, default!, default, default, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnrichmentWithoutAnExtractor_FailsBeforeTheFileIsProcessed(bool streaming)
    {
        var (integration, _, factory) = Create(withExtractor: false, o =>
        {
            o.UseStreamingApi = streaming;
            o.EnableMetadataEnrichment = true;
        });

        var act = () => integration.ProcessAndIndexAsync("report.pdf", cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*metadata extractor*");
        factory.DidNotReceiveWithAnyArgs().Create(default(string)!);
    }

    [Fact]
    public async Task Indexer_RequestedExtractionWithoutAnExtractor_Throws()
    {
        var context = FluxIndexContext.CreateBuilder().UseInMemoryEmbedding().SuppressStartupMessages().Build();
        var document = FluxIndex.Core.Domain.Entities.Document.Create("doc");
        document.Content = "text";

        var act = () => context.Indexer.IndexDocumentAsync(document, new IndexingOptions().WithAIMetadataExtraction(),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no metadata extractor*");
    }
}
