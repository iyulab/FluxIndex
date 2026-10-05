using Iyu.Conventions.Testing;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// The public surface follows the two API rules of the ecosystem: every public async method takes a
/// <see cref="CancellationToken"/>, and failure is reported by an exception rather than by a returned object carrying a
/// success flag and an error. The scans are <c>Iyu.Conventions.Testing</c>'s, over the same assemblies as the
/// operational-language scan.
/// </summary>
/// <remarks>
/// The rosters are the methods that break a rule today. Shrink them; never grow them silently. A change to a listed
/// method's parameters changes its entry, which is a roster change on purpose.
/// </remarks>
public class PublicApiConventionTests
{
    private static readonly string[] KnownUncancellable = [];

    private static readonly string[] KnownResultReturns =
    [
        // A report: one entry per image that could not be stored (the document keeps its base64 there); the call itself
        // throws when it cannot run, and the caller's cancellation propagates.
        "FluxIndex.Core.Application.Interfaces.IImageExtractionService.ExtractAndStoreAsync(String, String, IImageStore, String, CancellationToken)",
        "FluxIndex.Integrations.FileFlux.Processing.DocumentProcessingPipeline.ExtractOnlyAsync(String, Boolean, CancellationToken)",
        "FluxIndex.Integrations.FileFlux.Processing.DocumentProcessingPipeline.ExtractOnlyAsync(String, ExtractionOptions, CancellationToken)",
        "FluxIndex.Integrations.FileFlux.Processing.DocumentProcessingPipeline.ProcessAndSaveAsync(String, String, DocumentProcessingOptions, CancellationToken)",
        "FluxIndex.Integrations.FileFlux.Processing.DocumentProcessingPipeline.ProcessAsync(String, DocumentProcessingOptions, CancellationToken)",
        "FluxIndex.Integrations.FileFlux.Processing.DocumentProcessingPipeline.ProcessFromContentAsync(String, ContentProcessingOptions, CancellationToken)",
        "FluxIndex.Integrations.FileFlux.Processing.DocumentProcessingPipeline.ProcessFromExtractionAsync(ExtractionResult, String, ContentProcessingOptions, CancellationToken)",
        "FluxIndex.Integrations.FluxImprover.FluxIndexContextExtensions.RunPipelineAsync(FluxIndexContext, IEnrichedChunk, PipelineOptions, CancellationToken)",
        "FluxIndex.Integrations.FluxImprover.Services.FluxImproverPipeline.ProcessChunkAsync(IEnrichedChunk, PipelineOptions, CancellationToken)",
    ];

    [Fact]
    public void PublicAsyncMethods_TakeACancellationToken() =>
        AsyncCancellation.Scan(OperationalLanguageConventionTests.LibraryAssemblies()).ShouldMatchRoster(KnownUncancellable);

    [Fact]
    public void PublicMethods_DoNotReturnResultObjects() =>
        ResultReturns.Scan(OperationalLanguageConventionTests.LibraryAssemblies()).ShouldMatchRoster(KnownResultReturns);

    // Positive control: an empty roster would also pass if the scan saw no public method at all.
    [Fact]
    public void Scan_SeesThePublicSurface() =>
        Assert.True(ResultReturns.Scan(OperationalLanguageConventionTests.LibraryAssemblies()).MembersRead > 0, "the scan read too few public methods");
}
