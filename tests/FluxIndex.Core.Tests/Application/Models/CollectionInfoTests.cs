using AwesomeAssertions;
using FluxIndex.Core.Application.Models;
using Xunit;

namespace FluxIndex.Core.Tests.Application.Models;

public class CollectionInfoTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var info = new CollectionInfo(
            Name: "chunk_embeddings_a1b2c3d4",
            Dimension: 1536,
            EntryCount: 42,
            StorageSizeBytes: 1024 * 1024);

        info.Name.Should().Be("chunk_embeddings_a1b2c3d4");
        info.Dimension.Should().Be(1536);
        info.EntryCount.Should().Be(42);
        info.StorageSizeBytes.Should().Be(1024 * 1024);
    }

    [Fact]
    public void StorageSizeBytes_CanBeNull()
    {
        var info = new CollectionInfo(
            Name: "chunk_embeddings_a1b2c3d4",
            Dimension: 384,
            EntryCount: 0,
            StorageSizeBytes: null);

        info.StorageSizeBytes.Should().BeNull();
    }

    [Fact]
    public void Record_Equality_WorksByValue()
    {
        var a = new CollectionInfo("test", 384, 10, 100);
        var b = new CollectionInfo("test", 384, 10, 100);

        a.Should().Be(b);
    }
}
