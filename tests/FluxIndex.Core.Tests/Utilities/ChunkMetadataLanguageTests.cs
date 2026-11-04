using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Utilities;

/// <summary>
/// A chunk's rich metadata used to say <c>Language = "ko"</c> for every chunk nobody labelled, and a store wrote that label
/// next to whatever else the caller had set - so English chunks came back marked Korean. Nothing in the library assigns the
/// field, so the default is the only value it ever had unless the caller set one.
/// </summary>
public class ChunkMetadataLanguageTests
{
    private static ChunkMetadata RoundTrip(ChunkMetadata metadata)
    {
        var written = new DocumentChunk();
        written.SetMetadata(metadata);
        MetadataHelper.ForStorage(written);

        var read = new DocumentChunk { Metadata = new Dictionary<string, object>(written.Metadata!) };
        MetadataHelper.RestoreRichMetadata(read);
        return read.ChunkMetadata;
    }

    [Fact]
    public void NewChunkMetadata_DeclaresNoLanguage()
    {
        Assert.True(string.IsNullOrEmpty(new ChunkMetadata().Language));
    }

    [Fact]
    public void ChunkWithOnlyKeywords_IsStoredWithoutAnInventedLanguage()
    {
        var metadata = new ChunkMetadata { Keywords = ["tax", "invoice"] };

        var restored = RoundTrip(metadata);

        Assert.Equal(["tax", "invoice"], restored.Keywords);
        Assert.True(string.IsNullOrEmpty(restored.Language), $"language was '{restored.Language}'");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ko")]
    public void AnExplicitLanguage_SurvivesTheRoundTripEvenWhenItIsTheOnlyThingSet(string language)
    {
        var restored = RoundTrip(new ChunkMetadata { Language = language });

        Assert.Equal(language, restored.Language);
    }

    [Fact]
    public void UntouchedMetadata_StoresNothing()
    {
        var chunk = new DocumentChunk();

        var stored = MetadataHelper.ForStorage(chunk);

        Assert.False(stored.ContainsKey(MetadataHelper.ReservedKeys.ChunkMetadata));
    }
}
