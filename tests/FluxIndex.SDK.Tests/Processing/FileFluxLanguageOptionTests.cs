using FileFlux.Core;
using FluxIndex.Integrations.FileFlux;
using Xunit;

namespace FluxIndex.SDK.Tests.Processing;

/// <summary>
/// The FileFlux integration's <c>Language</c> used to be written into <c>ChunkingOptions.CustomProperties["language"]</c>,
/// a key FileFlux never reads, so a configured language had no effect on segmentation. It is passed as
/// <see cref="ChunkingOptions.LanguageCode"/> — which FileFlux 0.33.0 hands to the chunker on the stateful path.
/// </summary>
public class FileFluxLanguageOptionTests
{
    [Fact]
    public void Language_BecomesTheChunkingLanguageCode()
    {
        var chunking = new ChunkingOptions();

        FileFluxIntegration.ApplyCustomProperties(chunking, new FluxIndex.Integrations.FileFlux.ProcessingOptions { Language = "ko" });

        Assert.Equal("ko", chunking.LanguageCode);
        Assert.False(chunking.CustomProperties.ContainsKey("language"));
    }

    [Fact]
    public void NoLanguage_DetectsIt()
    {
        var chunking = new ChunkingOptions { LanguageCode = "en" };

        FileFluxIntegration.ApplyCustomProperties(chunking, new FluxIndex.Integrations.FileFlux.ProcessingOptions { Language = null });

        Assert.Equal("auto", chunking.LanguageCode);
        Assert.False(chunking.CustomProperties.ContainsKey("enableLanguageAutoDetection"));
    }
}
