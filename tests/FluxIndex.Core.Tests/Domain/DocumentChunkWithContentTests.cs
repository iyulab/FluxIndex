using System.Reflection;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Domain;

public class DocumentChunkWithContentTests
{
    [Fact]
    public void WithContent_ReplacesContent_AndLeavesTheSourceUntouched()
    {
        var source = Populated();

        var copy = source.WithContent("rewritten");

        Assert.Equal("rewritten", copy.Content);
        Assert.Equal("original", source.Content);
        Assert.NotSame(source, copy);
    }

    /// <summary>
    /// Every public property but <c>Content</c> carries over. Reflection, not a list: a property added to
    /// the entity later is covered without anyone remembering this method exists.
    /// </summary>
    [Fact]
    public void WithContent_CopiesEveryOtherPublicProperty()
    {
        var source = Populated();

        var copy = source.WithContent("rewritten");

        var properties = typeof(DocumentChunk).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != nameof(DocumentChunk.Content) && p.GetIndexParameters().Length == 0)
            .ToList();
        Assert.NotEmpty(properties);
        foreach (var property in properties)
        {
            var expected = property.GetValue(source);
            var actual = property.GetValue(copy);
            if (expected is System.Collections.IEnumerable sequence and not string)
                Assert.Equal(sequence.Cast<object>(), ((System.Collections.IEnumerable)actual!).Cast<object>());
            else
                Assert.True(Equals(expected, actual), $"{property.Name}: expected {expected}, got {actual}");
        }
    }

    [Fact]
    public void WithContent_CopiesCollections_SoTheCopyCannotRewriteTheSource()
    {
        var source = Populated();

        var copy = source.WithContent("rewritten");
        copy.Metadata!["added"] = 1;
        copy.AddProperty("added", 1);

        Assert.False(source.Metadata!.ContainsKey("added"));
        Assert.False(source.Properties.ContainsKey("added"));
    }

    private static DocumentChunk Populated()
    {
        var chunk = new DocumentChunk
        {
            Id = "chunk-1",
            DocumentId = "doc-1",
            Content = "original",
            ChunkIndex = 2,
            TotalChunks = 5,
            Embedding = [0.1f, 0.2f],
            CreatedAt = new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc),
            TokenCount = 7,
            Score = 0.42f,
            Metadata = new Dictionary<string, object> { ["k"] = "v" },
        };
        chunk.AddProperty("p", 3);
        chunk.SetMetadata(new ChunkMetadata { TokenCount = 7 });
        chunk.SetQuality(new ChunkQuality());
        return chunk;
    }
}
