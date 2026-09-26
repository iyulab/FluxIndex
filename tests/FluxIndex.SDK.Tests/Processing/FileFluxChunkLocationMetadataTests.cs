using FileFlux.Core;
using FluxIndex.Integrations.FileFlux;
using Xunit;
using FileFluxChunk = FileFlux.Core.DocumentChunk;

namespace FluxIndex.SDK.Tests.Processing;

/// <summary>
/// A FileFlux chunk's source location reaches the indexed chunk's metadata: pages for paginated sources, seconds for
/// recordings — the same keys FluxFeed writes, so a consumer reads one vocabulary either way.
/// </summary>
public class FileFluxChunkLocationMetadataTests
{
    [Fact]
    public void Pages_MapToPageKeys()
    {
        var chunk = new FileFluxChunk { Content = "text", Location = new SourceLocation { StartChar = 0, EndChar = 4, StartPage = 3, EndPage = 4 } };

        var metadata = FileFluxIntegration.ConvertToFluxIndexChunk(chunk, 0, "report.pdf").Metadata;

        Assert.Equal(3, metadata["pageNumber"]);
        Assert.Equal(3, metadata["ff_start_page"]);
        Assert.Equal(4, metadata["ff_end_page"]);
        Assert.False(metadata.ContainsKey("ff_start_seconds"));
    }

    [Fact]
    public void Times_MapToSecondsKeys()
    {
        var chunk = new FileFluxChunk
        {
            Content = "text",
            Location = new SourceLocation { StartChar = 0, EndChar = 4, StartTime = TimeSpan.FromSeconds(12.5), EndTime = TimeSpan.FromSeconds(30) },
        };

        var metadata = FileFluxIntegration.ConvertToFluxIndexChunk(chunk, 0, "meeting.wav").Metadata;

        Assert.Equal(12.5, metadata["ff_start_seconds"]);
        Assert.Equal(30.0, metadata["ff_end_seconds"]);
        Assert.False(metadata.ContainsKey("ff_start_page"));
    }
}
