using AwesomeAssertions;
using FluxIndex.Storage.Qdrant;
using Xunit;

namespace FluxIndex.Storage.Qdrant.Tests;

/// <summary>
/// Tests for <see cref="QdrantVectorStore.ToPointId"/> — the mapping from a free-string
/// <c>DocumentChunk.Id</c> onto the UUID Qdrant requires for a point id. Pure function, no Qdrant
/// instance involved.
/// </summary>
public class QdrantPointIdTests
{
    [Fact]
    public void ToPointId_GuidChunkId_IsUsedVerbatim()
    {
        // Backward compatibility is the whole reason for this branch: collections written before
        // the derivation existed stored points under the chunk id itself, so a UUID chunk id must
        // still resolve to exactly that point.
        var id = Guid.NewGuid();

        QdrantVectorStore.ToPointId(id.ToString()).Should().Be(id);
    }

    [Theory]
    [InlineData("chunk-1")]
    [InlineData("doc/2026/report.pdf#3")]
    [InlineData("한글 청크 id")]
    public void ToPointId_NonGuidChunkId_IsDeterministic(string chunkId)
    {
        // Determinism is what keeps re-storing a chunk an upsert rather than a duplicate insert,
        // and it has to hold across processes, not just within one call.
        QdrantVectorStore.ToPointId(chunkId).Should().Be(QdrantVectorStore.ToPointId(chunkId));
    }

    [Fact]
    public void ToPointId_DifferentChunkIds_DoNotCollide()
    {
        var ids = new[] { "chunk-1", "chunk-2", "chunk-10", "", " ", "CHUNK-1" };

        ids.Select(QdrantVectorStore.ToPointId).Should().OnlyHaveUniqueItems(
            "distinct chunk ids must address distinct points — a collision silently overwrites a " +
            "different chunk");
    }

    [Fact]
    public void ToPointId_NonGuidChunkId_ProducesAWellFormedVersion8Uuid()
    {
        // The value is labelled honestly rather than borrowing version 5's number, since the digest
        // is SHA-256 rather than the SHA-1 that RFC 4122 fixes version 5 to.
        var bytes = QdrantVectorStore.ToPointId("chunk-1").ToByteArray();

        // Guid.ToByteArray is little-endian across the first three fields, so the version nibble
        // sits in byte 7 and the variant bits in byte 8.
        (bytes[7] >> 4).Should().Be(8, "RFC 9562 version 8 (implementation-defined)");
        (bytes[8] & 0xC0).Should().Be(0x80, "RFC 9562 variant");
    }

    [Fact]
    public void ToPointId_KnownChunkId_MatchesRecordedValue()
    {
        // Pins the derivation itself. Changing the namespace or the hash would orphan every point
        // already stored under a non-UUID chunk id, so that must never happen silently.
        QdrantVectorStore.ToPointId("chunk-1").Should().Be(
            Guid.Parse("b248ba6d-f7db-8512-9cf5-478959a405ae"),
            "the derivation is a storage contract, not an implementation detail");
    }
}
