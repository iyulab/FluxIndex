using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// The indexer splits any chunk over ~8,000 tokens (32,000 characters) into 8,000-character pieces as a
/// safety net for upstream chunkers. Until 0.38.0 that path threw on its second piece — the pieces were
/// created against the pre-split total, and the entity factory rejects an index at or past the total — and
/// the "adjusted later" comment on that total never was. Nothing exercised it.
/// </summary>
public class IndexerOversizedChunkSplitTests
{
    [Fact]
    public async Task AnOversizedChunk_IsSplit_AndEveryStoredChunkIsRenumbered()
    {
        var context = FluxIndexContext.CreateBuilder().UseInMemoryEmbedding().SuppressStartupMessages().Build();
        var document = Document.Create("doc-big");
        document.Content = "big";
        document.AddChunk(DocumentChunk.Create("doc-big", "lead paragraph", 0, 2));
        document.AddChunk(DocumentChunk.Create("doc-big", new string('x', 40_000), 1, 2));

        await context.Indexer.IndexDocumentAsync(document, TestContext.Current.CancellationToken);

        var stored = (await context.ServiceProvider.GetRequiredService<IVectorStore>()
            .GetByDocumentIdAsync("doc-big", TestContext.Current.CancellationToken))
            .OrderBy(c => c.ChunkIndex).ToList();

        stored.Should().HaveCount(6, "1 normal chunk + 40,000 characters in 8,000-character pieces");
        stored.Select(c => c.ChunkIndex).Should().Equal(0, 1, 2, 3, 4, 5);
        stored.Should().OnlyContain(c => c.TotalChunks == 6);
        stored.Skip(1).Should().OnlyContain(c => c.Content.Length == 8_000);
    }
}
